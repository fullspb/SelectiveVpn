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
            Normalize(settings);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return UserSettings.CreateDefault();
        }
    }

    public static void Save(UserSettings settings)
    {
        Normalize(settings);
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, FilePath, overwrite: true);
    }

    internal static void Normalize(UserSettings settings)
    {
        settings.Apps ??= [];
        settings.Targets ??= [];
        settings.Profiles ??= [];
        settings.Theme = AppTheme.Normalize(settings.Theme);
        settings.Language = AppLanguage.Normalize(settings.Language);
        if (settings.Targets.Count == 0 && settings.Apps.Count > 0)
            settings.Targets = settings.Apps.Select(path => new SavedTarget { Path = path, Kind = "file" }).ToList();

        if (settings.Profiles.Count == 0 && !string.IsNullOrWhiteSpace(settings.ConfigPath))
            settings.Profiles.Add(new SavedProfile { ConfigPath = settings.ConfigPath, Targets = CloneTargets(settings.Targets) });

        foreach (var profile in settings.Profiles)
        {
            // Two missing buckets mean an older release. Its shared list belongs to the mode
            // that was saved with it; an explicitly empty modern bucket stays empty.
            var legacy = CloneTargets(profile.Targets);
            var legacyProfile = profile.IncludeTargets is null && profile.ExcludeTargets is null;
            profile.IncludeTargets = profile.IncludeTargets is null
                ? legacyProfile && !profile.ExceptListed ? CloneTargets(legacy) : []
                : CloneTargets(profile.IncludeTargets);
            profile.ExcludeTargets = profile.ExcludeTargets is null
                ? legacyProfile && profile.ExceptListed ? CloneTargets(legacy) : []
                : CloneTargets(profile.ExcludeTargets);
            profile.Targets = CloneTargets(profile.ExceptListed ? profile.ExcludeTargets : profile.IncludeTargets);
        }

        var active = settings.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.ConfigPath, settings.ConfigPath, StringComparison.OrdinalIgnoreCase));
        settings.Targets = CloneTargets(active?.Targets ?? settings.Targets);
    }

    internal static List<SavedTarget> CloneTargets(IEnumerable<SavedTarget>? targets)
        => targets?.Select(target => new SavedTarget
        {
            Path = target.Path,
            Kind = target.Kind,
            Entries = target.Entries?.ToList() ?? [],
            ResolvedIps = target.ResolvedIps?.ToList() ?? [],
            SkipDomainResolve = target.SkipDomainResolve
        }).ToList() ?? [];
}

internal sealed class UserSettings
{
    public string? ConfigPath { get; set; }

    public string? EnginePath { get; set; }

    public List<string> Apps { get; set; } = [];

    public List<SavedTarget> Targets { get; set; } = [];

    public List<SavedProfile> Profiles { get; set; } = [];

    public string Theme { get; set; } = AppTheme.Orbit;

    public string Language { get; set; } = AppLanguage.Auto;

    public bool SkipFullTunnelWarning { get; set; }

    public bool LaunchWithWindows { get; set; }

    public bool ConnectOnLaunch { get; set; }

    public bool AutoReconnect { get; set; }

    public static UserSettings CreateDefault() => new()
    {
        Theme = AppTheme.Orbit,
        Language = AppLanguage.Auto,
        Apps = [],
        Targets = [],
        Profiles = []
    };
}

internal sealed class SavedProfile
{
    public string ConfigPath { get; set; } = "";

    public List<SavedTarget> Targets { get; set; } = [];

    public List<SavedTarget>? IncludeTargets { get; set; }

    public List<SavedTarget>? ExcludeTargets { get; set; }

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
