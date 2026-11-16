using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using SelectiveVpn.Abstractions;
using SelectiveVpn.Desktop.Abstractions;
using SelectiveVpn.Desktop.Models;
using SelectiveVpn.Desktop.Services;
using SelectiveVpn.Desktop.Views;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;

namespace SelectiveVpn.Desktop.ViewModels;

/// <summary>
/// Avalonia Orbit ViewModel: profiles, include/except (DOMAIN/IP + PROCESS-PATH),
/// subscription import/server picker, live Mihomo traffic counters, live rule reload,
/// Linux tunnel semantics (auto-redirect, dns-mode disabled, MATCH,VPN for full/exclude-empty,
/// MATCH,DIRECT for include-empty, no resolvectl flush). ConnectOnLaunch stays off.
/// </summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IMihomoController _mihomo;
    private readonly IUiDispatcher _dispatcher;
    private readonly DesktopSettings _settings;
    private readonly Stopwatch _connectedFor = new();
    private readonly Dictionary<string, Dictionary<string, int>> _nodeDelayCache = new(StringComparer.OrdinalIgnoreCase);
    private Window? _window;
    private bool _disposed;
    private bool _isBusy;
    private bool _isConnected;
    private bool _disconnecting;
    private bool _isLogVisible;
    private bool _exceptListed;
    private bool _suppressProfileSelect;
    private string _statusText = UiLoc.T("Orbit.Disconnected");
    private string _statusKind = "disconnected";
    private string _statusDetail = "";
    private string _profileName = UiLoc.T("Ui.NoProfile");
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
    private RouteRuleItem? _selectedTarget;
    private VpnProfileOption? _selectedProfile;
    private CancellationTokenSource? _connectCts;
    private CancellationTokenSource? _trafficCts;
    private long _sessionGeneration;
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
        UiLoc.Apply(string.Equals(_settings.Language, "auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : _settings.Language);
        UiLoc.LanguageChanged += OnLanguageChanged;

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
        ImportSubscriptionCommand = new RelayCommand(ImportSubscriptionAsync, () => !IsBusy);
        ManageServersCommand = new RelayCommand(ManageServersAsync, () => !IsBusy && IsSubscriptionProfile);
        RemoveProfileCommand = new RelayCommand(RemoveSelectedProfile, () => !IsBusy && SelectedProfile is not null);
        RefreshIpCommand = new RelayCommand(RefreshIpAsync, () => IsConnected && !IsBusy);
        ApplyCapabilitiesCommand = new RelayCommand(ApplyCapabilitiesAsync, () => !IsBusy);
        EmergencyTeardownCommand = new RelayCommand(EmergencyTeardownAsync, () => !IsBusy);
        AddTargetCommand = new RelayCommand(AddTarget, () => !IsBusy && !string.IsNullOrWhiteSpace(NewTargetText));
        RemoveTargetCommand = new RelayCommand(RemoveTarget, () => !IsBusy && SelectedTarget is not null);
        AddProcessCommand = new RelayCommand(() => AddFromPickerAsync(AppPickerSource.RunningProcesses), () => !IsBusy);
        AddAppCommand = new RelayCommand(() => AddFromPickerAsync(AppPickerSource.DesktopApps), () => !IsBusy);
        AddFolderCommand = new RelayCommand(AddFolderAsync, () => !IsBusy);
        AddAddressListCommand = new RelayCommand(AddAddressListAsync, () => !IsBusy);
        SetIncludeModeCommand = new RelayCommand(() => RoutingModeIndex = 0);
        SetExcludeModeCommand = new RelayCommand(() => RoutingModeIndex = 1);
        ReconnectCommand = new RelayCommand(ReconnectAsync, () => !IsBusy && HasConfig);
        ToggleLogCommand = new RelayCommand(() => IsLogVisible = !IsLogVisible);
        ShowWindowCommand = new RelayCommand(ShowWindow);
        ExitCommand = new RelayCommand(ExitAsync);
        CycleThemeCommand = new RelayCommand(CycleTheme);
        CycleLanguageCommand = new RelayCommand(CycleLanguage);

        _durationTimer = new System.Timers.Timer(1000);
        _durationTimer.Elapsed += (_, _) => Dispatch(UpdateDuration);
        _durationTimer.Start();

        AppendLog("Orbit Avalonia UI ready. ConnectOnLaunch is off.");
        AppendLog($"Settings: {AppPaths.SettingsPath}");
        AppendLog(_exceptListed ? UiLoc.T("Log.ModeExclude") : UiLoc.T("Log.ModeInclude"));
        AppendLog(LinuxElevation.PrivilegeHint);
        RefreshOrbitLabels();
    }

    public ObservableCollection<string> LogEntries { get; } = [];

    public ObservableCollection<RouteRuleItem> RouteTargets { get; } = [];

    public ObservableCollection<VpnProfileOption> Profiles { get; } = [];

    public ICommand ConnectionActionCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ImportConfigCommand { get; }
    public ICommand BrowseConfigCommand { get; }
    public ICommand ImportSubscriptionCommand { get; }
    public ICommand ManageServersCommand { get; }
    public ICommand RemoveProfileCommand { get; }
    public ICommand RefreshIpCommand { get; }
    public ICommand ApplyCapabilitiesCommand { get; }
    public ICommand EmergencyTeardownCommand { get; }
    public ICommand AddTargetCommand { get; }
    public ICommand RemoveTargetCommand { get; }
    public ICommand AddProcessCommand { get; }
    public ICommand AddAppCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand AddAddressListCommand { get; }
    public ICommand SetIncludeModeCommand { get; }
    public ICommand SetExcludeModeCommand { get; }
    public ICommand ReconnectCommand { get; }
    public ICommand ToggleLogCommand { get; }
    public ICommand ShowWindowCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand CycleThemeCommand { get; }
    public ICommand CycleLanguageCommand { get; }

    public string VersionLabel { get; } = "0.1.25";

    public bool HasConfig => !string.IsNullOrWhiteSpace(ConfigPath) && File.Exists(ConfigPath);

    public bool HasProfile => Profiles.Count > 0;

    public bool IsSubscriptionProfile => SubscriptionProfile.IsSubscriptionPath(ConfigPath);

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
            OnPropertyChanged(nameof(IsIncludeMode));
            OnPropertyChanged(nameof(IsExcludeMode));
            LoadActiveList();
            RefreshOrbitLabels();
            Persist();
            AppendLog(except ? UiLoc.T("Log.ModeExclude") : UiLoc.T("Log.ModeInclude"));
            _ = ApplyLiveRoutingAsync();
        }
    }

    public bool IsIncludeMode => !_exceptListed;

    public bool IsExcludeMode => _exceptListed;

    public bool HasRules => RouteTargets.Count > 0;

    public bool FullTunnel => _exceptListed && RouteTargets.Count == 0;

    public string ConnectionActionText => _disconnecting ? UiLoc.T("Orbit.Stopping")
        : _connectCts is not null ? UiLoc.T("Orbit.Connecting")
        : IsConnected ? UiLoc.T("Ui.Disconnect")
        : UiLoc.T("Ui.Connect");

    public string ConnectionStatusTitle => StatusKind switch
    {
        "connected" => UiLoc.T("Orbit.Connected"),
        "connecting" => UiLoc.T("Orbit.Connecting"),
        "error" => UiLoc.T("Orbit.Error"),
        _ => UiLoc.T("Orbit.Disconnected")
    };

    public string ConnectionProfileName => !HasProfile || string.IsNullOrWhiteSpace(ProfileName)
        ? UiLoc.T("Orbit.ChooseProfile")
        : ProfileName;

    public string ConnectionProtocol => IsSubscriptionProfile ? UiLoc.T("Orbit.Subscription") : "AmneziaWG";

    public string ConnectionSubtitle => $"{ConnectionProfileName} · {ConnectionProtocol}";

    public string RoutingHint => _exceptListed
        ? UiLoc.T("Hint.ExceptListed")
        : UiLoc.T("Hint.IncludeListed");

    public string ListedRouteLabel => _exceptListed ? UiLoc.T("Orbit.Direct") : UiLoc.T("Orbit.ThroughVpn");

    public string TargetCount => UiLoc.Tf("Orbit.TargetCount", RouteTargets.Count);

    public string RoutingSummary => RouteTargets.Count == 0
        ? (_exceptListed ? UiLoc.T("Orbit.AllTraffic") : UiLoc.T("Orbit.IncludeEmpty"))
        : (_exceptListed ? UiLoc.T("Orbit.RemainingVpn") : UiLoc.T("Orbit.RemainingDirect"));

    public string EmptyRulesText => UiLoc.T("Orbit.EmptyRules");

    public string RulesProfileLabel => HasProfile
        ? UiLoc.Tf("Orbit.RulesFor", ProfileName)
        : UiLoc.T("Orbit.ChooseProfile");

    // Chrome / tray labels (bound from AXAML).
    public string ProfilesEyebrow => UiLoc.T("Ui.Profiles");
    public string ConnectionEyebrow => UiLoc.T("Orbit.ConnectionEyebrow");
    public string RoutingEyebrow => UiLoc.T("Orbit.RoutingEyebrow");
    public string RoutingTitle => UiLoc.T("Orbit.RoutingTitle");
    public string ModeIncludeLabel => UiLoc.T("Orbit.ModeInclude");
    public string ModeExcludeLabel => UiLoc.T("Orbit.ModeExclude");
    public string SeparateListsHint => UiLoc.T("Orbit.SeparateListsHint");
    public string ThroughVpnLabel => UiLoc.T("Orbit.ThroughVpn");
    public string DirectLabel => UiLoc.T("Orbit.Direct");
    public string ServerLabel => UiLoc.T("Orbit.Server");
    public string ExternalIpLabel => UiLoc.T("Orbit.ExternalIp");
    public string DurationLabel => UiLoc.T("Orbit.ConnectionDuration");
    public string TrafficLabel => UiLoc.T("Orbit.Traffic");
    public string ReceivedLabel => UiLoc.T("Orbit.Received");
    public string SentLabel => UiLoc.T("Orbit.Sent");
    public string LogLabel => UiLoc.T("Orbit.Log");
    public string ShowLogTip => UiLoc.T("Orbit.ShowLog");
    public string HideLogTip => UiLoc.T("Orbit.HideLog");
    public string ReconnectText => UiLoc.T("Orbit.Reconnect");
    public string AddText => UiLoc.T("Ui.Add");
    public string RemoveText => UiLoc.T("Ui.Remove");
    public string AddProcessText => UiLoc.T("Ui.AddProcess");
    public string AddAppText => UiLoc.T("Ui.AddApp");
    public string AddFolderText => UiLoc.T("Ui.AddFolder");
    public string AddListText => UiLoc.T("Ui.AddList");
    public string AddTargetsText => UiLoc.T("Orbit.AddTargets");
    public string AddProcessTip => UiLoc.T("Ui.AddProcessTip");
    public string AddAppTip => UiLoc.T("Ui.AddAppTip");
    public string AddFolderTip => UiLoc.T("Ui.AddFolderTip");
    public string AddressWatermark => UiLoc.T("Ui.AddressWatermark");
    public string AddressPromptTitle => UiLoc.T("Ui.AddressPromptTitle");
    public string AddProfileText => UiLoc.T("Ui.AddProfile");
    public string FromFileText => UiLoc.T("Orbit.FromFile");
    public string ByLinkText => UiLoc.T("Orbit.ByLink");
    public string AddAmneziaText => UiLoc.T("Ui.AddProfile");
    public string AddByLinkText => UiLoc.T("Ui.AddByLink");
    public string ImportLinkText => UiLoc.T("Ui.ImportLink");
    public string ChooseServerText => UiLoc.T("Orbit.ManageServers");
    public string ApplyCapsText => UiLoc.T("Ui.ApplyCaps");
    public string ApplyCapsTip => UiLoc.T("Ui.ApplyCapsTip");
    public string TeardownText => UiLoc.T("Ui.Teardown");
    public string TeardownTip => UiLoc.T("Ui.TeardownTip");
    public string LinuxToolsText => UiLoc.T("Ui.LinuxTools");
    public string RemoveProfileTip => UiLoc.T("Ui.RemoveProfile");
    public string ProfileMenuTip => UiLoc.T("Orbit.ProfileMenu");
    public string RefreshTip => UiLoc.T("Ui.Refresh");
    public string CycleThemeTip => UiLoc.T("Ui.CycleTheme");
    public string LanguageDisplayName => UiLoc.CurrentCode == UiLoc.Russian ? "RU" : "EN";
    public string LanguageTip => UiLoc.T("Ui.Language");
    public string TrayShowText => UiLoc.T("Ui.Show");
    public string TrayQuitText => UiLoc.T("Ui.Exit");
    public string RemoveRuleTip => UiLoc.T("Orbit.RemoveRule");
    public string CancelText => UiLoc.T("Ui.Cancel");

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
                OnPropertyChanged(nameof(IsSubscriptionProfile));
                OnPropertyChanged(nameof(ConnectionProtocol));
                OnPropertyChanged(nameof(ConnectionSubtitle));
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

    public RouteRuleItem? SelectedTarget
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
                ProfileName = UiLoc.T("Ui.NoProfile");
                RouteTargets.Clear();
                ConnectedServer = "";
                RefreshOrbitLabels();
                Persist();
                return;
            }

            ConfigPath = value.ConfigPath;
            ProfileName = value.Name;
            LoadActiveList();
            RefreshActiveFlags();
            RefreshConnectedServerLabel();
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
        OrbitTheme.Aurora => UiLoc.T("Orbit.ThemeAurora"),
        OrbitTheme.Ocean => UiLoc.T("Orbit.ThemeOcean"),
        OrbitTheme.Plum => UiLoc.T("Orbit.ThemePlum"),
        OrbitTheme.Sunset => UiLoc.T("Orbit.ThemeSunset"),
        OrbitTheme.Graphite => UiLoc.T("Orbit.ThemeGraphite"),
        _ => UiLoc.T("Orbit.ThemeOrbit")
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
            RefreshConnectedServerLabel();
        }
        else if (!string.IsNullOrWhiteSpace(_settings.ConfigPath) && File.Exists(_settings.ConfigPath))
        {
            ConfigPath = _settings.ConfigPath;
            ProfileName = Path.GetFileNameWithoutExtension(_settings.ConfigPath);
            foreach (var t in ActiveTargetSource())
            {
                var item = ToRuleItem(t);
                if (item is not null)
                    RouteTargets.Add(item);
            }
        }
    }

    private IEnumerable<SavedTarget> ActiveTargetSource()
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
            var item = ToRuleItem(target);
            if (item is not null)
                RouteTargets.Add(item);
        }
        SelectedTarget = null;
    }

    private static RouteRuleItem? ToRuleItem(SavedTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Path))
            return null;

        var kind = target.Kind?.Trim().ToLowerInvariant() switch
        {
            "folder" => RouteKind.Folder,
            "file" => RouteKind.File,
            "ip" => RouteKind.Ip,
            "domain" => RouteKind.Domain,
            _ => GuessKind(target.Path)
        };

        return new RouteRuleItem(target.Path, kind);
    }

    private static RouteKind GuessKind(string path)
    {
        if (Directory.Exists(path))
            return RouteKind.Folder;
        if (File.Exists(path) || path.StartsWith('/'))
            return RouteKind.File;
        if (RouteAddress.TryParse(path, out _, out var kind, out _))
            return kind;
        return RouteKind.Domain;
    }

    private void PersistActiveList()
    {
        var snapshot = RouteTargets.Select(item => new SavedTarget
        {
            Path = item.Path,
            Kind = item.StoredKind
        }).ToList();

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
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void RefreshPrivilegeStatus()
    {
        PrivilegeStatus = LinuxElevation.CanConfigureTun()
            ? UiLoc.T("Ui.PrivilegesOk")
            : UiLoc.T("Ui.PrivilegesNeed");
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
        OnPropertyChanged(nameof(HasRules));
        OnPropertyChanged(nameof(IsIncludeMode));
        OnPropertyChanged(nameof(IsExcludeMode));
        OnPropertyChanged(nameof(IsSubscriptionProfile));
        OnPropertyChanged(nameof(ConnectionProtocol));
        OnPropertyChanged(nameof(ConnectionSubtitle));
        OnPropertyChanged(nameof(ConnectionActionText));
        OnPropertyChanged(nameof(ConnectionStatusTitle));
        OnPropertyChanged(nameof(ConnectionProfileName));
        OnPropertyChanged(nameof(ThemeDisplayName));
        OnPropertyChanged(nameof(ProfilesEyebrow));
        OnPropertyChanged(nameof(ConnectionEyebrow));
        OnPropertyChanged(nameof(RoutingEyebrow));
        OnPropertyChanged(nameof(RoutingTitle));
        OnPropertyChanged(nameof(ModeIncludeLabel));
        OnPropertyChanged(nameof(ModeExcludeLabel));
        OnPropertyChanged(nameof(SeparateListsHint));
        OnPropertyChanged(nameof(ThroughVpnLabel));
        OnPropertyChanged(nameof(DirectLabel));
        OnPropertyChanged(nameof(EmptyRulesText));
        OnPropertyChanged(nameof(ServerLabel));
        OnPropertyChanged(nameof(ExternalIpLabel));
        OnPropertyChanged(nameof(DurationLabel));
        OnPropertyChanged(nameof(TrafficLabel));
        OnPropertyChanged(nameof(ReceivedLabel));
        OnPropertyChanged(nameof(SentLabel));
        OnPropertyChanged(nameof(LogLabel));
        OnPropertyChanged(nameof(ShowLogTip));
        OnPropertyChanged(nameof(HideLogTip));
        OnPropertyChanged(nameof(ReconnectText));
        OnPropertyChanged(nameof(AddText));
        OnPropertyChanged(nameof(RemoveText));
        OnPropertyChanged(nameof(AddProcessText));
        OnPropertyChanged(nameof(AddAppText));
        OnPropertyChanged(nameof(AddFolderText));
        OnPropertyChanged(nameof(AddListText));
        OnPropertyChanged(nameof(AddTargetsText));
        OnPropertyChanged(nameof(AddProcessTip));
        OnPropertyChanged(nameof(AddAppTip));
        OnPropertyChanged(nameof(AddFolderTip));
        OnPropertyChanged(nameof(AddressWatermark));
        OnPropertyChanged(nameof(AddressPromptTitle));
        OnPropertyChanged(nameof(AddProfileText));
        OnPropertyChanged(nameof(FromFileText));
        OnPropertyChanged(nameof(ByLinkText));
        OnPropertyChanged(nameof(AddAmneziaText));
        OnPropertyChanged(nameof(AddByLinkText));
        OnPropertyChanged(nameof(ImportLinkText));
        OnPropertyChanged(nameof(ChooseServerText));
        OnPropertyChanged(nameof(ApplyCapsText));
        OnPropertyChanged(nameof(ApplyCapsTip));
        OnPropertyChanged(nameof(TeardownText));
        OnPropertyChanged(nameof(TeardownTip));
        OnPropertyChanged(nameof(LinuxToolsText));
        OnPropertyChanged(nameof(RemoveProfileTip));
        OnPropertyChanged(nameof(ProfileMenuTip));
        OnPropertyChanged(nameof(RefreshTip));
        OnPropertyChanged(nameof(CycleThemeTip));
        OnPropertyChanged(nameof(LanguageDisplayName));
        OnPropertyChanged(nameof(LanguageTip));
        OnPropertyChanged(nameof(TrayShowText));
        OnPropertyChanged(nameof(TrayQuitText));
        OnPropertyChanged(nameof(RemoveRuleTip));
        OnPropertyChanged(nameof(CancelText));
        RefreshPrivilegeStatus();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => Dispatch(() =>
        {
            RefreshOrbitLabels();
            RaiseCommands();
        });

    private void CycleLanguage()
    {
        UiLoc.Cycle();
        _settings.Language = UiLoc.CurrentCode;
        Persist();
        AppendLog($"{UiLoc.T("Ui.Language")}: {LanguageDisplayName}");
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

        if (RouteTargets.Any(existing =>
                existing.Kind == kind
                && string.Equals(existing.Path, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            AppendLog($"Already listed: {normalized}");
            return;
        }

        RouteTargets.Add(new RouteRuleItem(normalized, kind));
        NewTargetText = "";
        PersistActiveList();
        Persist();
        RefreshOrbitLabels();
        AppendLog($"Added {kind}: {normalized}");
        _ = ApplyLiveRoutingAsync();
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
        AppendLog($"Removed: {removed.Path}");
        _ = ApplyLiveRoutingAsync();
    }

    private async Task AddFromPickerAsync(AppPickerSource source)
    {
        if (_window is null)
            return;

        var dialog = new AppPickerWindow(source);
        var ok = await dialog.ShowDialog<bool>(_window).ConfigureAwait(true);
        if (!ok || string.IsNullOrWhiteSpace(dialog.SelectedExecutablePath))
            return;

        TryAddPathRule(dialog.SelectedExecutablePath, RouteKind.File, "process");
    }

    private async Task AddAddressListAsync()
    {
        if (_window is null)
            return;

        var dialog = new AddressListWindow();
        var ok = await dialog.ShowDialog<bool>(_window).ConfigureAwait(true);
        if (!ok || string.IsNullOrWhiteSpace(dialog.AddressText))
            return;

        var added = 0;
        foreach (var raw in dialog.AddressText.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!RouteAddress.TryParse(raw, out var normalized, out var kind, out var error))
            {
                AppendLog($"Bad target: {error}");
                continue;
            }

            if (RouteTargets.Any(existing =>
                    existing.Kind == kind
                    && string.Equals(existing.Path, normalized, StringComparison.OrdinalIgnoreCase)))
                continue;

            RouteTargets.Add(new RouteRuleItem(normalized, kind));
            added++;
        }

        if (added == 0)
            return;

        PersistActiveList();
        Persist();
        RefreshOrbitLabels();
        AppendLog($"Added addresses: {added}");
        _ = ApplyLiveRoutingAsync();
    }

    private async Task AddFolderAsync()
    {
        if (_window is null)
            return;

        var folders = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add folder (PROCESS-PATH-REGEX)",
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        TryAddPathRule(Path.GetFullPath(path), RouteKind.Folder, "folder");
    }

    private void TryAddPathRule(string path, RouteKind kind, string label)
    {
        path = Path.GetFullPath(path);
        if (RouteTargets.Any(existing =>
                existing.Kind == kind
                && HostPlatform.PathsEqual(existing.Path, path)))
        {
            AppendLog($"Already listed: {path}");
            return;
        }

        RouteTargets.Add(new RouteRuleItem(path, kind));
        PersistActiveList();
        Persist();
        RefreshOrbitLabels();
        AppendLog($"Added {label}: {path}");
        _ = ApplyLiveRoutingAsync();
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
            ProfileName = UiLoc.T("Ui.NoProfile");
            RouteTargets.Clear();
            ConnectedServer = "";
        }

        Persist();
        RefreshOrbitLabels();
        AppendLog($"Profile “{name}” removed from the list. The config file on disk was not deleted.");
        RaiseCommands();
    }

    private RouteTarget[] BuildTargets()
        => RouteTargets.Select(item => item.ToRouteTarget()).ToArray();

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
            AppendLog("Import an Amnezia .conf or subscription first.");
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
        StopTrafficPolling();
        IncomingTraffic = "—";
        OutgoingTraffic = "—";
        OnPropertyChanged(nameof(ConnectionActionText));
        _connectCts = new CancellationTokenSource();
        var generation = ++_sessionGeneration;
        try
        {
            var selectiveTargets = BuildTargets();
            var fullTunnelWhenEmpty = _exceptListed;
            AppendLog(_exceptListed
                ? selectiveTargets.Length == 0
                    ? "Connecting exclude/full tunnel (MATCH,VPN)."
                    : $"Connecting exclude ({selectiveTargets.Length} DIRECT, rest VPN)."
                : selectiveTargets.Length == 0
                    ? "Connecting selective include (empty → MATCH,DIRECT)."
                    : $"Connecting selective include ({selectiveTargets.Length} via VPN, rest DIRECT).");

            var request = new MihomoStartRequest(
                ConfigPath,
                selectiveTargets,
                string.IsNullOrWhiteSpace(_settings.EnginePath) ? null : _settings.EnginePath,
                ExceptListed: _exceptListed,
                FullTunnelWhenEmpty: fullTunnelWhenEmpty);

            var result = await _mihomo.StartAsync(request, _connectCts.Token);
            AppendLog(result.Message);
            IsConnected = result.Succeeded && _mihomo.IsRunning;
            if (IsConnected)
            {
                _connectedFor.Restart();
                await RefreshConnectedServerAfterConnectAsync();
                StatusText = ConnectionStatusTitle;
                StatusKind = "connected";
                StatusDetail = "";
                Persist();
                IncomingTraffic = "0 B";
                OutgoingTraffic = "0 B";
                if (generation == _sessionGeneration)
                    StartTrafficPolling();
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

    private void RefreshConnectedServerLabel()
    {
        if (SubscriptionProfile.IsSubscriptionPath(ConfigPath)
            && SubscriptionProfile.TryRead(ConfigPath, out var subscription, out _))
        {
            ConnectedServer = SubscriptionProfile.DisplayNodeName(subscription.EffectiveSelectedNode);
            return;
        }

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

    private async Task RefreshConnectedServerAfterConnectAsync()
    {
        if (IsSubscriptionProfile)
        {
            try
            {
                var node = await _mihomo.GetActiveVpnNodeAsync();
                ConnectedServer = SubscriptionProfile.DisplayNodeName(node);
            }
            catch
            {
                RefreshConnectedServerLabel();
            }

            return;
        }

        RefreshConnectedServerLabel();
    }

    private async Task DisconnectAsync()
    {
        _disconnecting = true;
        IsBusy = true;
        StatusText = "Disconnecting…";
        OnPropertyChanged(nameof(ConnectionActionText));
        try
        {
            _sessionGeneration++;
            StopTrafficPolling();
            _connectCts?.Cancel();
            if (_mihomo.IsRunning)
            {
                var result = await _mihomo.StopAsync();
                AppendLog(result.Message);
            }

            LinuxTunnelCleanup.TryCleanup();
            IsConnected = false;
            ExternalIp = "—";
            IncomingTraffic = "—";
            OutgoingTraffic = "—";
            _connectedFor.Reset();
            ConnectionDuration = "00:00:00";
            StatusText = "Disconnected";
            StatusKind = "disconnected";
            StatusDetail = "";
            RefreshConnectedServerLabel();
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

        RegisterProfileFromPath(Path.GetFullPath(path), $"Imported AmneziaWG profile «{{0}}» (endpoint {read.Profile.EndpointHost}).");
        ConnectedServer = read.Profile.EndpointHost;
    }

    private async Task ImportSubscriptionAsync()
    {
        if (_window is null)
            return;

        var dialog = new SubscriptionImportWindow();
        var ok = await dialog.ShowDialog<bool>(_window).ConfigureAwait(true);
        if (!ok || string.IsNullOrWhiteSpace(dialog.SavedPath))
            return;

        var detail = string.IsNullOrWhiteSpace(dialog.ResultMessage)
            ? "Imported subscription «{0}»."
            : dialog.ResultMessage + " Profile «{0}».";
        RegisterProfileFromPath(dialog.SavedPath, detail);
    }

    private void RegisterProfileFromPath(string fullPath, string createdMessageFormat)
    {
        PersistActiveList();
        var existing = Profiles.FirstOrDefault(p => SamePath(p.ConfigPath, fullPath));
        if (existing is not null)
        {
            SelectedProfile = existing;
            AppendLog($"Profile “{existing.Name}” was already in the list — selected again.");
            return;
        }

        _settings.Profiles.Add(new SavedProfile { ConfigPath = fullPath, IncludeTargets = [], ExcludeTargets = [] });
        var option = new VpnProfileOption(fullPath);
        Profiles.Add(option);
        SelectedProfile = option;
        Persist();
        RefreshOrbitLabels();
        AppendLog(string.Format(System.Globalization.CultureInfo.InvariantCulture, createdMessageFormat, option.Name));
        StatusText = "Profile ready";
        StatusKind = "disconnected";
        StatusDetail = "";
        RaiseCommands();
    }

    private async Task ManageServersAsync()
    {
        if (_window is null || !IsSubscriptionProfile || string.IsNullOrWhiteSpace(ConfigPath))
            return;

        var profilePath = ConfigPath;
        if (!SubscriptionProfile.TryRead(profilePath, out var subscription, out var error))
        {
            AppendLog(error);
            return;
        }

        var nodes = WithCachedDelays(profilePath, await LoadSubscriptionNodesAsync(profilePath, subscription).ConfigureAwait(true));
        if (!SamePath(profilePath, ConfigPath))
            return;

        var dialog = new ServerPickerWindow(
            nodes,
            subscription.EffectiveSelectedNode,
            async ct => WithCachedDelays(profilePath,
                await LoadSubscriptionNodesAsync(profilePath, subscription, forceRefresh: true, ct).ConfigureAwait(false)),
            async (names, ct) =>
            {
                if (!IsConnected || !SamePath(profilePath, ConfigPath))
                    return new Dictionary<string, int>();
                var delays = await _mihomo.MeasureVpnNodeDelaysAsync(names, ct).ConfigureAwait(false);
                RememberNodeDelays(profilePath, delays);
                return delays;
            },
            canPing: IsConnected && SamePath(profilePath, ConfigPath));

        var ok = await dialog.ShowDialog<bool>(_window).ConfigureAwait(true);
        if (!ok)
            return;

        await ApplySelectedNodeAsync(profilePath, dialog.SelectedNodeName).ConfigureAwait(true);
    }

    private async Task ApplySelectedNodeAsync(string profilePath, string nodeName)
    {
        if (!SubscriptionProfile.TrySaveSelectedNode(profilePath, nodeName, out var saveError))
        {
            AppendLog(saveError);
            return;
        }

        ConnectedServer = SubscriptionProfile.DisplayNodeName(nodeName);
        if (IsConnected && SamePath(profilePath, ConfigPath))
        {
            var applied = await _mihomo.SelectVpnNodeAsync(nodeName).ConfigureAwait(true);
            AppendLog(applied.Succeeded
                ? applied.Message
                : $"Saved offline: {applied.Message}");
            if (applied.Succeeded)
            {
                await RefreshConnectedServerAfterConnectAsync();
                _ = RefreshIpAsync();
            }

            return;
        }

        AppendLog($"Server saved: {SubscriptionProfile.DisplayNodeName(nodeName)}. Connect to use it.");
    }

    private IReadOnlyList<VpnNodeInfo> WithCachedDelays(string profilePath, IReadOnlyList<VpnNodeInfo> nodes)
    {
        if (!_nodeDelayCache.TryGetValue(profilePath, out var delays) || delays.Count == 0)
            return nodes;
        return SubscriptionNodeCatalog.ApplyDelays(nodes, delays, sortByDelay: false);
    }

    private void RememberNodeDelays(string profilePath, IReadOnlyDictionary<string, int> delays)
    {
        if (!_nodeDelayCache.TryGetValue(profilePath, out var cache))
        {
            cache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _nodeDelayCache[profilePath] = cache;
        }

        foreach (var pair in delays)
            cache[pair.Key] = pair.Value;
    }

    private async Task<IReadOnlyList<VpnNodeInfo>> LoadSubscriptionNodesAsync(
        string profilePath,
        SubscriptionProfile subscription,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (IsConnected && SamePath(profilePath, ConfigPath))
        {
            try
            {
                var live = await _mihomo.ListVpnNodesAsync(cancellationToken).ConfigureAwait(false);
                if (live.Count > 1)
                    return live;
            }
            catch (Exception)
            {
            }
        }

        if (!forceRefresh
            && SubscriptionNodeCatalog.TryListFromProviderCache(_mihomo.ProviderCachePath, out var cached, out _)
            && cached.Count > 0)
        {
            return SubscriptionNodeCatalog.WithAuto(cached);
        }

        if (subscription.IsInline)
            return SubscriptionNodeCatalog.WithAuto(SubscriptionNodeCatalog.ExtractNames(string.Join("\n", subscription.Links!)));

        if (string.IsNullOrWhiteSpace(subscription.Url))
            return SubscriptionNodeCatalog.WithAuto([]);

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            using var request = new HttpRequestMessage(HttpMethod.Get, subscription.Url);
            request.Headers.TryAddWithoutValidation("User-Agent", "SelectiveVpn");
            request.Headers.TryAddWithoutValidation("X-Hardware-Id", DeviceIdentity.GetHardwareId(AppPaths.RootDirectory));
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var names = SubscriptionNodeCatalog.ExtractNames(payload);
            if (names.Count > 0)
            {
                subscription.NodeCount = names.Count;
                try
                {
                    SubscriptionProfile.SaveToPath(profilePath, subscription);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }

                return SubscriptionNodeCatalog.WithAuto(names);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            AppendLog($"Could not refresh subscription nodes: {ex.Message}");
        }

        return SubscriptionNodeCatalog.WithAuto([]);
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

    private async Task ApplyLiveRoutingAsync()
    {
        if (!IsConnected || !_mihomo.IsRunning || _disconnecting)
            return;

        try
        {
            var result = await _mihomo.ReloadRoutingAsync(
                BuildTargets(),
                _exceptListed,
                fullTunnelWhenEmpty: _exceptListed).ConfigureAwait(true);
            AppendLog(result.Succeeded
                ? "Routing rules reloaded without reconnect."
                : $"Live rule reload failed: {result.Message}");
        }
        catch (Exception ex)
        {
            AppendLog($"Live rule reload error: {ex.Message}");
        }
    }

    private void StartTrafficPolling()
    {
        StopTrafficPolling();
        _trafficCts = new CancellationTokenSource();
        _ = PollTrafficAsync(_trafficCts.Token);
    }

    private void StopTrafficPolling()
    {
        if (_trafficCts is null)
            return;

        _trafficCts.Cancel();
        _trafficCts.Dispose();
        _trafficCts = null;
    }

    private async Task PollTrafficAsync(CancellationToken cancellationToken)
    {
        var generation = _sessionGeneration;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                var traffic = await _mihomo.QueryVpnTrafficAsync(cancellationToken).ConfigureAwait(false);
                Dispatch(() =>
                {
                    if (cancellationToken.IsCancellationRequested
                        || generation != _sessionGeneration
                        || !IsConnected
                        || !_mihomo.IsRunning)
                    {
                        return;
                    }

                    IncomingTraffic = FormatBytes(traffic.IncomingBytes);
                    OutgoingTraffic = FormatBytes(traffic.OutgoingBytes);
                });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0)
            bytes = 0;

        string[] unitKeys = ["Unit.B", "Unit.KB", "Unit.MB", "Unit.GB", "Unit.TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < unitKeys.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var text = unit == 0
            ? bytes.ToString(CultureInfo.CurrentCulture)
            : value.ToString(value >= 100 ? "0" : "0.#", CultureInfo.CurrentCulture);
        return text + " " + UiLoc.T(unitKeys[unit]);
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
        _sessionGeneration++;
        StopTrafficPolling();
        IsConnected = false;
        ExternalIp = "—";
        IncomingTraffic = "—";
        OutgoingTraffic = "—";
        _connectedFor.Reset();
        ConnectionDuration = "00:00:00";
        StatusText = "Disconnected";
        StatusKind = "disconnected";
        StatusDetail = "";
        RefreshConnectedServerLabel();
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
        _settings.Language = string.IsNullOrWhiteSpace(_settings.Language) ? "auto" : _settings.Language;
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

    private void OnEngineMessage(object? sender, string message)
    {
        const string childPrefix = "CHILD_PROCESSES_ADDED:";
        if (message.StartsWith(childPrefix, StringComparison.Ordinal)
            && int.TryParse(message.AsSpan(childPrefix.Length), out var count))
        {
            AppendLog($"Child processes added to rules: {count}.");
            return;
        }

        AppendLog(message);
    }

    private void OnEngineStopped(object? sender, EventArgs e) => Dispatch(() =>
    {
        if (_disposed || _disconnecting)
            return;

        _sessionGeneration++;
        StopTrafficPolling();
        IsConnected = false;
        ExternalIp = "—";
        IncomingTraffic = "—";
        OutgoingTraffic = "—";
        _connectedFor.Reset();
        ConnectionDuration = "00:00:00";
        StatusText = "Engine stopped";
        StatusKind = "error";
        StatusDetail = "Mihomo process exited unexpectedly.";
        AppendLog("Mihomo process exited unexpectedly.");
        LinuxTunnelCleanup.TryCleanup();
        RefreshConnectedServerLabel();
        RaiseCommands();
    });

    private void RaiseCommands()
    {
        (ConnectionActionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ConnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DisconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ImportConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ImportSubscriptionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ManageServersCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RemoveProfileCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RefreshIpCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ApplyCapabilitiesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EmergencyTeardownCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddTargetCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RemoveTargetCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddProcessCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddAppCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
        _sessionGeneration++;
        StopTrafficPolling();
        _durationTimer.Stop();
        _durationTimer.Dispose();
        _connectCts?.Cancel();
        UiLoc.LanguageChanged -= OnLanguageChanged;
        _mihomo.EngineMessage -= OnEngineMessage;
        _mihomo.EngineStopped -= OnEngineStopped;
        try
        {
            if (_mihomo.IsRunning)
                _mihomo.StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        LinuxTunnelCleanup.TryCleanup();
    }
}
