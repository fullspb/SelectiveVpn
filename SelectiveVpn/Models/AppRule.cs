using System.IO;
using SelectiveVpn.Services;

namespace SelectiveVpn.Models;

public sealed record AppRule(
    string ExecutablePath,
    bool IsFolder = false,
    RouteKind Kind = RouteKind.File,
    IReadOnlyList<string>? Entries = null,
    IReadOnlyList<string>? ResolvedIps = null,
    bool SkipDomainResolve = false)
{
    public RouteKind EffectiveKind => IsFolder ? RouteKind.Folder : Kind;

    public bool HasLocation => EffectiveKind is RouteKind.File or RouteKind.Folder;

    public bool IsAddressList => EffectiveKind == RouteKind.AddressList;

    public IReadOnlyList<string> EntryList => Entries ?? [];

    public IReadOnlyList<string> ResolvedIpList => ResolvedIps ?? [];

    public string StoredKind => EffectiveKind switch
    {
        RouteKind.Folder => "folder",
        RouteKind.Domain => "domain",
        RouteKind.Ip => "ip",
        RouteKind.AddressList => "address-list",
        _ => "file"
    };

    public string FileName => EffectiveKind switch
    {
        RouteKind.Folder => new DirectoryInfo(ExecutablePath).Name,
        RouteKind.File => Path.GetFileName(ExecutablePath),
        RouteKind.AddressList => ExecutablePath,
        _ => ExecutablePath
    };

    public string DetailText => EffectiveKind switch
    {
        RouteKind.AddressList => SkipDomainResolve
            ? FormatListSummary(EntryList, ResolvedIpList) + " · без проверки IP"
            : FormatListSummary(EntryList, ResolvedIpList),
        RouteKind.File or RouteKind.Folder => ExecutablePath,
        _ => ExecutablePath
    };

    public string KindLabel => EffectiveKind switch
    {
        RouteKind.Folder => Loc.T("Kind.Folder"),
        RouteKind.Domain => Loc.T("Kind.Domain"),
        RouteKind.Ip => Loc.T("Kind.Ip"),
        RouteKind.AddressList => Loc.T("Kind.AddressList"),
        _ => Loc.T("Kind.App")
    };

    public static string FormatListSummary(IReadOnlyList<string> entries, IReadOnlyList<string> resolvedIps)
    {
        var domains = 0;
        var ips = 0;
        foreach (var entry in entries)
        {
            if (entry.Contains('/'))
                ips++;
            else
                domains++;
        }

        ips += resolvedIps.Count;
        return $"{FormatCount(domains, "домен", "домена", "доменов")} · {FormatCount(ips, "IP", "IP", "IP")}";
    }

    private static string FormatCount(int count, string one, string few, string many)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;
        var word = mod100 is >= 11 and <= 14
            ? many
            : mod10 == 1
                ? one
                : mod10 is >= 2 and <= 4
                    ? few
                    : many;
        return $"{count} {word}";
    }
}
