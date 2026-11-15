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
            return JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(AppPaths.SettingsPath), Options)
                   ?? DesktopSettings.CreateDefault();
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

    public static DesktopSettings CreateDefault() => new()
    {
        ConnectOnLaunch = false
    };
}
