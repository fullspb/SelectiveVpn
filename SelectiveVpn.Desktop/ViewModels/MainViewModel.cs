using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using SelectiveVpn.Abstractions;
using SelectiveVpn.Desktop.Abstractions;
using SelectiveVpn.Desktop.Services;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;

namespace SelectiveVpn.Desktop.ViewModels;

/// <summary>
/// Lean Avalonia MVP ViewModel: connect/disconnect, import Amnezia .conf, status/IP, tray.
/// Full WPF MainViewModel stays Windows-only; shared seam is IUiDispatcher + Core engine.
/// </summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IMihomoController _mihomo;
    private readonly IUiDispatcher _dispatcher;
    private readonly DesktopSettings _settings;
    private Window? _window;
    private bool _disposed;
    private bool _isBusy;
    private bool _isConnected;
    private bool _disconnecting;
    private string _statusText = "Disconnected";
    private string _statusKind = "disconnected";
    private string _profileName = "(no profile)";
    private string _configPath = "";
    private string _externalIp = "—";
    private string _engineLabel = "";
    private string _logLine = "";
    private string _privilegeStatus = "";
    private CancellationTokenSource? _connectCts;

    public MainViewModel(IMihomoController mihomo, IUiDispatcher dispatcher)
    {
        _mihomo = mihomo;
        _dispatcher = dispatcher;
        _mihomo.EngineMessage += OnEngineMessage;
        _mihomo.EngineStopped += OnEngineStopped;

        _settings = DesktopSettingsStore.Load(out _);
        // Hard rule for development: never auto-start VPN without Sergey's ok.
        _settings.ConnectOnLaunch = false;

        if (!string.IsNullOrWhiteSpace(_settings.ConfigPath) && File.Exists(_settings.ConfigPath))
        {
            _configPath = _settings.ConfigPath;
            _profileName = Path.GetFileNameWithoutExtension(_configPath);
        }

        _engineLabel = _mihomo.EngineId;
        RefreshPrivilegeStatus();

        ConnectCommand = new RelayCommand(ConnectAsync, () => !IsBusy && !IsConnected && HasConfig);
        DisconnectCommand = new RelayCommand(DisconnectAsync, () => !IsBusy && (IsConnected || _mihomo.IsRunning));
        ImportConfigCommand = new RelayCommand(ImportConfigAsync, () => !IsBusy);
        RefreshIpCommand = new RelayCommand(RefreshIpAsync, () => IsConnected && !IsBusy);
        ApplyCapabilitiesCommand = new RelayCommand(ApplyCapabilitiesAsync, () => !IsBusy);
        EmergencyTeardownCommand = new RelayCommand(EmergencyTeardownAsync, () => !IsBusy);
        ShowWindowCommand = new RelayCommand(ShowWindow);
        ExitCommand = new RelayCommand(ExitAsync);

        AppendLog("Linux Avalonia MVP ready. ConnectOnLaunch is off.");
        AppendLog($"Settings: {AppPaths.SettingsPath}");
        AppendLog(LinuxElevation.PrivilegeHint);
    }

    public ObservableCollection<string> LogEntries { get; } = [];

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ImportConfigCommand { get; }
    public ICommand RefreshIpCommand { get; }
    public ICommand ApplyCapabilitiesCommand { get; }
    public ICommand EmergencyTeardownCommand { get; }
    public ICommand ShowWindowCommand { get; }
    public ICommand ExitCommand { get; }

    public bool HasConfig => !string.IsNullOrWhiteSpace(ConfigPath) && File.Exists(ConfigPath);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                RaiseCommands();
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(ConnectionActionText));
                RaiseCommands();
            }
        }
    }

    public string ConnectionActionText => IsConnected ? "Disconnect" : "Connect";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string StatusKind
    {
        get => _statusKind;
        private set => SetProperty(ref _statusKind, value);
    }

    public string ProfileName
    {
        get => _profileName;
        private set => SetProperty(ref _profileName, value);
    }

    public string ConfigPath
    {
        get => _configPath;
        private set
        {
            if (SetProperty(ref _configPath, value))
            {
                OnPropertyChanged(nameof(HasConfig));
                RaiseCommands();
            }
        }
    }

    public string ExternalIp
    {
        get => _externalIp;
        private set => SetProperty(ref _externalIp, value);
    }

    public string EngineLabel
    {
        get => _engineLabel;
        private set => SetProperty(ref _engineLabel, value);
    }

    public string LogLine
    {
        get => _logLine;
        private set => SetProperty(ref _logLine, value);
    }

    public string PrivilegeStatus
    {
        get => _privilegeStatus;
        private set => SetProperty(ref _privilegeStatus, value);
    }

    public void AttachWindow(Window window) => _window = window;

    private void RefreshPrivilegeStatus()
    {
        PrivilegeStatus = LinuxElevation.CanConfigureTun()
            ? "Privileges: CAP_NET_ADMIN / root available"
            : "Privileges: need setcap on mihomo (or sudo). See Apply capabilities.";
    }

    private async Task ConnectAsync()
    {
        if (!HasConfig)
        {
            AppendLog("Import an Amnezia .conf first.");
            return;
        }

        RefreshPrivilegeStatus();
        if (!LinuxElevation.CanConfigureTun() && !File.Exists(AppPaths.EngineBinaryPath))
        {
            AppendLog("First connect will download mihomo. After that, run Apply capabilities (one pkexec).");
        }
        else if (!LinuxElevation.CanConfigureTun())
        {
            AppendLog("Warning: without CAP_NET_ADMIN, TUN configure will fail. Use Apply capabilities.");
        }

        IsBusy = true;
        StatusText = "Connecting…";
        StatusKind = "connecting";
        ExternalIp = "—";
        _connectCts = new CancellationTokenSource();
        try
        {
            // Empty targets → full tunnel (MATCH,VPN). Full-tunnel breaks direct browsing while up — expected.
            var request = new MihomoStartRequest(
                ConfigPath,
                Array.Empty<RouteTarget>(),
                string.IsNullOrWhiteSpace(_settings.EnginePath) ? null : _settings.EnginePath);

            var result = await _mihomo.StartAsync(request, _connectCts.Token);
            AppendLog(result.Message);
            IsConnected = result.Succeeded && _mihomo.IsRunning;
            if (IsConnected)
            {
                StatusText = "Connected (full tunnel)";
                StatusKind = "connected";
                Persist();
                _ = RefreshIpAsync();
            }
            else
            {
                StatusText = "Error";
                StatusKind = "error";
                LinuxTunnelCleanup.TryCleanup();
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Connect cancelled.");
            StatusText = "Disconnected";
            StatusKind = "disconnected";
            IsConnected = false;
        }
        catch (Exception ex)
        {
            AppendLog($"Connect error: {ex.Message}");
            StatusText = "Error";
            StatusKind = "error";
            IsConnected = false;
        }
        finally
        {
            _connectCts?.Dispose();
            _connectCts = null;
            IsBusy = _disconnecting;
            RaiseCommands();
        }
    }

    private async Task DisconnectAsync()
    {
        _disconnecting = true;
        IsBusy = true;
        StatusText = "Disconnecting…";
        try
        {
            _connectCts?.Cancel();
            if (_mihomo.IsRunning)
            {
                var result = await _mihomo.StopAsync();
                AppendLog(result.Message);
            }

            // Spike emergency cleanup: Meta + ip rules 9000–9010 (also done inside engine Stop).
            LinuxTunnelCleanup.TryCleanup();
            IsConnected = false;
            ExternalIp = "—";
            StatusText = "Disconnected";
            StatusKind = "disconnected";
        }
        catch (Exception ex)
        {
            AppendLog($"Disconnect error: {ex.Message}");
            StatusText = "Error";
            StatusKind = "error";
            LinuxTunnelCleanup.TryCleanup();
        }
        finally
        {
            _disconnecting = false;
            IsBusy = false;
            RaiseCommands();
        }
    }

    private async Task ImportConfigAsync()
    {
        if (_window is null)
            return;

        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import AmneziaWG config",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Amnezia / WireGuard")
                {
                    Patterns = ["*.conf", "*.awg"]
                },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });

        var file = files.FirstOrDefault();
        if (file is null)
            return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            AppendLog("Could not resolve selected file path.");
            return;
        }

        var read = AmneziaWgConfigReader.ReadFile(path);
        if (!read.Succeeded || read.Profile is null)
        {
            AppendLog($"Import failed: {read.Message}");
            StatusText = "Error";
            StatusKind = "error";
            return;
        }

        ConfigPath = path;
        ProfileName = Path.GetFileNameWithoutExtension(path);
        Persist();
        AppendLog($"Imported AmneziaWG profile «{ProfileName}» (endpoint {read.Profile.EndpointHost}).");
        StatusText = "Profile ready";
        StatusKind = "disconnected";
    }

    private async Task RefreshIpAsync()
    {
        if (!_mihomo.IsRunning)
            return;

        try
        {
            var result = await _mihomo.QueryExitIpAsync();
            Dispatch(() =>
            {
                if (result.Succeeded)
                {
                    ExternalIp = result.Message;
                    AppendLog($"Exit IP: {result.Message}");
                }
                else
                {
                    AppendLog($"IP lookup: {result.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            AppendLog($"IP lookup failed: {ex.Message}");
        }
    }

    private Task ApplyCapabilitiesAsync()
    {
        var result = LinuxElevation.TryApplyMihomoCapabilities(
            File.Exists(AppPaths.EngineBinaryPath) ? AppPaths.EngineBinaryPath : null);
        AppendLog(result.Message);
        RefreshPrivilegeStatus();
        // Note: setcap is on mihomo binary; the app process itself may still lack CapEff.
        // Capability on the executable is what matters when MihomoEngine starts it.
        if (result.Succeeded)
            AppendLog("mihomo binary now has caps — Connect should work without running the UI as root.");
        return Task.CompletedTask;
    }

    private Task EmergencyTeardownAsync()
    {
        var result = LinuxElevation.TryEmergencyTeardown();
        AppendLog(result.Message);
        IsConnected = false;
        ExternalIp = "—";
        StatusText = "Disconnected";
        StatusKind = "disconnected";
        RaiseCommands();
        return Task.CompletedTask;
    }

    private void ShowWindow()
    {
        if (_window is null)
            return;

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private async Task ExitAsync()
    {
        if (IsConnected || _mihomo.IsRunning)
            await DisconnectAsync();

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void Persist()
    {
        _settings.ConfigPath = ConfigPath;
        _settings.ConnectOnLaunch = false;
        DesktopSettingsStore.Save(_settings);
    }

    private void AppendLog(string message)
    {
        Dispatch(() =>
        {
            var line = $"{DateTime.Now:HH:mm:ss}  {message}";
            LogEntries.Insert(0, line);
            while (LogEntries.Count > 200)
                LogEntries.RemoveAt(LogEntries.Count - 1);
            LogLine = line;
        });
    }

    private void OnEngineMessage(object? sender, string message) => AppendLog(message);

    private void OnEngineStopped(object? sender, EventArgs e) => Dispatch(() =>
    {
        if (_disposed || _disconnecting)
            return;

        IsConnected = false;
        ExternalIp = "—";
        StatusText = "Engine stopped";
        StatusKind = "error";
        AppendLog("Mihomo process exited unexpectedly.");
        LinuxTunnelCleanup.TryCleanup();
        RaiseCommands();
    });

    private void RaiseCommands()
    {
        (ConnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DisconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ImportConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RefreshIpCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ApplyCapabilitiesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EmergencyTeardownCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void Dispatch(Action action)
    {
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.Post(action);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _connectCts?.Cancel();
        _mihomo.EngineMessage -= OnEngineMessage;
        _mihomo.EngineStopped -= OnEngineStopped;
        try
        {
            if (_mihomo.IsRunning)
                _mihomo.StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            LinuxTunnelCleanup.TryCleanup();
        }
    }
}
