using SelectiveVpn.Abstractions;
using SelectiveVpn.Models;

namespace SelectiveVpn.Engine;

/// <summary>
/// Factory and compatibility facade for process-tree expansion.
/// </summary>
public static class ProcessTreeExpander
{
    public static IProcessTreeExpander CreateDefault()
    {
        if (OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416 // Guarded by OperatingSystem.IsWindows above.
            return new WindowsProcessTreeExpander();
#pragma warning restore CA1416
        }

        if (OperatingSystem.IsLinux())
            return new LinuxProcessTreeExpander();

        return NullProcessTreeExpander.Instance;
    }

    /// <summary>Expands using the default expander for the current OS.</summary>
    public static IReadOnlyList<string> Expand(IEnumerable<RouteTarget> targets)
        => CreateDefault().Expand(targets);
}
