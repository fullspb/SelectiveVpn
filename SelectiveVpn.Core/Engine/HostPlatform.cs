namespace SelectiveVpn.Engine;

/// <summary>Small OS helpers shared by Core engine code (headers, paths, binaries).</summary>
public static class HostPlatform
{
    public static string DeviceOsName
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return "Windows";
            if (OperatingSystem.IsLinux())
                return "Linux";
            if (OperatingSystem.IsMacOS())
                return "macOS";
            return "Unknown";
        }
    }

    public static char ProcessPathSeparator
        => OperatingSystem.IsWindows() ? '\\' : '/';

    public static StringComparer PathComparer
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static bool PathsEqual(string left, string right)
        => PathComparer.Equals(left, right);

    public static bool IsEngineExecutableName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        if (OperatingSystem.IsWindows())
            return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        // Unix release archives ship a single binary without an extension.
        return !fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
               && !fileName.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
               && !fileName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
               && !fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
               && !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikeCustomEnginePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        if (OperatingSystem.IsWindows())
            return string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);

        return true;
    }
}
