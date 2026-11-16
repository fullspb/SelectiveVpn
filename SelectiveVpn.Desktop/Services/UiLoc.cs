using System.Globalization;

namespace SelectiveVpn.Desktop.Services;

/// <summary>
/// Lightweight RU/EN strings for Avalonia Orbit, matching WPF Localization/Lang.*.xaml tone.
/// System culture picks the default; CycleLanguage toggles RU ↔ EN.
/// </summary>
internal static class UiLoc
{
    public const string Russian = "ru";
    public const string English = "en";

    private static readonly Dictionary<string, string> En = BuildEnglish();
    private static readonly Dictionary<string, string> Ru = BuildRussian();

    public static event EventHandler? LanguageChanged;

    public static string CurrentCode { get; private set; } = Resolve(null);

    public static void Apply(string? language)
    {
        var resolved = Resolve(language);
        if (string.Equals(CurrentCode, resolved, StringComparison.OrdinalIgnoreCase))
            return;
        CurrentCode = resolved;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void Cycle()
        => Apply(CurrentCode == Russian ? English : Russian);

    public static string Resolve(string? language)
    {
        if (string.Equals(language, Russian, StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "ru-ru", StringComparison.OrdinalIgnoreCase))
            return Russian;
        if (string.Equals(language, English, StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "en-us", StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "en-gb", StringComparison.OrdinalIgnoreCase))
            return English;

        var culture = CultureInfo.CurrentUICulture;
        while (true)
        {
            if (culture.TwoLetterISOLanguageName.Equals(Russian, StringComparison.OrdinalIgnoreCase))
                return Russian;
            if (Equals(culture, culture.Parent) || string.IsNullOrEmpty(culture.Parent.Name))
                break;
            culture = culture.Parent;
        }

        return English;
    }

    public static string T(string key)
    {
        var map = CurrentCode == Russian ? Ru : En;
        if (map.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            return value;
        if (En.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
            return value;
        return key;
    }

    public static string Tf(string key, params object?[] args)
    {
        var format = T(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    private static Dictionary<string, string> BuildEnglish() => new(StringComparer.Ordinal)
    {
        ["Ui.Profiles"] = "PROFILES",
        ["Ui.AddProfile"] = "Add AmneziaWG",
        ["Ui.AddByLink"] = "By link / file",
        ["Ui.RemoveProfile"] = "Remove selected profile",
        ["Ui.Connect"] = "Connect",
        ["Ui.Disconnect"] = "Disconnect",
        ["Ui.Refresh"] = "Refresh",
        ["Ui.Add"] = "Add",
        ["Ui.Remove"] = "Remove",
        ["Ui.Cancel"] = "Cancel",
        ["Ui.Exit"] = "Quit",
        ["Ui.Show"] = "Show",
        ["Ui.AddProcess"] = "Process",
        ["Ui.AddApp"] = "App",
        ["Ui.AddFolder"] = "Folder",
        ["Ui.AddProcessTip"] = "Add a running process as a PROCESS-PATH rule",
        ["Ui.AddAppTip"] = "Add a desktop app (.desktop → binary)",
        ["Ui.AddFolderTip"] = "Add a folder as a PROCESS-PATH-REGEX rule",
        ["Ui.AddressWatermark"] = "example.com or 1.2.3.4/32",
        ["Ui.ApplyCaps"] = "Apply capabilities",
        ["Ui.ApplyCapsTip"] = "Grant CAP_NET_ADMIN to mihomo (pkexec)",
        ["Ui.Teardown"] = "Emergency teardown",
        ["Ui.TeardownTip"] = "Kill leftovers, delete Meta, flush ip prefs 9000–9010",
        ["Ui.ChooseServer"] = "Choose server",
        ["Ui.Language"] = "Language",
        ["Ui.CycleTheme"] = "Cycle Orbit color scheme",
        ["Ui.ImportAmnezia"] = "Add AmneziaWG",
        ["Ui.ImportLink"] = "Import by link / file",
        ["Ui.NoProfile"] = "(no profile)",
        ["Ui.PrivilegesOk"] = "Privileges: CAP_NET_ADMIN / root available",
        ["Ui.PrivilegesNeed"] = "Privileges: need setcap on mihomo (or sudo). See Apply capabilities.",

        ["Orbit.ConnectionEyebrow"] = "YOUR SECURE SPACE",
        ["Orbit.RoutingEyebrow"] = "TRAFFIC BY YOUR RULES",
        ["Orbit.RoutingTitle"] = "Two routes.",
        ["Orbit.ModeInclude"] = "Only selected through VPN",
        ["Orbit.ModeExclude"] = "All via VPN except selected",
        ["Orbit.ThroughVpn"] = "Through VPN",
        ["Orbit.Direct"] = "Direct",
        ["Orbit.BypassVpn"] = "Bypass VPN",
        ["Orbit.AllTraffic"] = "All traffic goes through VPN.",
        ["Orbit.RemainingVpn"] = "Listed items go direct. Other traffic goes through VPN.",
        ["Orbit.RemainingDirect"] = "Listed items go through VPN. Other traffic goes direct.",
        ["Orbit.IncludeEmpty"] = "List empty — traffic stays direct until you add domains, IPs, or apps.",
        ["Orbit.SeparateListsHint"] = "Each mode keeps its own list of rules.",
        ["Orbit.RulesFor"] = "Rules for profile {0}",
        ["Orbit.ChooseProfile"] = "Import a profile to edit rules.",
        ["Orbit.Connected"] = "Connected",
        ["Orbit.Disconnected"] = "Disconnected",
        ["Orbit.Connecting"] = "Connecting…",
        ["Orbit.Stopping"] = "Disconnecting…",
        ["Orbit.Subscription"] = "Subscription",
        ["Orbit.Log"] = "Log",
        ["Orbit.ShowLog"] = "Show / hide log",
        ["Orbit.HideLog"] = "Hide log",
        ["Orbit.Server"] = "Server",
        ["Orbit.ExternalIp"] = "External IP",
        ["Orbit.ConnectionDuration"] = "Connected for",
        ["Orbit.Traffic"] = "Traffic",
        ["Orbit.Received"] = "Received",
        ["Orbit.Sent"] = "Sent",
        ["Orbit.Reconnect"] = "Reconnect",
        ["Orbit.ManageServers"] = "Choose server",
        ["Orbit.RemoveRule"] = "Remove rule",
        ["Orbit.TargetCount"] = "{0} in list",
        ["Orbit.Theme"] = "Theme",
        ["Orbit.ThemeOrbit"] = "Orbit",
        ["Orbit.ThemeAurora"] = "Aurora",
        ["Orbit.ThemeOcean"] = "Ocean",
        ["Orbit.ThemePlum"] = "Plum",
        ["Orbit.ThemeSunset"] = "Sunset",
        ["Orbit.ThemeGraphite"] = "Graphite",
        ["Orbit.Error"] = "Error",

        // Clear user-facing rule help (no MATCH/DIRECT jargon). Linux empty-include stays direct.
        ["Hint.ExceptListed"] =
            "All traffic goes through VPN except domains, IPs, processes, and folders in this list. An empty list sends everything through VPN.",
        ["Hint.IncludeListed"] =
            "Domains, IPs, processes, and folders in this list go through VPN. Everything else goes direct. An empty list keeps traffic direct.",
        ["Log.ModeExclude"] = "Mode: all traffic via VPN except the list.",
        ["Log.ModeInclude"] = "Mode: only listed traffic via VPN; empty list stays direct.",
        ["Kind.Domain"] = "Domain",
        ["Kind.Ip"] = "IP",
        ["Kind.Process"] = "Process",
        ["Kind.Folder"] = "Folder",
        ["Kind.Rule"] = "Rule",
        ["Unit.B"] = "B",
        ["Unit.KB"] = "KB",
        ["Unit.MB"] = "MB",
        ["Unit.GB"] = "GB",
        ["Unit.TB"] = "TB",
    };

    private static Dictionary<string, string> BuildRussian() => new(StringComparer.Ordinal)
    {
        ["Ui.Profiles"] = "ПРОФИЛИ",
        ["Ui.AddProfile"] = "Добавить AmneziaWG",
        ["Ui.AddByLink"] = "По ссылке / из файла",
        ["Ui.RemoveProfile"] = "Убрать выбранный профиль",
        ["Ui.Connect"] = "Подключить",
        ["Ui.Disconnect"] = "Отключить",
        ["Ui.Refresh"] = "Обновить",
        ["Ui.Add"] = "Добавить",
        ["Ui.Remove"] = "Удалить",
        ["Ui.Cancel"] = "Отмена",
        ["Ui.Exit"] = "Выход",
        ["Ui.Show"] = "Показать",
        ["Ui.AddProcess"] = "Процесс",
        ["Ui.AddApp"] = "Приложение",
        ["Ui.AddFolder"] = "Папка",
        ["Ui.AddProcessTip"] = "Добавить запущенный процесс как правило PROCESS-PATH",
        ["Ui.AddAppTip"] = "Добавить приложение рабочего стола (.desktop → бинарник)",
        ["Ui.AddFolderTip"] = "Добавить папку как правило PROCESS-PATH-REGEX",
        ["Ui.AddressWatermark"] = "example.com или 1.2.3.4/32",
        ["Ui.ApplyCaps"] = "Выдать права",
        ["Ui.ApplyCapsTip"] = "Выдать CAP_NET_ADMIN для mihomo (pkexec)",
        ["Ui.Teardown"] = "Аварийный сброс",
        ["Ui.TeardownTip"] = "Убить остатки, удалить Meta, сбросить ip prefs 9000–9010",
        ["Ui.ChooseServer"] = "Выбрать сервер",
        ["Ui.Language"] = "Язык",
        ["Ui.CycleTheme"] = "Сменить цветовую схему Orbit",
        ["Ui.ImportAmnezia"] = "Добавить AmneziaWG",
        ["Ui.ImportLink"] = "Импорт по ссылке / из файла",
        ["Ui.NoProfile"] = "(нет профиля)",
        ["Ui.PrivilegesOk"] = "Права: CAP_NET_ADMIN / root доступны",
        ["Ui.PrivilegesNeed"] = "Права: нужен setcap на mihomo (или sudo). См. «Выдать права».",

        ["Orbit.ConnectionEyebrow"] = "ВАШЕ ЗАЩИЩЁННОЕ ПРОСТРАНСТВО",
        ["Orbit.RoutingEyebrow"] = "ТРАФИК ПО ВАШИМ ПРАВИЛАМ",
        ["Orbit.RoutingTitle"] = "Два маршрута.",
        ["Orbit.ModeInclude"] = "Только выбранное через VPN",
        ["Orbit.ModeExclude"] = "Всё через VPN, кроме выбранного",
        ["Orbit.ThroughVpn"] = "Через VPN",
        ["Orbit.Direct"] = "Напрямую",
        ["Orbit.BypassVpn"] = "В обход VPN",
        ["Orbit.AllTraffic"] = "Весь трафик идёт через VPN.",
        ["Orbit.RemainingVpn"] = "Список идёт напрямую. Остальной трафик — через VPN.",
        ["Orbit.RemainingDirect"] = "Список идёт через VPN. Остальной трафик — напрямую.",
        ["Orbit.IncludeEmpty"] = "Список пуст — трафик идёт напрямую, пока не добавите домены, IP или приложения.",
        ["Orbit.SeparateListsHint"] = "У каждого режима свой список правил.",
        ["Orbit.RulesFor"] = "Правила профиля {0}",
        ["Orbit.ChooseProfile"] = "Импортируйте профиль, чтобы править правила.",
        ["Orbit.Connected"] = "Подключено",
        ["Orbit.Disconnected"] = "Отключено",
        ["Orbit.Connecting"] = "Подключение…",
        ["Orbit.Stopping"] = "Отключение…",
        ["Orbit.Subscription"] = "Подписка",
        ["Orbit.Log"] = "Журнал",
        ["Orbit.ShowLog"] = "Показать / скрыть журнал",
        ["Orbit.HideLog"] = "Скрыть журнал",
        ["Orbit.Server"] = "Сервер",
        ["Orbit.ExternalIp"] = "Внешний IP",
        ["Orbit.ConnectionDuration"] = "Время в VPN",
        ["Orbit.Traffic"] = "Трафик",
        ["Orbit.Received"] = "Получено",
        ["Orbit.Sent"] = "Отправлено",
        ["Orbit.Reconnect"] = "Переподключить",
        ["Orbit.ManageServers"] = "Выбрать сервер",
        ["Orbit.RemoveRule"] = "Удалить правило",
        ["Orbit.TargetCount"] = "В списке: {0}",
        ["Orbit.Theme"] = "Тема",
        ["Orbit.ThemeOrbit"] = "Орбита",
        ["Orbit.ThemeAurora"] = "Северное сияние",
        ["Orbit.ThemeOcean"] = "Океан",
        ["Orbit.ThemePlum"] = "Слива",
        ["Orbit.ThemeSunset"] = "Закат",
        ["Orbit.ThemeGraphite"] = "Графит",
        ["Orbit.Error"] = "Ошибка",

        ["Hint.ExceptListed"] =
            "Через VPN идёт весь трафик, кроме доменов, IP, процессов и папок из этого списка. Пустой список отправляет через VPN всё.",
        ["Hint.IncludeListed"] =
            "Домены, IP, процессы и папки из списка идут через VPN. Остальной трафик — напрямую. Пока список пуст, трафик остаётся напрямую.",
        ["Log.ModeExclude"] = "Режим: весь трафик через VPN, кроме списка.",
        ["Log.ModeInclude"] = "Режим: через VPN только список; пустой список остаётся напрямую.",
        ["Kind.Domain"] = "Домен",
        ["Kind.Ip"] = "IP",
        ["Kind.Process"] = "Процесс",
        ["Kind.Folder"] = "Папка",
        ["Kind.Rule"] = "Правило",
        ["Unit.B"] = "Б",
        ["Unit.KB"] = "КБ",
        ["Unit.MB"] = "МБ",
        ["Unit.GB"] = "ГБ",
        ["Unit.TB"] = "ТБ",
    };
}
