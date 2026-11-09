using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;
using SelectiveVpn.Services;
using SelectiveVpn.Stubs;
using SelectiveVpn.ViewModels;
using Forms = System.Windows.Forms;

namespace SelectiveVpn;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _tray;
    private EventWaitHandle? _shutdownRequest;
    private bool _allowClose;
    private bool _announcedTray;
    private bool _exiting;

    public MainWindow()
    {
        InitializeComponent();

        var connectOnStart = Environment.GetCommandLineArgs().Any(static argument => argument == "--connect");
        _viewModel = new MainViewModel(
            new MihomoEngine(),
            new StubAppRuleStore(),
            new WpfFilePicker());
        _viewModel.ApplicationExitRequested += (_, _) => _allowClose = true;

        _viewModel.LogEntries.CollectionChanged += (_, args) =>
        {
            if (args.Action != NotifyCollectionChangedAction.Add)
                return;

            Dispatcher.BeginInvoke(
                () =>
                {
                    if (EventLogList.Items.Count == 0)
                        return;

                    EventLogList.ScrollIntoView(EventLogList.Items[EventLogList.Items.Count - 1]);
                },
                System.Windows.Threading.DispatcherPriority.Background);
        };

        _tray = CreateTrayIcon();
        DataContext = _viewModel;
        SourceInitialized += (_, _) => TitleBarTheme.Apply(this, AppTheme.UsesDarkChrome(_viewModel.ThemeId));
        if (connectOnStart)
            Loaded += OnLoadedConnect;
        else if (_viewModel.ConnectOnLaunch)
            Loaded += OnLoadedAutoConnect;
        WatchShutdownRequest();
    }

    private void WatchShutdownRequest()
    {
        _shutdownRequest = InstanceShutdown.CreateListener();
        var handle = _shutdownRequest;
        var thread = new Thread(() =>
        {
            try
            {
                if (!handle.WaitOne())
                    return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => _ = ExitFromTrayAsync()));
        })
        {
            IsBackground = true,
            Name = "SelectiveVpn.Shutdown"
        };
        thread.Start();
    }

    private async void OnLoadedAutoConnect(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedAutoConnect;
        try
        {
            await _viewModel.ConnectOnLaunchAsync();
        }
        catch (Exception)
        {
            _viewModel.LogEntries.Add(new LogEntry("Ошибка интерфейса при автоподключении."));
        }
    }

    private async void OnLoadedConnect(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedConnect;
        try
        {
            await _viewModel.ConnectFromSavedSessionAsync();
        }
        catch (Exception)
        {
            _viewModel.LogEntries.Add(new LogEntry("Ошибка интерфейса при восстановлении подключения."));
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.SaveSettings();
        if (_allowClose)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    protected override void OnClosed(EventArgs e)
    {
        _shutdownRequest?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnClosed(e);
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "");
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => ShowFromTray());
        menu.Items.Add("Подключить", null, async (_, _) => await RunTrayAction(_viewModel.ConnectFromUserAsync));
        menu.Items.Add("Отключить", null, async (_, _) => await RunTrayAction(_viewModel.DisconnectFromUserAsync));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, async (_, _) => await ExitFromTrayAsync());

        var tray = new Forms.NotifyIcon
        {
            Icon = icon ?? System.Drawing.SystemIcons.Shield,
            Text = "SelectiveVpn",
            Visible = true,
            ContextMenuStrip = menu
        };
        tray.DoubleClick += (_, _) => ShowFromTray();
        return tray;
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        if (_announcedTray)
            return;

        _announcedTray = true;
        _tray.ShowBalloonTip(
            3000,
            "SelectiveVpn",
            "SelectiveVpn свёрнут в трей и продолжает работать.",
            Forms.ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async Task RunTrayAction(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception)
        {
            _viewModel.LogEntries.Add(new LogEntry("Ошибка интерфейса. Туннель из-за неё не менялся."));
        }
    }

    private async Task ExitFromTrayAsync()
    {
        if (_exiting)
            return;

        _exiting = true;
        _allowClose = true;
        _tray.Visible = false;
        try
        {
            await _viewModel.PrepareForCloseAsync();
        }
        catch (Exception)
        {
            _viewModel.LogEntries.Add(new LogEntry("Не удалось остановить туннель перед выходом."));
        }

        Close();
        System.Windows.Application.Current?.Shutdown();
    }
}
