using Avalonia;
using System;

namespace SelectiveVpn.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Do not auto-connect on launch during development (ConnectOnLaunch stays false).
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
