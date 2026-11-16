using System.Text.Json;
using System.Text.Json.Serialization;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;

namespace SelectiveVpn.Desktop.Services;

internal static class DesktopSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new SavedTargetJsonConverter() }
    };

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

    /// <summary>"auto", "ru", or "en". Auto follows CurrentUICulture.</summary>
    public string Language { get; set; } = "auto";

    public List<SavedTarget> IncludeTargets { get; set; } = [];

    public List<SavedTarget> ExcludeTargets { get; set; } = [];

    public List<SavedProfile> Profiles { get; set; } = [];

    public void MigrateLegacy()
    {
        ConnectOnLaunch = false;
        Theme = OrbitTheme.Normalize(Theme);
        if (string.IsNullOrWhiteSpace(Language))
            Language = "auto";

        if (Profiles.Count == 0 && !string.IsNullOrWhiteSpace(ConfigPath) && File.Exists(ConfigPath))
        {
            Profiles.Add(new SavedProfile
            {
                ConfigPath = ConfigPath,
                IncludeTargets = CloneTargets(IncludeTargets),
                ExcludeTargets = CloneTargets(ExcludeTargets)
            });
        }

        foreach (var profile in Profiles)
        {
            profile.IncludeTargets = CloneTargets(profile.IncludeTargets);
            profile.ExcludeTargets = CloneTargets(profile.ExcludeTargets);
        }

        IncludeTargets = CloneTargets(IncludeTargets);
        ExcludeTargets = CloneTargets(ExcludeTargets);
    }

    public static List<SavedTarget> CloneTargets(IEnumerable<SavedTarget>? targets)
        => (targets ?? [])
            .Where(target => !string.IsNullOrWhiteSpace(target.Path))
            .Select(target => new SavedTarget
            {
                Path = target.Path.Trim(),
                Kind = string.IsNullOrWhiteSpace(target.Kind) ? "domain" : target.Kind.Trim().ToLowerInvariant()
            })
            .ToList();

    public static DesktopSettings CreateDefault() => new()
    {
        ConnectOnLaunch = false,
        FullTunnel = false,
        ExceptListed = false,
        Theme = OrbitTheme.Orbit,
        Language = "auto",
        IncludeTargets = [],
        ExcludeTargets = [],
        Profiles = []
    };
}

internal sealed class SavedProfile
{
    public string ConfigPath { get; set; } = "";

    public List<SavedTarget> IncludeTargets { get; set; } = [];

    public List<SavedTarget> ExcludeTargets { get; set; } = [];
}

internal sealed class SavedTarget
{
    public string Path { get; set; } = "";

    public string Kind { get; set; } = "domain";
}

/// <summary>
/// Accepts legacy Avalonia string entries ("example.com") and typed objects ({ path, kind }).
/// </summary>
internal sealed class SavedTargetJsonConverter : JsonConverter<SavedTarget>
{
    public override SavedTarget Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var path = reader.GetString() ?? "";
            return FromLegacyString(path);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected string or object for SavedTarget.");

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var pathValue = root.TryGetProperty("Path", out var p) ? p.GetString()
            : root.TryGetProperty("path", out var p2) ? p2.GetString()
            : "";
        var kindValue = root.TryGetProperty("Kind", out var k) ? k.GetString()
            : root.TryGetProperty("kind", out var k2) ? k2.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(kindValue) && !string.IsNullOrWhiteSpace(pathValue))
            return FromLegacyString(pathValue);

        return new SavedTarget
        {
            Path = pathValue?.Trim() ?? "",
            Kind = string.IsNullOrWhiteSpace(kindValue) ? "domain" : kindValue.Trim().ToLowerInvariant()
        };
    }

    public override void Write(Utf8JsonWriter writer, SavedTarget value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Path", value.Path);
        writer.WriteString("Kind", value.Kind);
        writer.WriteEndObject();
    }

    private static SavedTarget FromLegacyString(string path)
    {
        path = path.Trim();
        if (path.Length == 0)
            return new SavedTarget();

        if (Directory.Exists(path))
            return new SavedTarget { Path = path, Kind = "folder" };

        if (File.Exists(path) || path.StartsWith('/') || path.Contains('\\'))
            return new SavedTarget { Path = path, Kind = "file" };

        if (RouteAddress.TryParse(path, out var normalized, out var kind, out _))
            return new SavedTarget
            {
                Path = normalized,
                Kind = kind == RouteKind.Ip ? "ip" : "domain"
            };

        return new SavedTarget { Path = path, Kind = "domain" };
    }
}
