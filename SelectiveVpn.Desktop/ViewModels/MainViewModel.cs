using System.Collections.ObjectModel;
using System.Diagnostics;
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
/// Avalonia Orbit-shaped ViewModel: mirrors WPF MainViewModel surface for layout/bindings
/// (profiles, include/except, status chrome, connect) while keeping Linux tunnel semantics
/// (auto-redirect, dns-mode disabled, MATCH,VPN for full/exclude-empty, no resolvectl flush).
/// Full WPF MainViewModel stays Windows-only (process rules, subscription UI, elevation).
/// </summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IMihomoController _mihomo;
    private readonly IUiDispatcher _dispatcher;
    private readonly DesktopSettings _settings;
    private readonly Stopwatch _connectedFor = new();
    private Window? _window;
    private bool _disposed;
    private bool _isBusy;
    private bool _isConnected;
    private bool _disconnecting;
    private bool _isLogVisible;
    private bool _exceptListed;
    private bool _suppressProfileSelect;
    private string _statusText = "Disconnected";
    private string _statusKind = "disconnected";
    private string _statusDetail = "";
    private string _profileName = "(no profile)";
    private string _configPath = "";
    private string _externalIp = "—";
    private string _connectedServer = "";
    private string _engineLabel = "";
    private string _logLine = "";
    private string _privilegeStatus = "";
    private string _newTargetText = "";
    private string _connectionDuration = "00:00:00";
    private string _incomingTraffic = "—";
    private string _outgoingTraffic = "—";
    private string? _selectedTarget;
    private VpnProfileOption? _selectedProfile;
    private CancellationTokenSource? _connectCts;
    private readonly System.Timers.Timer _durationTimer;

    public MainViewModel(IMihomoController mihomo, IUiDispatcher dispatcher)
    {
        _mihomo = mihomo;
        _dispatcher = dispatcher;
        _mihomo.EngineMessage += OnEngineMessage;
        _mihomo.EngineStopped += OnEngineStopped;

        _settings = DesktopSettingsStore.Load(out _);
        _settings.ConnectOnLaunch = false;
        _exceptListed = _settings.ExceptListed || _settings.FullTunnel;

        LoadProfilesFromSettings();
        _engineLabel = string.IsNullOrWhiteSpace(_mihomo.EngineId)
            ? "mihomo — downloads on connect"
            : _mihomo.EngineId;
        RefreshPrivilegeStatus();

        ConnectionActionCommand = new RelayCommand(ToggleConnectionAsync, () => !IsBusy || IsConnected || _connectCts is not null);
        ConnectCommand = new RelayCommand(ConnectAsync, () => !IsBusy && !IsConnected && HasConfig);
        DisconnectCommand = new RelayCommand(DisconnectAsync, () => !IsBusy && (IsConnected || _mihomo.IsRunning));
        ImportConfigCommand = new RelayCommand(ImportConfigAsync, () => !IsBusy);
        BrowseConfigCommand = ImportConfigCommand;
        RemoveProfileCommand = new RelayCommand(RemoveSelectedProfile, () => !IsBusy && SelectedProfile is not null);
        RefreshIpCommand = new RelayCommand(RefreshIpAsync, () => IsConnected && !IsBusy);
        ApplyCapabilitiesCommand = new RelayCommand(ApplyCapabilitiesAsync, () => !IsBusy);
        EmergencyTeardownCommand = new RelayCommand(EmergencyTeardownAsync, () => !IsBusy);
        AddTargetCommand = new RelayCommand(AddTarget, () => !IsBusy && !string.IsNullOrWhiteSpace(NewTargetText));
        RemoveTargetCommand = new RelayCommand(RemoveTarget, () => !IsBusy && SelectedTarget is not null);
        ReconnectCommand = new RelayCommand(ReconnectAsync, () => !IsBusy && HasConfig);
        ToggleLogCommand = new RelayCommand(() => IsLogVisible = !IsLogVisible);
        ShowWindowCommand = new RelayCommand(ShowWindow);
        ExitCommand = new RelayCommand(ExitAsync);
        CycleThemeCommand = new RelayCommand(CycleTheme);

        _durationTimer = new System.Timers.Timer(1000);
        _durationTimer.Elapsed += (_, _) => Dispatch(UpdateDuration);
        _durationTimer.Start();

        AppendLog("Orbit Avalonia UI ready. ConnectOnLaunch is off.");
        AppendLog($"Settings: {AppPaths.SettingsPath}");
        AppendLog(_exceptListed
            ? "Mode: all traffic via VPN except listed DOMAIN/IP (exclude)."
            : "Mode: selective include — empty list → MATCH,DIRECT (Linux browsing fix).");
        AppendLog(LinuxElevation.PrivilegeHint);
        RefreshOrbitLabels();
    }

    public ObservableCollection<string> LogEntries { get; } = [];

    public ObservableCollection<string> RouteTargets { get; } = [];

    public ObservableCollection<VpnProfileOption> Profiles { get; } = [];

    public ICommand ConnectionActionCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ImportConfigCommand { get; }
    public ICommand BrowseConfigCommand { get; }
    public ICommand RemoveProfileCommand { get; }
    public ICommand RefreshIpCommand { get; }
    public ICommand ApplyCapabilitiesCommand { get; }
    public ICommand EmergencyTeardownCommand { get; }
    public ICommand AddTargetCommand { get; }
    public ICommand RemoveTargetCommand { get; }
    public ICommand ReconnectCommand { get; }
    public ICommand ToggleLogCommand { get; }
    public ICommand ShowWindowCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand CycleThemeCommand { get; }

    public string VersionLabel { get; } = "0.1.25";

    public bool HasConfig => !string.IsNullOrWhiteSpace(ConfigPath) && File.Exists(ConfigPath);

    public bool HasProfile => Profiles.Count > 0;

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
                OnPropertyChanged(nameof(ConnectionActionCommand));
                OnPropertyChanged(nameof(ConnectionStatusTitle));
                RefreshRunningFlags();
                RaiseCommands();
            }
        }
    }

    public bool IsLogVisible
    {
        get => _isLogVisible;
        set => SetProperty(ref _isLogVisible, value);
    }

    /// <summary>0 = include (selected via VPN), 1 = exclude (all via VPN except selected).</summary>
    public int RoutingModeIndex
    {
        get => _exceptListed ? 1 : 0;
        set
        {
            var except = value == 1;
            if (_exceptListed == except)
                return;
            PersistActiveList();
            _exceptListed = except;
            OnPropertyChanged();
            LoadActiveList();
            RefreshOrbitLabels();
            Persist();
            AppendLog(except
                ? "Exclude mode: listed DOMAIN/IP stay direct; rest MATCH,VPN."
                : "Include mode: listed DOMAIN/IP via VPN; rest DIRECT (empty → DIRECT on Linux).");
        }
    }

    public bool FullTunnel => _exceptListed && RouteTargets.Count == 0;

    public string ConnectionActionText => _disconnecting ? "Disconnecting…"
        : _connectCts is not null ? "Connecting…"
        : IsConnected ? "Disconnect"
        : "Connect";

    public string ConnectionStatusTitle => StatusKind switch
    {
        "connected" => "Connected",
        "connecting" => "Connecting…",
        "error" => "Error",
        _ => "Disconnected"
    };

    public string ConnectionProfileName => string.IsNullOrWhiteSpace(ProfileName) || ProfileName == "(no profile)"
        ? "Choose a profile"
        : ProfileName;

    public string ConnectionProtocol => "AmneziaWG";

    public string ConnectionSubtitle => $"{ConnectionProfileName} · {ConnectionProtocol}";

    public string RoutingHint => _exceptListed
        ? "Exclude: listed DOMAIN/IP → DIRECT; everything else via VPN. Empty list = full tunnel (MATCH,VPN)."
        : "Include: listed DOMAIN/IP → VPN; rest DIRECT. Empty list stays MATCH,DIRECT (Linux browsing fix).";

    public string ListedRouteLabel => _exceptListed ? "Bypass VPN" : "Through VPN";

    public string TargetCount => $"{RouteTargets.Count} in list";

    public string RoutingSummary => _exceptListed
        ? (RouteTargets.Count == 0
            ? "All traffic goes through VPN."
            : "Listed addresses go direct. Other traffic goes through VPN.")
        : (RouteTargets.Count == 0
            ? "List empty — traffic stays direct until you add domains/IPs."
            : "Listed addresses go through VPN. Other traffic goes direct.");

    public string RulesProfileLabel => HasProfile
        ? $"Rules for profile {ProfileName}"
        : "Import a profile to edit rules.";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string StatusKind
    {
        get => _statusKind;
        private set
        {
            if (SetProperty(ref _statusKind, value))
            {
                OnPropertyChanged(nameof(ConnectionStatusTitle));
                OnPropertyChanged(nameof(HeroAccentBrushKey));
            }
        }
    }

    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetProperty(ref _statusDetail, value);
    }

    public string HeroAccentBrushKey => StatusKind switch
    {
        "connected" => "HeroActive",
        "connecting" => "HeroBusy",
        "error" => "DangerForeground",
        _ => "HeroIdle"
    };

    public string ProfileName
    {
        get => _profileName;
        private set
        {
            if (SetProperty(ref _profileName, value))
            {
                OnPropertyChanged(nameof(ConnectionProfileName));
                OnPropertyChanged(nameof(ConnectionSubtitle));
                OnPropertyChanged(nameof(RulesProfileLabel));
            }
        }
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

    public string ConnectedServer
    {
        get => _connectedServer;
        private set => SetProperty(ref _connectedServer, value);
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

    public string NewTargetText
    {
        get => _newTargetText;
        set
        {
            if (SetProperty(ref _newTargetText, value))
                (AddTargetCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value))
                (RemoveTargetCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public VpnProfileOption? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (_suppressProfileSelect)
            {
                SetProperty(ref _selectedProfile, value);
                return;
            }

            if (ReferenceEquals(_selectedProfile, value))
                return;

            PersistActiveList();
            if (!SetProperty(ref _selectedProfile, value))
                return;

            if (value is null)
            {
                ConfigPath = "";
                ProfileName = "(no profile)";
                RouteTargets.Clear();
                RefreshOrbitLabels();
                Persist();
                return;
            }

            ConfigPath = value.ConfigPath;
            ProfileName = value.Name;
            LoadActiveList();
            RefreshActiveFlags();
            RefreshOrbitLabels();
            Persist();
            RaiseCommands();
        }
    }

    public string ConnectionDuration
    {
        get => _connectionDuration;
        private set => SetProperty(ref _connectionDuration, value);
    }

    public string IncomingTraffic
    {
        get => _incomingTraffic;
        private set => SetProperty(ref _incomingTraffic, value);
    }

    public string OutgoingTraffic
    {
        get => _outgoingTraffic;
        private set => SetProperty(ref _outgoingTraffic, value);
    }

    public string ThemeDisplayName => OrbitTheme.CurrentTheme switch
    {
        OrbitTheme.Aurora => "Aurora",
        OrbitTheme.Ocean => "Ocean",
        OrbitTheme.Plum => "Plum",
        OrbitTheme.Sunset => "Sunset",
        OrbitTheme.Graphite => "Graphite",
        _ => "Orbit"
    };

    public void AttachWindow(Window window) => _window = window;

    private void LoadProfilesFromSettings()
    {
        Profiles.Clear();
        foreach (var saved in _settings.Profiles)
        {
            if (string.IsNullOrWhiteSpace(saved.ConfigPath) || !File.Exists(saved.ConfigPath))
                continue;
            Profiles.Add(new VpnProfileOption(saved.ConfigPath));
        }

        // Legacy single ConfigPath / IncludeTargets.
        if (Profiles.Count == 0 && !string.IsNullOrWhiteSpace(_settings.ConfigPath) && File.Exists(_settings.ConfigPath))
            Profiles.Add(new VpnProfileOption(_settings.ConfigPath));

        OnPropertyChanged(nameof(HasProfile));

        var activePath = _settings.ConfigPath;
        var match = Profiles.FirstOrDefault(p => SamePath(p.ConfigPath, activePath))
                    ?? Profiles.FirstOrDefault();
        _suppressProfileSelect = true;
        SelectedProfile = match;
        _suppressProfileSelect = false;
        if (match is not null)
        {
            ConfigPath = match.ConfigPath;
            ProfileName = match.Name;
            LoadActiveList();
            RefreshActiveFlags();
        }
        else if (!string.IsNullOrWhiteSpace(_settings.ConfigPath) && File.Exists(_settings.ConfigPath))
        {
            ConfigPath = _settings.ConfigPath;
            ProfileName = Path.GetFileNameWithoutExtension(_settings.ConfigPath);
            foreach (var t in ActiveTargetSource())
                RouteTargets.Add(t);
        }
    }

    private IEnumerable<string> ActiveTargetSource()
    {
        var profile = FindSaved(ConfigPath);
        if (profile is not null)
            return (_exceptListed ? profile.ExcludeTargets : profile.IncludeTargets) ?? [];
        return (_exceptListed ? _settings.ExcludeTargets : _settings.IncludeTargets) ?? [];
    }

    private void LoadActiveList()
    {
        RouteTargets.Clear();
        foreach (var target in ActiveTargetSource())
        {
            if (!string.IsNullOrWhiteSpace(target))
                RouteTargets.Add(target);
        }
        SelectedTarget = null;
    }

    private void PersistActiveList()
    {
        var snapshot = RouteTargets.ToList();
        var profile = FindSaved(ConfigPath);
        if (profile is not null)
        {
            if (_exceptListed)
                profile.ExcludeTargets = snapshot;
            else
                profile.IncludeTargets = snapshot;
        }

        if (_exceptListed)
            _settings.ExcludeTargets = snapshot;
        else
            _settings.IncludeTargets = snapshot;
    }

    private SavedProfile? FindSaved(string? path)
        => _settings.Profiles.FirstOrDefault(p => SamePath(p.ConfigPath, path));

    private static bool SamePath(string? a, string? b)
        => !string.IsNullOrWhiteSpace(a)
           && !string.IsNullOrWhiteSpace(b)
           && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private void RefreshPrivilegeStatus()
    {
        PrivilegeStatus = LinuxElevation.CanConfigureTun()
            ? "Privileges: CAP_NET_ADMIN / root available"
            : "Privileges: need setcap on mihomo (or sudo). See Apply capabilities.";
    }

    private void RefreshOrbitLabels()
    {
        OnPropertyChanged(nameof(RoutingHint));
        OnPropertyChanged(nameof(ListedRouteLabel));
        OnPropertyChanged(nameof(TargetCount));
        OnPropertyChanged(nameof(RoutingSummary));
        OnPropertyChanged(nameof(RulesProfileLabel));
        OnPropertyChanged(nameof(FullTunnel));
        OnPropertyChanged(nameof(HasProfile));
    }

    private void RefreshActiveFlags()
    {
        foreach (var profile in Profiles)
            profile.IsActive = SamePath(profile.ConfigPath, ConfigPath);
    }

    private void RefreshRunningFlags()
    {
        foreach (var profile in Profiles)
            profile.IsRunning = IsConnected && SamePath(profile.ConfigPath, ConfigPath);
    }

    private void AddTarget()
    {
        if (!RouteAddress.TryParse(NewTargetText, out var normalized, out var kind, out var error))
        {
            AppendLog($"Bad target: {error}");
            return;
        }

        if (RouteTargets.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            AppendLog($"Already listed: {normalized}");
            return;
        }

        RouteTargets.Add(normalized);
        NewTargetText = "";
        PersistActiveList();
        Persist();
        RefreshOrbitLabels();
        AppendLog($"Added {kind}: {normalized}");
    }

    private void RemoveTarget()
    {
        if (SelectedTarget is null)
            return;

        var removed = SelectedTarget;
        RouteTargets.Remove(removed);
        SelectedTarget = null;
        PersistActiveList();
        Persist();
        RefreshOrbitLabels();
        AppendLog($"Removed: {removed}");
    }

    private void RemoveSelectedProfile()
    {
        if (SelectedProfile is null)
            return;

        var removing = SelectedProfile.ConfigPath;
        var name = SelectedProfile.Name;
        _settings.Profiles.RemoveAll(p => SamePath(p.ConfigPath, removing));
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.FirstOrDefault();
        if (SelectedProfile is null)
        {
            ConfigPath = "";
            ProfileName = "(no profile)";
            RouteTargets.Clear();
        }

        Persist();
        RefreshOrbitLabels();
        AppendLog($"Profile “{name}” removed from the list. The config file on disk was not deleted.");
        RaiseCommands();
    }

    private RouteTarget[] BuildTargets()
    {
        var list = new List<RouteTarget>();
        foreach (var entry in RouteTargets)
        {
            if (!RouteAddress.TryParse(entry, out var normalized, out var kind, out _))
                continue;
            list.Add(new RouteTarget(normalized, Kind: kind));
        }

        return list.ToArray();
    }

    private async Task ToggleConnectionAsync()
    {
        if (IsConnected || _mihomo.IsRunning || _disconnecting)
            await DisconnectAsync();
        else
            await ConnectAsync();
    }

    private async Task ReconnectAsync()
    {
        if (IsConnected || _mihomo.IsRunning)
            await DisconnectAsync();
        await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        if (!HasConfig)
        {
            AppendLog("Import an Amnezia .conf first.");
            StatusDetail = "No profile selected.";
            return;
        }

        RefreshPrivilegeStatus();
        if (!LinuxElevation.CanConfigureTun() && !File.Exists(AppPaths.EngineBinaryPath))
            AppendLog("First connect will download mihomo. After that, run Apply capabilities (one pkexec).");
        else if (!LinuxElevation.CanConfigureTun())
            AppendLog("Warning: without CAP_NET_ADMIN, TUN configure will fail. Use Apply capabilities.");

        IsBusy = true;
        StatusText = "Connecting…";
        StatusKind = "connecting";
        StatusDetail = "";
        ExternalIp = "—";
        IncomingTraffic = "—";
        OutgoingTraffic = "—";
        OnPropertyChanged(nameof(ConnectionActionText));
        _connectCts = new CancellationTokenSource();
        try
        {
            var selectiveTargets = BuildTargets();
            // Exclude-empty ⇒ full tunnel (MATCH,VPN). Include-empty ⇒ MATCH,DIRECT (Linux fix).
            var fullTunnelWhenEmpty = _exceptListed;
            var targets = selectiveTargets;
            AppendLog(_exceptListed
                ? selectiveTargets.Length == 0
                    ? "Connecting exclude/full tunnel (MATCH,VPN)."
                    : $"Connecting exclude ({selectiveTargets.Length} DIRECT, rest VPN)."
                : selectiveTargets.Length == 0
                    ? "Connecting selective include (empty → MATCH,DIRECT)."
                    : $"Connecting selective include ({selectiveTargets.Length} via VPN, rest DIRECT).");

            var request = new MihomoStartRequest(
                ConfigPath,
                targets,
                string.IsNullOrWhiteSpace(_settings.EnginePath) ? null : _settings.EnginePath,
                ExceptListed: _exceptListed,
                FullTunnelWhenEmpty: fullTunnelWhenEmpty);

            var result = await _mihomo.StartAsync(request, _connectCts.Token);
            AppendLog(result.Message);
            IsConnected = result.Succeeded && _mihomo.IsRunning;
            if (IsConnected)
            {
                _connectedFor.Restart();
                TryReadEndpoint();
                StatusText = ConnectionStatusTitle;
                StatusKind = "connected";
                StatusDetail = "";
                Persist();
                _ = RefreshIpAsync();
            }
            else
            {
                StatusText = "Error";
                StatusKind = "error";
                StatusDetail = result.Message;
                LinuxTunnelCleanup.TryCleanup();
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Connect cancelled.");
            StatusText = "Disconnected";
            StatusKind = "disconnected";
            StatusDetail = "";
            IsConnected = false;
        }
        catch (Exception ex)
        {
            AppendLog($"Connect error: {ex.Message}");
            StatusText = "Error";
            StatusKind = "error";
            StatusDetail = ex.Message;
            IsConnected = false;
        }
        finally
        {
            _connectCts?.Dispose();
            _connectCts = null;
            IsBusy = _disconnecting;
            OnPropertyChanged(nameof(ConnectionActionText));
            RaiseCommands();
        }
    }

    private void TryReadEndpoint()
    {
        try
        {
            var read = AmneziaWgConfigReader.ReadFile(ConfigPath);
            ConnectedServer = read.Succeeded && read.Profile is not null
                ? read.Profile.EndpointHost
                : "";
        }
        catch
        {
            ConnectedServer = "";
        }
    }

    private async Task DisconnectAsync()
    {
        _disconnecting = true;
        IsBusy = true;
        StatusText = "Disconnecting…";
        OnPropertyChanged(nameof(ConnectionActionText));
        try
        {
            _connectCts?.Cancel();
            if (_mihomo.IsRunning)
            {
                var result = await _mihomo.StopAsync();
                AppendLog(result.Message);
            }

            LinuxTunnelCleanup.TryCleanup();
            IsConnected = false;
            ExternalIp = "—";
            ConnectedServer = "";
            IncomingTraffic = "—";
            OutgoingTraffic = "—";
            _connectedFor.Reset();
            ConnectionDuration = "00:00:00";
            StatusText = "Disconnected";
            StatusKind = "disconnected";
            StatusDetail = "";
        }
        catch (Exception ex)
        {
            AppendLog($"Disconnect error: {ex.Message}");
            StatusText = "Error";
            StatusKind = "error";
            StatusDetail = ex.Message;
            LinuxTunnelCleanup.TryCleanup();
        }
        finally
        {
            _disconnecting = false;
            IsBusy = false;
            OnPropertyChanged(nameof(ConnectionActionText));
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
            StatusDetail = read.Message;
            return;
        }

        var fullPath = Path.GetFullPath(path);
        var existing = Profiles.FirstOrDefault(p => SamePath(p.ConfigPath, fullPath));
        if (existing is not null)
        {
            SelectedProfile = existing;
            AppendLog($"Profile “{existing.Name}” was already in the list — selected again.");
            return;
        }

        PersistActiveList();
        _settings.Profiles.Add(new SavedProfile { ConfigPath = fullPath, IncludeTargets = [], ExcludeTargets = [] });
        var option = new VpnProfileOption(fullPath);
        Profiles.Add(option);
        SelectedProfile = option;
        ConnectedServer = read.Profile.EndpointHost;
        Persist();
        RefreshOrbitLabels();
        AppendLog($"Imported AmneziaWG profile «{option.Name}» (endpoint {read.Profile.EndpointHost}).");
        StatusText = "Profile ready";
        StatusKind = "disconnected";
        StatusDetail = "";
        RaiseCommands();
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
        ConnectedServer = "";
        _connectedFor.Reset();
        ConnectionDuration = "00:00:00";
        StatusText = "Disconnected";
        StatusKind = "disconnected";
        StatusDetail = "";
        RaiseCommands();
        return Task.CompletedTask;
    }

    private void CycleTheme()
    {
        var all = OrbitTheme.All;
        var index = Array.FindIndex(all, t => t.Equals(OrbitTheme.CurrentTheme, StringComparison.OrdinalIgnoreCase));
        var next = all[(index + 1 + all.Length) % all.Length];
        OrbitTheme.Apply(Avalonia.Application.Current, next);
        _settings.Theme = next;
        Persist();
        OnPropertyChanged(nameof(ThemeDisplayName));
        AppendLog($"Theme: {ThemeDisplayName}");
    }

    private void UpdateDuration()
    {
        if (!IsConnected || !_connectedFor.IsRunning)
        {
            if (!IsConnected)
                ConnectionDuration = "00:00:00";
            return;
        }

        ConnectionDuration = _connectedFor.Elapsed.ToString(@"hh\:mm\:ss");
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
        PersistActiveList();
        _settings.ConfigPath = ConfigPath;
        _settings.ConnectOnLaunch = false;
        _settings.ExceptListed = _exceptListed;
        _settings.FullTunnel = FullTunnel;
        _settings.Theme = OrbitTheme.CurrentTheme;
        if (!string.IsNullOrWhiteSpace(ConfigPath) && FindSaved(ConfigPath) is null && File.Exists(ConfigPath))
            _settings.Profiles.Add(new SavedProfile { ConfigPath = ConfigPath, IncludeTargets = [], ExcludeTargets = [] });
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
        ConnectedServer = "";
        _connectedFor.Reset();
        ConnectionDuration = "00:00:00";
        StatusText = "Engine stopped";
        StatusKind = "error";
        StatusDetail = "Mihomo process exited unexpectedly.";
        AppendLog("Mihomo process exited unexpectedly.");
        LinuxTunnelCleanup.TryCleanup();
        RaiseCommands();
    });

    private void RaiseCommands()
    {
        (ConnectionActionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ConnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DisconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ImportConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RemoveProfileCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RefreshIpCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ApplyCapabilitiesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EmergencyTeardownCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddTargetCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RemoveTargetCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ReconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
        _durationTimer.Stop();
        _durationTimer.Dispose();
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
