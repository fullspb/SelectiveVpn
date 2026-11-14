using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace SelectiveVpn.Engine;

public static class ConfigSubscriptionImporter
{
    public const int MaxBytes = 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(25);

    private static readonly HashSet<string> ShareSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "vless", "vmess", "trojan", "ss", "ssr", "hysteria", "hysteria2", "hy2",
        "tuic", "wireguard", "wg", "socks", "socks5", "anytls", "mieru"
    };

    private static readonly Regex ClashField = new(
        @"^[ \t-]*(?<key>[A-Za-z0-9_-]+):[ \t]*(?<value>.*)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static string ProfilesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SelectiveVpn",
        "profiles");

    /// <summary>Most links one pasted profile may hold.</summary>
    public const int MaxShareLinks = 200;

    /// <summary>
    /// Imports whatever was pasted into the "by link" dialog: server links such as vless://…
    /// (one per line) become a profile on the spot, anything else is treated as an http(s) URL.
    /// </summary>
    public static Task<ConfigImportResult> ImportAsync(
        string input,
        string? displayName = null,
        HttpMessageHandler? handler = null,
        string? hardwareId = null,
        string? profilesDirectory = null,
        CancellationToken cancellationToken = default)
        => LooksLikeShareLinks(input)
            ? Task.FromResult(ImportShareLinks(input, displayName, profilesDirectory))
            : ImportFromUrlAsync(input, displayName, handler, hardwareId, profilesDirectory, cancellationToken);

    /// <summary>Cheap check for the dialog before it starts importing.</summary>
    public static bool TryValidateInput(string? input, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(input))
        {
            error = CoreText.T("Core.Import.PasteLink", "Вставьте ссылку на конфигурацию или подписку.");
            return false;
        }

        return LooksLikeShareLinks(input) || TryNormalizeUrl(input, out _, out error);
    }

    /// <summary>True when the first meaningful line is a server link (vless://, vmess://, trojan://, ss://…).</summary>
    public static bool LooksLikeShareLinks(string? input)
    {
        foreach (var rawLine in (input ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            return IsShareLink(line);
        }

        return false;
    }

    public static bool IsShareLink(string? line)
        => !string.IsNullOrWhiteSpace(line) && TryParseShareScheme(line.Trim(), out _);

    /// <summary>
    /// Saves pasted server links as a profile. Mihomo parses the protocols; this only checks that
    /// every line is a link and that vless/trojan links carry an identifier, a host and a port.
    /// </summary>
    public static ConfigImportResult ImportShareLinks(
        string input,
        string? displayName = null,
        string? profilesDirectory = null)
    {
        var links = new List<string>();
        var protocols = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var usable = 0;
        var number = 0;
        foreach (var rawLine in StripBom(input ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            number++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (!TryParseShareScheme(line, out var scheme))
            {
                return ConfigImportResult.Fail(CoreText.F("Core.Import.BadLinkLine", "Строка {0} не похожа на ссылку сервера (vless://, vmess://, trojan://, ss://).",
                    number));
            }

            if (!HasEndpoint(line, scheme))
            {
                return ConfigImportResult.Fail(CoreText.F("Core.Import.BadShareLink", "Ссылка {0}:// в строке {1} неполная: нужны идентификатор, адрес сервера и порт.",
                    scheme.ToLowerInvariant(),
                    number));
            }

            if (!seen.Add(line))
                continue;

            if (links.Count >= MaxShareLinks)
            {
                return ConfigImportResult.Fail(CoreText.F("Core.Import.TooManyLinks", "Слишком много ссылок. В одном профиле может быть не больше {0}.",
                    MaxShareLinks));
            }

            links.Add(line);
            protocols.Add(NormalizeProtocolName(scheme));
            if (!IsPlaceholderShareLink(line))
                usable++;
        }

        if (links.Count == 0)
            return ConfigImportResult.Fail(CoreText.T("Core.Import.PasteLink", "Вставьте ссылку на конфигурацию или подписку."));

        if (usable == 0)
        {
            return ConfigImportResult.Fail(
                CoreText.T("Core.Import.LinkPlaceholder", "Ссылка указывает на заглушку (0.0.0.0), а не на рабочий сервер."));
        }

        var directory = string.IsNullOrWhiteSpace(profilesDirectory) ? ProfilesDirectory : profilesDirectory;
        try
        {
            var title = !string.IsNullOrWhiteSpace(displayName)
                ? displayName.Trim()
                : SuggestNameFromShareLink(links[0]) ?? CoreText.T("Core.Sub.DefaultTitle", "Подписка");
            var profile = new SubscriptionProfile
            {
                Url = "",
                Links = links.ToArray(),
                Title = title,
                NodeCount = links.Count,
                Protocols = protocols.ToArray(),
                SavedAt = DateTimeOffset.UtcNow
            };
            var savedPath = SubscriptionProfile.Write(directory, profile, title);
            return ConfigImportResult.Ok(
                savedPath,
                profile.Describe(),
                CoreText.F("Core.Import.LinksImported", "Импортирован профиль «{0}»: серверов — {1} ({2}).",
                    title,
                    links.Count,
                    string.Join(", ", protocols)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ConfigImportResult.Fail(CoreText.T("Core.Import.SaveSubFail", "Не удалось сохранить профиль подписки."));
        }
    }

    private static bool HasEndpoint(string line, string scheme)
    {
        // Other schemes pack everything into Base64 (vmess, some ss links): leave those to Mihomo.
        if (!scheme.Equals("vless", StringComparison.OrdinalIgnoreCase)
            && !scheme.Equals("trojan", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var hash = line.IndexOf('#');
        var body = hash >= 0 ? line[..hash] : line;
        return Uri.TryCreate(body, UriKind.Absolute, out var uri)
               && uri.UserInfo.Length > 0
               && !string.IsNullOrWhiteSpace(uri.Host)
               && uri.Port is > 0 and <= 65535;
    }

    private static string? SuggestNameFromShareLink(string link)
    {
        var hash = link.IndexOf('#');
        if (hash >= 0 && hash < link.Length - 1)
        {
            string name;
            try
            {
                name = Uri.UnescapeDataString(link[(hash + 1)..]).Trim();
            }
            catch (UriFormatException)
            {
                name = link[(hash + 1)..].Trim();
            }

            if (name.Length > 0)
                return name;
        }

        var body = hash >= 0 ? link[..hash] : link;
        return Uri.TryCreate(body, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : null;
    }

    public static async Task<ConfigImportResult> ImportFromUrlAsync(
        string url,
        string? displayName = null,
        HttpMessageHandler? handler = null,
        string? hardwareId = null,
        string? profilesDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeUrl(url, out var uri, out var urlError))
            return ConfigImportResult.Fail(urlError);

        using var client = handler is null
            ? new HttpClient { Timeout = RequestTimeout }
            : new HttpClient(handler, disposeHandler: false) { Timeout = RequestTimeout };

        var hwid = string.IsNullOrWhiteSpace(hardwareId) ? DeviceIdentity.GetHardwareId() : hardwareId.Trim();
        // Prefer a generic UA: ClashMeta often forces full Clash YAML; URI/base64 is enough for Mihomo.
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SelectiveVpn/" + MihomoRelease.Version);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-hwid", hwid);
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-device-os", "Windows");
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-ver-os", Environment.OSVersion.Version.ToString());
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-device-model", "SelectiveVpn");

        byte[] bytes;
        string? profileTitleHeader = null;
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return ConfigImportResult.Fail(
                    CoreText.F("Core.Import.HttpStatus", "Ссылка ответила {0} {1}. Конфигурация не загружена.", (int)response.StatusCode, response.ReasonPhrase));
            }

            if (response.Headers.TryGetValues("profile-title", out var titles))
                profileTitleHeader = titles.FirstOrDefault();
            else if (response.Content.Headers.TryGetValues("profile-title", out titles))
                profileTitleHeader = titles.FirstOrDefault();

            var length = response.Content.Headers.ContentLength;
            if (length is > MaxBytes)
                return ConfigImportResult.Fail(CoreText.T("Core.Import.TooLarge", "Ответ по ссылке больше 1 МБ и не был прочитан."));

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var memory = new MemoryStream(checked((int)Math.Min(MaxBytes + 1L, int.MaxValue)));
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (memory.Length + read > MaxBytes)
                    return ConfigImportResult.Fail(CoreText.T("Core.Import.TooLarge", "Ответ по ссылке больше 1 МБ и не был прочитан."));

                memory.Write(buffer, 0, read);
            }

            bytes = memory.ToArray();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ConfigImportResult.Fail(CoreText.T("Core.Import.Timeout", "Таймаут при загрузке ссылки."));
        }
        catch (HttpRequestException)
        {
            return ConfigImportResult.Fail(CoreText.T("Core.Import.LoadFail", "Не удалось загрузить ссылку."));
        }
        catch (IOException)
        {
            return ConfigImportResult.Fail(CoreText.T("Core.Import.ReadFail", "Не удалось прочитать ответ по ссылке."));
        }

        var payload = DecodePayload(bytes);
        var headerTitle = DecodeProfileTitle(profileTitleHeader);
        var preferred = !string.IsNullOrWhiteSpace(displayName)
            ? displayName.Trim()
            : !string.IsNullOrWhiteSpace(headerTitle)
                ? headerTitle!
                : SuggestNameFromUri(uri);

        return ImportFromPayload(
            payload,
            preferred,
            uri.Host,
            subscriptionUrl: uri.AbsoluteUri,
            profileTitleHeader: headerTitle,
            profilesDirectory: profilesDirectory);
    }

    public static ConfigImportResult ImportFromPayload(
        string payload,
        string? displayName = null,
        string? sourceHint = null,
        string? subscriptionUrl = null,
        string? profileTitleHeader = null,
        string? profilesDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return ConfigImportResult.Fail(CoreText.T("Core.Import.Empty", "По ссылке пустой ответ."));

        var directory = string.IsNullOrWhiteSpace(profilesDirectory) ? ProfilesDirectory : profilesDirectory;

        if (TryInspectShareSubscription(payload, out var shareInspection))
            return SaveSubscriptionProfile(
                directory,
                subscriptionUrl,
                displayName,
                profileTitleHeader,
                sourceHint,
                shareInspection,
                CoreText.T("Core.Import.KindUri", "URI-подписка"));

        if (TryInspectClashSubscription(payload, out var clashInspection))
            return SaveSubscriptionProfile(
                directory,
                subscriptionUrl,
                displayName,
                profileTitleHeader,
                sourceHint,
                clashInspection,
                CoreText.T("Core.Import.KindClash", "подписка Clash/Mihomo"));

        if (!TryMaterializeConfig(payload, out var confText, out var materializeMessage))
            return ConfigImportResult.Fail(materializeMessage);

        var parsed = AmneziaWgConfigReader.Read(confText);
        if (!parsed.Succeeded || parsed.Profile is null)
            return ConfigImportResult.Fail(parsed.Message);

        string savedConfPath;
        try
        {
            Directory.CreateDirectory(directory);
            var fileName = BuildFileName(displayName, sourceHint);
            savedConfPath = Path.Combine(directory, fileName);
            File.WriteAllText(savedConfPath, NormalizeNewlines(confText), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ConfigImportResult.Fail(CoreText.T("Core.Import.SaveConfFail", "Не удалось сохранить конфигурацию в папку профилей."));
        }

        var description = parsed.Profile.Describe();
        var message = string.IsNullOrWhiteSpace(materializeMessage)
            ? CoreText.F("Core.Import.ProfileImported", "Импортирован профиль: {0}.", description)
            : $"{materializeMessage} {description}.";
        return ConfigImportResult.Ok(savedConfPath, description, message.Trim());
    }

    public static bool TryInspectShareSubscription(string payload, out ShareSubscriptionInspection inspection)
    {
        inspection = new ShareSubscriptionInspection(0, 0, []);
        var text = StripBom(payload).Trim();
        if (text.Length == 0)
            return false;

        if (!LooksLikeShareList(text) && TryDecodeBase64(text, out var decoded))
            text = decoded.Trim();

        if (!LooksLikeShareList(text))
            return false;

        var protocols = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        var usable = 0;
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (!TryParseShareScheme(line, out var scheme))
                continue;

            total++;
            protocols.Add(NormalizeProtocolName(scheme));
            if (!IsPlaceholderShareLink(line))
                usable++;
        }

        if (total == 0)
            return false;

        inspection = new ShareSubscriptionInspection(total, usable, protocols.ToArray());
        return true;
    }

    public static bool TryMaterializeConfig(string payload, out string confText, out string message)
    {
        confText = "";
        message = "";
        var text = StripBom(payload).Trim();
        if (text.Length == 0)
        {
            message = CoreText.T("Core.Import.Empty", "По ссылке пустой ответ.");
            return false;
        }

        if (LooksLikeShareList(text) || (TryDecodeBase64(text, out var shareDecoded) && LooksLikeShareList(shareDecoded)))
        {
            message = CoreText.T("Core.Import.IsUriSubscription", "Это URI-подписка. Её нужно импортировать как профиль подписки, а не как одиночный .conf.");
            return false;
        }

        if (LooksLikeWgIni(text))
        {
            confText = text;
            message = CoreText.T("Core.Import.FoundConf", "Найдена конфигурация AmneziaWG/WireGuard.");
            return true;
        }

        if (TryMaterializeFromSubscriptionLines(text, out confText, out var found, out var total))
        {
            message = total > 1
                ? CoreText.F("Core.Import.FoundSeveral", "В подписке найдено конфигураций: {0} из {1}. Взят первый подходящий профиль.", found, total)
                : CoreText.T("Core.Import.FoundInSubscription", "В подписке найдена конфигурация AmneziaWG/WireGuard.");
            return true;
        }

        if (TryDecodeBase64(text, out var wholeDecoded) && LooksLikeWgIni(wholeDecoded))
        {
            confText = wholeDecoded.Trim();
            message = CoreText.T("Core.Import.WasBase64", "Конфигурация была в Base64.");
            return true;
        }

        if (TryMaterializeFromClashYaml(text, out confText, out var clashName))
        {
            message = string.IsNullOrWhiteSpace(clashName)
                ? CoreText.T("Core.Import.ClashFirst", "Из Clash/Mihomo YAML взят первый WireGuard-прокси.")
                : CoreText.F("Core.Import.ClashNamed", "Из Clash/Mihomo YAML взят прокси «{0}».", clashName);
            return true;
        }

        message = CoreText.T("Core.Import.NotRecognized", "Ссылка не распознана. Поддерживаются URI-подписки (vless/vmess/ss/trojan/…), Clash/Mihomo YAML и .conf/.awg.");
        return false;
    }

    private static ConfigImportResult SaveSubscriptionProfile(
        string directory,
        string? subscriptionUrl,
        string? displayName,
        string? profileTitleHeader,
        string? sourceHint,
        ShareSubscriptionInspection inspection,
        string kindLabel)
    {
        if (inspection.UsableCount == 0)
        {
            return ConfigImportResult.Fail(
                CoreText.T("Core.Import.Stubs", "Подписка ответила заглушками без рабочих узлов. Обычно так при лимите устройств: очистите список в боте/панели (/devices) и повторите импорт."));
        }

        if (string.IsNullOrWhiteSpace(subscriptionUrl))
        {
            return ConfigImportResult.Fail(
                CoreText.F("Core.Import.NoSourceUrl", "Найдена {0}, но нет исходной ссылки для сохранения профиля.", kindLabel));
        }

        try
        {
            var title = string.IsNullOrWhiteSpace(displayName)
                ? (profileTitleHeader ?? sourceHint ?? CoreText.T("Core.Sub.DefaultTitle", "Подписка"))
                : displayName.Trim();
            var profile = new SubscriptionProfile
            {
                Url = subscriptionUrl.Trim(),
                Title = title,
                ProfileTitleHeader = profileTitleHeader,
                NodeCount = inspection.UsableCount,
                Protocols = inspection.Protocols,
                SavedAt = DateTimeOffset.UtcNow
            };
            var savedPath = SubscriptionProfile.Write(directory, profile, title);
            var protocols = string.Join(", ", inspection.Protocols);
            return ConfigImportResult.Ok(
                savedPath,
                profile.Describe(),
                CoreText.F("Core.Import.SubscriptionImported", "Импортирована {0} «{1}»: узлов — {2} ({3}). Разбор протоколов делает Mihomo.", kindLabel, title, inspection.UsableCount, protocols));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ConfigImportResult.Fail(CoreText.T("Core.Import.SaveSubFail", "Не удалось сохранить профиль подписки."));
        }
    }

    public static bool TryInspectClashSubscription(string payload, out ShareSubscriptionInspection inspection)
    {
        inspection = new ShareSubscriptionInspection(0, 0, []);
        var text = StripBom(payload).Trim();
        if (text.Length == 0)
            return false;

        if (!text.Contains("proxies:", StringComparison.OrdinalIgnoreCase))
            return false;

        var protocols = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        var usable = 0;
        string? currentType = null;
        string? currentServer = null;
        var inProxiesSection = false;
        var inProxyItem = false;
        var proxiesIndent = 0;

        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine;
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var indent = LeadingWhitespace(line);
            if (trimmed.Equals("proxies:", StringComparison.OrdinalIgnoreCase))
            {
                if (inProxyItem && currentType is not null)
                    AccountClashProxy(currentType, currentServer, protocols, ref total, ref usable);
                inProxiesSection = true;
                inProxyItem = false;
                currentType = null;
                currentServer = null;
                proxiesIndent = indent;
                continue;
            }

            if (inProxiesSection
                && indent <= proxiesIndent
                && trimmed.EndsWith(':')
                && !trimmed.StartsWith('-'))
            {
                if (inProxyItem && currentType is not null)
                    AccountClashProxy(currentType, currentServer, protocols, ref total, ref usable);
                inProxiesSection = false;
                inProxyItem = false;
                currentType = null;
                currentServer = null;
            }

            if (!inProxiesSection)
                continue;

            if (trimmed.StartsWith("- ") || trimmed.StartsWith("-\t"))
            {
                if (inProxyItem && currentType is not null)
                    AccountClashProxy(currentType, currentServer, protocols, ref total, ref usable);

                inProxyItem = LooksLikeClashProxyStart(trimmed);
                currentType = null;
                currentServer = null;

                var inline = trimmed[1..].TrimStart();
                if (inline.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
                    currentType = inline["type:".Length..].Trim().Trim('"', '\'');
                if (inline.StartsWith("server:", StringComparison.OrdinalIgnoreCase))
                    currentServer = inline["server:".Length..].Trim().Trim('"', '\'');
                continue;
            }

            if (!inProxyItem)
                continue;

            if (trimmed.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
                currentType = trimmed["type:".Length..].Trim().Trim('"', '\'');
            else if (trimmed.StartsWith("server:", StringComparison.OrdinalIgnoreCase))
                currentServer = trimmed["server:".Length..].Trim().Trim('"', '\'');
        }

        if (inProxyItem && currentType is not null)
            AccountClashProxy(currentType, currentServer, protocols, ref total, ref usable);

        if (total == 0)
            return false;

        inspection = new ShareSubscriptionInspection(total, usable, protocols.ToArray());
        return true;
    }

    private static bool LooksLikeClashProxyStart(string trimmedDashLine)
    {
        var body = trimmedDashLine[1..].TrimStart();
        return body.StartsWith("name:", StringComparison.OrdinalIgnoreCase)
               || body.StartsWith("type:", StringComparison.OrdinalIgnoreCase)
               || body.StartsWith("{");
    }

    private static void AccountClashProxy(
        string type,
        string? server,
        SortedSet<string> protocols,
        ref int total,
        ref int usable)
    {
        if (type.Equals("select", StringComparison.OrdinalIgnoreCase)
            || type.Equals("url-test", StringComparison.OrdinalIgnoreCase)
            || type.Equals("fallback", StringComparison.OrdinalIgnoreCase)
            || type.Equals("load-balance", StringComparison.OrdinalIgnoreCase)
            || type.Equals("relay", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        total++;
        protocols.Add(NormalizeProtocolName(type));
        if (!string.IsNullOrWhiteSpace(server)
            && server is not "0.0.0.0" and not "127.0.0.1"
            && !server.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            usable++;
        }
    }

    public static bool TryNormalizeUrl(string? url, out Uri uri, out string error)
    {
        uri = null!;
        error = "";
        if (string.IsNullOrWhiteSpace(url))
        {
            error = CoreText.T("Core.Import.PasteLink", "Вставьте ссылку на конфигурацию или подписку.");
            return false;
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            error = CoreText.T("Core.Import.NeedHttp", "Нужна ссылка http:// или https:// либо ссылка сервера: vless://, vmess://, trojan://, ss://.");
            return false;
        }

        uri = parsed;
        return true;
    }

    private static bool LooksLikeShareList(string text)
    {
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (TryParseShareScheme(line, out _))
                return true;
        }

        return false;
    }

    private static bool TryParseShareScheme(string line, out string scheme)
    {
        scheme = "";
        var separator = line.IndexOf("://", StringComparison.Ordinal);
        if (separator <= 0)
            return false;

        scheme = line[..separator];
        return ShareSchemes.Contains(scheme);
    }

    private static string NormalizeProtocolName(string scheme)
        => scheme.ToLowerInvariant() switch
        {
            "hy2" => "hysteria2",
            "wg" => "wireguard",
            "socks5" => "socks",
            _ => scheme.ToLowerInvariant()
        };

    private static bool IsPlaceholderShareLink(string line)
    {
        try
        {
            var trimmed = line;
            var hash = trimmed.IndexOf('#');
            if (hash >= 0)
                trimmed = trimmed[..hash];

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
                return line.Contains("@0.0.0.0:", StringComparison.Ordinal)
                       || line.Contains("00000000-0000-0000-0000-000000000000", StringComparison.OrdinalIgnoreCase);

            if (uri.Host is "0.0.0.0" or "127.0.0.1" || uri.Port == 1)
                return true;

            if (uri.UserInfo.Contains("00000000-0000-0000-0000-000000000000", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch (UriFormatException)
        {
            return line.Contains("@0.0.0.0:", StringComparison.Ordinal);
        }

        return false;
    }

    private static string? DecodeProfileTitle(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return null;

        var value = header.Trim();
        if (value.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
        {
            var b64 = value["base64:".Length..].Trim();
            if (TryDecodeBase64(b64, out var decoded))
                return decoded.Trim();
        }

        try
        {
            return Uri.UnescapeDataString(value).Trim();
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static bool TryMaterializeFromSubscriptionLines(
        string text,
        out string confText,
        out int found,
        out int total)
    {
        confText = "";
        found = 0;
        total = 0;
        string? first = null;

        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            total++;
            if (!TryDecodeBase64(line, out var decoded))
                continue;

            decoded = decoded.Trim();
            if (!LooksLikeWgIni(decoded))
                continue;

            found++;
            first ??= decoded;
        }

        if (first is null || total < 2)
            return false;

        confText = first;
        return true;
    }

    private static bool TryMaterializeFromClashYaml(string text, out string confText, out string proxyName)
    {
        confText = "";
        proxyName = "";
        if (!text.Contains("type:", StringComparison.OrdinalIgnoreCase)
            || !text.Contains("wireguard", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!IsClashTypeWireGuard(lines[i]))
                continue;

            var start = FindClashProxyStart(lines, i);
            var end = FindClashProxyEnd(lines, i);
            var block = string.Join('\n', lines[start..(end + 1)]);
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var localName = "";
            foreach (Match field in ClashField.Matches(block))
            {
                var key = field.Groups["key"].Value.Trim();
                var value = StripYamlQuotes(field.Groups["value"].Value.Trim());
                if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    localName = value;
                    continue;
                }

                if (key.Equals("type", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("udp", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("ip-version", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("dialer-proxy", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("remote-dns-resolve", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(value)
                    || value.StartsWith('-'))
                {
                    continue;
                }

                fields[key] = value;
            }

            if (!fields.TryGetValue("private-key", out var privateKey)
                && !fields.TryGetValue("private_key", out privateKey)
                && !fields.TryGetValue("privateKey", out privateKey))
            {
                continue;
            }

            if (!fields.TryGetValue("public-key", out var publicKey)
                && !fields.TryGetValue("public_key", out publicKey)
                && !fields.TryGetValue("publicKey", out publicKey))
            {
                continue;
            }

            if (!fields.TryGetValue("server", out var server))
                continue;

            if (!fields.TryGetValue("port", out var portRaw)
                || !int.TryParse(portRaw, out var port)
                || port is < 1 or > 65535)
            {
                continue;
            }

            var address = fields.GetValueOrDefault("ip")
                          ?? fields.GetValueOrDefault("address");
            if (string.IsNullOrWhiteSpace(address))
                continue;

            if (fields.TryGetValue("ipv6", out var ipv6) && !string.IsNullOrWhiteSpace(ipv6))
                address = $"{address.Trim()}, {ipv6.Trim()}";

            var sb = new StringBuilder();
            sb.AppendLine("[Interface]");
            sb.AppendLine($"PrivateKey = {privateKey.Trim()}");
            sb.AppendLine($"Address = {address.Trim()}");
            AppendOptional(sb, "DNS", fields, "dns", "nameserver");
            AppendOptional(sb, "MTU", fields, "mtu");
            AppendOptional(sb, "Jc", fields, "jc");
            AppendOptional(sb, "Jmin", fields, "jmin");
            AppendOptional(sb, "Jmax", fields, "jmax");
            AppendOptional(sb, "S1", fields, "s1");
            AppendOptional(sb, "S2", fields, "s2");
            AppendOptional(sb, "S3", fields, "s3");
            AppendOptional(sb, "S4", fields, "s4");
            AppendOptional(sb, "H1", fields, "h1");
            AppendOptional(sb, "H2", fields, "h2");
            AppendOptional(sb, "H3", fields, "h3");
            AppendOptional(sb, "H4", fields, "h4");
            AppendOptional(sb, "I1", fields, "i1");
            AppendOptional(sb, "I2", fields, "i2");
            AppendOptional(sb, "I3", fields, "i3");
            AppendOptional(sb, "I4", fields, "i4");
            AppendOptional(sb, "I5", fields, "i5");
            sb.AppendLine();
            sb.AppendLine("[Peer]");
            sb.AppendLine($"PublicKey = {publicKey.Trim()}");
            AppendOptional(sb, "PresharedKey", fields, "pre-shared-key", "preshared-key", "presharedKey");
            sb.AppendLine($"Endpoint = {server.Trim()}:{port}");
            AppendOptional(sb, "AllowedIPs", fields, "allowed-ips", "allowed_ips");
            AppendOptional(sb, "PersistentKeepalive", fields, "persistent-keepalive", "keepalive");

            var candidate = sb.ToString();
            var parsed = AmneziaWgConfigReader.Read(candidate);
            if (!parsed.Succeeded)
                continue;

            confText = candidate;
            proxyName = localName;
            return true;
        }

        return false;
    }

    private static bool IsClashTypeWireGuard(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith('-'))
            trimmed = trimmed[1..].TrimStart();

        return trimmed.StartsWith("type:", StringComparison.OrdinalIgnoreCase)
               && trimmed.Contains("wireguard", StringComparison.OrdinalIgnoreCase);
    }

    private static int FindClashProxyStart(string[] lines, int typeIndex)
    {
        for (var i = typeIndex; i >= 0; i--)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("-\t"))
                return i;
        }

        return typeIndex;
    }

    private static int FindClashProxyEnd(string[] lines, int typeIndex)
    {
        var startIndent = LeadingWhitespace(lines[FindClashProxyStart(lines, typeIndex)]);
        for (var i = typeIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var trimmed = line.TrimStart();
            var indent = LeadingWhitespace(line);
            if ((trimmed.StartsWith("- ") || trimmed.StartsWith("-\t")) && indent <= startIndent)
                return i - 1;
        }

        return lines.Length - 1;
    }

    private static int LeadingWhitespace(string line)
    {
        var count = 0;
        while (count < line.Length && (line[count] == ' ' || line[count] == '\t'))
            count++;
        return count;
    }

    private static void AppendOptional(
        StringBuilder sb,
        string iniKey,
        Dictionary<string, string> fields,
        params string[] yamlKeys)
    {
        foreach (var key in yamlKeys)
        {
            if (!fields.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                continue;

            if (value.StartsWith('[') && value.EndsWith(']'))
                value = value.Trim('[', ']');

            sb.AppendLine($"{iniKey} = {value.Trim()}");
            return;
        }
    }

    private static string StripYamlQuotes(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        if (comment > 0)
            value = value[..comment].TrimEnd();

        return value;
    }

    private static bool LooksLikeWgIni(string text)
        => text.Contains("[Interface]", StringComparison.OrdinalIgnoreCase)
           && text.Contains("[Peer]", StringComparison.OrdinalIgnoreCase);

    public static bool TryDecodeBase64Public(string text, out string decoded)
        => TryDecodeBase64(text, out decoded);

    private static bool TryDecodeBase64(string text, out string decoded)
    {
        decoded = "";
        var compact = text.Trim().Replace("\r", "").Replace("\n", "").Replace(" ", "");
        if (compact.Length < 16)
            return false;

        if (compact.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '+' or '/' or '=' or '-' or '_')))
            return false;

        try
        {
            var normalized = compact.TrimEnd('=').Replace('-', '+').Replace('_', '/');
            var pad = normalized.Length % 4;
            if (pad == 1)
                return false;
            if (pad > 0)
                normalized += new string('=', 4 - pad);

            var bytes = Convert.FromBase64String(normalized);
            if (bytes.Length == 0 || bytes.Length > MaxBytes)
                return false;

            decoded = DecodePayload(bytes);
            return !string.IsNullOrWhiteSpace(decoded);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string DecodePayload(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        return Encoding.UTF8.GetString(bytes);
    }

    private static string StripBom(string text)
        => text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;

    private static string NormalizeNewlines(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);

    private static string SuggestNameFromUri(Uri uri)
    {
        var host = string.IsNullOrWhiteSpace(uri.Host) ? "subscription" : uri.Host;
        var leaf = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(leaf) && leaf is not "/" and not ".")
            return SanitizeFileStem($"{host}-{leaf}");

        return SanitizeFileStem(host);
    }

    private static string BuildFileName(string? displayName, string? sourceHint)
    {
        var stem = !string.IsNullOrWhiteSpace(displayName)
            ? SanitizeFileStem(displayName)
            : !string.IsNullOrWhiteSpace(sourceHint)
                ? SanitizeFileStem(sourceHint)
                : "profile";

        if (stem.Length == 0)
            stem = "profile";

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        return $"{stem}-{stamp}.conf";
    }

    private static string SanitizeFileStem(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(ch => invalid.Contains(ch) || ch is ':' or '?' or '#' ? '-' : ch).ToArray();
        var cleaned = new string(chars).Trim(' ', '.', '-');
        while (cleaned.Contains("--", StringComparison.Ordinal))
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);

        return cleaned.Length > 48 ? cleaned[..48].TrimEnd('-', '.') : cleaned;
    }
}

public sealed record ShareSubscriptionInspection(int TotalCount, int UsableCount, string[] Protocols);
