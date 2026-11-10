using System.Windows;
using SelectiveVpn.Services;

namespace SelectiveVpn;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Any(static argument => argument == "--exit"))
        {
            InstanceShutdown.SignalAndWait();
            Shutdown();
            return;
        }

        // При --connect ждём выход не-elevated копии после UAC; иначе сразу активируем существующее окно.
        var wait = e.Args.Any(static argument => argument == "--connect")
            ? TimeSpan.FromSeconds(30)
            : TimeSpan.Zero;
        if (!AppInstance.TryOwn(wait))
        {
            AppInstance.SignalActivate();
            Shutdown();
            return;
        }

        Exit += (_, _) => AppInstance.Release();

        var settings = UserSettingsStore.Load(out _);
        AppLanguage.Apply(settings.Language);
        AppTheme.Apply(settings.Theme);
        base.OnStartup(e);
    }
}
