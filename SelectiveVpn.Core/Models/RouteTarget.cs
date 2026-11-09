namespace SelectiveVpn.Models;

public enum RouteKind
{
    File = 0,
    Folder = 1,
    Domain = 2,
    Ip = 3,
    AddressList = 4
}

public sealed record RouteTarget(
    string Path,
    bool IsFolder = false,
    RouteKind Kind = RouteKind.File,
    IReadOnlyList<string>? Entries = null,
    IReadOnlyList<string>? ResolvedIps = null)
{
    public RouteKind EffectiveKind => IsFolder ? RouteKind.Folder : Kind;

    public IReadOnlyList<string> EntryList => Entries ?? [];

    public IReadOnlyList<string> ResolvedIpList => ResolvedIps ?? [];
}
