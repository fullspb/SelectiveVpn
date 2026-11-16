using System.Text.Json;

namespace SelectiveVpn.Desktop.Services;

internal static class DesktopSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static DesktopSettings Load(out bool existed)
    {
        existed = File.Exists(AppPaths.SettingsPath);
        if (!existed)
            return DesktopSettings.CreateDefault();

        try
        {
            var settings = JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(AppPaths.SettingsPath), Options)
                           ?? DesktopSettings.CreateDefault();
            settings.MigrateLegacy();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return DesktopSettings.CreateDefault();
        }
    }

    public static void Save(DesktopSettings settings)
    {
        Directory.CreateDirectory(AppPaths.RootDirectory);
        var temporary = AppPaths.SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, AppPaths.SettingsPath, overwrite: true);
    }
}

internal sealed class DesktopSettings
{
    public string? ConfigPath { get; set; }

    public string? EnginePath { get; set; }

    /// <summary>Always leave false unless Sergey explicitly enables auto-connect.</summary>
    public bool ConnectOnLaunch { get; set; }

    /// <summary>Legacy Linux MVP flag; kept for migration. Prefer RoutingModeExcept + empty list.</summary>
    public bool FullTunnel { get; set; }

    public bool ExceptListed { get; set; }

    public string Theme { get; set; } = OrbitTheme.Orbit;

    public List<string> IncludeTargets { get; set; } = [];

    public List<string> ExcludeTargets { get; set; } = [];

    public List<SavedProfile> Profiles { get; set; } = [];

    public void MigrateLegacy()
    {
        ConnectOnLaunch = false;
        Theme = OrbitTheme.Normalize(Theme);

        if (Profiles.Count == 0 && !string.IsNullOrWhiteSpace(ConfigPath) && File.Exists(ConfigPath))
        {
            Profiles.Add(new SavedProfile
            {
                ConfigPath = ConfigPath,
                IncludeTargets = IncludeTargets?.ToList() ?? [],
                ExcludeTargets = ExcludeTargets?.ToList() ?? []
            });
        }

        foreach (var profile in Profiles)
        {
            profile.IncludeTargets ??= [];
            profile.ExcludeTargets ??= [];
        }
    }

    public static DesktopSettings CreateDefault() => new()
    {
        ConnectOnLaunch = false,
        FullTunnel = false,
        ExceptListed = false,
        Theme = OrbitTheme.Orbit,
        IncludeTargets = [],
        ExcludeTargets = [],
        Profiles = []
    };
}

internal sealed class SavedProfile
{
    public string ConfigPath { get; set; } = "";

    public List<string> IncludeTargets { get; set; } = [];

    public List<string> ExcludeTargets { get; set; } = [];
}
