using SelectiveVpn.Desktop.Services;
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

    /// <summary>StreamGeometry path data for the rule kind icon.</summary>
    public string IconData => Kind switch
    {
        RouteKind.Folder => "M4,7 L9,7 L11,9 L20,9 L20,18 L4,18 Z",
        RouteKind.File => "M12,4 A3.2,3.2 0 1 1 11.99,4 M5.5,19 C5.5,15.8 8.3,13.5 12,13.5 C15.7,13.5 18.5,15.8 18.5,19",
        RouteKind.Ip => "M7,8 L17,8 M7,12 L17,12 M7,16 L14,16",
        _ => "M12,4 A8,8 0 1 1 11.99,4 M4,12 L20,12 M12,4 C9,7.5 9,16.5 12,20 C15,16.5 15,7.5 12,4"
    };

    public string KindLabel => Kind switch
    {
        RouteKind.Folder => UiLoc.T("Kind.Folder"),
        RouteKind.File => UiLoc.T("Kind.Process"),
        RouteKind.Ip => UiLoc.T("Kind.Ip"),
        RouteKind.Domain => UiLoc.T("Kind.Domain"),
        _ => UiLoc.T("Kind.Rule")
    };

    public RouteTarget ToRouteTarget()
        => new(Path, IsFolder: Kind == RouteKind.Folder, Kind: Kind);
}
