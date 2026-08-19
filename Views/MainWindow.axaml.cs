using Avalonia.Controls;
using Avalonia.Input;
using PiClock.ViewModels;

namespace PiClock.Views;

public partial class MainWindow : Window
{
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

    // Tap anywhere to toggle between analog and digital clock
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (DataContext is ClockViewModel vm)
            vm.ToggleMode();
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
        }

        base.OnKeyDown(e);
    }
}
