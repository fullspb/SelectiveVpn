using System.Net;
using System.Net.Sockets;
using SelectiveVpn.Models;

namespace SelectiveVpn.Engine;

public sealed record AddressListParseResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> Entries,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> DirectIps);

public sealed record AddressListResolveResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> Entries,
    IReadOnlyList<string> ResolvedIps,
    int DomainCount,
    int FailedDomains);

public static class AddressListResolver
{
    public static AddressListParseResult Parse(string? text)
    {
        var lines = (text ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            return new AddressListParseResult(false, CoreText.T("Core.List.Empty", "Список пуст. Добавьте домены или IP-адреса."), [], [], []);

        var entries = new List<string>();
        var domains = new List<string>();
        var ips = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var item = line;
            var comment = item.IndexOf('#');
            if (comment >= 0)
                item = item[..comment].Trim();
            if (item.Length == 0)
                continue;

            if (!RouteAddress.TryParse(item, out var normalized, out var kind, out var error))
                return new AddressListParseResult(false, error, [], [], []);

            if (!seen.Add(normalized))
                continue;

            entries.Add(normalized);
            if (kind == RouteKind.Domain)
                domains.Add(normalized);
            else
                ips.Add(normalized);
        }

        if (entries.Count == 0)
            return new AddressListParseResult(false, CoreText.T("Core.List.Empty", "Список пуст. Добавьте домены или IP-адреса."), [], [], []);

        return new AddressListParseResult(
            true,
            CoreText.F("Core.List.Parsed", "Распознано адресов: {0}.", entries.Count),
            entries,
            domains,
            ips);
    }

    public static async Task<AddressListResolveResult> ResolveAsync(
        IReadOnlyList<string> entries,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var domains = new List<string>();
        var ips = new List<string>();
        foreach (var entry in entries)
        {
            if (entry.Contains('/'))
                ips.Add(entry);
            else
                domains.Add(entry);
        }

        if (domains.Count == 0)
        {
            progress?.Report(CoreText.F("Core.List.IpOnlyDone", "Готово: IP в списке — {0}.", ips.Count));
            return new AddressListResolveResult(
                true,
                CoreText.F("Core.List.Ready", "Список готов: доменов — {0}, IP — {1}.", 0, ips.Count),
                entries.ToArray(),
                [],
                0,
                0);
        }

        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = 0;
        for (var index = 0; index < domains.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var domain = domains[index];
            var step = index + 1;
            progress?.Report(CoreText.F("Core.List.Checking", "Проверяю {0}/{1}: {2}", step, domains.Count, domain));
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(domain, cancellationToken).ConfigureAwait(false);
                var added = 0;
                foreach (var address in addresses)
                {
                    if (address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
                        continue;
                    if (IPAddress.IsLoopback(address))
                        continue;

                    var cidr = address.AddressFamily == AddressFamily.InterNetwork
                        ? address + "/32"
                        : address + "/128";
                    if (resolved.Add(cidr))
                        added++;
                }

                if (added == 0)
                    failed++;
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                failed++;
            }
        }

        var resolvedIps = resolved.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        var totalIps = ips.Count + resolvedIps.Length;
        var message = failed == 0
            ? CoreText.F("Core.List.Ready", "Список готов: доменов — {0}, IP — {1}.", domains.Count, totalIps)
            : CoreText.F("Core.List.ReadyFailed", "Список готов: доменов — {0}, IP — {1}. Не удалось разрешить доменов: {2}.", domains.Count, totalIps, failed);
        progress?.Report(message);
        return new AddressListResolveResult(true, message, entries.ToArray(), resolvedIps, domains.Count, failed);
    }

    public static IReadOnlyList<RouteTarget> Expand(IReadOnlyList<RouteTarget> targets)
    {
        var expanded = new List<RouteTarget>();
        foreach (var target in targets)
        {
            if (target.EffectiveKind != RouteKind.AddressList)
            {
                expanded.Add(target);
                continue;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in target.EntryList)
            {
                if (entry.Contains('/'))
                {
                    if (seen.Add("ip:" + entry))
                        expanded.Add(new RouteTarget(entry, Kind: RouteKind.Ip));
                }
                else if (seen.Add("domain:" + entry))
                {
                    expanded.Add(new RouteTarget(entry, Kind: RouteKind.Domain));
                }
            }

            foreach (var ip in target.ResolvedIpList)
            {
                if (seen.Add("ip:" + ip))
                    expanded.Add(new RouteTarget(ip, Kind: RouteKind.Ip));
            }
        }

        return expanded;
    }
}
