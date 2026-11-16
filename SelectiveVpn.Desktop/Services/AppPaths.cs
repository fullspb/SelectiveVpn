namespace SelectiveVpn.Desktop.Services;

internal static class AppPaths
{
    /// <summary>
    /// Linux: ~/.local/share/SelectiveVpn (LocalApplicationData).
    /// Holds settings.json, engine/, runtime/, device-id.txt.
    /// </summary>
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SelectiveVpn");

    public static string SettingsPath => Path.Combine(RootDirectory, "settings.json");

    public static string EngineBinaryPath => Path.Combine(RootDirectory, "engine", "mihomo");

    /// <summary>
    /// Packaged / published mihomo next to the app (e.g. /usr/lib/selectivevpn/mihomo).
    /// MihomoEngine prefers this over downloading into ~/.local/share.
    /// </summary>
    public static string BundledEngineBinaryPath
        => Path.Combine(AppContext.BaseDirectory, "mihomo");

    public static string HelpersDirectory
    {
        get
        {
            var besideApp = Path.Combine(AppContext.BaseDirectory, "Helpers");
            if (Directory.Exists(besideApp))
                return besideApp;

            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Helpers"));
        }
    }

    public static string ApplyCapsScript => Path.Combine(HelpersDirectory, "apply-mihomo-caps.sh");

    public static string TeardownScript => Path.Combine(HelpersDirectory, "teardown-tunnel.sh");
}
