using Avalonia;
using PiClock.Lights;

namespace PiClock;

class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // One-off setup command: pair with the Hue bridge and write assets/hue.json
        if (args.Contains("--hue-pair"))
            return HuePairing.Run(args);

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
