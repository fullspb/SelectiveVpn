using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SelectiveVpn.Models;

namespace SelectiveVpn.Engine;

/// <summary>
/// Finds executable image paths for selected File targets and their descendant processes.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ProcessTreeExpander
{
    public static IReadOnlyList<string> Expand(IEnumerable<RouteTarget> targets)
    {
        if (!OperatingSystem.IsWindows())
            return [];

        var roots = targets
            .Where(target => target.EffectiveKind == RouteKind.File)
            .Select(target => NormalizePath(target.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (roots.Length == 0)
            return [];

        var snapshot = Capture();
        if (snapshot.Count == 0)
            return [];

        var rootPids = new HashSet<int>();
        foreach (var entry in snapshot)
        {
            if (entry.Pid is 0 or 4)
                continue;
            if (string.IsNullOrWhiteSpace(entry.Path))
                continue;
            if (roots.Any(root => PathsEqual(root!, entry.Path!)))
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
                if (child is 0 or 4)
                    continue;
                if (descendants.Add(child))
                    queue.Enqueue(child);
            }
        }

        var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot)
        {
            if (!descendants.Contains(entry.Pid))
                continue;
            if (string.IsNullOrWhiteSpace(entry.Path))
                continue;
            if (!entry.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;
            if (roots.Any(root => PathsEqual(root!, entry.Path)))
                continue;
            paths.Add(NormalizePath(entry.Path)!);
        }

        return paths.ToArray();
    }

    private static List<ProcessEntry> Capture()
    {
        var entries = new List<ProcessEntry>();
        var parentByPid = ReadParentPids();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id is 0 or 4)
                    continue;

                string? path = null;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                }

                if (string.IsNullOrWhiteSpace(path))
                    path = QueryFullImageName(process.Id);

                parentByPid.TryGetValue(process.Id, out var parentPid);
                entries.Add(new ProcessEntry(process.Id, parentPid, NormalizePath(path)));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return entries;
    }

    private static Dictionary<int, int> ReadParentPids()
    {
        var map = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(0x00000002 /* TH32CS_SNAPPROCESS */, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
            return map;

        try
        {
            var entry = new PROCESSENTRY32W
            {
                dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>()
            };
            if (!Process32FirstW(snapshot, ref entry))
                return map;

            do
            {
                map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
            }
            while (Process32NextW(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return map;
    }

    private static string? QueryFullImageName(int pid)
    {
        var handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            if (!QueryFullProcessImageNameW(handle, 0, buffer, ref size))
                return null;
            return new string(buffer, 0, (int)size);
        }
        finally
        {
            CloseHandle(handle);
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

    private static bool PathsEqual(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private readonly record struct ProcessEntry(int Pid, int ParentPid, string? Path);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, char[] lpExeName, ref uint lpdwSize);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }
}
