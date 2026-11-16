using System.Diagnostics;
using SelectiveVpn.Desktop.Models;
using SelectiveVpn.Engine;

namespace SelectiveVpn.Desktop.Services;

/// <summary>
/// Lists running programs via <c>/proc/*/exe</c>, one entry per executable path —
/// Linux parity with Windows <c>RunningProcessCatalog</c> for PROCESS-PATH rules.
/// </summary>
internal static class LinuxRunningProcessCatalog
{
    public static IReadOnlyList<ProcessAppInfo> Load()
    {
        var own = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var found = new Dictionary<string, (string Path, bool LikelyGui)>(HostPlatform.PathComparer);

        string[] dirs;
        try
        {
            dirs = Directory.GetDirectories("/proc");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        foreach (var dir in dirs)
        {
            var name = Path.GetFileName(dir);
            if (!int.TryParse(name, out var pid) || pid <= 0)
                continue;

            var path = TryReadExe(dir);
            if (path is null)
                continue;

            try
            {
                path = Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!File.Exists(path))
                continue;

            if (path.StartsWith(own, StringComparison.Ordinal))
                continue;

            if (IsNoisePath(path))
                continue;

            var gui = LikelyHasDisplay(dir);
            if (!found.TryGetValue(path, out var known) || (gui && !known.LikelyGui))
                found[path] = (path, gui || known.LikelyGui);
        }

        return found.Values
            .OrderByDescending(entry => entry.LikelyGui)
            .ThenBy(entry => Path.GetFileName(entry.Path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Path, HostPlatform.PathComparer)
            .Select(entry => new ProcessAppInfo(Describe(entry.Path), entry.Path))
            .ToList();
    }

    private static string Describe(string path)
    {
        var file = Path.GetFileName(path);
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            if (!string.IsNullOrEmpty(description)
                && !description.Equals(Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)
                && !description.Equals(file, StringComparison.OrdinalIgnoreCase))
            {
                return $"{file} — {description}";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return file;
    }

    private static string? TryReadExe(string procDir)
    {
        try
        {
            var link = Path.Combine(procDir, "exe");
            var target = File.ResolveLinkTarget(link, returnFinalTarget: true);
            var path = target?.FullName;
            if (string.IsNullOrWhiteSpace(path))
                return null;

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

    private static bool LikelyHasDisplay(string procDir)
    {
        try
        {
            var environ = File.ReadAllBytes(Path.Combine(procDir, "environ"));
            var text = System.Text.Encoding.UTF8.GetString(environ);
            return text.Contains("DISPLAY=", StringComparison.Ordinal)
                   || text.Contains("WAYLAND_DISPLAY=", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsNoisePath(string path)
    {
        var file = Path.GetFileName(path);
        if (file is "mihomo" or "clash" or "clash-meta")
            return true;

        // Kernel helpers and interpreters that rarely make useful PROCESS-PATH rules alone.
        return path.StartsWith("/usr/lib/systemd/", StringComparison.Ordinal)
               || path.StartsWith("/usr/libexec/systemd/", StringComparison.Ordinal);
    }
}
