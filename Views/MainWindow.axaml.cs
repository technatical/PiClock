using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
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
    private const string AudioFile = "assets/wheels-on-the-bus.mp3";
    private const string EchoMac   = "B8:5F:98:E4:FD:B1";
    private double _songDuration   = 39;   // overridden by ffprobe at runtime
    private Process? _audioProcess;

    public MainWindow()
    {
        InitializeComponent();

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

        // ── Swipe up → bus animation ──
        if (dy > 80 && absDy > absDx * 1.5 && ms < 800
            && _phase == Phase.None && !vm.IsAnimationPlaying)
        {
            BeginBusEnter();
            return;
        }

        // ── Swipe down → dismiss bus animation ──
        if (dy < -80 && absDy > absDx * 1.5 && ms < 800
            && vm.IsAnimationPlaying && _phase == Phase.None)
        {
            BeginBusExit();
            return;
        }

        // ── Swipe left / right → cycle clock mode ──
        if (absDx > 80 && absDx > absDy * 1.5 && ms < 800
            && _phase == Phase.None && !vm.IsAnimationPlaying)
        {
            BeginModeSlide(dx < 0 ? -1 : 1);
            return;
        }

        // ── Tap → dismiss bus animation ──
        if (absDx < 30 && absDy < 30
            && vm.IsAnimationPlaying && _phase == Phase.None)
        {
            BeginBusExit();
        }
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
        var audioPath = Path.Combine(AppContext.BaseDirectory, AudioFile);
        _songDuration = ProbeAudioDuration(audioPath);

        // Tell the bus control how long the song is — it adapts all timing
        var busOverlay = this.FindControl<Controls.WheelsOnTheBus>("BusOverlay");
        if (busOverlay != null) busOverlay.SongDuration = _songDuration;

        if (DataContext is ClockViewModel vm)
            vm.StartBusAnimation();

        StartAudio(audioPath);
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

    private void StartAudio(string audioPath)
    {
        try
        {
            // Reconnect Bluetooth if idle-disconnected (instant if already connected)
            Process.Start(new ProcessStartInfo("bluetoothctl", $"connect {EchoMac}")
            {
                UseShellExecute = false, CreateNoWindow = true
            });

            if (!File.Exists(audioPath)) return;

            _audioProcess = Process.Start(new ProcessStartInfo("mpv",
                $"--no-video --no-terminal \"{audioPath}\"")
            {
                UseShellExecute = false, CreateNoWindow = true
            });
        }
        catch { /* Audio is best-effort — don't crash the clock */ }
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
