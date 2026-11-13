using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
    private readonly System.Drawing.Icon _trayIcon;
    private readonly Forms.ContextMenuStrip _trayMenu;
    private readonly Forms.ToolStripMenuItem _trayOpen;
    private readonly Forms.ToolStripMenuItem _trayConnect;
    private readonly Forms.ToolStripMenuItem _trayDisconnect;
    private readonly Forms.ToolStripMenuItem _trayExit;
    private EventWaitHandle? _shutdownRequest;
    private EventWaitHandle? _activateRequest;
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
                    if (EventLogList.Items.Count > 0)
                        EventLogList.ScrollIntoView(EventLogList.Items[EventLogList.Items.Count - 1]);
                },
                System.Windows.Threading.DispatcherPriority.Background);
        };

        _trayIcon = AppIcon.LoadTrayIcon();
        (_tray, _trayMenu, _trayOpen, _trayConnect, _trayDisconnect, _trayExit) = CreateTrayIcon();
        ApplyTrayMenuTheme();
        RefreshTrayMenuTexts();
        AppTheme.ThemeChanged += OnAppThemeChanged;
        AppLanguage.LanguageChanged += OnLanguageChanged;
        DataContext = _viewModel;
        SourceInitialized += (_, _) => TitleBarTheme.Apply(this, AppTheme.UsesDarkChrome(_viewModel.ThemeId));
        Loaded += OnLoadedCheckUpdates;
        if (connectOnStart)
            Loaded += OnLoadedConnect;
        else if (_viewModel.ConnectOnLaunch)
            Loaded += OnLoadedAutoConnect;
        WatchShutdownRequest();
        WatchActivateRequest();
    }

    private async void OnLoadedCheckUpdates(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedCheckUpdates;
        try
        {
            await _viewModel.CheckForUpdatesAsync();
        }
        catch (Exception)
        {
            // Network / parse failures stay silent.
        }
    }

    private void OnAppThemeChanged(object? sender, string theme)
        => Dispatcher.BeginInvoke(() => TrayMenuTheme.Apply(_trayMenu, theme));

    private void OnLanguageChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(RefreshTrayMenuTexts);

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

    private void WatchActivateRequest()
    {
        _activateRequest = AppInstance.CreateActivateListener();
        var handle = _activateRequest;
        var thread = new Thread(() =>
        {
            while (true)
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

                Dispatcher.BeginInvoke(new Action(ShowFromTray));
            }
        })
        {
            IsBackground = true,
            Name = "SelectiveVpn.Activate"
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
            _viewModel.LogEntries.Add(new LogEntry(Loc.T("Msg.AutoConnectUiError")));
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
            _viewModel.LogEntries.Add(new LogEntry(Loc.T("Msg.RestoreUiError")));
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
        _viewModel.Dispose();
        AppTheme.ThemeChanged -= OnAppThemeChanged;
        AppLanguage.LanguageChanged -= OnLanguageChanged;
        _shutdownRequest?.Dispose();
        _activateRequest?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _trayIcon.Dispose();
        _trayMenu.Dispose();
        base.OnClosed(e);
    }

    private (
        Forms.NotifyIcon Tray,
        Forms.ContextMenuStrip Menu,
        Forms.ToolStripMenuItem Open,
        Forms.ToolStripMenuItem Connect,
        Forms.ToolStripMenuItem Disconnect,
        Forms.ToolStripMenuItem Exit) CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        var open = new Forms.ToolStripMenuItem(Loc.T("Ui.Open"), null, (_, _) => ShowFromTray());
        var connect = new Forms.ToolStripMenuItem(Loc.T("Ui.Connect"), null, async (_, _) => await RunTrayAction(_viewModel.ConnectFromUserAsync));
        var disconnect = new Forms.ToolStripMenuItem(Loc.T("Ui.Disconnect"), null, async (_, _) => await RunTrayAction(_viewModel.DisconnectFromUserAsync));
        var exit = new Forms.ToolStripMenuItem(Loc.T("Ui.Exit"), null, async (_, _) => await ExitFromTrayAsync())
        {
            Tag = "exit"
        };
        menu.Items.Add(open);
        menu.Items.Add(connect);
        menu.Items.Add(disconnect);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exit);

        var tray = new Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "SelectiveVpn",
            Visible = true,
            ContextMenuStrip = menu
        };
        tray.DoubleClick += (_, _) => ShowFromTray();
        return (tray, menu, open, connect, disconnect, exit);
    }

    private void ApplyTrayMenuTheme() => TrayMenuTheme.Apply(_trayMenu, _viewModel.ThemeId);

    private void RefreshTrayMenuTexts()
    {
        _trayOpen.Text = Loc.T("Ui.Open");
        _trayConnect.Text = Loc.T("Ui.Connect");
        _trayDisconnect.Text = Loc.T("Ui.Disconnect");
        _trayExit.Text = Loc.T("Ui.Exit");
    }

    private void OpenMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
            return;

        menu.DataContext = _viewModel;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void EditRuleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AppRule rule } || !rule.IsAddressList)
            return;

        _viewModel.SelectedApp = rule;
        if (_viewModel.EditAddressListCommand.CanExecute(null))
            _viewModel.EditAddressListCommand.Execute(null);
        e.Handled = true;
    }

    private void RemoveRuleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AppRule rule })
            return;

        _viewModel.SelectedApp = rule;
        if (_viewModel.RemoveCommand.CanExecute(null))
            _viewModel.RemoveCommand.Execute(null);
        e.Handled = true;
    }

    private void ToggleLogClick(object sender, RoutedEventArgs e)
    {
        var show = LogPanel.Visibility != Visibility.Visible;
        LogPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (sender is FrameworkElement toggle)
            toggle.ToolTip = Loc.T(show ? "Orbit.HideLog" : "Orbit.ShowLog");
        if (show && EventLogList.Items.Count > 0)
            EventLogList.ScrollIntoView(EventLogList.Items[EventLogList.Items.Count - 1]);
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
            Loc.T("Msg.TrayHint"),
            Forms.ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private async Task RunTrayAction(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception)
        {
            _viewModel.LogEntries.Add(new LogEntry(Loc.T("Msg.UiError")));
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
            if (_viewModel.IsConnected)
            {
                _exiting = false;
                _allowClose = false;
                _tray.Visible = true;
                ShowFromTray();
                return;
            }
        }
        catch (Exception)
        {
            _viewModel.LogEntries.Add(new LogEntry(Loc.T("Msg.StopTunnelFail")));
        }

        Close();
        System.Windows.Application.Current?.Shutdown();
    }
}
