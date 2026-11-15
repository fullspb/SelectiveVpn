using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SelectiveVpn.Desktop.Services;
using SelectiveVpn.Desktop.ViewModels;
using SelectiveVpn.Desktop.Views;
using SelectiveVpn.Engine;

namespace SelectiveVpn.Desktop;

public partial class App : Application
{
    private MainViewModel? _mainViewModel;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var root = AppPaths.RootDirectory;
        Directory.CreateDirectory(root);

        var engine = new MihomoEngine(root);
        var dispatcher = new AvaloniaUiDispatcher();
        _mainViewModel = new MainViewModel(engine, dispatcher);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            var window = new MainWindow
            {
                DataContext = _mainViewModel
            };
            _mainViewModel.AttachWindow(window);
            desktop.MainWindow = window;
            DataContext = _mainViewModel;

            desktop.Exit += (_, _) =>
            {
                _mainViewModel.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
