using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using PiClock.ViewModels;

namespace PiClock.Views;

public partial class MainWindow : Window
{
    private Point? _gestureStart;
    private DateTime _gestureStartTime;

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
    }

    // ── Touch gesture handling ──
    // Swipe left/right: cycle clock mode (Analog ↔ Digital ↔ Calendar)
    // Swipe up:         trigger Wheels on the Bus animation
    // Tap:              dismiss animation (if playing)

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

        // Swipe up: ≥80 px upward, mostly vertical, quick gesture
        if (dy > 80 && absDy > absDx * 1.5 && ms < 800)
        {
            vm.StartBusAnimation();
            return;
        }

        // Swipe left or right: ≥80 px horizontal, mostly horizontal, quick gesture
        if (absDx > 80 && absDx > absDy * 1.5 && ms < 800)
        {
            if (!vm.IsAnimationPlaying)
                vm.ToggleMode();
            return;
        }

        // Tap: small movement — dismiss animation if playing
        if (absDx < 30 && absDy < 30)
        {
            if (vm.IsAnimationPlaying)
                vm.StopBusAnimation();
        }
    }

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

            // B key triggers the bus animation (handy for testing without touch)
            case Key.B:
                if (DataContext is ClockViewModel bvm)
                {
                    if (bvm.IsAnimationPlaying)
                        bvm.StopBusAnimation();
                    else
                        bvm.StartBusAnimation();
                }
                break;
        }

        base.OnKeyDown(e);
    }
}
