using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SelectiveVpn.Abstractions;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;
using SelectiveVpn.Services;

namespace SelectiveVpn.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IMihomoController _mihomo;
    private readonly IAppRuleStore _rules;
    private readonly IFilePicker _files;
    private readonly MainViewModelEnvironment _environment;
    private readonly UserSettings _settings;

    private string? _configPath;
    private string? _enginePath;
    private AppRule? _selectedApp;
    private bool _isBusy;
    private bool _isConnected;
    private string? _runningConfigPath;
    private bool _disconnecting;
    private bool _expectExit;
    private bool _isDarkTheme;
    private string _theme = AppTheme.Orbit;
    private string _statusText = "";
    private string _statusKind = "disconnected";
    private string _statusDetail = "";
    private string _profileName = "";
    private string _engineLabel = "";
    private string _externalIp = "—";
    private string _externalIpCountryCode = "";
    private string _connectedServer = "";
    private string _ipCaption = "";
    private string _incomingTraffic = "0";
    private string _outgoingTraffic = "0";
    private long? _connectionStartedTimestamp;
    private DispatcherTimer? _connectionDurationTimer;
    private bool _exceptListed;
    private bool _suppressExceptListed;
    private bool _updateAvailable;
    private readonly HashSet<string> _pendingProfilePaths = new(StringComparer.OrdinalIgnoreCase);
    private UpdateInfo? _pendingUpdate;
    private CancellationTokenSource? _connectCts;
    private CancellationTokenSource? _trafficCts;
    private CancellationTokenSource? _sessionCts;
    private CancellationTokenSource? _ipRefreshCts;
    private CancellationTokenSource? _ipCountryCts;
    private CancellationTokenSource? _recoveryCts;
    private Task? _connectTask;
    private Task? _recoveryTask;
    private MihomoStartRequest? _activeRequest;
    private long _sessionGeneration;
    private bool _closing;
    private bool _disposed;
    private readonly Dictionary<string, Dictionary<string, int>> _lastNodeDelays =
        new(StringComparer.OrdinalIgnoreCase);

    public MainViewModel(
        IMihomoController mihomo,
        IAppRuleStore rules,
        IFilePicker files)
        : this(mihomo, rules, files, new MainViewModelEnvironment())
    {
    }

    internal MainViewModel(
        IMihomoController mihomo,
        IAppRuleStore rules,
        IFilePicker files,
        MainViewModelEnvironment environment)
    {
        _environment = environment;
        _mihomo = mihomo;
        _rules = rules;
        _files = files;
        _mihomo.EngineMessage += OnEngineMessage;
        _mihomo.EngineStopped += OnEngineStopped;

        ConnectCommand = new RelayCommand(ConnectAsync, () => !IsBusy && !IsConnected, LogUnexpected);
        DisconnectCommand = new RelayCommand(DisconnectAsync, CanDisconnect, LogUnexpected);
        ReconnectCommand = new RelayCommand(ReconnectFromUserAsync,
            () => !IsBusy && IsSelectedProfileRunning && HasPendingChanges, LogUnexpected);
        BrowseConfigCommand = new RelayCommand(BrowseConfig, () => !IsBusy, LogUnexpected);
        ImportSubscriptionCommand = new RelayCommand(ImportSubscriptionAsync, () => !IsBusy, LogUnexpected);
        RemoveProfileCommand = new RelayCommand(RemoveProfile, () => !IsBusy && Profiles.Count > 0, LogUnexpected);
        ManageServersCommand = new RelayCommand(ManageServersAsync, () => !IsBusy && IsSubscriptionProfile, LogUnexpected);
        EditConfigCommand = new RelayCommand(EditConfig, () => !IsBusy && IsWireGuardProfile, LogUnexpected);
        SelectProfileCommand = new ParameterRelayCommand(SelectProfile, () => !IsBusy, LogUnexpected);
        AddAppCommand = new RelayCommand(AddApp, () => !IsBusy, LogUnexpected);
        AddFolderCommand = new RelayCommand(AddFolder, () => !IsBusy, LogUnexpected);
        AddAddressListCommand = new RelayCommand(AddAddressListAsync, () => !IsBusy, LogUnexpected);
        EditAddressListCommand = new RelayCommand(EditAddressListAsync, () => !IsBusy && SelectedApp?.IsAddressList == true, LogUnexpected);
        RemoveCommand = new RelayCommand(RemoveSelected, () => !IsBusy && SelectedApp is not null, LogUnexpected);
        RefreshIpCommand = new RelayCommand(() => RefreshIpAsync(), () => IsConnected && !IsBusy, LogUnexpected);
        BrowseEngineCommand = new RelayCommand(BrowseEngine, () => !IsBusy, LogUnexpected);
        CheckConfigCommand = new RelayCommand(CheckConfig, () => !IsBusy, LogUnexpected);
        UseLightThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Orbit, persist: true), () => true, LogUnexpected);
        UseDarkThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Orbit, persist: true), () => true, LogUnexpected);
        UseGreenThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Orbit, persist: true), () => true, LogUnexpected);
        UseOledThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Orbit, persist: true), () => true, LogUnexpected);
        UseAmberThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Orbit, persist: true), () => true, LogUnexpected);
        UseIndigoThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Orbit, persist: true), () => true, LogUnexpected);
        OpenSettingsCommand = new RelayCommand(OpenSettings, () => true, LogUnexpected);
        OpenUpdateCommand = new RelayCommand(OpenUpdate, () => UpdateAvailable, LogUnexpected);

        var loaded = _environment.LoadSettings();
        _settings = loaded.Settings;
        UserSettingsStore.Normalize(_settings);
        var settingsExisted = loaded.Existed;
        RestoreSettings();
        RefreshLocalizedStrings();
        AppLanguage.LanguageChanged += OnLanguageChanged;

        var rights = _environment.IsElevated()
            ? Loc.T("Msg.AdminOk")
            : Loc.T("Msg.AdminNeeded");
        Report(Loc.Tf("Msg.UiStarted", rights));
        if (settingsExisted)
            Report(Loc.T("Msg.SettingsLoaded"));
    }

    public event EventHandler? ApplicationExitRequested;

    public string ThemeId => _theme;

    public IReadOnlyList<ThemeOption> ThemeOptions { get; private set; } = [];

    public string SelectedThemeId
    {
        get => _theme;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var normalized = AppTheme.Normalize(value);
            if (!string.Equals(_theme, normalized, StringComparison.Ordinal))
                ApplyTheme(normalized, persist: true);
        }
    }

    public string VersionLabel { get; } = AppVersion.Current;

    public VpnProfileOption? SelectedProfile
    {
        get => Profiles.FirstOrDefault(profile => SamePath(profile.ConfigPath, ConfigPath));
        set
        {
            if (value is not null && !SamePath(value.ConfigPath, ConfigPath)
                && SelectProfileCommand.CanExecute(value))
                SelectProfileCommand.Execute(value);
        }
    }

    public bool IsRecovering => _recoveryCts is not null;

    public ICommand ConnectionActionCommand => IsConnected || _connectCts is not null || _disconnecting || IsRecovering
        ? DisconnectCommand : ConnectCommand;

    public string ConnectionActionText => _disconnecting ? Loc.T("Orbit.Stopping")
        : IsConnected ? Loc.T("Ui.Disconnect")
        : _connectCts is not null || IsRecovering ? Loc.T("Ui.Cancel") : Loc.T("Ui.Connect");

    public string ConnectionStatusTitle => IsRecovering && !IsConnected ? Loc.T("Orbit.Recovering") : StatusKind switch
    {
        "connected" => Loc.T("Orbit.Connected"),
        "connecting" => Loc.T("Orbit.Connecting"),
        "error" => Loc.T("Status.Error"),
        _ => Loc.T("Orbit.Disconnected")
    };

    public string RunningProfileName => Profiles.FirstOrDefault(profile => profile.IsRunning)?.Name
        ?? (string.IsNullOrWhiteSpace(_runningConfigPath) ? ProfileName : Path.GetFileNameWithoutExtension(_runningConfigPath));

    public string ConnectionProfileName => IsConnected ? RunningProfileName : ProfileName;

    public string ConnectionProtocol
    {
        get
        {
            var path = IsConnected ? _runningConfigPath : ConfigPath;
            return Profiles.FirstOrDefault(profile => SamePath(profile.ConfigPath, path))?.KindLabel
                ?? (SubscriptionProfile.IsSubscriptionPath(path) ? Loc.T("Orbit.Subscription") : "AmneziaWG");
        }
    }

    public bool IsSelectedProfileRunning => IsConnected && SamePath(ConfigPath, _runningConfigPath);

    public bool IsOtherProfileRunning => IsConnected && !IsSelectedProfileRunning;

    public bool HasPendingChanges => IsSelectedProfileRunning && ConfigPath is not null
        && _pendingProfilePaths.Contains(ConfigPath);

    public bool ShowRoutingNotice => HasPendingChanges || IsOtherProfileRunning;

    public string RoutingNotice => IsOtherProfileRunning
        ? Loc.Tf("Orbit.OtherProfileRunning", RunningProfileName) : Loc.T("Orbit.PendingChanges");

    public string RulesProfileLabel => HasProfile ? Loc.Tf("Orbit.RulesFor", ProfileName) : Loc.T("Orbit.ChooseProfile");

    public string ListedRouteLabel => ExceptListed ? Loc.T("Orbit.Direct") : Loc.T("Orbit.ThroughVpn");

    public string RemainingRouteLabel => ExceptListed ? Loc.T("Orbit.ThroughVpn") : Loc.T("Orbit.Direct");

    public string RoutingSummary => !HasApps ? Loc.T("Orbit.AllTraffic")
        : ExceptListed ? Loc.T("Orbit.RemainingVpn") : Loc.T("Orbit.RemainingDirect");

    public int RoutingModeIndex
    {
        get => ExceptListed ? 1 : 0;
        set { if (value is 0 or 1) ExceptListed = value == 1; }
    }

    public string ThemeDisplayName => ThemeOptions.FirstOrDefault(option => option.Code == _theme)?.Title ?? Loc.T("Orbit.ThemeOrbit");

    public bool UpdateAvailable
    {
        get => _updateAvailable;
        private set
        {
            if (!SetProperty(ref _updateAvailable, value))
                return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public UpdateInfo? PendingUpdate
    {
        get => _pendingUpdate;
        private set => SetProperty(ref _pendingUpdate, value);
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        private set => SetProperty(ref _isDarkTheme, value);
    }

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

    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetProperty(ref _statusDetail, value);
    }

    public string ProfileName
    {
        get => _profileName;
        private set => SetProperty(ref _profileName, value);
    }

    public string EngineLabel
    {
        get => _engineLabel;
        private set => SetProperty(ref _engineLabel, value);
    }

    public string ExternalIp
    {
        get => _externalIp;
        private set
        {
            if (SetProperty(ref _externalIp, value))
                ClearExternalIpCountry();
        }
    }

    public string ExternalIpCountryCode
    {
        get => _externalIpCountryCode;
        private set
        {
            if (SetProperty(ref _externalIpCountryCode, value))
                OnPropertyChanged(nameof(ExternalIpCountryName));
        }
    }

    public string ExternalIpCountryName => CountryFlags.GetName(ExternalIpCountryCode);

    public string ConnectedServer
    {
        get => _connectedServer;
        private set => SetProperty(ref _connectedServer, value);
    }

    public string IpCaption
    {
        get => _ipCaption;
        private set => SetProperty(ref _ipCaption, value);
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

    public string ConnectionDuration
    {
        get
        {
            if (!IsConnected || _connectionStartedTimestamp is not long started)
                return "00:00:00";

            var elapsed = _environment.TimeProvider.GetElapsedTime(started);
            var seconds = Math.Max(0, elapsed.Ticks / TimeSpan.TicksPerSecond);
            return string.Create(CultureInfo.InvariantCulture,
                $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}");
        }
    }

    public string? EnginePath
    {
        get => _enginePath;
        private set => SetProperty(ref _enginePath, value);
    }

    public string? ConfigPath
    {
        get => _configPath;
        private set
        {
            if (SetProperty(ref _configPath, value))
            {
                UpdateProfileName();
                OnPropertyChanged(nameof(HasProfile));
                OnPropertyChanged(nameof(IsSubscriptionProfile));
                OnPropertyChanged(nameof(IsWireGuardProfile));
                RefreshOrbitProperties();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsSubscriptionProfile => SubscriptionProfile.IsSubscriptionPath(ConfigPath);

    public bool IsWireGuardProfile => HasProfile && !IsSubscriptionProfile;

    public ObservableCollection<VpnProfileOption> Profiles { get; } = [];

    public ObservableCollection<AppRule> Apps { get; } = [];

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    public bool HasApps => Apps.Count > 0;

    public bool HasProfile => !string.IsNullOrWhiteSpace(ConfigPath);

    public bool ExceptListed
    {
        get => _exceptListed;
        set
        {
            if (_exceptListed == value)
                return;

            var profile = _suppressExceptListed ? null : FindProfile(ConfigPath);
            if (profile is not null)
                SaveCurrentTargets(profile);
            if (!SetProperty(ref _exceptListed, value))
                return;

            OnPropertyChanged(nameof(AppsHint));
            OnPropertyChanged(nameof(AppsFooter));
            RefreshOrbitProperties();
            if (_suppressExceptListed)
                return;

            if (profile is not null)
            {
                profile.ExceptListed = value;
                LoadTargets(TargetsFor(profile, value));
            }
            NoteLiveListChange();
            Persist();
        }
    }

    public string AppsHint => ExceptListed
        ? Loc.T("Hint.ExceptListed")
        : Loc.T("Hint.IncludeListed");

    public string AppsFooter => ExceptListed
        ? Loc.T("Footer.ExceptListed")
        : Loc.T("Footer.IncludeListed");

    public string InListLabel => Loc.T("Ui.InList");

    public int TargetCount => Apps.Count;

    public AppRule? SelectedApp
    {
        get => _selectedApp;
        set
        {
            if (SetProperty(ref _selectedApp, value))
            {
                OnPropertyChanged(nameof(CanEditAddressList));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanEditAddressList => SelectedApp?.IsAddressList == true;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshOrbitProperties();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            var changed = SetProperty(ref _isConnected, value);
            if (!value)
            {
                _runningConfigPath = null;
                ResetConnectionDuration();
            }
            else if (changed)
            {
                // A failed startup cleanup can leave a real tunnel running, so use the
                // observed connection transition rather than only the successful result path.
                StartConnectionDuration();
            }
            ApplyRunningLamps();
            if (changed)
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public RelayCommand ConnectCommand { get; }

    public RelayCommand DisconnectCommand { get; }

    public RelayCommand ReconnectCommand { get; }

    public RelayCommand BrowseConfigCommand { get; }

    public RelayCommand ImportSubscriptionCommand { get; }

    public RelayCommand RemoveProfileCommand { get; }

    public RelayCommand ManageServersCommand { get; }

    public RelayCommand EditConfigCommand { get; }

    public ParameterRelayCommand SelectProfileCommand { get; }

    public RelayCommand AddAppCommand { get; }

    public RelayCommand AddFolderCommand { get; }

    public RelayCommand AddAddressListCommand { get; }

    public RelayCommand EditAddressListCommand { get; }

    public RelayCommand RemoveCommand { get; }

    public RelayCommand RefreshIpCommand { get; }

    public RelayCommand BrowseEngineCommand { get; }

    public RelayCommand CheckConfigCommand { get; }

    public RelayCommand UseLightThemeCommand { get; }

    public RelayCommand UseDarkThemeCommand { get; }

    public RelayCommand UseGreenThemeCommand { get; }

    public RelayCommand UseOledThemeCommand { get; }

    public RelayCommand UseAmberThemeCommand { get; }

    public RelayCommand UseIndigoThemeCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public RelayCommand OpenUpdateCommand { get; }

    public bool ConnectOnLaunch => _settings.ConnectOnLaunch;

    public bool AutoReconnect => _settings.AutoReconnect;

    public void SaveSettings() => Persist();

    public Task ConnectFromUserAsync() => ConnectAsync();

    public Task ConnectOnLaunchAsync()
    {
        Report(Loc.T("Msg.AutoConnect"));
        return ConnectAsync();
    }

    public Task DisconnectFromUserAsync() => DisconnectAsync();

    public async Task ReconnectFromUserAsync()
    {
        if (IsBusy || !IsSelectedProfileRunning || !HasPendingChanges)
            return;
        await DisconnectAsync();
        if (!_mihomo.IsRunning && !_closing)
            await ConnectAsync();
    }

    public async Task ConnectFromSavedSessionAsync()
    {
        if (!ConnectSessionStore.TryTake(out var configPath, out var targets, out var exceptListed))
        {
            Report(Loc.T("Msg.RestoreAppsFail"));
            return;
        }

        RestoreSavedSessionTargets(configPath, targets, exceptListed);
        if (string.IsNullOrWhiteSpace(ConfigPath))
        {
            Report(Loc.T("Msg.SessionNoConfig"));
            return;
        }

        await StartEngineAsync();
    }

    internal void RestoreSavedSessionTargets(
        string configPath,
        IReadOnlyList<ConnectSessionStore.SessionTarget> targets,
        bool exceptListed)
    {
        if (FindProfile(configPath) is null)
        {
            _settings.Profiles.Add(new SavedProfile { ConfigPath = configPath, Targets = [] });
            Profiles.Add(new VpnProfileOption(configPath));
        }

        SwitchTo(configPath);
        ShowExceptListed(exceptListed);
        // Elevation restores the exact requested mode snapshot. It must never merge
        // the include list into exclusions (or resurrect rules removed before relaunch).
        _rules.Clear();
        foreach (var target in targets)
        {
            var added = AddStoredTarget(
                target.Path,
                target.IsFolder,
                target.Kind,
                target.Entries,
                target.ResolvedIps,
                target.SkipDomainResolve);
            if (!added.Succeeded)
                Report(added.Message);
        }

        ReloadApps();
        Persist();
    }

    public async Task PrepareForCloseAsync()
    {
        _closing = true;
        CancelRecovery();
        if (!IsBusy && !_mihomo.IsRunning)
        {
            InvalidateSession();
            return;
        }

        await DisconnectAsync();
    }

    public async Task CheckForUpdatesAsync()
    {
        var update = await AppUpdateChecker.CheckLatestAsync().ConfigureAwait(true);
        if (update is null)
            return;

        PendingUpdate = update;
        UpdateAvailable = true;
    }

    private void BrowseConfig()
    {
        if (IsBusy)
            return;

        var path = _files.PickAmneziaConfig();
        if (string.IsNullOrWhiteSpace(path))
            return;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            Report(Loc.T("Msg.ConfigBadPath"));
            return;
        }

        if (!File.Exists(fullPath))
        {
            Report(Loc.T("Msg.ConfigMissing"));
            return;
        }

        RegisterProfileFromPath(fullPath, Loc.T("Msg.ProfileAdded"));
    }

    private async Task ImportSubscriptionAsync()
    {
        if (IsBusy)
            return;

        var dialog = new SelectiveVpn.SubscriptionImportPrompt
        {
            Owner = Application.Current?.MainWindow
        };
        if (_environment.ShowDialog(dialog) != true || string.IsNullOrWhiteSpace(dialog.SavedPath))
            return;

        var detail = string.IsNullOrWhiteSpace(dialog.ResultMessage)
            ? Loc.T("Msg.ProfileImported")
            : dialog.ResultMessage + " " + Loc.T("Msg.ProfileImported");
        RegisterProfileFromPath(dialog.SavedPath, detail);
    }

    private void RegisterProfileFromPath(string fullPath, string createdMessageFormat)
    {
        Persist();
        var created = FindProfile(fullPath) is null;
        if (created)
        {
            _settings.Profiles.Add(new SavedProfile { ConfigPath = fullPath, Targets = [] });
            Profiles.Add(new VpnProfileOption(fullPath));
        }

        SwitchTo(fullPath);
        if (!created)
        {
            Report(Loc.Tf("Msg.ProfileReselected", ProfileName));
            return;
        }

        Report(string.Format(CultureInfo.InvariantCulture, createdMessageFormat, ProfileName));
        if (IsConnected)
            Report(Loc.T("Msg.TunnelUnchanged"));
    }

    private void SelectProfile(object? parameter)
    {
        if (IsBusy || parameter is not VpnProfileOption option)
            return;

        if (SamePath(option.ConfigPath, ConfigPath))
            return;

        Persist();
        SwitchTo(option.ConfigPath);
    }

    private async Task ManageServersAsync()
    {
        if (!IsSubscriptionProfile || string.IsNullOrWhiteSpace(ConfigPath))
            return;

        var profilePath = ConfigPath;
        if (!SubscriptionProfile.TryRead(profilePath, out var subscription, out var error))
        {
            Report(error);
            return;
        }

        var nodes = WithCachedDelays(profilePath, await LoadSubscriptionNodesAsync(profilePath, subscription).ConfigureAwait(true));
        if (!SamePath(profilePath, ConfigPath))
            return;
        var dialog = new SelectiveVpn.ServerPickerPrompt(
            nodes,
            subscription.EffectiveSelectedNode,
            async ct => WithCachedDelays(profilePath,
                await LoadSubscriptionNodesAsync(profilePath, subscription, forceRefresh: true, ct).ConfigureAwait(false)),
            async (names, ct) =>
            {
                if (!IsRunningProfile(profilePath))
                    return new Dictionary<string, int>();
                var delays = await _mihomo.MeasureVpnNodeDelaysAsync(names, ct).ConfigureAwait(false);
                RememberNodeDelays(profilePath, delays);
                return delays;
            },
            canPing: IsRunningProfile(profilePath))
        {
            Owner = Application.Current?.MainWindow
        };
        if (_environment.ShowDialog(dialog) != true)
            return;

        await ApplySelectedNodeAsync(profilePath, dialog.SelectedNodeName);
    }

    internal async Task ApplySelectedNodeAsync(string profilePath, string nodeName)
    {
        if (!SubscriptionProfile.TrySaveSelectedNode(profilePath, nodeName, out var saveError))
        {
            Report(saveError);
            return;
        }

        if (IsRunningProfile(profilePath))
        {
            var generation = _sessionGeneration;
            var applied = await _mihomo.SelectVpnNodeAsync(nodeName, _sessionCts?.Token ?? CancellationToken.None).ConfigureAwait(true);
            if (!IsCurrentSession(generation) || !IsRunningProfile(profilePath))
                return;
            Report(applied.Succeeded
                ? applied.Message
                : Loc.Tf("Msg.ServerSavedOffline", applied.Message));
            if (applied.Succeeded)
            {
                InvalidateSession();
                _sessionCts = new CancellationTokenSource();
                if (_environment.EnableTrafficPolling) StartTrafficPolling();
                await RefreshConnectedServerAsync().ConfigureAwait(true);
                _ = RefreshIpSafelyAsync();
            }

            return;
        }

        Report(Loc.Tf("Msg.ServerSavedLive", SubscriptionProfile.DisplayNodeName(nodeName)));
    }

    private bool IsRunningProfile(string? profilePath)
        => IsConnected && _mihomo.IsRunning && SamePath(profilePath, _runningConfigPath);

    private IReadOnlyList<VpnNodeInfo> WithCachedDelays(string profilePath, IReadOnlyList<VpnNodeInfo> nodes)
    {
        if (!_lastNodeDelays.TryGetValue(profilePath, out var delays)
            || delays.Count == 0)
        {
            return nodes;
        }

        return SubscriptionNodeCatalog.ApplyDelays(nodes, delays, sortByDelay: false);
    }

    private void RememberNodeDelays(string profilePath, IReadOnlyDictionary<string, int> delays)
    {
        if (delays.Count == 0)
            return;

        if (!_lastNodeDelays.TryGetValue(profilePath, out var cached))
        {
            cached = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _lastNodeDelays[profilePath] = cached;
        }

        foreach (var pair in delays)
            cached[pair.Key] = pair.Value;
    }

    private async Task<IReadOnlyList<VpnNodeInfo>> LoadSubscriptionNodesAsync(
        string profilePath,
        SubscriptionProfile subscription,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (IsRunningProfile(profilePath))
        {
            var live = await _mihomo.ListVpnNodesAsync(cancellationToken).ConfigureAwait(false);
            if (live.Count > 1 && IsRunningProfile(profilePath))
                return live;
        }

        if (IsRunningProfile(profilePath) && !forceRefresh
            && SubscriptionNodeCatalog.TryListFromProviderCache(_mihomo.ProviderCachePath, out var cached, out _)
            && cached.Count > 0)
        {
            return SubscriptionNodeCatalog.WithAuto(cached);
        }

        // Pasted links carry their own node list: nothing to download.
        if (subscription.IsInline)
            return SubscriptionNodeCatalog.WithAuto(SubscriptionNodeCatalog.ExtractNames(string.Join("\n", subscription.Links!)));

        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var hwid = DeviceIdentity.GetHardwareId();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SelectiveVpn/" + MihomoRelease.Version);
            client.DefaultRequestHeaders.TryAddWithoutValidation("x-hwid", hwid);
            client.DefaultRequestHeaders.TryAddWithoutValidation("x-device-os", "Windows");
            client.DefaultRequestHeaders.TryAddWithoutValidation("x-device-model", "SelectiveVpn");
            var bytes = await client.GetByteArrayAsync(subscription.Url, cancellationToken).ConfigureAwait(false);
            var payload = System.Text.Encoding.UTF8.GetString(bytes);
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
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
        }

        return SubscriptionNodeCatalog.WithAuto([]);
    }

    private void EditConfig()
    {
        if (!IsWireGuardProfile || string.IsNullOrWhiteSpace(ConfigPath))
            return;

        if (!File.Exists(ConfigPath))
        {
            Report(Loc.T("Msg.ConfigMissing"));
            return;
        }

        string content;
        try
        {
            content = File.ReadAllText(ConfigPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(Loc.T("Msg.ConfigReadFail"));
            return;
        }

        var dialog = new SelectiveVpn.ConfigEditPrompt(ConfigPath, content)
        {
            Owner = Application.Current?.MainWindow
        };
        if (_environment.ShowDialog(dialog) != true)
            return;

        Report(dialog.ResultMessage);
        if (IsConnected)
        {
            MarkPendingChanges();
            Report(Loc.T("Msg.ApplyAfterRestart"));
        }
    }

    private void RemoveProfile()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(ConfigPath))
            return;

        var name = ProfileName;
        var prompt = new SelectiveVpn.RemoveProfilePrompt(name)
        {
            Owner = Application.Current?.MainWindow
        };
        if (_environment.ShowDialog(prompt) != true)
            return;

        var removing = ConfigPath;
        _settings.Profiles.RemoveAll(profile => SamePath(profile.ConfigPath, removing));
        var option = Profiles.FirstOrDefault(profile => SamePath(profile.ConfigPath, removing));
        if (option is not null)
            Profiles.Remove(option);

        _rules.Clear();
        ConfigPath = null;
        var next = _settings.Profiles.FirstOrDefault();
        if (next is null)
        {
            ShowExceptListed(false);
            ReloadApps();
            MarkActiveProfile();
            Persist();
        }
        else
        {
            SwitchTo(next.ConfigPath);
        }

        Report(Loc.Tf("Msg.ProfileRemoved", name));
        CommandManager.InvalidateRequerySuggested();
    }

    private void BrowseEngine()
    {
        var path = _files.PickEngine();
        if (string.IsNullOrWhiteSpace(path))
            return;

        EnginePath = path;
        UpdateEngineLabel();
        Report(Loc.Tf("Msg.EngineSelected", Path.GetFileName(path)));
        Persist();
    }

    private void CheckConfig()
    {
        if (string.IsNullOrWhiteSpace(ConfigPath))
        {
            Report(Loc.T("Msg.ConfigNotSelected"));
            return;
        }

        if (SubscriptionProfile.IsSubscriptionPath(ConfigPath))
        {
            if (!SubscriptionProfile.TryRead(ConfigPath, out var subscription, out var error))
            {
                Report(error);
                return;
            }

            Report(Loc.Tf("Msg.SubOk", subscription.Describe()));
            return;
        }

        var read = AmneziaWgConfigReader.ReadFile(ConfigPath);
        if (!read.Succeeded || read.Profile is null)
        {
            Report(string.IsNullOrWhiteSpace(read.Message)
                ? Loc.T("Msg.ConfigFail")
                : read.Message);
            return;
        }

        Report(Loc.Tf("Msg.ConfigOk", read.Profile.Describe()));
    }

    private void AddApp()
    {
        var dialog = new SelectiveVpn.AppPickerPrompt(_files.PickExecutable)
        {
            Owner = Application.Current?.MainWindow
        };
        if (_environment.ShowDialog(dialog) != true || string.IsNullOrWhiteSpace(dialog.SelectedExecutablePath))
            return;

        var result = _rules.AddExecutable(dialog.SelectedExecutablePath);
        ReloadApps();
        Report(result.Message);
        if (!result.Succeeded)
            return;

        NoteLiveListChange();
        Persist();
    }

    private void AddFolder()
    {
        var path = _files.PickFolder();
        if (string.IsNullOrWhiteSpace(path))
            return;

        var result = _rules.AddFolder(path);
        ReloadApps();
        Report(result.Message);
        if (!result.Succeeded)
            return;

        NoteLiveListChange();
        Persist();
    }

    private async Task AddAddressListAsync()
    {
        var dialog = new SelectiveVpn.AddressListPrompt
        {
            Owner = Application.Current?.MainWindow
        };
        if (_environment.ShowDialog(dialog) != true)
            return;

        var result = _rules.AddAddressList(
            dialog.ListName,
            dialog.Entries,
            dialog.ResolvedIps,
            dialog.SkipDomainResolve);
        ReloadApps();
        Report(string.IsNullOrWhiteSpace(dialog.ResultMessage)
            ? result.Message
            : $"{result.Message} {dialog.ResultMessage}");
        if (!result.Succeeded)
            return;

        NoteLiveListChange();
        Persist();
    }

    private async Task EditAddressListAsync()
    {
        if (SelectedApp is not { IsAddressList: true } selected)
            return;

        var currentName = selected.ExecutablePath;
        var dialog = new SelectiveVpn.AddressListPrompt(selected)
        {
            Owner = Application.Current?.MainWindow
        };
        if (_environment.ShowDialog(dialog) != true)
            return;

        var result = _rules.ReplaceAddressList(
            currentName,
            dialog.ListName,
            dialog.Entries,
            dialog.ResolvedIps,
            dialog.SkipDomainResolve);
        ReloadApps();
        SelectedApp = Apps.FirstOrDefault(rule =>
            rule.IsAddressList
            && string.Equals(rule.ExecutablePath, dialog.ListName, StringComparison.OrdinalIgnoreCase));
        Report(string.IsNullOrWhiteSpace(dialog.ResultMessage)
            ? result.Message
            : $"{result.Message} {dialog.ResultMessage}");
        if (!result.Succeeded)
            return;

        NoteLiveListChange();
        Persist();
    }

    private void RemoveSelected()
    {
        if (SelectedApp is null)
            return;

        var result = _rules.RemoveExecutable(SelectedApp.ExecutablePath);
        SelectedApp = null;
        ReloadApps();
        Report(result.Message);
        if (!result.Succeeded)
            return;

        NoteLiveListChange();
        Persist();
    }

    private async Task ConnectAsync()
    {
        if (IsBusy || IsConnected)
            return;

        if (string.IsNullOrWhiteSpace(ConfigPath))
        {
            SetStatus(Loc.T("Status.Error"), "error");
            Report(Loc.T("Msg.NoConfig"));
            return;
        }

        if (!ConfirmFullTunnel())
            return;

        if (!_environment.IsElevated())
        {
            Persist();
            _environment.SaveConnectSession(ConfigPath, _rules.GetRules().ToArray(), ExceptListed);
            var elevation = _environment.Relaunch("--connect");
            if (!elevation.Succeeded)
            {
                SetStatus(Loc.T("Status.Error"), "error");
                Report(elevation.Message);
                return;
            }

            Report(elevation.Message);
            ApplicationExitRequested?.Invoke(this, EventArgs.Empty);
            Application.Current?.Shutdown();
            return;
        }

        await StartEngineAsync();
    }

    private Task StartEngineAsync(MihomoStartRequest? restoredRequest = null,
        CancellationToken cancellationToken = default, bool isRecovery = false)
    {
        var request = restoredRequest ?? new MihomoStartRequest(
            ConfigPath!, BuildRouteTargets(),
            string.IsNullOrWhiteSpace(EnginePath) ? null : EnginePath, ExceptListed);
        _connectTask = StartEngineCoreAsync(request, cancellationToken, isRecovery);
        return _connectTask;
    }

    private async Task StartEngineCoreAsync(MihomoStartRequest request,
        CancellationToken cancellationToken, bool isRecovery)
    {
        InvalidateSession();
        _sessionCts = new CancellationTokenSource();
        var generation = _sessionGeneration;
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessionCts.Token);
        _connectCts = connectCts;
        IsBusy = true;
        ExternalIp = "—";
        ConnectedServer = "";
        IpCaption = Loc.T("Ip.CaptionIdle");
        SetStatus(Loc.T("Status.Starting"), "connecting");
        CommandManager.InvalidateRequerySuggested();
        try
        {
            var result = await _mihomo.StartAsync(request, connectCts.Token);
            connectCts.Token.ThrowIfCancellationRequested();
            if (generation != _sessionGeneration || _closing)
                throw new OperationCanceledException(connectCts.Token);
            Report(result.Message);
            _runningConfigPath = result.Succeeded && _mihomo.IsRunning ? request.ConfigFilePath : null;
            _activeRequest = result.Succeeded && _mihomo.IsRunning ? request : null;
            IsConnected = result.Succeeded && _mihomo.IsRunning;
            if (IsConnected)
            {
                if (!isRecovery)
                    _pendingProfilePaths.Remove(request.ConfigFilePath);
                SetStatus(Loc.T("Status.Running"), "connected");
            }
            else if (string.Equals(result.Message, MihomoEngine.ConnectCanceledMessage, StringComparison.Ordinal)
                     || result.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase))
                SetStatus(Loc.T("Status.Stopped"), "disconnected");
            else
                SetStatus(Loc.T("Status.Error"), "error");
        }
        catch (OperationCanceledException)
        {
            try { if (_mihomo.IsRunning) await _mihomo.StopAsync(); }
            catch (Exception exception) { LogUnexpected(exception); }
            var running = _mihomo.IsRunning;
            _runningConfigPath = running ? request.ConfigFilePath : null;
            _activeRequest = running ? request : null;
            IsConnected = running;
            SetStatus(Loc.T(running ? "Status.Error" : "Status.Stopped"), running ? "error" : "disconnected");
            if (!running) Report(Loc.T("Msg.ConnectCanceled"));
        }
        catch (Exception exception)
        {
            _runningConfigPath = _mihomo.IsRunning ? request.ConfigFilePath : null;
            _activeRequest = _mihomo.IsRunning ? request : null;
            IsConnected = _mihomo.IsRunning;
            SetStatus(Loc.T("Status.Error"), "error");
            LogUnexpected(exception);
        }
        finally
        {
            if (ReferenceEquals(_connectCts, connectCts))
                _connectCts = null;
            IsBusy = IsRecovering || _disconnecting;
            CommandManager.InvalidateRequerySuggested();
        }

        if (IsConnected && generation == _sessionGeneration && _environment.EnableTrafficPolling)
        {
            IncomingTraffic = "0 " + Loc.T("Unit.B");
            OutgoingTraffic = "0 " + Loc.T("Unit.B");
            StartTrafficPolling();
            _ = RefreshIpSafelyAsync(waitForTunnel: true);
        }
    }

    private void InvalidateSession()
    {
        _sessionGeneration++;
        _ipRefreshCts?.Cancel();
        ClearExternalIpCountry();
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _sessionCts = null;
        StopTrafficPolling();
    }

    private void StartConnectionDuration()
    {
        if (_disposed || _connectionStartedTimestamp.HasValue)
            return;

        _connectionStartedTimestamp = _environment.TimeProvider.GetTimestamp();
        if (_connectionDurationTimer is null)
        {
            _connectionDurationTimer = new DispatcherTimer(DispatcherPriority.Background,
                Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _connectionDurationTimer.Tick += OnConnectionDurationTick;
        }

        _connectionDurationTimer.Start();
        OnPropertyChanged(nameof(ConnectionDuration));
    }

    private void ResetConnectionDuration()
    {
        if (!_connectionStartedTimestamp.HasValue)
            return;

        _connectionStartedTimestamp = null;
        _connectionDurationTimer?.Stop();
        OnPropertyChanged(nameof(ConnectionDuration));
    }

    private void OnConnectionDurationTick(object? sender, EventArgs e)
    {
        if (!_disposed && IsConnected && _connectionStartedTimestamp.HasValue)
            OnPropertyChanged(nameof(ConnectionDuration));
    }

    private bool IsCurrentSession(long generation)
        => !_disposed && generation == _sessionGeneration && IsConnected && _mihomo.IsRunning;

    private async Task RefreshIpSafelyAsync(bool waitForTunnel = false)
    {
        try { await RefreshIpAsync(waitForTunnel); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (IsConnected) LogUnexpected(exception); }
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
                await Task.Delay(1000, cancellationToken);
                var traffic = await _mihomo.QueryVpnTrafficAsync(cancellationToken);
                Dispatch(() =>
                {
                    if (cancellationToken.IsCancellationRequested || !IsCurrentSession(generation))
                        return;
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

        string[] units = [Loc.T("Unit.B"), Loc.T("Unit.KB"), Loc.T("Unit.MB"), Loc.T("Unit.GB"), Loc.T("Unit.TB")];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var text = unit == 0
            ? bytes.ToString(CultureInfo.CurrentCulture)
            : value.ToString(value >= 100 ? "0" : "0.#", CultureInfo.CurrentCulture);
        return text + " " + units[unit];
    }

    private async Task RefreshIpAsync(bool waitForTunnel = false)
    {
        if (!IsConnected || !_mihomo.IsRunning)
            return;
        var generation = _sessionGeneration;
        using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(_sessionCts?.Token ?? CancellationToken.None);
        _ipRefreshCts?.Cancel();
        _ipRefreshCts = refreshCts;
        ClearExternalIpCountry();
        bool Current() => IsCurrentSession(generation) && ReferenceEquals(_ipRefreshCts, refreshCts)
            && !refreshCts.IsCancellationRequested;
        try
        {
            await RefreshConnectedServerAsync();
            if (!Current()) return;
            IpCaption = Loc.T("Ip.CaptionBusy");
            var attempts = waitForTunnel ? 4 : 1;
            OperationResult result = OperationResult.Fail(Loc.T("Ip.Fail"));
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                if (waitForTunnel)
                    await _environment.DelayAsync(TimeSpan.FromSeconds(2), refreshCts.Token);
                if (!Current()) return;
                try
                {
                    using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(refreshCts.Token);
                    attemptTimeout.CancelAfter(TimeSpan.FromSeconds(waitForTunnel ? 6 : 12));
                    result = await _mihomo.QueryExitIpAsync(attemptTimeout.Token);
                }
                catch (OperationCanceledException) when (!refreshCts.IsCancellationRequested)
                {
                    result = OperationResult.Fail(Loc.T("Ip.Fail"));
                }
                if (!Current()) return;
                if (result.Succeeded && IPAddress.TryParse(result.Message, out _))
                {
                    ExternalIp = result.Message;
                    IpCaption = Loc.T("Ip.CaptionOk");
                    StartIpCountryLookup(result.Message, generation, refreshCts);
                    await RefreshConnectedServerAsync();
                    if (Current()) Report(Loc.T("Msg.IpUpdated"));
                    return;
                }
            }
            if (!Current()) return;
            ExternalIp = "—";
            IpCaption = result.Message;
            Report(result.Message);
        }
        catch (OperationCanceledException) when (refreshCts.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_ipRefreshCts, refreshCts)) _ipRefreshCts = null; }
    }

    private void ClearExternalIpCountry()
    {
        _ipCountryCts?.Cancel();
        _ipCountryCts = null;
        ExternalIpCountryCode = "";
    }

    private void StartIpCountryLookup(string ip, long generation, CancellationTokenSource refreshCts)
    {
        ClearExternalIpCountry();
        var countryCts = CancellationTokenSource.CreateLinkedTokenSource(refreshCts.Token);
        _ipCountryCts = countryCts;
        _ = RefreshIpCountryAsync(ip, generation, refreshCts, countryCts);
    }

    private async Task RefreshIpCountryAsync(string ip, long generation,
        CancellationTokenSource refreshCts, CancellationTokenSource countryCts)
    {
        try
        {
            var result = await _environment.LookupIpCountryAsync(ip, countryCts.Token).ConfigureAwait(true);
            if (countryCts.IsCancellationRequested || !IsCurrentSession(generation)
                || !ReferenceEquals(_ipCountryCts, countryCts)
                || (_ipRefreshCts is not null && !ReferenceEquals(_ipRefreshCts, refreshCts))
                || !string.Equals(ExternalIp, ip, StringComparison.Ordinal))
                return;
            ExternalIpCountryCode = CountryFlags.Normalize(result);
        }
        catch (Exception)
        {
            // Optional country data must not delay or fail the IP refresh or connection lifecycle.
        }
        finally
        {
            if (ReferenceEquals(_ipCountryCts, countryCts))
                _ipCountryCts = null;
            countryCts.Dispose();
        }
    }

    private async Task RefreshConnectedServerAsync()
    {
        var generation = _sessionGeneration;
        var runningPath = _runningConfigPath;
        if (!IsCurrentSession(generation) || !SubscriptionProfile.IsSubscriptionPath(runningPath))
        {
            ConnectedServer = "";
            return;
        }

        try
        {
            var node = await _mihomo.GetActiveVpnNodeAsync(_sessionCts?.Token ?? CancellationToken.None).ConfigureAwait(true);
            if (!IsCurrentSession(generation) || !SamePath(runningPath, _runningConfigPath))
                return;
            ConnectedServer = string.IsNullOrWhiteSpace(node)
                ? ""
                : node.Equals(SubscriptionProfile.AutoNodeName, StringComparison.OrdinalIgnoreCase)
                    ? Loc.T("Node.Auto")
                    : node;
        }
        catch (Exception)
        {
            // Не мешаем основному UI, если API узла временно недоступен.
        }
    }

    private async Task DisconnectAsync()
    {
        if (_disconnecting)
            return;
        _disconnecting = true;
        _expectExit = true;
        var wasRecovering = IsRecovering;
        var recoveryTask = _recoveryTask;
        CancelRecovery();
        _connectCts?.Cancel();
        InvalidateSession();
        IsBusy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            if (_connectTask is { IsCompleted: false } connecting)
                await connecting;
            if (recoveryTask is { IsCompleted: false })
                await recoveryTask;
            if (_mihomo.IsRunning)
            {
                var result = await _mihomo.StopAsync();
                Report(result.Message);
            }
            IsConnected = _mihomo.IsRunning;
            if (!IsConnected)
            {
                _activeRequest = null;
                _pendingProfilePaths.Clear();
                ExternalIp = "—";
                ConnectedServer = "";
                IpCaption = Loc.T("Ip.CaptionIdle");
            }
            SetStatus(
                IsConnected ? Loc.T("Status.Running") : Loc.T("Status.Stopped"),
                IsConnected ? "error" : "disconnected");
        }
        catch (Exception exception)
        {
            IsConnected = _mihomo.IsRunning;
            SetStatus(Loc.T("Status.Error"), "error");
            LogUnexpected(exception);
        }
        finally
        {
            if (IsConnected && _environment.EnableTrafficPolling)
            {
                _sessionCts = new CancellationTokenSource();
                StartTrafficPolling();
            }
            _expectExit = false;
            _disconnecting = false;
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
        if (wasRecovering) Report(Loc.T("Orbit.RecoveryCanceled"));
    }

    private bool ConfirmFullTunnel()
    {
        if (_rules.GetRules().Count > 0 || _settings.SkipFullTunnelWarning)
            return true;

        var prompt = new SelectiveVpn.FullTunnelPrompt
        {
            Owner = Application.Current?.MainWindow
        };
        if (_environment.ShowDialog(prompt) != true)
        {
            Report(Loc.T("Msg.ConnectCanceled"));
            return false;
        }

        if (prompt.DontAskAgain)
        {
            _settings.SkipFullTunnelWarning = true;
            Persist();
        }

        return true;
    }

    private bool CanDisconnect() => !_disconnecting && (!IsBusy || IsConnected || _connectCts is not null || IsRecovering);

    private void ReloadApps()
    {
        var selectedPath = SelectedApp?.ExecutablePath;
        Apps.Clear();
        foreach (var rule in _rules.GetRules())
            Apps.Add(rule);

        SelectedApp = Apps.FirstOrDefault(rule =>
            string.Equals(rule.ExecutablePath, selectedPath, StringComparison.OrdinalIgnoreCase));

        OnPropertyChanged(nameof(HasApps));
        OnPropertyChanged(nameof(TargetCount));
        RefreshOrbitProperties();
        CommandManager.InvalidateRequerySuggested();
    }

    private void RestoreSettings()
    {
        var removedMissing = false;
        ApplyTheme(_settings.Theme, persist: false);
        foreach (var profile in _settings.Profiles)
            Profiles.Add(new VpnProfileOption(profile.ConfigPath));

        var active = _settings.Profiles.FirstOrDefault(profile => SamePath(profile.ConfigPath, _settings.ConfigPath))
                     ?? _settings.Profiles.FirstOrDefault();
        if (active is not null)
        {
            ConfigPath = active.ConfigPath;
            if (!File.Exists(active.ConfigPath))
                Report(Loc.T("Msg.SavedConfigMissing"));
            ShowExceptListed(active.ExceptListed);
            removedMissing = LoadTargets(TargetsFor(active, active.ExceptListed));
            MarkActiveProfile();
        }

        if (!string.IsNullOrWhiteSpace(_settings.EnginePath))
        {
            EnginePath = _settings.EnginePath;
            if (!File.Exists(_settings.EnginePath))
                Report(Loc.T("Msg.SavedEngineMissing"));
        }

        UpdateEngineLabel();
        if (_settings.LaunchWithWindows && !WindowsStartup.TryApply(true, out var startupMessage))
            Report(startupMessage);
        if (removedMissing)
            Persist();
    }

    private void OpenUpdate()
    {
        if (PendingUpdate is null)
            return;

        var dialog = new SelectiveVpn.UpdatePrompt(
            PendingUpdate,
            PrepareForCloseAsync,
            () =>
            {
                ApplicationExitRequested?.Invoke(this, EventArgs.Empty);
                Application.Current?.Shutdown();
            })
        {
            Owner = Application.Current?.MainWindow
        };
        _environment.ShowDialog(dialog);
    }

    private void OpenSettings()
    {
        var dialog = new SelectiveVpn.SettingsWindow(
            _settings.LaunchWithWindows,
            _settings.ConnectOnLaunch,
            _settings.Language,
            _theme,
            _settings.AutoReconnect)
        {
            Owner = Application.Current?.MainWindow
        };
        void ApplySelectedTheme(object? sender, string theme) => SelectedThemeId = theme;
        dialog.ThemeSelected += ApplySelectedTheme;
        bool? accepted;
        try { accepted = _environment.ShowDialog(dialog); }
        finally { dialog.ThemeSelected -= ApplySelectedTheme; }
        if (accepted != true) return;

        var launch = dialog.LaunchWithWindows;
        var connect = dialog.ConnectOnLaunch;
        var language = AppLanguage.Normalize(dialog.SelectedLanguage);
        if (launch != _settings.LaunchWithWindows || launch)
        {
            var startup = _environment.ApplyStartup(launch);
            if (!startup.Succeeded)
            {
                Report(startup.Message);
                if (launch != _settings.LaunchWithWindows) return;
            }
            else if (launch != _settings.LaunchWithWindows) Report(startup.Message);
        }

        var connectChanged = connect != _settings.ConnectOnLaunch;
        var languageChanged = !string.Equals(language, _settings.Language, StringComparison.OrdinalIgnoreCase);
        _settings.LaunchWithWindows = launch;
        _settings.ConnectOnLaunch = connect;
        _settings.AutoReconnect = dialog.AutoReconnect;
        if (!_settings.AutoReconnect) CancelRecovery();
        _settings.Language = language;
        Persist();
        OnPropertyChanged(nameof(ConnectOnLaunch));
        OnPropertyChanged(nameof(AutoReconnect));
        if (languageChanged)
        {
            AppLanguage.Apply(language);
            RefreshLocalizedStrings();
        }

        if (connectChanged)
        {
            Report(connect
                ? Loc.T("Msg.SettingsConnectOn")
                : Loc.T("Msg.SettingsConnectOff"));
        }
    }

    private void ApplyTheme(string theme, bool persist)
    {
        var normalized = AppTheme.Normalize(theme);
        var changed = !string.Equals(_theme, normalized, StringComparison.Ordinal);
        _theme = normalized;
        AppTheme.Apply(normalized);
        IsDarkTheme = AppTheme.UsesDarkChrome(normalized);
        OnPropertyChanged(nameof(ThemeId));
        OnPropertyChanged(nameof(SelectedThemeId));
        OnPropertyChanged(nameof(ThemeDisplayName));
        if (!persist || !changed)
            return;

        Report(string.Format(Loc.T("Msg.ThemeChanged"), ThemeDisplayName));
        Persist();
    }

    private void Persist()
    {
        try
        {
            var rules = _rules.GetRules();
            var savedTargets = SnapshotTargets();
            var active = FindProfile(ConfigPath);
            if (active is not null)
                SaveCurrentTargets(active, savedTargets);

            _settings.ConfigPath = string.IsNullOrWhiteSpace(ConfigPath) ? null : ConfigPath;
            _settings.EnginePath = string.IsNullOrWhiteSpace(EnginePath) ? null : EnginePath;
            _settings.Apps = rules.Where(rule => rule.EffectiveKind == RouteKind.File).Select(rule => rule.ExecutablePath).ToList();
            _settings.Targets = UserSettingsStore.CloneTargets(savedTargets);
            _settings.Theme = _theme;
            _environment.SaveSettings(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(Loc.T("Msg.SaveSettingsFail"));
        }
    }

    private List<SavedTarget> SnapshotTargets()
        => _rules.GetRules().Select(rule => new SavedTarget
        {
            Path = rule.ExecutablePath,
            Kind = rule.StoredKind,
            Entries = rule.EntryList.ToList(),
            ResolvedIps = rule.ResolvedIpList.ToList(),
            SkipDomainResolve = rule.SkipDomainResolve
        }).ToList();

    private void SaveCurrentTargets(SavedProfile profile, IReadOnlyList<SavedTarget>? targets = null)
    {
        var snapshot = UserSettingsStore.CloneTargets(targets ?? SnapshotTargets());
        if (ExceptListed)
            profile.ExcludeTargets = snapshot;
        else
            profile.IncludeTargets = snapshot;
        profile.ExceptListed = ExceptListed;
        profile.Targets = UserSettingsStore.CloneTargets(snapshot);
    }

    private static IReadOnlyList<SavedTarget> TargetsFor(SavedProfile profile, bool exceptListed)
        => (exceptListed ? profile.ExcludeTargets : profile.IncludeTargets) ?? [];

    private void SwitchTo(string configPath)
    {
        var profile = FindProfile(configPath);
        if (profile is null)
            return;

        ConfigPath = profile.ConfigPath;
        ShowExceptListed(profile.ExceptListed);
        LoadTargets(TargetsFor(profile, profile.ExceptListed));
        MarkActiveProfile();
        Persist();
    }

    private bool LoadTargets(IReadOnlyList<SavedTarget> targets)
    {
        _rules.Clear();
        var removedMissing = false;
        foreach (var target in targets)
        {
            var added = AddStoredTarget(
                target.Path,
                isFolder: false,
                target.Kind,
                target.Entries,
                target.ResolvedIps,
                target.SkipDomainResolve);
            if (added.Succeeded)
                continue;

            removedMissing = true;
            Report(added.Message);
        }

        ReloadApps();
        // The caller persists after restoring both the profile and its mode, preventing
        // a missing EXE from saving a partially loaded list into the wrong bucket.
        return removedMissing;
    }

    private OperationResult AddStoredTarget(
        string path,
        bool isFolder,
        string? kind,
        IReadOnlyList<string>? entries = null,
        IReadOnlyList<string>? resolvedIps = null,
        bool skipDomainResolve = false)
    {
        if (string.Equals(kind, "address-list", StringComparison.OrdinalIgnoreCase))
            return _rules.AddAddressList(path, entries ?? [], resolvedIps ?? [], skipDomainResolve);

        if (string.Equals(kind, "domain", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "ip", StringComparison.OrdinalIgnoreCase))
            return _rules.AddAddress(path);

        if (string.Equals(kind, "folder", StringComparison.OrdinalIgnoreCase) || isFolder)
            return _rules.AddFolder(path);

        return _rules.AddExecutable(path);
    }

    private RouteTarget[] BuildRouteTargets()
        => AddressListResolver.Expand(
                _rules.GetRules()
                    .Select(rule => new RouteTarget(
                        rule.ExecutablePath,
                        rule.IsFolder,
                        rule.EffectiveKind,
                        rule.EntryList,
                        rule.ResolvedIpList))
                    .ToArray())
            .ToArray();

    private void ShowExceptListed(bool value)
    {
        _suppressExceptListed = true;
        ExceptListed = value;
        _suppressExceptListed = false;
    }

    private void MarkActiveProfile()
    {
        foreach (var profile in Profiles)
            profile.IsActive = SamePath(profile.ConfigPath, ConfigPath);
        ApplyRunningLamps();
    }

    private void ApplyRunningLamps()
    {
        foreach (var profile in Profiles)
            profile.IsRunning = _isConnected && SamePath(profile.ConfigPath, _runningConfigPath);
        RefreshOrbitProperties();
    }

    private SavedProfile? FindProfile(string? configPath)
        => _settings.Profiles.FirstOrDefault(profile => SamePath(profile.ConfigPath, configPath));

    private static bool SamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void UpdateProfileName()
    {
        if (string.IsNullOrWhiteSpace(ConfigPath))
        {
            ProfileName = Loc.T("Status.NoProfile");
            return;
        }

        if (SubscriptionProfile.TryRead(ConfigPath, out var subscription, out _))
        {
            ProfileName = subscription.Title;
            return;
        }

        ProfileName = Path.GetFileNameWithoutExtension(ConfigPath);
    }

    private void UpdateEngineLabel()
    {
        EngineLabel = string.IsNullOrWhiteSpace(EnginePath)
            ? Loc.T("Status.EngineDefault")
            : Path.GetFileName(EnginePath);
    }

    private void NoteLiveListChange()
    {
        if (IsConnected)
        {
            MarkPendingChanges();
            Report(Loc.T("Msg.ListWillApplyLater"));
        }
    }

    private void MarkPendingChanges()
    {
        if (IsSelectedProfileRunning && ConfigPath is not null)
            _pendingProfilePaths.Add(ConfigPath);
        RefreshOrbitProperties();
    }

    private void RefreshOrbitProperties()
    {
        foreach (var property in new[]
        {
            nameof(SelectedProfile), nameof(ConnectionActionCommand), nameof(ConnectionActionText), nameof(IsRecovering),
            nameof(ConnectionStatusTitle), nameof(RunningProfileName), nameof(ConnectionProfileName),
            nameof(ConnectionProtocol), nameof(IsSelectedProfileRunning), nameof(IsOtherProfileRunning),
            nameof(HasPendingChanges), nameof(ShowRoutingNotice), nameof(RoutingNotice),
            nameof(RulesProfileLabel), nameof(ListedRouteLabel), nameof(RemainingRouteLabel),
            nameof(RoutingSummary), nameof(RoutingModeIndex), nameof(ThemeDisplayName)
        })
            OnPropertyChanged(property);
    }

    private void Report(string message)
    {
        var entry = new LogEntry(message);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            LogEntries.Add(entry);
            return;
        }

        dispatcher.BeginInvoke(() => LogEntries.Add(entry));
    }

    private void LogUnexpected(Exception exception)
        => Report($"{Loc.T("Msg.UiError")} {exception.GetType().Name}: {exception.Message}");

    private void OnEngineMessage(object? sender, string message) => Dispatch(() =>
    {
        const string childPrefix = "CHILD_PROCESSES_ADDED:";
        if (message.StartsWith(childPrefix, StringComparison.Ordinal)
            && int.TryParse(message.AsSpan(childPrefix.Length), out var count))
        {
            Report(Loc.Tf("Msg.ChildProcessesAdded", count));
            return;
        }

        Report(message);
    });

    private void OnEngineStopped(object? sender, EventArgs e) => Dispatch(() =>
    {
        if (_disposed || _expectExit || !IsConnected)
            return;
        var request = _activeRequest;
        InvalidateSession();
        IsConnected = false;
        ExternalIp = "—";
        ConnectedServer = "";
        IpCaption = Loc.T("Ip.CaptionIdle");
        SetStatus(Loc.T("Status.Error"), "error");
        Report(Loc.T("Msg.EngineStopped"));
        if (_settings.AutoReconnect && !_closing && request is not null)
        {
            var recoveryCts = new CancellationTokenSource();
            _recoveryCts = recoveryCts;
            IsBusy = true;
            RefreshOrbitProperties();
            _recoveryTask = RecoverConnectionAsync(request, recoveryCts);
        }
    });

    private void CancelRecovery() => _recoveryCts?.Cancel();

    private async Task RecoverConnectionAsync(MihomoStartRequest request, CancellationTokenSource recoveryCts)
    {
        try
        {
            int[] delays = [2, 5, 10];
            for (var attempt = 0; attempt < delays.Length; attempt++)
            {
                await _environment.DelayAsync(TimeSpan.FromSeconds(delays[attempt]), recoveryCts.Token);
                recoveryCts.Token.ThrowIfCancellationRequested();
                if (_closing || _disposed || !_settings.AutoReconnect)
                    return;
                Report(Loc.Tf("Orbit.RecoveryAttempt", attempt + 1, delays.Length));
                await StartEngineAsync(request, recoveryCts.Token, isRecovery: true);
                if (IsConnected)
                    return;
            }
            Report(Loc.T("Orbit.RecoveryFailed"));
        }
        catch (OperationCanceledException) when (recoveryCts.IsCancellationRequested) { }
        catch (Exception exception) { LogUnexpected(exception); }
        finally
        {
            var canceled = recoveryCts.IsCancellationRequested;
            if (ReferenceEquals(_recoveryCts, recoveryCts))
                _recoveryCts = null;
            recoveryCts.Dispose();
            IsBusy = _disconnecting || _connectCts is not null;
            if (!IsConnected && !_disconnecting)
                SetStatus(Loc.T(canceled ? "Status.Stopped" : "Status.Error"), canceled ? "disconnected" : "error");
            RefreshOrbitProperties();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Dispatch(RefreshLocalizedStrings);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _closing = true;
        CancelRecovery();
        _connectCts?.Cancel();
        InvalidateSession();
        if (_connectionDurationTimer is not null)
        {
            _connectionDurationTimer.Stop();
            _connectionDurationTimer.Tick -= OnConnectionDurationTick;
            _connectionDurationTimer = null;
        }
        AppLanguage.LanguageChanged -= OnLanguageChanged;
        _mihomo.EngineMessage -= OnEngineMessage;
        _mihomo.EngineStopped -= OnEngineStopped;
    }

    private void RefreshLocalizedStrings()
    {
        OnPropertyChanged(nameof(ExternalIpCountryName));
        var translatedThemes = ThemeOption.CreateOptions();
        if (ThemeOptions.Count == 0)
        {
            ThemeOptions = translatedThemes;
            OnPropertyChanged(nameof(ThemeOptions));
        }
        else
        {
            foreach (var option in ThemeOptions)
                option.Title = translatedThemes.First(translated => translated.Code == option.Code).Title;
        }
        OnPropertyChanged(nameof(SelectedThemeId));
        OnPropertyChanged(nameof(AppsHint));
        OnPropertyChanged(nameof(AppsFooter));
        OnPropertyChanged(nameof(InListLabel));
        UpdateEngineLabel();
        if (string.IsNullOrWhiteSpace(ConfigPath))
            ProfileName = Loc.T("Status.NoProfile");
        else
            UpdateProfileName();

        SetStatus(StatusKind switch
        {
            "connected" => Loc.T("Status.Running"),
            "connecting" => Loc.T("Status.Starting"),
            "error" => Loc.T("Status.Error"),
            _ => Loc.T("Status.Stopped")
        }, StatusKind);

        if (!IsConnected)
            IpCaption = Loc.T("Ip.CaptionIdle");
        else if (IPAddress.TryParse(ExternalIp, out _))
            IpCaption = Loc.T("Ip.CaptionOk");

        ReloadApps();
        RefreshOrbitProperties();
    }

    private void SetStatus(string text, string kind)
    {
        StatusText = text;
        StatusKind = kind;
        StatusDetail = kind switch
        {
            "connected" => _rules.GetRules().Count == 0
                ? Loc.T("Status.AllViaVpn")
                : ExceptListed
                    ? Loc.T("Status.AllExceptList")
                    : Loc.T("Status.SelectedViaVpn"),
            "connecting" => Loc.T("Status.ConnectingDetail"),
            "error" => Loc.T("Status.ErrorDetail"),
            _ => Loc.T("Status.StoppedDetail")
        };
        RefreshOrbitProperties();
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }
}
