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

        var settings = UserSettingsStore.Load(out _);
        AppTheme.Apply(settings.Theme);
        base.OnStartup(e);
    }
}
