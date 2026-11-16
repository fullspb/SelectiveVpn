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
        ["Ui.AddProfile"] = "Add profile",
        ["Ui.AddByLink"] = "From link",
        ["Ui.RemoveProfile"] = "Remove profile",
        ["Ui.Connect"] = "Connect",
        ["Ui.Disconnect"] = "Disconnect",
        ["Ui.Refresh"] = "Refresh",
        ["Ui.Add"] = "Add",
        ["Ui.Remove"] = "Remove",
        ["Ui.Cancel"] = "Cancel",
        ["Ui.Exit"] = "Quit",
        ["Ui.Show"] = "Show",
        ["Ui.AddProcess"] = "Add process",
        ["Ui.AddApp"] = "Add application",
        ["Ui.AddFolder"] = "Add folder",
        ["Ui.AddList"] = "Add addresses",
        ["Ui.AddProcessTip"] = "Add a running process as a PROCESS-PATH rule",
        ["Ui.AddAppTip"] = "Add a desktop app (.desktop → binary)",
        ["Ui.AddFolderTip"] = "Add a folder as a PROCESS-PATH-REGEX rule",
        ["Ui.AddressWatermark"] = "example.com or 1.2.3.4/32 (one per line)",
        ["Ui.AddressPromptTitle"] = "Address list",
        ["Ui.Browse"] = "Browse…",
        ["Ui.Select"] = "Select",
        ["Ui.Import"] = "Import",
        ["Ui.ApplyCaps"] = "Apply capabilities",
        ["Ui.ApplyCapsTip"] = "Grant CAP_NET_ADMIN to mihomo (pkexec)",
        ["Ui.Teardown"] = "Emergency teardown",
        ["Ui.TeardownTip"] = "Kill leftovers, delete Meta, flush ip prefs 9000–9010",
        ["Ui.LinuxTools"] = "Linux tools",
        ["Ui.ChooseServer"] = "Choose server",
        ["Ui.Language"] = "Language",
        ["Ui.CycleTheme"] = "Cycle Orbit color scheme",
        ["Ui.ImportAmnezia"] = "Add AmneziaWG",
        ["Ui.ImportLink"] = "Import by link / file",
        ["Ui.NoProfile"] = "(no profile)",
        ["Ui.PrivilegesOk"] = "Privileges: CAP_NET_ADMIN / root available",
        ["Ui.PrivilegesNeed"] = "Privileges: need setcap on mihomo (or sudo).",
        ["Ui.ChooseExecutable"] = "Choose executable",
        ["Ui.AddFolderTitle"] = "Add folder",
        ["Ui.ImportAmneziaTitle"] = "Import AmneziaWG config",

        ["AppPicker.Title"] = "Add application",
        ["AppPicker.Heading"] = "Choose a desktop application",
        ["AppPicker.Hint"] = "Applications come from .desktop entries. The rule uses the resolved binary path.",
        ["ProcessPicker.Title"] = "Add process",
        ["ProcessPicker.Heading"] = "Choose a running process",
        ["ProcessPicker.Hint"] = "Select a program that is running now. The rule binds to its file and stays after restart.",
        ["Import.Title"] = "Add by link",
        ["Import.Heading"] = "Import by link or file",
        ["Import.Hint"] = "Paste an https subscription URL, vless/vmess/trojan/ss links (one per line), or load a file.",
        ["Servers.Title"] = "Subscription servers",
        ["Servers.Heading"] = "Servers",
        ["Servers.Hint"] = "Pick a node for this subscription profile.",
        ["Servers.Ping"] = "Ping",
        ["Servers.PingTip"] = "Measure delays for listed nodes (requires active tunnel).",
        ["Servers.PingDisabledTip"] = "Connect this subscription first to ping nodes.",
        ["Servers.Name"] = "Name",
        ["Servers.Delay"] = "Delay",

        ["Orbit.ConnectionEyebrow"] = "YOUR SECURE SPACE",
        ["Orbit.RoutingEyebrow"] = "TRAFFIC BY YOUR RULES",
        ["Orbit.RoutingTitle"] = "Two routes.",
        ["Orbit.ModeInclude"] = "Only selected traffic through VPN",
        ["Orbit.ModeExclude"] = "All traffic through VPN except selected",
        ["Orbit.ThroughVpn"] = "Through VPN",
        ["Orbit.Direct"] = "Direct",
        ["Orbit.BypassVpn"] = "Direct",
        ["Orbit.AllTraffic"] = "All traffic goes through VPN",
        ["Orbit.RemainingVpn"] = "Other traffic goes through VPN",
        ["Orbit.RemainingDirect"] = "Other traffic goes directly",
        ["Orbit.IncludeEmpty"] = "Add an application, folder, or addresses. While the list is empty, traffic stays direct.",
        ["Orbit.EmptyRules"] = "Add an application, folder, or addresses. While the list is empty, traffic stays direct.",
        ["Orbit.SeparateListsHint"] = "Each mode keeps its own list of rules.",
        ["Orbit.AddTargets"] = "Application, folder, or addresses",
        ["Orbit.FromFile"] = "From file",
        ["Orbit.ByLink"] = "From link",
        ["Orbit.RulesFor"] = "Rules for profile {0}",
        ["Orbit.ChooseProfile"] = "Choose a profile",
        ["Orbit.Connected"] = "Connected",
        ["Orbit.Disconnected"] = "Disconnected",
        ["Orbit.Connecting"] = "Connecting…",
        ["Orbit.Stopping"] = "Disconnecting…",
        ["Orbit.Subscription"] = "Subscription",
        ["Orbit.Log"] = "Log",
        ["Orbit.ShowLog"] = "Open log",
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
        ["Orbit.TargetCount"] = "{0}",
        ["Orbit.Theme"] = "Theme",
        ["Orbit.ThemeOrbit"] = "Orbit",
        ["Orbit.ThemeAurora"] = "Aurora",
        ["Orbit.ThemeOcean"] = "Ocean",
        ["Orbit.ThemePlum"] = "Plum",
        ["Orbit.ThemeSunset"] = "Sunset",
        ["Orbit.ThemeGraphite"] = "Graphite",
        ["Orbit.Error"] = "Error",
        ["Orbit.ProfileMenu"] = "Profile actions",

        ["Hint.ExceptListed"] =
            "All traffic goes through VPN except apps, folders and addresses in this list. An empty list sends everything through VPN.",
        ["Hint.IncludeListed"] =
            "Apps, folders and addresses in this list go through VPN. Everything else goes direct. An empty list keeps traffic direct.",
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
        ["Ui.AddProfile"] = "Добавить профиль",
        ["Ui.AddByLink"] = "По ссылке",
        ["Ui.RemoveProfile"] = "Убрать профиль",
        ["Ui.Connect"] = "Подключить",
        ["Ui.Disconnect"] = "Отключить",
        ["Ui.Refresh"] = "Обновить",
        ["Ui.Add"] = "Добавить",
        ["Ui.Remove"] = "Удалить",
        ["Ui.Cancel"] = "Отмена",
        ["Ui.Exit"] = "Выход",
        ["Ui.Show"] = "Показать",
        ["Ui.AddProcess"] = "Добавить процесс",
        ["Ui.AddApp"] = "Добавить приложение",
        ["Ui.AddFolder"] = "Добавить папку",
        ["Ui.AddList"] = "Добавить список",
        ["Ui.AddProcessTip"] = "Добавить запущенный процесс как правило PROCESS-PATH",
        ["Ui.AddAppTip"] = "Добавить приложение рабочего стола (.desktop → бинарник)",
        ["Ui.AddFolderTip"] = "Добавить папку как правило PROCESS-PATH-REGEX",
        ["Ui.AddressWatermark"] = "example.com или 1.2.3.4/32 (по одному в строке)",
        ["Ui.AddressPromptTitle"] = "Список адресов",
        ["Ui.Browse"] = "Обзор…",
        ["Ui.Select"] = "Выбрать",
        ["Ui.Import"] = "Импортировать",
        ["Ui.ApplyCaps"] = "Выдать права",
        ["Ui.ApplyCapsTip"] = "Выдать CAP_NET_ADMIN для mihomo (pkexec)",
        ["Ui.Teardown"] = "Аварийный сброс",
        ["Ui.TeardownTip"] = "Убить остатки, удалить Meta, сбросить ip prefs 9000–9010",
        ["Ui.LinuxTools"] = "Средства Linux",
        ["Ui.ChooseServer"] = "Выбрать сервер",
        ["Ui.Language"] = "Язык",
        ["Ui.CycleTheme"] = "Сменить цветовую схему Orbit",
        ["Ui.ImportAmnezia"] = "Добавить AmneziaWG",
        ["Ui.ImportLink"] = "Импорт по ссылке / из файла",
        ["Ui.NoProfile"] = "(нет профиля)",
        ["Ui.PrivilegesOk"] = "Права: CAP_NET_ADMIN / root доступны",
        ["Ui.PrivilegesNeed"] = "Права: нужен setcap на mihomo (или sudo).",
        ["Ui.ChooseExecutable"] = "Выбрать исполняемый файл",
        ["Ui.AddFolderTitle"] = "Добавить папку",
        ["Ui.ImportAmneziaTitle"] = "Импорт конфига AmneziaWG",

        ["AppPicker.Title"] = "Добавить приложение",
        ["AppPicker.Heading"] = "Выберите приложение",
        ["AppPicker.Hint"] = "Список из .desktop. Правило использует путь к бинарнику.",
        ["ProcessPicker.Title"] = "Добавить процесс",
        ["ProcessPicker.Heading"] = "Выберите запущенный процесс",
        ["ProcessPicker.Hint"] = "Выберите программу, которая запущена сейчас. Правило привяжется к файлу и сохранится после перезапуска.",
        ["Import.Title"] = "Добавить по ссылке",
        ["Import.Heading"] = "Импорт по ссылке или из файла",
        ["Import.Hint"] = "Вставьте https-ссылку подписки, ссылки vless/vmess/trojan/ss (по одной в строке) или загрузите файл.",
        ["Servers.Title"] = "Серверы подписки",
        ["Servers.Heading"] = "Серверы",
        ["Servers.Hint"] = "Выберите узел для этого профиля подписки.",
        ["Servers.Ping"] = "Пинг",
        ["Servers.PingTip"] = "Измерить задержку узлов (нужен активный туннель).",
        ["Servers.PingDisabledTip"] = "Сначала подключите эту подписку, чтобы пинговать узлы.",
        ["Servers.Name"] = "Имя",
        ["Servers.Delay"] = "Задержка",

        ["Orbit.ConnectionEyebrow"] = "ВАШЕ ЗАЩИЩЁННОЕ ПРОСТРАНСТВО",
        ["Orbit.RoutingEyebrow"] = "ТРАФИК ПО ВАШИМ ПРАВИЛАМ",
        ["Orbit.RoutingTitle"] = "Два маршрута.",
        ["Orbit.ModeInclude"] = "Только выбранное через VPN",
        ["Orbit.ModeExclude"] = "Всё через VPN, кроме выбранного",
        ["Orbit.ThroughVpn"] = "Через VPN",
        ["Orbit.Direct"] = "Напрямую",
        ["Orbit.BypassVpn"] = "Напрямую",
        ["Orbit.AllTraffic"] = "Весь трафик идёт через VPN",
        ["Orbit.RemainingVpn"] = "Остальной трафик — через VPN",
        ["Orbit.RemainingDirect"] = "Остальной трафик — напрямую",
        ["Orbit.IncludeEmpty"] = "Добавьте приложение, папку или адреса. Пока список пуст, трафик идёт напрямую.",
        ["Orbit.EmptyRules"] = "Добавьте приложение, папку или адреса. Пока список пуст, трафик идёт напрямую.",
        ["Orbit.SeparateListsHint"] = "У каждого режима свой список правил.",
        ["Orbit.AddTargets"] = "Приложение, папка или адреса",
        ["Orbit.FromFile"] = "Из файла",
        ["Orbit.ByLink"] = "По ссылке",
        ["Orbit.RulesFor"] = "Правила профиля {0}",
        ["Orbit.ChooseProfile"] = "Выберите профиль",
        ["Orbit.Connected"] = "Подключено",
        ["Orbit.Disconnected"] = "Отключено",
        ["Orbit.Connecting"] = "Подключение…",
        ["Orbit.Stopping"] = "Отключение…",
        ["Orbit.Subscription"] = "Подписка",
        ["Orbit.Log"] = "Журнал",
        ["Orbit.ShowLog"] = "Открыть журнал",
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
        ["Orbit.TargetCount"] = "{0}",
        ["Orbit.Theme"] = "Тема",
        ["Orbit.ThemeOrbit"] = "Орбита",
        ["Orbit.ThemeAurora"] = "Северное сияние",
        ["Orbit.ThemeOcean"] = "Океан",
        ["Orbit.ThemePlum"] = "Слива",
        ["Orbit.ThemeSunset"] = "Закат",
        ["Orbit.ThemeGraphite"] = "Графит",
        ["Orbit.Error"] = "Ошибка",
        ["Orbit.ProfileMenu"] = "Действия с профилем",

        ["Hint.ExceptListed"] =
            "Через VPN идёт весь трафик, кроме приложений, папок и адресов из этого списка. Пустой список отправляет через VPN всё.",
        ["Hint.IncludeListed"] =
            "Приложения, папки и адреса из списка идут через VPN. Остальной трафик — напрямую. Пока список пуст, трафик остаётся напрямую.",
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
