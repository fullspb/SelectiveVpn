using System.IO;
using System.Text.Json;

namespace SelectiveVpn.Services;

internal static class UserSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SelectiveVpn",
        "settings.json");

    public static UserSettings Load(out bool existed)
    {
        existed = File.Exists(FilePath);
        if (!existed)
            return UserSettings.CreateDefault();

        try
        {
            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath), Options)
                           ?? UserSettings.CreateDefault();
            settings.Apps ??= [];
            settings.Targets ??= [];
            settings.Profiles ??= [];
            settings.Theme = AppTheme.Normalize(settings.Theme);
            if (settings.Targets.Count == 0 && settings.Apps.Count > 0)
            {
                settings.Targets = settings.Apps
                    .Select(path => new SavedTarget { Path = path, Kind = "file" })
                    .ToList();
            }

            foreach (var profile in settings.Profiles)
            {
                profile.Targets ??= [];
                foreach (var target in profile.Targets)
                {
                    target.Entries ??= [];
                    target.ResolvedIps ??= [];
                }
            }

            if (settings.Profiles.Count == 0 && !string.IsNullOrWhiteSpace(settings.ConfigPath))
            {
                settings.Profiles.Add(new SavedProfile
                {
                    ConfigPath = settings.ConfigPath,
                    Targets = settings.Targets.ToList()
                });
            }

            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return UserSettings.CreateDefault();
        }
    }

    public static void Save(UserSettings settings)
    {
        settings.Apps ??= [];
        settings.Targets ??= [];
        settings.Profiles ??= [];
        settings.Theme = AppTheme.Normalize(settings.Theme);
        foreach (var profile in settings.Profiles)
        {
            profile.Targets ??= [];
            foreach (var target in profile.Targets)
            {
                target.Entries ??= [];
                target.ResolvedIps ??= [];
            }
        }

        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, FilePath, overwrite: true);
    }
}

internal sealed class UserSettings
{
    public string? ConfigPath { get; set; }

    public string? EnginePath { get; set; }

    public List<string> Apps { get; set; } = [];

    public List<SavedTarget> Targets { get; set; } = [];

    public List<SavedProfile> Profiles { get; set; } = [];

    public string Theme { get; set; } = AppTheme.Light;

    public bool SkipFullTunnelWarning { get; set; }

    public bool LaunchWithWindows { get; set; }

    public bool ConnectOnLaunch { get; set; }

    public static UserSettings CreateDefault() => new()
    {
        Theme = AppTheme.Light,
        Apps = [],
        Targets = [],
        Profiles = []
    };
}

internal sealed class SavedProfile
{
    public string ConfigPath { get; set; } = "";

    public List<SavedTarget> Targets { get; set; } = [];

    public bool ExceptListed { get; set; }
}

internal sealed class SavedTarget
{
    public string Path { get; set; } = "";

    public string Kind { get; set; } = "file";

    public List<string> Entries { get; set; } = [];

    public List<string> ResolvedIps { get; set; } = [];

    public bool SkipDomainResolve { get; set; }
}
