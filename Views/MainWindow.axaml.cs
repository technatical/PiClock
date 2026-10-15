using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using PiClock.Audio;
using PiClock.Controls;
using PiClock.Lights;
using PiClock.ViewModels;

namespace PiClock.Views;

public partial class MainWindow : Window
{
    // ── Gesture tracking ──
    private Point? _gestureStart;
    private DateTime _gestureStartTime;

    // ── Transition animation ──
    private enum Phase { None, ModeOut, ModeIn, BusEnter, BusExit }

    private Phase _phase = Phase.None;
    private readonly TranslateTransform _clockSlide = new();
    private readonly TranslateTransform _busSlide = new();
    private DispatcherTimer? _transTimer;
    private DispatcherTimer? _autoDismissTimer;
    private double _progress;
    private double _swipeDir;       // -1 = left, +1 = right

    private const double FrameMs   = 16;    // ~60 fps
    private const double OutMs     = 180;   // snappy slide-out
    private const double InMs      = 250;   // smooth slide-in
    private const double BusMs     = 400;   // bus enter/exit

    // A press held this long (without moving) ends the nap and clears the play count
    private const double LongPressMs = 700;

    // ── Song picker: a tap shows one button per scene ──
    private SongPicker _picker = null!;
    private DispatcherTimer? _pickerTimer;
    private const double PickerSeconds = 15;   // closes by itself if nothing is picked

    // ── Audio playback (Bluetooth → Echo Studio) ──
    private const string EchoMac   = "B8:5F:98:E4:FD:B1";
    private readonly SongLibrary _library;
    private readonly PlayLimiter _limiter;
    private double _songDuration   = 39;   // overridden by ffprobe at runtime
    private Process? _audioProcess;

    // ── Spotify songs (only when songs.json lists some), played by Spotify Soloist ──
    private readonly SpotifyPlayer? _spotify;
    /// <summary>Scene length while Spotify has not said how long a song without "end" is.</summary>
    private const double UnknownSpotifyLength = 180;

    // ── The scene on screen (bus, rainbow, …) and the song it belongs to ──
    private SongScene? _scene;
    private Song? _sceneSong;
    private DateTime _sceneStartedAt;

    // ── Hue lights follow the bus (only when assets/hue.json exists) ──
    private readonly HueLights? _hue;

    // ── Nap (too many plays in a row) ──
    private DispatcherTimer? _napTimer;
    private DateTime _lastClipAt = DateTime.MinValue;
    private DateTime _lastTapAt  = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();

        _library = new SongLibrary(Path.Combine(AppContext.BaseDirectory, "assets"));
        _limiter = new PlayLimiter(_library.Limit);
        _hue = HueLights.Load(Path.Combine(AppContext.BaseDirectory, "assets"));
        if (_library.SpotifyCount > 0)
            _spotify = new SpotifyPlayer(_library.Spotify);

        // Always hide the cursor — this is a clock, not a desktop app
        Cursor = new Cursor(StandardCursorType.None);

        // Launch in kiosk mode when --kiosk is passed (intended for the Pi)
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--kiosk"))
        {
            WindowState = WindowState.FullScreen;
            SystemDecorations = SystemDecorations.None;
        }

        // Attach transition transforms (separate from anti-burn-in shift)
        this.FindControl<Panel>("ClockPanel")!.RenderTransform = _clockSlide;
        this.FindControl<Control>("SceneOverlay")!.RenderTransform = _busSlide;
        _picker = this.FindControl<SongPicker>("Picker")!;
    }

    // ════════════════════════════════════════════════════
    //  Touch / mouse gestures
    // ════════════════════════════════════════════════════

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _gestureStart = e.GetPosition(this);
        _gestureStartTime = DateTime.UtcNow;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_gestureStart is not { } start) return;

        var end = e.GetPosition(this);
        double dx = end.X - start.X;           // positive = rightward
        double dy = start.Y - end.Y;           // positive = upward
        double absDx = Math.Abs(dx);
        double absDy = Math.Abs(dy);
        double ms = (DateTime.UtcNow - _gestureStartTime).TotalMilliseconds;
        _gestureStart = null;

        if (DataContext is not ClockViewModel vm) return;

        // ── Tap → song picker, or dismiss the song (kid-friendly mode) ──
        if (absDx < 30 && absDy < 30)
        {
            var now = DateTime.UtcNow;

            // Picker up: a tap on a button plays that scene's song. Taps elsewhere, and
            // taps before the buttons have bounced in, are ignored.
            if (_picker.IsOpen)
            {
                if (_picker.IsReady && _picker.ButtonAt(end) is { } scene)
                {
                    _lastTapAt = now;
                    ClosePicker();
                    BeginBusEnter(scene);
                }
                return;
            }

            // Long press: a grown-up ending the nap early (or clearing the play count)
            if (ms >= LongPressMs)
            {
                ResetNap();
                return;
            }

            // Rolling debounce: every tap resets the clock, only a tap after a quiet
            // spell is acted on. Mashing the screen can't cut the song short.
            bool tooSoon = (now - _lastTapAt).TotalSeconds < _library.Limit.TapDebounceSeconds;
            _lastTapAt = now;
            if (tooSoon || _phase != Phase.None) return;

            if (vm.IsAnimationPlaying)
                BeginBusExit();
            else if (_limiter.IsLocked(DateTime.Now))
                PlayComeBackLater();
            else
                OpenPicker();
            return;
        }

        // Swipe gestures disabled for now (toddler mode 😄)
        // Uncomment to restore: swipe up/down = bus, left/right = cycle clock mode
        //
        // if (dy > 80 && absDy > absDx * 1.5 && ms < 800
        //     && _phase == Phase.None && !vm.IsAnimationPlaying)
        // { BeginBusEnter(); return; }
        //
        // if (dy < -80 && absDy > absDx * 1.5 && ms < 800
        //     && vm.IsAnimationPlaying && _phase == Phase.None)
        // { BeginBusExit(); return; }
        //
        // if (absDx > 80 && absDx > absDy * 1.5 && ms < 800
        //     && _phase == Phase.None && !vm.IsAnimationPlaying)
        // { BeginModeSlide(dx < 0 ? -1 : 1); return; }
    }

    // ════════════════════════════════════════════════════
    //  Mode slide (swipe left / right)
    // ════════════════════════════════════════════════════

    private void BeginModeSlide(double direction)
    {
        _swipeDir = direction;
        _progress = 0;
        _phase = Phase.ModeOut;
        StartTimer();
    }

    // ════════════════════════════════════════════════════
    //  Song picker
    // ════════════════════════════════════════════════════

    /// <summary>Shows a button per scene (up to three). With only one scene there is
    /// nothing to choose, so its song starts straight away.</summary>
    private void OpenPicker()
    {
        var scenes = _library.Scenes.Take(3).ToList();
        if (scenes.Count <= 1)
        {
            BeginBusEnter(scenes.FirstOrDefault() ?? "bus");
            return;
        }

        _picker.Open(scenes);
        _pickerTimer?.Stop();
        _pickerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(PickerSeconds) };
        _pickerTimer.Tick += (_, _) => ClosePicker();
        _pickerTimer.Start();
    }

    private void ClosePicker()
    {
        _pickerTimer?.Stop();
        _pickerTimer = null;
        _picker.Close();
    }

    // ════════════════════════════════════════════════════
    //  Bus enter / exit
    // ════════════════════════════════════════════════════

    /// <summary>Starts a song for the scene ("bus", "rainbow", "spider", …) and the scene.</summary>
    private void BeginBusEnter(string scene)
    {
        var song = _library.Pick(scene);

        if (song is null)
            _songDuration = 39;
        else if (song.SpotifyTrack != null)
            _songDuration = song.End > 0 ? Math.Max(5, song.End - song.Start) : UnknownSpotifyLength;
        else
        {
            double end = ProbeAudioDuration(song.Path);
            if (song.End > 0) end = Math.Min(end, song.End);
            _songDuration = Math.Max(5, end - song.Start);
        }

        // Start the song's scene and tell it how long the song is — it adapts all timing
        if (_scene != null) _scene.IsPlaying = false;
        _scene = SceneFor(song?.Animation);
        _sceneSong = song;
        _sceneStartedAt = DateTime.UtcNow;
        _scene.SongDuration = _songDuration;
        _scene.IsPlaying = true;

        if (DataContext is ClockViewModel vm)
            vm.StartBusAnimation();

        if (song?.SpotifyTrack != null && _spotify != null)
        {
            StopAudio();
            Task.Run(EnsureBluetoothConnected);
            _spotify.Play(song, length => Dispatcher.UIThread.Post(() => SongLengthKnown(song, length)));
        }
        else if (song != null) StartAudio(song);
        _hue?.BusArrived(_scene.LightColors);
        _limiter.RecordPlayStart(DateTime.Now);
        _busSlide.Y = Bounds.Height;
        _progress = 0;
        _phase = Phase.BusEnter;
        StartTimer();
    }

    private void BeginBusExit()
    {
        _autoDismissTimer?.Stop();
        _autoDismissTimer = null;
        StopAudio();
        _hue?.BusLeft();
        _progress = 0;
        _phase = Phase.BusExit;
        StartTimer();
    }

    // ════════════════════════════════════════════════════
    //  Animation driver
    // ════════════════════════════════════════════════════

    private void StartTimer()
    {
        _transTimer?.Stop();
        _transTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FrameMs) };
        _transTimer.Tick += Tick;
        _transTimer.Start();
    }

    private void StopTimer()
    {
        _transTimer?.Stop();
        _transTimer = null;
        _phase = Phase.None;
    }

    private void Tick(object? sender, EventArgs e)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;

        switch (_phase)
        {
            // ── Clock slides out in swipe direction ──
            case Phase.ModeOut:
            {
                _progress += FrameMs / OutMs;
                double t = Ease(Math.Min(_progress, 1));
                _clockSlide.X = _swipeDir * t * w;

                if (_progress >= 1)
                {
                    // Content is off-screen — switch the mode
                    if (DataContext is ClockViewModel vm)
                    {
                        if (_swipeDir < 0) vm.NextMode();
                        else vm.PreviousMode();
                    }

                    // Reposition on the opposite side for slide-in
                    _clockSlide.X = -_swipeDir * w;
                    _progress = 0;
                    _phase = Phase.ModeIn;
                }
                break;
            }

            // ── New mode slides in from opposite side ──
            case Phase.ModeIn:
            {
                _progress += FrameMs / InMs;
                double t = Ease(Math.Min(_progress, 1));
                _clockSlide.X = -_swipeDir * (1 - t) * w;

                if (_progress >= 1)
                {
                    _clockSlide.X = 0;
                    StopTimer();
                }
                break;
            }

            // ── Clock slides up, bus slides up from below ──
            case Phase.BusEnter:
            {
                _progress += FrameMs / BusMs;
                double t = Ease(Math.Min(_progress, 1));
                _clockSlide.Y = -t * h;
                _busSlide.Y = (1 - t) * h;

                if (_progress >= 1)
                {
                    _clockSlide.Y = -h;
                    _busSlide.Y = 0;
                    StopTimer();

                    // Auto-dismiss when the song ends
                    _autoDismissTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(_songDuration)
                    };
                    _autoDismissTimer.Tick += (_, _) => BeginBusExit();
                    _autoDismissTimer.Start();
                }
                break;
            }

            // ── Bus slides down, clock slides back down ──
            case Phase.BusExit:
            {
                _progress += FrameMs / BusMs;
                double t = Ease(Math.Min(_progress, 1));
                _clockSlide.Y = -(1 - t) * h;
                _busSlide.Y = t * h;

                if (_progress >= 1)
                {
                    _clockSlide.Y = 0;
                    _busSlide.Y = h;

                    if (DataContext is ClockViewModel vm)
                        vm.StopBusAnimation();
                    if (_scene != null) _scene.IsPlaying = false;
                    _sceneSong = null;

                    StopTimer();

                    // Third play in a short span? The bus takes a nap.
                    if (_limiter.CheckAfterPlay(DateTime.Now))
                        BeginNap();
                }
                break;
            }
        }
    }

    /// <summary>The scene named in songs.json ("bus" when missing or unknown).</summary>
    private SongScene SceneFor(string? animation) =>
        animation switch
        {
            "rainbow" => this.FindControl<SongScene>("RainbowScene")!,
            "spider"  => this.FindControl<SongScene>("SpiderScene")!,
            _         => this.FindControl<SongScene>("BusScene")!
        };

    /// <summary>Spotify has said how long the playing song is: stretch or shorten the scene
    /// and its auto-dismiss to match, if that song is still on screen.</summary>
    private void SongLengthKnown(Song song, double length)
    {
        if (song != _sceneSong || _scene == null || _phase == Phase.BusExit) return;

        _songDuration = length;
        _scene.SongDuration = length;

        if (_autoDismissTimer != null)
        {
            _autoDismissTimer.Stop();
            double left = length - (DateTime.UtcNow - _sceneStartedAt).TotalSeconds;
            _autoDismissTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.5, left));
            _autoDismissTimer.Start();
        }
    }

    // ════════════════════════════════════════════════════
    //  Audio playback (mpv → Bluetooth Echo Studio)
    // ════════════════════════════════════════════════════

    /// <summary>Probe audio duration via ffprobe. Falls back to 39s.</summary>
    private static double ProbeAudioDuration(string path)
    {
        if (!File.Exists(path)) return 39;
        try
        {
            var proc = Process.Start(new ProcessStartInfo("ffprobe",
                $"-v quiet -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{path}\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true
            });
            if (proc == null) return 39;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            return double.TryParse(output.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double dur) ? dur : 39;
        }
        catch { return 39; }
    }

    private void StartAudio(Song song)
    {
        try
        {
            StopAudio();

            // Reconnect Bluetooth if idle-disconnected, off the UI thread
            Task.Run(EnsureBluetoothConnected);

            if (!File.Exists(song.Path)) return;

            // Use wrapper script to guarantee PipeWire environment from systemd service.
            // The wrapper passes every argument through to mpv.
            var scriptPath = Path.Combine(AppContext.BaseDirectory, "play-audio.sh");
            bool useScript = File.Exists(scriptPath) && !OperatingSystem.IsWindows();

            var psi = new ProcessStartInfo(useScript ? scriptPath : "mpv")
            {
                UseShellExecute = false, CreateNoWindow = true
            };
            psi.Environment["XDG_RUNTIME_DIR"] = "/run/user/1000";

            if (!useScript)
            {
                psi.ArgumentList.Add("--no-video");
                psi.ArgumentList.Add("--no-terminal");
            }

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string startArg = song.Start.ToString("0.###", inv);
            if (song.Start > 0)
                psi.ArgumentList.Add($"--start={startArg}");
            if (song.End > 0)
                psi.ArgumentList.Add($"--end={song.End.ToString("0.###", inv)}");

            // afade runs on the file's own clock, so the fade must start at the offset,
            // not at zero, or it is already finished by the time playback begins.
            var filters = new List<string>();
            if (song.FadeIn > 0)
                filters.Add($"afade=t=in:st={startArg}:d={song.FadeIn.ToString("0.###", inv)}");
            if (song.End > 0 && song.FadeOut > 0)
                filters.Add($"afade=t=out:st={(song.End - song.FadeOut).ToString("0.###", inv)}:d={song.FadeOut.ToString("0.###", inv)}");
            if (Math.Abs(song.Gain) > 0.01)
                filters.Add($"volume={song.Gain.ToString("0.#", inv)}dB");
            if (filters.Count > 0)
                psi.ArgumentList.Add($"--af=lavfi=[{string.Join(",", filters)}]");

            psi.ArgumentList.Add(song.Path);
            _audioProcess = Process.Start(psi);
        }
        catch { /* Audio is best-effort — don't crash the clock */ }
    }

    /// <summary>Connects the Echo only if bluetoothctl says it is not already connected,
    /// so the journal stays free of "already connected" complaints on every play.</summary>
    private static void EnsureBluetoothConnected()
    {
        try
        {
            using var info = Process.Start(new ProcessStartInfo("bluetoothctl", $"info {EchoMac}")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            });
            if (info == null) return;
            string status = info.StandardOutput.ReadToEnd();
            info.WaitForExit(3000);
            if (status.Contains("Connected: yes")) return;

            using var connect = Process.Start(new ProcessStartInfo("bluetoothctl", $"connect {EchoMac}")
            {
                UseShellExecute = false, CreateNoWindow = true
            });
            connect?.WaitForExit(10000);
        }
        catch { /* best effort */ }
    }

    // ════════════════════════════════════════════════════
    //  Nap: three plays in five minutes → ten minute cooldown
    // ════════════════════════════════════════════════════

    private void BeginNap()
    {
        var nap = this.FindControl<NapCountdown>("NapOverlay");
        if (nap == null) return;

        UpdateNap(nap);
        nap.IsActive = true;

        _napTimer?.Stop();
        _napTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _napTimer.Tick += (_, _) =>
        {
            if (_limiter.IsLocked(DateTime.Now)) UpdateNap(nap);
            else EndNap();
        };
        _napTimer.Start();
    }

    private void UpdateNap(NapCountdown nap)
    {
        var now = DateTime.Now;
        var left = _limiter.Remaining(now);
        nap.Fraction = _limiter.RemainingFraction(now);
        nap.RemainingText = $"{(int)left.TotalMinutes}:{left.Seconds:00}";
    }

    /// <summary>Long press: end the nap now if there is one, and forget recent plays so the
    /// next ones don't bring it straight back.</summary>
    private void ResetNap()
    {
        if (_napTimer != null) EndNap();
        else _limiter.ClearLock();
    }

    private void EndNap()
    {
        _napTimer?.Stop();
        _napTimer = null;
        _limiter.ClearLock();

        var nap = this.FindControl<NapCountdown>("NapOverlay");
        if (nap != null) nap.IsActive = false;
    }

    /// <summary>Tapped during the nap: play the "come back soon" clip, but not more than
    /// once every few seconds so a determined tapper doesn't get a stuttering loop.</summary>
    private void PlayComeBackLater()
    {
        var now = DateTime.Now;
        if ((now - _lastClipAt).TotalSeconds < _library.Limit.ClipEverySeconds) return;
        if (_library.ComeBackLaterPath is not { } clip) return;

        _lastClipAt = now;
        StartAudio(new Song(clip, 0, 0, _library.Limit.ComeBackLaterGain));
    }

    private void StopAudio()
    {
        try
        {
            if (_audioProcess is { HasExited: false })
            {
                _audioProcess.Kill();
                _audioProcess.Dispose();
            }
            _audioProcess = null;
        }
        catch { }
        _spotify?.Stop();
    }

    /// <summary>Closed mid-song (Escape, or the service stopping): don't leave the room yellow.</summary>
    protected override void OnClosed(EventArgs e)
    {
        StopAudio();
        _spotify?.StopNow(TimeSpan.FromSeconds(3));
        _spotify?.Dispose();
        _hue?.RestoreNow(TimeSpan.FromSeconds(3));
        _hue?.Dispose();
        base.OnClosed(e);
    }

    /// <summary>Ease-out cubic — fast start, smooth deceleration.</summary>
    private static double Ease(double t) =>
        1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);

    // ════════════════════════════════════════════════════
    //  Keyboard shortcuts
    // ════════════════════════════════════════════════════

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;

            case Key.F11:
                if (WindowState == WindowState.FullScreen)
                {
                    WindowState = WindowState.Normal;
                    SystemDecorations = SystemDecorations.Full;
                    Cursor = Cursor.Default;
                }
                else
                {
                    WindowState = WindowState.FullScreen;
                    SystemDecorations = SystemDecorations.None;
                    Cursor = new Cursor(StandardCursorType.None);
                }
                break;

            // B = song picker / dismiss the song (like a tap, without the nap)
            case Key.B:
                if (_picker.IsOpen)
                    ClosePicker();
                else if (DataContext is ClockViewModel bvm && _phase == Phase.None)
                {
                    if (bvm.IsAnimationPlaying) BeginBusExit();
                    else OpenPicker();
                }
                break;

            // 1 / 2 / 3 = play the scene on that picker button straight away
            case Key.D1 or Key.D2 or Key.D3 or Key.NumPad1 or Key.NumPad2 or Key.NumPad3:
            {
                int n = e.Key is Key.D1 or Key.NumPad1 ? 0 : e.Key is Key.D2 or Key.NumPad2 ? 1 : 2;
                var scenes = _library.Scenes;
                if (n < scenes.Count && _phase == Phase.None
                    && DataContext is ClockViewModel nvm && !nvm.IsAnimationPlaying)
                {
                    ClosePicker();
                    BeginBusEnter(scenes[n]);
                }
                break;
            }

            // R = end the nap early (same as a long press)
            case Key.R:
                ResetNap();
                break;

            // Up / Down arrows = song picker / dismiss (matches swipe gestures)
            case Key.Up:
                if (_phase == Phase.None && DataContext is ClockViewModel uvm
                    && !uvm.IsAnimationPlaying && !_picker.IsOpen)
                    OpenPicker();
                break;

            case Key.Down:
                if (_phase == Phase.None && DataContext is ClockViewModel dvm
                    && dvm.IsAnimationPlaying)
                    BeginBusExit();
                break;

            // Left / Right arrows = cycle clock mode
            case Key.Left:
                if (_phase == Phase.None && DataContext is ClockViewModel lvm
                    && !lvm.IsAnimationPlaying)
                    BeginModeSlide(-1);
                break;

            case Key.Right:
                if (_phase == Phase.None && DataContext is ClockViewModel rvm
                    && !rvm.IsAnimationPlaying)
                    BeginModeSlide(1);
                break;
        }

        base.OnKeyDown(e);
    }
}
