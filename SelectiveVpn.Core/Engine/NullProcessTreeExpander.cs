using SelectiveVpn.Abstractions;
using SelectiveVpn.Models;

namespace SelectiveVpn.Engine;

public sealed class NullProcessTreeExpander : IProcessTreeExpander
{
    public static NullProcessTreeExpander Instance { get; } = new();

    public bool IsSupported => false;

    public IReadOnlyList<string> Expand(IEnumerable<RouteTarget> targets) => [];
}
