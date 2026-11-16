using SelectiveVpn.Models;

namespace SelectiveVpn.Desktop.Models;

/// <summary>One include/except rule shown in the Orbit routing list.</summary>
public sealed class RouteRuleItem
{
    public RouteRuleItem(string path, RouteKind kind)
    {
        Path = path;
        Kind = kind;
    }

    public string Path { get; }

    public RouteKind Kind { get; }

    public string StoredKind => Kind switch
    {
        RouteKind.Folder => "folder",
        RouteKind.Domain => "domain",
        RouteKind.Ip => "ip",
        RouteKind.File => "file",
        _ => "file"
    };

    public string Title => Kind switch
    {
        RouteKind.Folder => System.IO.Path.GetFileName(Path.TrimEnd('/', '\\')) is { Length: > 0 } name ? name : Path,
        RouteKind.File => System.IO.Path.GetFileName(Path),
        _ => Path
    };

    public string Detail => Kind is RouteKind.File or RouteKind.Folder ? Path : "";

    public string Glyph => Kind switch
    {
        RouteKind.Folder => "[]",
        RouteKind.File => "P",
        RouteKind.Ip => "#",
        _ => "@"
    };

    public string KindLabel => Kind switch
    {
        RouteKind.Folder => "Folder",
        RouteKind.File => "Process",
        RouteKind.Ip => "IP",
        RouteKind.Domain => "Domain",
        _ => "Rule"
    };

    public RouteTarget ToRouteTarget()
        => new(Path, IsFolder: Kind == RouteKind.Folder, Kind: Kind);
}
