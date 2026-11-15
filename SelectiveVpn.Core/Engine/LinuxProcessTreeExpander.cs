using SelectiveVpn.Abstractions;
using SelectiveVpn.Models;

namespace SelectiveVpn.Engine;

/// <summary>
/// Expands selected file targets to descendant processes via <c>/proc</c> (ppid + exe symlink).
/// </summary>
public sealed class LinuxProcessTreeExpander : IProcessTreeExpander
{
    public bool IsSupported => OperatingSystem.IsLinux();

    public IReadOnlyList<string> Expand(IEnumerable<RouteTarget> targets)
    {
        if (!OperatingSystem.IsLinux())
            return [];

        var roots = targets
            .Where(target => target.EffectiveKind == RouteKind.File)
            .Select(target => NormalizePath(target.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(HostPlatform.PathComparer)
            .ToArray();
        if (roots.Length == 0)
            return [];

        var snapshot = Capture();
        if (snapshot.Count == 0)
            return [];

        var rootPids = new HashSet<int>();
        foreach (var entry in snapshot)
        {
            if (entry.Pid <= 0)
                continue;
            if (string.IsNullOrWhiteSpace(entry.Path))
                continue;
            if (roots.Any(root => HostPlatform.PathsEqual(root!, entry.Path!)))
                rootPids.Add(entry.Pid);
        }

        if (rootPids.Count == 0)
            return [];

        var byParent = snapshot
            .GroupBy(entry => entry.ParentPid)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Pid).ToArray());

        var descendants = new HashSet<int>(rootPids);
        var queue = new Queue<int>(rootPids);
        while (queue.Count > 0)
        {
            var pid = queue.Dequeue();
            if (!byParent.TryGetValue(pid, out var children))
                continue;
            foreach (var child in children)
            {
                if (child <= 0)
                    continue;
                if (descendants.Add(child))
                    queue.Enqueue(child);
            }
        }

        var paths = new SortedSet<string>(HostPlatform.PathComparer);
        foreach (var entry in snapshot)
        {
            if (!descendants.Contains(entry.Pid))
                continue;
            if (string.IsNullOrWhiteSpace(entry.Path))
                continue;
            if (roots.Any(root => HostPlatform.PathsEqual(root!, entry.Path)))
                continue;
            paths.Add(NormalizePath(entry.Path)!);
        }

        return paths.ToArray();
    }

    private static List<ProcessEntry> Capture()
    {
        var entries = new List<ProcessEntry>();
        string[] dirs;
        try
        {
            dirs = Directory.GetDirectories("/proc");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return entries;
        }

        foreach (var dir in dirs)
        {
            var name = Path.GetFileName(dir);
            if (!int.TryParse(name, out var pid) || pid <= 0)
                continue;

            if (!TryReadParentPid(dir, out var parentPid))
                continue;

            var path = TryReadExePath(dir);
            entries.Add(new ProcessEntry(pid, parentPid, NormalizePath(path)));
        }

        return entries;
    }

    private static bool TryReadParentPid(string procDir, out int parentPid)
    {
        parentPid = 0;
        try
        {
            foreach (var line in File.ReadLines(Path.Combine(procDir, "status")))
            {
                if (!line.StartsWith("PPid:", StringComparison.Ordinal))
                    continue;
                return int.TryParse(line.AsSpan(5).Trim(), out parentPid);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return false;
    }

    private static string? TryReadExePath(string procDir)
    {
        try
        {
            var link = Path.Combine(procDir, "exe");
            var target = File.ResolveLinkTarget(link, returnFinalTarget: true);
            var path = target?.FullName;
            if (string.IsNullOrWhiteSpace(path))
                return null;

            // Kernel appends " (deleted)" when the binary was replaced; keep the path prefix.
            const string deleted = " (deleted)";
            if (path.EndsWith(deleted, StringComparison.Ordinal))
                path = path[..^deleted.Length];

            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.Trim();
        }
    }

    private readonly record struct ProcessEntry(int Pid, int ParentPid, string? Path);
}
