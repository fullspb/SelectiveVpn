using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using System.Text;
using Microsoft.Win32;
using SelectiveVpn.Models;

namespace SelectiveVpn.Services;

/// <summary>
/// Собирает установленные приложения из реестра и ярлыков меню Пуск.
/// </summary>
public static class InstalledAppCatalog
{
    private static readonly string[] UninstallRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    public static IReadOnlyList<InstalledAppInfo> Load()
    {
        var apps = new Dictionary<string, InstalledAppInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in UninstallRoots)
        {
            TryCollect(() => CollectFromUninstall(RegistryHive.LocalMachine, root, apps));
            TryCollect(() => CollectFromUninstall(RegistryHive.CurrentUser, root, apps));
        }

        TryCollect(() => CollectFromAppPaths(RegistryHive.LocalMachine, apps));
        TryCollect(() => CollectFromAppPaths(RegistryHive.CurrentUser, apps));
        TryCollect(() => CollectFromStartMenu(apps));

        return apps.Values
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static void TryCollect(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // Отдельный источник не должен ронять весь список.
        }
    }

    private static void CollectFromUninstall(RegistryHive hive, string root, IDictionary<string, InstalledAppInfo> apps)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(root);
        if (key is null)
            return;

        foreach (var subName in key.GetSubKeyNames())
        {
            try
            {
                using var sub = key.OpenSubKey(subName);
                if (sub is null)
                    continue;

                if (EqualsOne(sub.GetValue("SystemComponent"), 1))
                    continue;

                var name = ReadString(sub, "DisplayName");
                if (string.IsNullOrWhiteSpace(name) || IsUninstallerName(name))
                    continue;

                var exe = ResolveExecutable(
                    ReadString(sub, "DisplayIcon"),
                    ReadString(sub, "InstallLocation"),
                    name);

                TryAdd(apps, name, exe);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
            }
        }

        // 32-bit view for WOW apps when scanning non-WOW path on 64-bit OS.
        if (root.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            using var base32 = RegistryKey.OpenBaseKey(hive, RegistryView.Registry32);
            using var key32 = base32.OpenSubKey(root);
            if (key32 is null)
                return;

            foreach (var subName in key32.GetSubKeyNames())
            {
                try
                {
                    using var sub = key32.OpenSubKey(subName);
                    if (sub is null)
                        continue;

                    if (EqualsOne(sub.GetValue("SystemComponent"), 1))
                        continue;

                    var name = ReadString(sub, "DisplayName");
                    if (string.IsNullOrWhiteSpace(name) || IsUninstallerName(name))
                        continue;

                    var exe = ResolveExecutable(
                        ReadString(sub, "DisplayIcon"),
                        ReadString(sub, "InstallLocation"),
                        name);

                    TryAdd(apps, name, exe);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                {
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private static void CollectFromAppPaths(RegistryHive hive, IDictionary<string, InstalledAppInfo> apps)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
        if (key is null)
            return;

        foreach (var subName in key.GetSubKeyNames())
        {
            try
            {
                using var sub = key.OpenSubKey(subName);
                if (sub is null)
                    continue;

                var exe = CleanPath(ReadString(sub, null));
                if (string.IsNullOrWhiteSpace(exe)
                    || !string.Equals(Path.GetExtension(exe), ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    exe = CleanPath(subName);
                }

                var name = Path.GetFileNameWithoutExtension(exe);
                if (string.IsNullOrWhiteSpace(name))
                    name = Path.GetFileNameWithoutExtension(subName);

                TryAdd(apps, name!, exe);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
            }
        }
    }

    private static void CollectFromStartMenu(IDictionary<string, InstalledAppInfo> apps)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;

            foreach (var link in EnumerateFilesSafe(root, "*.lnk"))
            {
                string? target = null;
                try
                {
                    target = ShellLink.TryResolveTarget(link);
                }
                catch (Exception)
                {
                }

                if (string.IsNullOrWhiteSpace(target))
                    continue;

                var name = Path.GetFileNameWithoutExtension(link);
                if (string.IsNullOrWhiteSpace(name) || IsUninstallerName(name))
                    continue;

                TryAdd(apps, name, target);
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            string[] files = [];
            string[] dirs = [];
            try
            {
                files = Directory.GetFiles(dir, pattern);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
            }

            try
            {
                dirs = Directory.GetDirectories(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
            }

            foreach (var file in files)
                yield return file;

            foreach (var child in dirs)
                pending.Push(child);
        }
    }

    private static void TryAdd(IDictionary<string, InstalledAppInfo> apps, string name, string? executablePath)
    {
        var exe = CleanPath(executablePath);
        if (string.IsNullOrWhiteSpace(exe)
            || !string.Equals(Path.GetExtension(exe), ".exe", StringComparison.OrdinalIgnoreCase)
            || IsUninstallerPath(exe)
            || IsNoisePath(exe))
        {
            return;
        }

        // Expand env vars like %ProgramFiles%
        try
        {
            exe = Environment.ExpandEnvironmentVariables(exe);
        }
        catch (Exception)
        {
            return;
        }

        if (!File.Exists(exe))
            return;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(exe);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return;
        }

        if (apps.ContainsKey(fullPath))
            return;

        apps[fullPath] = new InstalledAppInfo(name.Trim(), fullPath);
    }

    private static string? ResolveExecutable(string? displayIcon, string? installLocation, string name)
    {
        var fromIcon = CleanPath(displayIcon);
        if (!string.IsNullOrWhiteSpace(fromIcon))
        {
            fromIcon = Environment.ExpandEnvironmentVariables(fromIcon);
            if (string.Equals(Path.GetExtension(fromIcon), ".exe", StringComparison.OrdinalIgnoreCase)
                && File.Exists(fromIcon))
            {
                return fromIcon;
            }
        }

        if (string.IsNullOrWhiteSpace(installLocation))
            return fromIcon;

        try
        {
            installLocation = Environment.ExpandEnvironmentVariables(installLocation);
            if (!Directory.Exists(installLocation))
                return fromIcon;

            var preferred = Path.Combine(installLocation, name + ".exe");
            if (File.Exists(preferred))
                return preferred;

            return Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => !IsUninstallerPath(path))
                ?? fromIcon;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return fromIcon;
        }
    }

    private static string? CleanPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var path = value.Trim().Trim('"');
        var comma = path.LastIndexOf(',');
        if (comma > 0 && comma >= path.Length - 4)
        {
            var suffix = path[(comma + 1)..].Trim();
            if (int.TryParse(suffix, out _))
                path = path[..comma].Trim().Trim('"');
        }

        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private static string? ReadString(RegistryKey key, string? name)
        => key.GetValue(name) as string;

    private static bool EqualsOne(object? value, int expected)
        => value is int number && number == expected
           || value is long longNumber && longNumber == expected;

    private static bool IsUninstallerName(string name)
        => name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
           || name.Contains("удаление", StringComparison.OrdinalIgnoreCase);

    private static bool IsUninstallerPath(string path)
    {
        var file = Path.GetFileName(path);
        return file.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
               || file.Contains("uninstall", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNoisePath(string path)
    {
        if (path.Contains(@"\Windows\Installer\", StringComparison.OrdinalIgnoreCase))
            return true;

        var file = Path.GetFileName(path);
        return file.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)
               || file.Equals("control.exe", StringComparison.OrdinalIgnoreCase)
               || file.Equals("InstallAgent.exe", StringComparison.OrdinalIgnoreCase)
               || file.Equals("Setup.exe", StringComparison.OrdinalIgnoreCase)
               || file.Equals("installer.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static class ShellLink
    {
        public static string? TryResolveTarget(string shortcutPath)
        {
            object? com = null;
            try
            {
                var type = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"));
                if (type is null)
                    return null;

                com = Activator.CreateInstance(type);
                if (com is null)
                    return null;

                var link = (IShellLinkW)com;
                ((IPersistFile)link).Load(shortcutPath, 0);
                var buffer = new StringBuilder(260);
                link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
                return buffer.Length == 0 ? null : buffer.ToString();
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (com is not null && Marshal.IsComObject(com))
                    Marshal.FinalReleaseComObject(com);
            }
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath(
                [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
                int cchMaxPath,
                IntPtr pfd,
                int fFlags);

            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }
}
