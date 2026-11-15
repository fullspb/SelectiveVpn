using SelectiveVpn.Models;

namespace SelectiveVpn.Abstractions;

/// <summary>
/// Discovers descendant process image paths for selected file route targets
/// so Mihomo PROCESS-PATH rules can follow helper/child processes.
/// </summary>
public interface IProcessTreeExpander
{
    /// <summary>False on platforms where process-tree watching is unavailable.</summary>
    bool IsSupported { get; }

    IReadOnlyList<string> Expand(IEnumerable<RouteTarget> targets);
}
