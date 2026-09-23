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

    // ── Audio playback (Bluetooth → Echo Studio) ──
    private const string EchoMac   = "B8:5F:98:E4:FD:B1";
    private readonly SongLibrary _library;
    private readonly PlayLimiter _limiter;
    private double _songDuration   = 39;   // overridden by ffprobe at runtime
    private Process? _audioProcess;

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
        this.FindControl<Control>("BusOverlay")!.RenderTransform = _busSlide;
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

        // ── Tap → toggle bus animation (kid-friendly mode) ──
        if (absDx < 30 && absDy < 30)
        {
            // Rolling debounce: every tap resets the clock, only a tap after a quiet
            // spell is acted on. Mashing the screen can't cut the song short.
            var now = DateTime.UtcNow;
            bool tooSoon = (now - _lastTapAt).TotalSeconds < _library.Limit.TapDebounceSeconds;
            _lastTapAt = now;
            if (tooSoon || _phase != Phase.None) return;

            if (vm.IsAnimationPlaying)
                BeginBusExit();
            else if (_limiter.IsLocked(DateTime.Now))
                PlayComeBackLater();
            else
                BeginBusEnter();
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
    //  Bus enter / exit
    // ════════════════════════════════════════════════════

    private void BeginBusEnter()
    {
        var song = _library.Pick();
        if (song is null)
            _songDuration = 39;
        else
        {
            double end = ProbeAudioDuration(song.Path);
            if (song.End > 0) end = Math.Min(end, song.End);
            _songDuration = Math.Max(5, end - song.Start);
        }

        // Tell the bus control how long the song is — it adapts all timing
        var busOverlay = this.FindControl<Controls.WheelsOnTheBus>("BusOverlay");
        if (busOverlay != null) busOverlay.SongDuration = _songDuration;

        if (DataContext is ClockViewModel vm)
            vm.StartBusAnimation();

        if (song != null) StartAudio(song);
        _hue?.BusArrived();
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

                    StopTimer();

                    // Third play in a short span? The bus takes a nap.
                    if (_limiter.CheckAfterPlay(DateTime.Now))
                        BeginNap();
                }
                break;
            }
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
    }

    /// <summary>Closed mid-song (Escape, or the service stopping): don't leave the room yellow.</summary>
    protected override void OnClosed(EventArgs e)
    {
        StopAudio();
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

            // B = bus animation toggle
            case Key.B:
                if (DataContext is ClockViewModel bvm)
                {
                    if (bvm.IsAnimationPlaying && _phase == Phase.None)
                        BeginBusExit();
                    else if (!bvm.IsAnimationPlaying && _phase == Phase.None)
                        BeginBusEnter();
                }
                break;

            // Up / Down arrows = bus animation (matches swipe gestures)
            case Key.Up:
                if (_phase == Phase.None && DataContext is ClockViewModel uvm
                    && !uvm.IsAnimationPlaying)
                    BeginBusEnter();
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
