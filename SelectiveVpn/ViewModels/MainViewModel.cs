using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net;
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
    private string _theme = AppTheme.Light;
    private string _statusText = "Маршрутизация остановлена";
    private string _statusKind = "disconnected";
    private string _statusDetail = "Маршрутизация остановлена. Приложения ходят напрямую.";
    private string _profileName = "Профиль не выбран";
    private string _engineLabel = "mihomo — скачается при подключении";
    private string _externalIp = "—";
    private string _ipCaption = "Проверка идёт через VPN только после запуска маршрутизации.";
    private string _incomingTraffic = "0 Б";
    private string _outgoingTraffic = "0 Б";
    private bool _exceptListed;
    private bool _suppressExceptListed;
    private CancellationTokenSource? _connectCts;
    private CancellationTokenSource? _trafficCts;

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
        RemoveProfileCommand = new RelayCommand(RemoveProfile, () => !IsBusy && Profiles.Count > 0, LogUnexpected);
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

        var rights = WindowsElevation.IsElevated()
            ? "Права администратора уже есть."
            : "Для TUN Windows запросит права администратора.";
        Report($"Интерфейс запущен. Туннель не создан. {rights}");
        if (settingsExisted)
            Report("Сохранённые настройки загружены.");
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
            }
        }
    }

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
        ? "Через VPN идёт весь трафик, кроме приложений, папок и списков адресов из этого списка. Пустой список отправляет через VPN всё."
        : "Файлы, папки и списки адресов этого профиля идут через VPN. Пустой список отправляет через VPN весь трафик.";

    public string AppsFooter => ExceptListed
        ? "Список обходит VPN. Всё остальное идёт через VPN. Программы вне выбранной папки добавляются отдельно."
        : "Если список пуст, через VPN идёт весь трафик. Программы вне выбранной папки добавляются отдельно.";

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

    public RelayCommand RemoveProfileCommand { get; }

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
        Report("Автоподключение при запуске.");
        return ConnectAsync();
    }

    public Task DisconnectFromUserAsync() => DisconnectAsync();

    public async Task ConnectFromSavedSessionAsync()
    {
        if (!ConnectSessionStore.TryTake(out var configPath, out var targets, out var exceptListed))
        {
            Report("Не удалось восстановить выбранные файлы после запроса прав.");
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
            Report("Сохранённый сеанс не содержит конфигурацию. Туннель не запускался.");
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
            Report("Некорректный путь к конфигурации.");
            return;
        }

        if (!File.Exists(fullPath))
        {
            Report("Файл конфигурации не найден.");
            return;
        }

        Persist();
        var created = FindProfile(fullPath) is null;
        if (created)
        {
            _settings.Profiles.Add(new SavedProfile { ConfigPath = fullPath, Targets = [] });
            Profiles.Add(new VpnProfileOption(fullPath));
        }

        SwitchTo(fullPath);
        if (!created)
            return;

        Report($"Добавлен профиль «{ProfileName}». Список приложений и папок у него свой и пока пуст.");
        if (IsConnected)
            Report("Запущенный туннель останется прежним, пока маршрутизацию не остановить и не запустить снова.");
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

    private void RemoveProfile()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(ConfigPath))
            return;

        var name = ProfileName;
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

        Report($"Профиль «{name}» убран из списка. Файл конфигурации на диске не удалён.");
        CommandManager.InvalidateRequerySuggested();
    }

    private void BrowseEngine()
    {
        var path = _files.PickEngine();
        if (string.IsNullOrWhiteSpace(path))
            return;

        EnginePath = path;
        UpdateEngineLabel();
        Report($"Выбрано ядро «{Path.GetFileName(path)}». Оно будет использовано при следующем подключении.");
        Persist();
    }

    private void CheckConfig()
    {
        if (string.IsNullOrWhiteSpace(ConfigPath))
        {
            Report("Файл конфигурации не выбран. Проверка не выполнялась.");
            return;
        }

        var read = AmneziaWgConfigReader.ReadFile(ConfigPath);
        if (!read.Succeeded || read.Profile is null)
        {
            Report(string.IsNullOrWhiteSpace(read.Message)
                ? "Конфигурация не подошла. Подключение не запускалось."
                : read.Message);
            return;
        }

        Report($"Конфиг подходит: {read.Profile.Describe()}. Подключение не запускалось.");
    }

    private void AddApp()
    {
        var path = _files.PickExecutable();
        if (string.IsNullOrWhiteSpace(path))
            return;

        var result = _rules.AddExecutable(path);
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
            SetStatus("Ошибка", "error");
            Report("Подключение не выполнено: не выбран файл конфигурации AmneziaWG.");
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
                SetStatus("Ошибка", "error");
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
        IpCaption = "Проверка идёт через VPN только после запуска маршрутизации.";
        SetStatus("Запуск…", "connecting");
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
                SetStatus("Маршрутизация запущена", "connected");
            else if (result.Message.Contains("отменено", StringComparison.OrdinalIgnoreCase))
                SetStatus("Маршрутизация остановлена", "disconnected");
            else
                SetStatus("Ошибка", "error");
        }
        catch (OperationCanceledException)
        {
            IsConnected = false;
            SetStatus("Маршрутизация остановлена", "disconnected");
            Report("Подключение отменено. Туннель не запускался.");
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
            IncomingTraffic = "0 Б";
            OutgoingTraffic = "0 Б";
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

        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
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
            IpCaption = "Маршрутизация не запущена. Запрос не отправлялся.";
            return;
        }

        IpCaption = "Запрос идёт через VPN…";
        var attempts = waitForTunnel ? 4 : 1;
        OperationResult result = OperationResult.Fail("Не удалось получить внешний IP через VPN.");
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
                IpCaption = "Маршрутизация не запущена. Запрос не отправлялся.";
                return;
            }

            try
            {
                using var attemptTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(waitForTunnel ? 6 : 12));
                result = await _mihomo.QueryExitIpAsync(attemptTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                result = OperationResult.Fail("Не удалось получить внешний IP через VPN.");
            }

            if (result.Succeeded && IPAddress.TryParse(result.Message, out _))
            {
                ExternalIp = result.Message;
                IpCaption = "Внешний IP VPN · проверка через api.ipify.org";
                Report("Внешний IP обновлён.");
                return;
            }
        }

        if (!_mihomo.IsRunning)
            return;

        ExternalIp = "—";
        IpCaption = result.Message;
        Report(result.Message);
    }

    private async Task DisconnectAsync()
    {
        if (_disconnecting)
            return;

        if (IsBusy && !_mihomo.IsRunning)
        {
            _connectCts?.Cancel();
            Report("Отмена подключения…");
            return;
        }

        if (!_mihomo.IsRunning)
        {
            SetStatus("Маршрутизация остановлена", "disconnected");
            Report("Соединение не запущено.");
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
            IpCaption = "Проверка идёт через VPN только после запуска маршрутизации.";
            SetStatus(
                IsConnected ? "Маршрутизация запущена" : "Маршрутизация остановлена",
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
            Report("Подключение отменено. Туннель не запускался.");
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
                Report("Сохранённый файл конфигурации не найден. Путь оставлен.");
            LoadTargets(active.Targets);
            MarkActiveProfile();
        }

        if (!string.IsNullOrWhiteSpace(_settings.EnginePath))
        {
            EnginePath = _settings.EnginePath;
            if (!File.Exists(_settings.EnginePath))
                Report("Сохранённое ядро не найдено. Путь оставлен.");
        }

        UpdateEngineLabel();
        if (_settings.LaunchWithWindows && !WindowsStartup.TryApply(true, out var startupMessage))
            Report(startupMessage);
    }

    private void OpenSettings()
    {
        var dialog = new SelectiveVpn.SettingsWindow(_settings.LaunchWithWindows, _settings.ConnectOnLaunch)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true)
            return;

        var launch = dialog.LaunchWithWindows;
        var connect = dialog.ConnectOnLaunch;
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
        _settings.LaunchWithWindows = launch;
        _settings.ConnectOnLaunch = connect;
        Persist();
        OnPropertyChanged(nameof(ConnectOnLaunch));
        if (!connectChanged)
            return;

        Report(connect
            ? "Автоподключение при запуске включено. Туннель поднимется при следующем открытии приложения."
            : "Автоподключение при запуске выключено.");
    }

    private void ApplyTheme(string theme, bool persist)
    {
        var normalized = AppTheme.Normalize(theme);
        var changed = !string.Equals(_theme, normalized, StringComparison.Ordinal);
        _theme = normalized;
        IsDarkTheme = AppTheme.UsesDarkChrome(normalized);
        OnPropertyChanged(nameof(ThemeId));
        AppTheme.Apply(normalized);
        if (!persist || !changed)
            return;

        Report(normalized switch
        {
            AppTheme.Dark => "Включена тёмная тема.",
            AppTheme.Green => "Включена зелёная тема.",
            AppTheme.Oled => "Включена тема OLED.",
            AppTheme.Amber => "Включена янтарная тема.",
            AppTheme.Indigo => "Включена индиго-тема.",
            _ => "Включена светлая тема."
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
            Report("Не удалось сохранить настройки.");
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
        ProfileName = string.IsNullOrWhiteSpace(ConfigPath)
            ? "Профиль не выбран"
            : Path.GetFileNameWithoutExtension(ConfigPath);
    }

    private void UpdateEngineLabel()
    {
        EngineLabel = string.IsNullOrWhiteSpace(EnginePath)
            ? "mihomo — скачается при подключении"
            : Path.GetFileName(EnginePath);
    }

    private void NoteLiveListChange()
    {
        if (IsConnected)
            Report("Список изменится после остановки и нового запуска маршрутизации.");
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
        => Report("Ошибка интерфейса. Туннель из-за неё не менялся.");

    private void OnEngineMessage(object? sender, string message) => Dispatch(() => Report(message));

    private void OnEngineStopped(object? sender, EventArgs e) => Dispatch(() =>
    {
        StopTrafficPolling();
        if (_expectExit || !IsConnected)
            return;

        IsConnected = false;
        ExternalIp = "—";
        IpCaption = "Проверка идёт через VPN только после запуска маршрутизации.";
        SetStatus("Ошибка", "error");
        Report("Процесс Mihomo завершился. Туннель остановлен.");
    });

    private void SetStatus(string text, string kind)
    {
        StatusText = text;
        StatusKind = kind;
        StatusDetail = kind switch
        {
            "connected" => _rules.GetRules().Count == 0
                ? "Маршрутизация запущена. Весь трафик идёт через VPN."
                : ExceptListed
                    ? "Маршрутизация запущена. Весь трафик идёт через VPN, кроме списка."
                    : "Маршрутизация запущена. Выбранные приложения и папки идут через VPN.",
            "connecting" => "Поднимаю TUN и запускаю Mihomo.",
            "error" => "Маршрутизация не запущена.",
            _ => "Маршрутизация остановлена. Приложения ходят напрямую."
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
