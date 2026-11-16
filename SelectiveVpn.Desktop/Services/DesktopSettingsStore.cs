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
            profile.IncludeTargets = CollapseFlatYouTubePreset(CloneTargets(profile.IncludeTargets));
            profile.ExcludeTargets = CollapseFlatYouTubePreset(CloneTargets(profile.ExcludeTargets));
        }

        IncludeTargets = CollapseFlatYouTubePreset(CloneTargets(IncludeTargets));
        ExcludeTargets = CollapseFlatYouTubePreset(CloneTargets(ExcludeTargets));
    }

    public static List<SavedTarget> CloneTargets(IEnumerable<SavedTarget>? targets)
        => (targets ?? [])
            .Where(target => !string.IsNullOrWhiteSpace(target.Path))
            .Select(CloneTarget)
            .ToList();

    public static SavedTarget CloneTarget(SavedTarget target)
        => new()
        {
            Path = target.Path.Trim(),
            Kind = string.IsNullOrWhiteSpace(target.Kind) ? "domain" : target.Kind.Trim().ToLowerInvariant(),
            Entries = [.. (target.Entries ?? []).Where(entry => !string.IsNullOrWhiteSpace(entry)).Select(entry => entry.Trim())],
            ResolvedIps = [.. (target.ResolvedIps ?? []).Where(ip => !string.IsNullOrWhiteSpace(ip)).Select(ip => ip.Trim())],
            SkipDomainResolve = target.SkipDomainResolve
        };

    /// <summary>
    /// Earlier Avalonia YouTube quick-add dumped DomainListPresets.YouTubeGoogle as flat DOMAIN rows.
    /// Collapse that exact set into one address-list (WPF parity) so Sergey can delete it as a group.
    /// </summary>
    public static List<SavedTarget> CollapseFlatYouTubePreset(IEnumerable<SavedTarget>? targets)
    {
        var list = CloneTargets(targets);
        var preset = DomainListPresets.YouTubeGoogle;
        var presetSet = new HashSet<string>(preset, StringComparer.OrdinalIgnoreCase);

        var flatMatches = list
            .Where(target =>
                string.Equals(target.Kind, "domain", StringComparison.OrdinalIgnoreCase)
                && presetSet.Contains(target.Path))
            .ToList();

        if (flatMatches.Count == 0)
            return list;

        // Only collapse when the flat domain rows cover the whole preset (the bad dump case).
        var matchedPaths = new HashSet<string>(flatMatches.Select(t => t.Path), StringComparer.OrdinalIgnoreCase);
        if (!presetSet.IsSubsetOf(matchedPaths))
            return list;

        var alreadyHasYouTubeList = list.Any(target =>
            string.Equals(target.Kind, "address-list", StringComparison.OrdinalIgnoreCase)
            && EntriesCoverPreset(target.Entries, presetSet));

        var rebuilt = new List<SavedTarget>();
        var removedFlat = false;
        foreach (var target in list)
        {
            if (string.Equals(target.Kind, "domain", StringComparison.OrdinalIgnoreCase)
                && presetSet.Contains(target.Path))
            {
                removedFlat = true;
                continue;
            }

            rebuilt.Add(target);
        }

        if (removedFlat && !alreadyHasYouTubeList)
        {
            rebuilt.Insert(0, new SavedTarget
            {
                Path = "YouTube",
                Kind = "address-list",
                Entries = [.. preset],
                ResolvedIps = [],
                SkipDomainResolve = true
            });
        }

        return rebuilt;
    }

    private static bool EntriesCoverPreset(IEnumerable<string>? entries, HashSet<string> presetSet)
    {
        var set = new HashSet<string>(
            (entries ?? []).Where(entry => !string.IsNullOrWhiteSpace(entry)),
            StringComparer.OrdinalIgnoreCase);
        return presetSet.IsSubsetOf(set);
    }

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

    public List<string> Entries { get; set; } = [];

    public List<string> ResolvedIps { get; set; } = [];

    public bool SkipDomainResolve { get; set; }
}

/// <summary>
/// Accepts legacy Avalonia string entries ("example.com") and typed objects ({ path, kind, entries? }).
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
        var entries = ReadStringArray(root, "Entries", "entries");
        var resolved = ReadStringArray(root, "ResolvedIps", "resolvedIps");
        var skip = root.TryGetProperty("SkipDomainResolve", out var s) ? s.GetBoolean()
            : root.TryGetProperty("skipDomainResolve", out var s2) && s2.GetBoolean();

        if (string.IsNullOrWhiteSpace(kindValue) && !string.IsNullOrWhiteSpace(pathValue) && entries.Count == 0)
            return FromLegacyString(pathValue);

        var kind = string.IsNullOrWhiteSpace(kindValue) ? "domain" : kindValue.Trim().ToLowerInvariant();
        if (kind is "addresslist" or "list")
            kind = "address-list";

        return new SavedTarget
        {
            Path = pathValue?.Trim() ?? "",
            Kind = kind,
            Entries = entries,
            ResolvedIps = resolved,
            SkipDomainResolve = skip
        };
    }

    public override void Write(Utf8JsonWriter writer, SavedTarget value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Path", value.Path);
        writer.WriteString("Kind", value.Kind);
        if (string.Equals(value.Kind, "address-list", StringComparison.OrdinalIgnoreCase)
            || value.Entries.Count > 0
            || value.ResolvedIps.Count > 0
            || value.SkipDomainResolve)
        {
            writer.WritePropertyName("Entries");
            writer.WriteStartArray();
            foreach (var entry in value.Entries)
                writer.WriteStringValue(entry);
            writer.WriteEndArray();

            writer.WritePropertyName("ResolvedIps");
            writer.WriteStartArray();
            foreach (var ip in value.ResolvedIps)
                writer.WriteStringValue(ip);
            writer.WriteEndArray();

            writer.WriteBoolean("SkipDomainResolve", value.SkipDomainResolve);
        }

        writer.WriteEndObject();
    }

    private static List<string> ReadStringArray(JsonElement root, string pascal, string camel)
    {
        JsonElement array;
        if (root.TryGetProperty(pascal, out array) || root.TryGetProperty(camel, out array))
        {
            if (array.ValueKind != JsonValueKind.Array)
                return [];
            return array.EnumerateArray()
                .Select(item => item.GetString()?.Trim() ?? "")
                .Where(item => item.Length > 0)
                .ToList();
        }

        return [];
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
