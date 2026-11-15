using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SelectiveVpn.Models;

namespace SelectiveVpn.Services;

/// <summary>
/// Lists the programs that are running right now, one entry per executable file, so a rule can
/// be made for something that has no installer entry or shortcut.
/// </summary>
internal static class RunningProcessCatalog
{
    private const uint QueryLimitedInformation = 0x1000;

    public static IReadOnlyList<InstalledAppInfo> Load()
    {
        var own = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var found = new Dictionary<string, (string Path, bool HasWindow)>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var path = TryGetPath(process.Id);
                // The app's own folder holds SelectiveVpn and its engine: routing those would loop.
                if (path is null || path.StartsWith(own, StringComparison.OrdinalIgnoreCase))
                    continue;

                var hasWindow = HasWindow(process);
                if (!found.TryGetValue(path, out var known) || (hasWindow && !known.HasWindow))
                    found[path] = (path, hasWindow || known.HasWindow);
            }
        }

        return found.Values
            // Programs with a window are what people look for; services and helpers follow.
            .OrderByDescending(entry => entry.HasWindow)
            .ThenBy(entry => Path.GetFileName(entry.Path), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new InstalledAppInfo(Describe(entry.Path), entry.Path))
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

    private static bool HasWindow(Process process)
    {
        try
        {
            return process.MainWindowHandle != IntPtr.Zero;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Asks Windows for the image path with the limited right, which also works for elevated and
    /// service processes that <see cref="Process.MainModule"/> cannot open.
    /// </summary>
    private static string? TryGetPath(int processId)
    {
        if (processId is 0 or 4)
            return null;

        var handle = OpenProcess(QueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            if (!QueryFullProcessImageName(handle, 0, buffer, ref size) || size == 0)
                return null;

            var path = buffer.ToString(0, (int)size);
            return Path.IsPathFullyQualified(path) && File.Exists(path) ? path : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder exeName, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
