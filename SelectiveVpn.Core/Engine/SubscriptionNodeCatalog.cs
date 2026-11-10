using System.Text.RegularExpressions;
using SelectiveVpn.Models;

namespace SelectiveVpn.Engine;

public static class SubscriptionNodeCatalog
{
    private static readonly Regex ClashProxyName = new(
        @"^[ \t]*-[ \t]+name:[ \t]*(?<name>.+?)\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex ClashProxyType = new(
        @"^[ \t]+type:[ \t]*(?<type>[A-Za-z0-9_-]+)\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static IReadOnlyList<VpnNodeInfo> WithAuto(IEnumerable<string> nodeNames, string? selected = null)
    {
        var list = new List<VpnNodeInfo>
        {
            new(SubscriptionProfile.AutoNodeName, SubscriptionProfile.AutoNodeDisplayName, true)
        };

        foreach (var name in nodeNames
                     .Where(n => !string.IsNullOrWhiteSpace(n))
                     .Select(n => n.Trim())
                     .Where(n => !n.Equals(SubscriptionProfile.AutoNodeName, StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
        {
            list.Add(new VpnNodeInfo(name, name, false));
        }

        return list;
    }

    public static IReadOnlyList<VpnNodeInfo> ApplyDelays(
        IEnumerable<VpnNodeInfo> nodes,
        IReadOnlyDictionary<string, int> delays,
        bool sortByDelay)
    {
        var updated = nodes
            .Select(node =>
            {
                if (node.IsAuto)
                    return node.WithDelay(null);

                if (delays.TryGetValue(node.Name, out var delay))
                    return node.WithDelay(delay > 0 ? delay : 0);

                return node.WithDelay(node.DelayMs);
            })
            .ToList();

        if (!sortByDelay)
            return updated;

        var auto = updated.Where(node => node.IsAuto);
        var rest = updated
            .Where(node => !node.IsAuto)
            .OrderBy(node => node.DelayMs is > 0 ? 0 : 1)
            .ThenBy(node => node.DelayMs is > 0 ? node.DelayMs : int.MaxValue)
            .ThenBy(node => node.DisplayName, StringComparer.CurrentCultureIgnoreCase);

        return auto.Concat(rest).ToList();
    }

    public static bool TryListFromProviderCache(string? cachePath, out IReadOnlyList<string> names, out string error)
    {
        names = [];
        error = "";
        if (string.IsNullOrWhiteSpace(cachePath) || !File.Exists(cachePath))
        {
            error = "Кэш подписки ещё не загружен. Запустите маршрутизацию или обновите список.";
            return false;
        }

        try
        {
            var text = File.ReadAllText(cachePath);
            names = ExtractNames(text);
            return names.Count > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Не удалось прочитать кэш подписки.";
            return false;
        }
    }

    public static IReadOnlyList<string> ExtractNames(string payload)
    {
        var text = payload.Trim();
        if (text.Length == 0)
            return [];

        if (ConfigSubscriptionImporter.TryInspectShareSubscription(text, out _))
            return ExtractShareLinkNames(text);

        if (text.Contains("proxies:", StringComparison.OrdinalIgnoreCase))
            return ExtractClashProxyNames(text);

        if (ConfigSubscriptionImporter.TryDecodeBase64Public(text, out var decoded))
            return ExtractNames(decoded);

        return [];
    }

    private static IReadOnlyList<string> ExtractShareLinkNames(string text)
    {
        if (!text.Contains("://", StringComparison.Ordinal)
            && ConfigSubscriptionImporter.TryDecodeBase64Public(text, out var decoded))
        {
            text = decoded;
        }

        var names = new List<string>();
        var index = 1;
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            var hash = line.IndexOf('#');
            string name;
            if (hash >= 0 && hash < line.Length - 1)
            {
                try
                {
                    name = Uri.UnescapeDataString(line[(hash + 1)..]).Trim();
                }
                catch (UriFormatException)
                {
                    name = line[(hash + 1)..].Trim();
                }
            }
            else
            {
                name = $"Узел {index}";
            }

            if (string.IsNullOrWhiteSpace(name))
                name = $"Узел {index}";

            names.Add(name);
            index++;
        }

        return Deduplicate(names);
    }

    private static IReadOnlyList<string> ExtractClashProxyNames(string text)
    {
        var names = new List<string>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var inProxies = false;
        var proxiesIndent = 0;
        string? pendingName = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var indent = 0;
            while (indent < line.Length && (line[indent] == ' ' || line[indent] == '\t'))
                indent++;

            if (trimmed.Equals("proxies:", StringComparison.OrdinalIgnoreCase))
            {
                inProxies = true;
                proxiesIndent = indent;
                pendingName = null;
                continue;
            }

            if (inProxies
                && indent <= proxiesIndent
                && trimmed.EndsWith(':')
                && !trimmed.StartsWith('-'))
            {
                inProxies = false;
                pendingName = null;
            }

            if (!inProxies)
                continue;

            var nameMatch = ClashProxyName.Match(line);
            if (nameMatch.Success)
            {
                pendingName = StripQuotes(nameMatch.Groups["name"].Value.Trim());
                continue;
            }

            if (pendingName is null)
                continue;

            var typeMatch = ClashProxyType.Match(line);
            if (!typeMatch.Success)
                continue;

            var type = typeMatch.Groups["type"].Value;
            if (type.Equals("select", StringComparison.OrdinalIgnoreCase)
                || type.Equals("url-test", StringComparison.OrdinalIgnoreCase)
                || type.Equals("fallback", StringComparison.OrdinalIgnoreCase)
                || type.Equals("load-balance", StringComparison.OrdinalIgnoreCase)
                || type.Equals("relay", StringComparison.OrdinalIgnoreCase))
            {
                pendingName = null;
                continue;
            }

            names.Add(pendingName);
            pendingName = null;
        }

        return Deduplicate(names);
    }

    private static IReadOnlyList<string> Deduplicate(IEnumerable<string> names)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var unique = name;
            var suffix = 2;
            while (!seen.Add(unique))
            {
                unique = $"{name} ({suffix})";
                suffix++;
            }

            result.Add(unique);
        }

        return result;
    }

    private static string StripQuotes(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
