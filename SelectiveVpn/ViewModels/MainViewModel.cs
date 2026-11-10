using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using SelectiveVpn.Abstractions;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;
using SelectiveVpn.Services;

namespace SelectiveVpn.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IMihomoController _mihomo;
    private readonly IAppRuleStore _rules;
    private readonly IFilePicker _files;
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
    private string _theme = AppTheme.Dark;
    private string _statusText = "";
    private string _statusKind = "disconnected";
    private string _statusDetail = "";
    private string _profileName = "";
    private string _engineLabel = "";
    private string _externalIp = "—";
    private string _connectedServer = "";
    private string _ipCaption = "";
    private string _incomingTraffic = "0";
    private string _outgoingTraffic = "0";
    private bool _exceptListed;
    private bool _suppressExceptListed;
    private CancellationTokenSource? _connectCts;
    private CancellationTokenSource? _trafficCts;
    private readonly Dictionary<string, Dictionary<string, int>> _lastNodeDelays =
        new(StringComparer.OrdinalIgnoreCase);

    public MainViewModel(
        IMihomoController mihomo,
        IAppRuleStore rules,
        IFilePicker files)
    {
        _mihomo = mihomo;
        _rules = rules;
        _files = files;
        _mihomo.EngineMessage += OnEngineMessage;
        _mihomo.EngineStopped += OnEngineStopped;

        ConnectCommand = new RelayCommand(ConnectAsync, () => !IsBusy && !IsConnected, LogUnexpected);
        DisconnectCommand = new RelayCommand(DisconnectAsync, CanDisconnect, LogUnexpected);
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
        UseLightThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Light, persist: true), () => true, LogUnexpected);
        UseDarkThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Dark, persist: true), () => true, LogUnexpected);
        UseGreenThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Green, persist: true), () => true, LogUnexpected);
        UseOledThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Oled, persist: true), () => true, LogUnexpected);
        UseAmberThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Amber, persist: true), () => true, LogUnexpected);
        UseIndigoThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Indigo, persist: true), () => true, LogUnexpected);
        OpenSettingsCommand = new RelayCommand(OpenSettings, () => true, LogUnexpected);

        _settings = UserSettingsStore.Load(out var settingsExisted);
        RestoreSettings();
        RefreshLocalizedStrings();
        AppLanguage.LanguageChanged += (_, _) => Dispatch(RefreshLocalizedStrings);

        var rights = WindowsElevation.IsElevated()
            ? Loc.T("Msg.AdminOk")
            : Loc.T("Msg.AdminNeeded");
        Report(Loc.Tf("Msg.UiStarted", rights));
        if (settingsExisted)
            Report(Loc.T("Msg.SettingsLoaded"));
    }

    public event EventHandler? ApplicationExitRequested;

    public string ThemeId => _theme;

    public string VersionLabel { get; } = AppVersion.Current;

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
        private set => SetProperty(ref _externalIp, value);
    }

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
            if (!SetProperty(ref _exceptListed, value))
                return;

            OnPropertyChanged(nameof(AppsHint));
            OnPropertyChanged(nameof(AppsFooter));
            if (_suppressExceptListed)
                return;

            var profile = FindProfile(ConfigPath);
            if (profile is not null)
                profile.ExceptListed = value;
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
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            var changed = SetProperty(ref _isConnected, value);
            if (!value)
                _runningConfigPath = null;
            ApplyRunningLamps();
            if (changed)
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public RelayCommand ConnectCommand { get; }

    public RelayCommand DisconnectCommand { get; }

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

    public bool ConnectOnLaunch => _settings.ConnectOnLaunch;

    public void SaveSettings() => Persist();

    public Task ConnectFromUserAsync() => ConnectAsync();

    public Task ConnectOnLaunchAsync()
    {
        Report(Loc.T("Msg.AutoConnect"));
        return ConnectAsync();
    }

    public Task DisconnectFromUserAsync() => DisconnectAsync();

    public async Task ConnectFromSavedSessionAsync()
    {
        if (!ConnectSessionStore.TryTake(out var configPath, out var targets, out var exceptListed))
        {
            Report(Loc.T("Msg.RestoreAppsFail"));
            return;
        }

        if (FindProfile(configPath) is null)
        {
            _settings.Profiles.Add(new SavedProfile { ConfigPath = configPath, Targets = [] });
            Profiles.Add(new VpnProfileOption(configPath));
        }

        SwitchTo(configPath);
        if (exceptListed != ExceptListed)
            ExceptListed = exceptListed;
        foreach (var target in targets)
        {
            if (_rules.GetRules().Any(rule => string.Equals(rule.ExecutablePath, target.Path, StringComparison.OrdinalIgnoreCase)))
                continue;

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
        if (string.IsNullOrWhiteSpace(ConfigPath))
        {
            Report(Loc.T("Msg.SessionNoConfig"));
            return;
        }

        await StartEngineAsync();
    }

    public async Task PrepareForCloseAsync()
    {
        if (!IsBusy && !_mihomo.IsRunning)
            return;

        await DisconnectAsync();
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
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.SavedPath))
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

        if (!SubscriptionProfile.TryRead(ConfigPath, out var subscription, out var error))
        {
            Report(error);
            return;
        }

        var nodes = WithCachedDelays(await LoadSubscriptionNodesAsync(subscription).ConfigureAwait(true));
        var dialog = new SelectiveVpn.ServerPickerPrompt(
            nodes,
            subscription.EffectiveSelectedNode,
            async ct => WithCachedDelays(
                await LoadSubscriptionNodesAsync(subscription, forceRefresh: true, ct).ConfigureAwait(false)),
            async (names, ct) =>
            {
                var delays = await _mihomo.MeasureVpnNodeDelaysAsync(names, ct).ConfigureAwait(false);
                RememberNodeDelays(delays);
                return delays;
            },
            canPing: IsConnected)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true)
            return;

        if (!SubscriptionProfile.TrySaveSelectedNode(ConfigPath, dialog.SelectedNodeName, out var saveError))
        {
            Report(saveError);
            return;
        }

        if (IsConnected)
        {
            var applied = await _mihomo.SelectVpnNodeAsync(dialog.SelectedNodeName).ConfigureAwait(true);
            Report(applied.Succeeded
                ? applied.Message
                : Loc.Tf("Msg.ServerSavedOffline", applied.Message));
            if (applied.Succeeded)
            {
                await RefreshConnectedServerAsync().ConfigureAwait(true);
                _ = RefreshIpAsync();
            }

            return;
        }

        Report(Loc.Tf("Msg.ServerSavedLive", SubscriptionProfile.DisplayNodeName(dialog.SelectedNodeName)));
    }

    private IReadOnlyList<VpnNodeInfo> WithCachedDelays(IReadOnlyList<VpnNodeInfo> nodes)
    {
        if (string.IsNullOrWhiteSpace(ConfigPath)
            || !_lastNodeDelays.TryGetValue(ConfigPath, out var delays)
            || delays.Count == 0)
        {
            return nodes;
        }

        return SubscriptionNodeCatalog.ApplyDelays(nodes, delays, sortByDelay: false);
    }

    private void RememberNodeDelays(IReadOnlyDictionary<string, int> delays)
    {
        if (string.IsNullOrWhiteSpace(ConfigPath) || delays.Count == 0)
            return;

        if (!_lastNodeDelays.TryGetValue(ConfigPath, out var cached))
        {
            cached = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _lastNodeDelays[ConfigPath] = cached;
        }

        foreach (var pair in delays)
            cached[pair.Key] = pair.Value;
    }

    private async Task<IReadOnlyList<VpnNodeInfo>> LoadSubscriptionNodesAsync(
        SubscriptionProfile subscription,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (_mihomo.IsRunning)
        {
            var live = await _mihomo.ListVpnNodesAsync(cancellationToken).ConfigureAwait(false);
            if (live.Count > 1)
                return live;
        }

        if (!forceRefresh
            && SubscriptionNodeCatalog.TryListFromProviderCache(_mihomo.ProviderCachePath, out var cached, out _)
            && cached.Count > 0)
        {
            return SubscriptionNodeCatalog.WithAuto(cached);
        }

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
                    SubscriptionProfile.SaveToPath(ConfigPath!, subscription);
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
        if (dialog.ShowDialog() != true)
            return;

        Report(dialog.ResultMessage);
        if (IsConnected)
            Report(Loc.T("Msg.ApplyAfterRestart"));
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
        if (prompt.ShowDialog() != true)
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
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.SelectedExecutablePath))
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
        if (dialog.ShowDialog() != true)
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
        if (dialog.ShowDialog() != true)
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

        if (!WindowsElevation.IsElevated())
        {
            Persist();
            ConnectSessionStore.Save(ConfigPath, _rules.GetRules().ToArray(), ExceptListed);
            if (!WindowsElevation.TryRelaunch("--connect", out var elevationMessage))
            {
                SetStatus(Loc.T("Status.Error"), "error");
                Report(elevationMessage);
                return;
            }

            Report(elevationMessage);
            ApplicationExitRequested?.Invoke(this, EventArgs.Empty);
            Application.Current?.Shutdown();
            return;
        }

        await StartEngineAsync();
    }

    private async Task StartEngineAsync()
    {
        _connectCts = new CancellationTokenSource();
        IsBusy = true;
        ExternalIp = "—";
        ConnectedServer = "";
        IpCaption = Loc.T("Ip.CaptionIdle");
        SetStatus(Loc.T("Status.Starting"), "connecting");
        CommandManager.InvalidateRequerySuggested();
        try
        {
            var request = new MihomoStartRequest(
                ConfigPath!,
                BuildRouteTargets(),
                string.IsNullOrWhiteSpace(EnginePath) ? null : EnginePath,
                ExceptListed);
            var result = await _mihomo.StartAsync(request, _connectCts.Token);
            Report(result.Message);
            _runningConfigPath = result.Succeeded && _mihomo.IsRunning ? ConfigPath : null;
            IsConnected = result.Succeeded && _mihomo.IsRunning;
            if (IsConnected)
                SetStatus(Loc.T("Status.Running"), "connected");
            else if (result.Message.Contains("отменено", StringComparison.OrdinalIgnoreCase)
                     || result.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase))
                SetStatus(Loc.T("Status.Stopped"), "disconnected");
            else
                SetStatus(Loc.T("Status.Error"), "error");
        }
        catch (OperationCanceledException)
        {
            IsConnected = false;
            SetStatus(Loc.T("Status.Stopped"), "disconnected");
            Report(Loc.T("Msg.ConnectCanceled"));
        }
        finally
        {
            _connectCts.Dispose();
            _connectCts = null;
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }

        if (IsConnected)
        {
            IncomingTraffic = "0 " + Loc.T("Unit.B");
            OutgoingTraffic = "0 " + Loc.T("Unit.B");
            StartTrafficPolling();
            await RefreshIpAsync(waitForTunnel: true);
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
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, cancellationToken);
                var traffic = await _mihomo.QueryVpnTrafficAsync(cancellationToken);
                Dispatch(() =>
                {
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
        if (!_mihomo.IsRunning)
        {
            ExternalIp = "—";
            ConnectedServer = "";
            IpCaption = Loc.T("Ip.CaptionOffline");
            return;
        }

        _ = RefreshConnectedServerAsync();
        IpCaption = Loc.T("Ip.CaptionBusy");
        var attempts = waitForTunnel ? 4 : 1;
        OperationResult result = OperationResult.Fail(Loc.T("Ip.Fail"));
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (waitForTunnel)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            if (!_mihomo.IsRunning)
            {
                ExternalIp = "—";
                ConnectedServer = "";
                IpCaption = Loc.T("Ip.CaptionOffline");
                return;
            }

            try
            {
                using var attemptTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(waitForTunnel ? 6 : 12));
                result = await _mihomo.QueryExitIpAsync(attemptTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                result = OperationResult.Fail(Loc.T("Ip.Fail"));
            }

            if (result.Succeeded && IPAddress.TryParse(result.Message, out _))
            {
                ExternalIp = result.Message;
                IpCaption = Loc.T("Ip.CaptionOk");
                await RefreshConnectedServerAsync().ConfigureAwait(true);
                Report(Loc.T("Msg.IpUpdated"));
                return;
            }
        }

        if (!_mihomo.IsRunning)
            return;

        ExternalIp = "—";
        IpCaption = result.Message;
        await RefreshConnectedServerAsync().ConfigureAwait(true);
        Report(result.Message);
    }

    private async Task RefreshConnectedServerAsync()
    {
        if (!IsSubscriptionProfile || !_mihomo.IsRunning)
        {
            ConnectedServer = "";
            return;
        }

        try
        {
            var node = await _mihomo.GetActiveVpnNodeAsync().ConfigureAwait(true);
            ConnectedServer = string.IsNullOrWhiteSpace(node)
                ? ""
                : "· " + (node.Equals(SubscriptionProfile.AutoNodeName, StringComparison.OrdinalIgnoreCase)
                    ? Loc.T("Node.Auto")
                    : node);
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

        if (IsBusy && !_mihomo.IsRunning)
        {
            _connectCts?.Cancel();
            Report(Loc.T("Msg.Canceling"));
            return;
        }

        if (!_mihomo.IsRunning)
        {
            SetStatus(Loc.T("Status.Stopped"), "disconnected");
            Report(Loc.T("Msg.NotRunning"));
            return;
        }

        _disconnecting = true;
        _expectExit = true;
        IsBusy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            var result = await _mihomo.StopAsync();
            Report(result.Message);
            IsConnected = _mihomo.IsRunning;
            ExternalIp = "—";
            ConnectedServer = "";
            IpCaption = Loc.T("Ip.CaptionIdle");
            SetStatus(
                IsConnected ? Loc.T("Status.Running") : Loc.T("Status.Stopped"),
                IsConnected ? "error" : "disconnected");
        }
        finally
        {
            StopTrafficPolling();
            _expectExit = false;
            _disconnecting = false;
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private bool ConfirmFullTunnel()
    {
        if (_rules.GetRules().Count > 0 || _settings.SkipFullTunnelWarning)
            return true;

        var prompt = new SelectiveVpn.FullTunnelPrompt
        {
            Owner = Application.Current?.MainWindow
        };
        if (prompt.ShowDialog() != true)
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

    private bool CanDisconnect() => !_disconnecting && (!IsBusy || IsConnected || _connectCts is not null);

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
        CommandManager.InvalidateRequerySuggested();
    }

    private void RestoreSettings()
    {
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
            LoadTargets(active.Targets);
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
    }

    private void OpenSettings()
    {
        var dialog = new SelectiveVpn.SettingsWindow(
            _settings.LaunchWithWindows,
            _settings.ConnectOnLaunch,
            _settings.Language)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true)
            return;

        var launch = dialog.LaunchWithWindows;
        var connect = dialog.ConnectOnLaunch;
        var language = AppLanguage.Normalize(dialog.SelectedLanguage);
        if (launch != _settings.LaunchWithWindows)
        {
            if (!WindowsStartup.TryApply(launch, out var startupMessage))
            {
                Report(startupMessage);
                return;
            }

            Report(startupMessage);
        }
        else if (launch && !WindowsStartup.TryApply(true, out var refreshMessage))
        {
            Report(refreshMessage);
        }

        var connectChanged = connect != _settings.ConnectOnLaunch;
        var languageChanged = !string.Equals(language, _settings.Language, StringComparison.OrdinalIgnoreCase);
        _settings.LaunchWithWindows = launch;
        _settings.ConnectOnLaunch = connect;
        _settings.Language = language;
        Persist();
        OnPropertyChanged(nameof(ConnectOnLaunch));
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
        if (!persist || !changed)
            return;

        Report(normalized switch
        {
            AppTheme.Dark => Loc.T("Msg.ThemeDark"),
            AppTheme.Green => Loc.T("Msg.ThemeGreen"),
            AppTheme.Oled => Loc.T("Msg.ThemeOled"),
            AppTheme.Amber => Loc.T("Msg.ThemeAmber"),
            AppTheme.Indigo => Loc.T("Msg.ThemeIndigo"),
            _ => Loc.T("Msg.ThemeLight")
        });
        Persist();
    }

    private void Persist()
    {
        try
        {
            var rules = _rules.GetRules();
            var savedTargets = rules
                .Select(rule => new SavedTarget
                {
                    Path = rule.ExecutablePath,
                    Kind = rule.StoredKind,
                    Entries = rule.EntryList.ToList(),
                    ResolvedIps = rule.ResolvedIpList.ToList(),
                    SkipDomainResolve = rule.SkipDomainResolve
                })
                .ToList();
            var active = FindProfile(ConfigPath);
            if (active is not null)
                active.Targets = savedTargets;

            _settings.ConfigPath = string.IsNullOrWhiteSpace(ConfigPath) ? null : ConfigPath;
            _settings.EnginePath = string.IsNullOrWhiteSpace(EnginePath) ? null : EnginePath;
            _settings.Apps = rules.Where(rule => rule.EffectiveKind == RouteKind.File).Select(rule => rule.ExecutablePath).ToList();
            _settings.Targets = active?.Targets ?? savedTargets;
            _settings.Theme = _theme;
            UserSettingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(Loc.T("Msg.SaveSettingsFail"));
        }
    }

    private void SwitchTo(string configPath)
    {
        var profile = FindProfile(configPath);
        if (profile is null)
            return;

        ConfigPath = profile.ConfigPath;
        LoadTargets(profile.Targets);
        ShowExceptListed(profile.ExceptListed);
        MarkActiveProfile();
        Persist();
    }

    private void LoadTargets(IReadOnlyList<SavedTarget> targets)
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
        if (removedMissing)
            Persist();
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
            Report(Loc.T("Msg.ListWillApplyLater"));
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

    private void OnEngineMessage(object? sender, string message) => Dispatch(() => Report(message));

    private void OnEngineStopped(object? sender, EventArgs e) => Dispatch(() =>
    {
        StopTrafficPolling();
        if (_expectExit || !IsConnected)
            return;

        IsConnected = false;
        ExternalIp = "—";
        ConnectedServer = "";
        IpCaption = Loc.T("Ip.CaptionIdle");
        SetStatus(Loc.T("Status.Error"), "error");
        Report(Loc.T("Msg.EngineStopped"));
    });

    private void RefreshLocalizedStrings()
    {
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
