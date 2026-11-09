using System.Globalization;

namespace SelectiveVpn.Engine;

public static class AmneziaWgConfigReader
{
    private const int MaxBytes = 256 * 1024;

    private static readonly string[] InterfaceParameters =
    [
        "PrivateKey", "Address", "DNS", "MTU",
        "Jc", "Jmin", "Jmax",
        "S1", "S2", "S3", "S4",
        "H1", "H2", "H3", "H4",
        "I1", "I2", "I3", "I4", "I5",
        "J1", "J2", "J3", "ITime",
        "HeaderProtectionKey", "ContentPaddingAddition",
        "RekeyAfterTime", "RekeyTimeout", "RejectAfterTime",
        "KeepaliveTimeout", "MaxHandshakeAttempts",
        "RandomTrailers", "DisableCookies"
    ];

    private static readonly string[] IgnoredInterfaceParameters =
    [
        "ListenPort", "FwMark", "Table", "SaveConfig",
        "PreUp", "PostUp", "PreDown", "PostDown"
    ];

    private static readonly string[] PeerParameters =
    [
        "PublicKey", "PresharedKey", "PreSharedKey", "Endpoint",
        "AllowedIPs", "PersistentKeepalive"
    ];

    public static AmneziaConfigReadResult ReadFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return AmneziaConfigReadResult.Fail("Файл конфигурации не найден.");

            if (info.Length > MaxBytes)
                return AmneziaConfigReadResult.Fail("Файл конфигурации больше 256 КБ и не был прочитан.");

            return Read(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AmneziaConfigReadResult.Fail("Не удалось прочитать файл конфигурации. Содержимое в журнал не записывалось.");
        }
    }

    public static AmneziaConfigReadResult Read(string text)
    {
        var interfaceValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var peerValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ignored = new List<string>();
        string? section = null;
        var sawInterface = false;
        var peerCount = 0;

        foreach (var rawLine in text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim();
                if (name.Equals("Interface", StringComparison.OrdinalIgnoreCase))
                {
                    if (sawInterface)
                        return AmneziaConfigReadResult.Fail("Секция [Interface] повторяется.");

                    sawInterface = true;
                    section = "Interface";
                    continue;
                }

                if (name.Equals("Peer", StringComparison.OrdinalIgnoreCase))
                {
                    peerCount++;
                    if (peerCount > 1)
                        return AmneziaConfigReadResult.Fail("Поддерживается только один [Peer].");

                    section = "Peer";
                    continue;
                }

                return AmneziaConfigReadResult.Fail(SafeSectionMessage(name));
            }

            var separator = line.IndexOf('=');
            if (separator <= 0 || section is null)
                return AmneziaConfigReadResult.Fail("Строка конфигурации не распознана. Её текст в журнал не записывался.");

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (section == "Interface")
            {
                var canonical = Canonical(key, InterfaceParameters);
                if (canonical is not null)
                {
                    if (!Add(interfaceValues, canonical, value))
                        return AmneziaConfigReadResult.Fail($"Параметр {canonical} указан дважды.");
                    continue;
                }

                var ignoredName = Canonical(key, IgnoredInterfaceParameters);
                if (ignoredName is not null)
                {
                    if (!ignored.Contains(ignoredName, StringComparer.OrdinalIgnoreCase))
                        ignored.Add(ignoredName);
                    continue;
                }

                return AmneziaConfigReadResult.Fail(SafeParameterMessage(key));
            }

            var peerName = Canonical(key, PeerParameters);
            if (peerName is null)
                return AmneziaConfigReadResult.Fail(SafeParameterMessage(key));

            if (!Add(peerValues, peerName, value))
                return AmneziaConfigReadResult.Fail($"Параметр {peerName} указан дважды.");
        }

        if (!sawInterface)
            return AmneziaConfigReadResult.Fail("Файл не похож на конфигурацию AmneziaWG: нет секции [Interface].");

        if (peerCount == 0)
            return AmneziaConfigReadResult.Fail("В конфигурации нет секции [Peer].");

        return Build(interfaceValues, peerValues, ignored);
    }

    private static AmneziaConfigReadResult Build(
        Dictionary<string, string> interfaceValues,
        Dictionary<string, string> peerValues,
        List<string> ignored)
    {
        if (!TryRequired(interfaceValues, "PrivateKey", out var privateKey)
            || !TryRequired(peerValues, "PublicKey", out var publicKey)
            || !TryRequired(interfaceValues, "Address", out var address)
            || !TryRequired(peerValues, "Endpoint", out var endpoint))
        {
            return AmneziaConfigReadResult.Fail("В конфигурации не хватает PrivateKey, Address, PublicKey или Endpoint.");
        }

        if (!IsCompactSecret(privateKey) || !IsCompactSecret(publicKey))
            return AmneziaConfigReadResult.Fail("PrivateKey или PublicKey пуст либо содержит пробелы.");

        if (!TrySplitAddresses(address, out var addressV4, out var addressV6))
            return AmneziaConfigReadResult.Fail("В секции [Interface] нужен IPv4-адрес.");

        if (!TryEndpoint(endpoint, out var host, out var port))
            return AmneziaConfigReadResult.Fail("Endpoint не распознан. Ожидается адрес и порт.");

        string? preshared = null;
        if (peerValues.TryGetValue("PresharedKey", out var presharedValue))
        {
            if (!IsCompactSecret(presharedValue))
                return AmneziaConfigReadResult.Fail("PresharedKey пуст либо содержит пробелы.");

            preshared = presharedValue;
        }

        if (!TryOptionalInt(interfaceValues, "MTU", 576, 9000, out var mtu)
            || !TryOptionalInt(interfaceValues, "Jc", 0, 1000, out var jc)
            || !TryOptionalInt(interfaceValues, "Jmin", 0, 65535, out var jmin)
            || !TryOptionalInt(interfaceValues, "Jmax", 0, 65535, out var jmax)
            || !TryOptionalInt(interfaceValues, "S1", 0, 65535, out var s1)
            || !TryOptionalInt(interfaceValues, "S2", 0, 65535, out var s2)
            || !TryOptionalInt(interfaceValues, "S3", 0, 65535, out var s3)
            || !TryOptionalInt(interfaceValues, "S4", 0, 65535, out var s4)
            || !TryOptionalInt(interfaceValues, "ITime", 0, 86400, out var itime)
            || !TryOptionalInt(peerValues, "PersistentKeepalive", 0, 65535, out var keepalive))
        {
            return AmneziaConfigReadResult.Fail("Числовой параметр конфигурации имеет недопустимое значение.");
        }

        if (!TryOptionalBool(interfaceValues, "RandomTrailers", out var randomTrailers)
            || !TryOptionalBool(interfaceValues, "DisableCookies", out var disableCookies))
        {
            return AmneziaConfigReadResult.Fail("RandomTrailers или DisableCookies имеет недопустимое значение.");
        }

        var hasVersion3 = HasAny(
            interfaceValues,
            "HeaderProtectionKey",
            "ContentPaddingAddition",
            "RekeyAfterTime",
            "RekeyTimeout",
            "RejectAfterTime",
            "KeepaliveTimeout",
            "MaxHandshakeAttempts",
            "RandomTrailers",
            "DisableCookies");

        if (hasVersion3 && HasAny(interfaceValues, "J1", "J2", "J3", "ITime"))
            return AmneziaConfigReadResult.Fail("Параметры J1, J2, J3 и ITime нельзя сочетать с AmneziaWG 3.");

        var allowed = SplitList(peerValues.GetValueOrDefault("AllowedIPs"));
        if (allowed.Count == 0)
            allowed.Add("0.0.0.0/0");

        var profile = new AmneziaWgProfile
        {
            PrivateKey = privateKey,
            AddressV4 = addressV4,
            AddressV6 = addressV6,
            PublicKey = publicKey,
            PresharedKey = preshared,
            EndpointHost = host,
            EndpointPort = port,
            AllowedIps = allowed,
            DnsServers = SplitList(interfaceValues.GetValueOrDefault("DNS")),
            Mtu = mtu,
            PersistentKeepalive = keepalive,
            Jc = jc,
            Jmin = jmin,
            Jmax = jmax,
            S1 = s1,
            S2 = s2,
            S3 = s3,
            S4 = s4,
            H1 = Optional(interfaceValues, "H1"),
            H2 = Optional(interfaceValues, "H2"),
            H3 = Optional(interfaceValues, "H3"),
            H4 = Optional(interfaceValues, "H4"),
            I1 = Optional(interfaceValues, "I1"),
            I2 = Optional(interfaceValues, "I2"),
            I3 = Optional(interfaceValues, "I3"),
            I4 = Optional(interfaceValues, "I4"),
            I5 = Optional(interfaceValues, "I5"),
            J1 = Optional(interfaceValues, "J1"),
            J2 = Optional(interfaceValues, "J2"),
            J3 = Optional(interfaceValues, "J3"),
            ITime = itime,
            HeaderProtectionKey = Optional(interfaceValues, "HeaderProtectionKey"),
            ContentPaddingAddition = Optional(interfaceValues, "ContentPaddingAddition"),
            RekeyAfterTime = Optional(interfaceValues, "RekeyAfterTime"),
            RekeyTimeout = Optional(interfaceValues, "RekeyTimeout"),
            RejectAfterTime = Optional(interfaceValues, "RejectAfterTime"),
            KeepaliveTimeout = Optional(interfaceValues, "KeepaliveTimeout"),
            MaxHandshakeAttempts = Optional(interfaceValues, "MaxHandshakeAttempts"),
            RandomTrailers = randomTrailers,
            DisableCookies = disableCookies,
            IgnoredParameters = ignored
        };

        if (profile.HeaderProtectionKey is not null && !IsCompactSecret(profile.HeaderProtectionKey))
            return AmneziaConfigReadResult.Fail("HeaderProtectionKey пуст либо содержит пробелы.");

        return AmneziaConfigReadResult.Ok(profile);
    }

    private static bool Add(Dictionary<string, string> target, string key, string value)
    {
        if (target.ContainsKey(key))
            return false;

        target[key] = value;
        return true;
    }

    private static string? Canonical(string key, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (!name.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            return name.Equals("PreSharedKey", StringComparison.OrdinalIgnoreCase) ? "PresharedKey" : name;
        }

        return null;
    }

    private static bool TryRequired(Dictionary<string, string> values, string key, out string value)
    {
        if (values.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
        {
            value = found.Trim();
            return true;
        }

        value = "";
        return false;
    }

    private static string? Optional(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static bool HasAny(Dictionary<string, string> values, params string[] keys)
        => keys.Any(values.ContainsKey);

    private static bool TryOptionalInt(
        Dictionary<string, string> values,
        string key,
        int min,
        int max,
        out int? number)
    {
        number = null;
        if (!values.TryGetValue(key, out var raw))
            return true;

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < min || parsed > max)
            return false;

        number = parsed;
        return true;
    }

    private static bool TryOptionalBool(Dictionary<string, string> values, string key, out bool? flag)
    {
        flag = null;
        if (!values.TryGetValue(key, out var raw))
            return true;

        switch (raw.Trim().ToLowerInvariant())
        {
            case "1" or "true" or "yes" or "on":
                flag = true;
                return true;
            case "0" or "false" or "no" or "off":
                flag = false;
                return true;
            default:
                return false;
        }
    }

    private static bool TrySplitAddresses(string raw, out string addressV4, out string? addressV6)
    {
        addressV4 = "";
        addressV6 = null;
        foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var address = token.Split('/', 2)[0];
            if (address.Contains(':'))
                addressV6 ??= address;
            else if (addressV4.Length == 0)
                addressV4 = address;
        }

        return addressV4.Length > 0;
    }

    private static bool TryEndpoint(string value, out string host, out int port)
    {
        host = "";
        port = 0;
        if (value.StartsWith('['))
        {
            var close = value.IndexOf("]:", StringComparison.Ordinal);
            if (close <= 1)
                return false;

            host = value[1..close];
            return host.Length > 0
                   && int.TryParse(value[(close + 2)..], NumberStyles.None, CultureInfo.InvariantCulture, out port)
                   && port is > 0 and <= 65535;
        }

        var colon = value.LastIndexOf(':');
        if (colon <= 0 || colon == value.Length - 1)
            return false;

        host = value[..colon];
        return !host.Contains(' ')
               && int.TryParse(value[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port)
               && port is > 0 and <= 65535;
    }

    private static List<string> SplitList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static bool IsCompactSecret(string value)
        => value.Length >= 8 && !value.Any(char.IsWhiteSpace);

    private static string SafeParameterMessage(string key)
        => IsSafeName(key)
            ? $"Неизвестный параметр «{key}». Подключение не запускалось."
            : "Неизвестный параметр с недопустимым именем. Подключение не запускалось.";

    private static string SafeSectionMessage(string name)
        => IsSafeName(name)
            ? $"Неизвестная секция «{name}»."
            : "Неизвестная секция конфигурации.";

    private static bool IsSafeName(string value)
        => value.Length is > 0 and <= 64
           && value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}
