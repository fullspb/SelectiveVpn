using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SelectiveVpn.Services;

internal static class CountryFlags
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Names = new(LoadNames);
    private static readonly ConcurrentDictionary<string, BitmapSource> Images = new(StringComparer.Ordinal);

    public static string Normalize(string? countryCode)
    {
        if (countryCode is null || countryCode.Length != 2)
            return "";
        var code = countryCode.ToLowerInvariant();
        return Names.Value.TryGetValue("en", out var countries) && countries.ContainsKey(code)
            ? code.ToUpperInvariant() : "";
    }

    public static string GetName(string? countryCode, string? language = null)
    {
        var normalized = Normalize(countryCode);
        if (normalized.Length == 0)
            return "";
        var code = normalized.ToLowerInvariant();
        var selectedLanguage = language ?? AppLanguage.CurrentCode;
        if (Names.Value.TryGetValue(selectedLanguage, out var names) && names.TryGetValue(code, out var localized))
            return localized;
        return Names.Value["en"].GetValueOrDefault(code, normalized);
    }

    public static BitmapSource? GetImage(string? countryCode)
    {
        var code = Normalize(countryCode);
        if (code.Length == 0)
            return null;
        if (Images.TryGetValue(code, out var image))
            return image;
        try
        {
            var resource = Application.GetResourceStream(Resource($"{code.ToLowerInvariant()}.png"));
            if (resource is null)
                return null;
            using var stream = resource.Stream;
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            frame.Freeze();
            return Images.GetOrAdd(code, frame);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static Dictionary<string, Dictionary<string, string>> LoadNames()
    {
        try
        {
            var resource = Application.GetResourceStream(Resource("names.json"));
            if (resource is null)
                return [];
            using var stream = resource.Stream;
            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream) ?? [];
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    private static Uri Resource(string name) => new(
        $"pack://application:,,,/SelectiveVpn;component/Assets/Flags/{name}", UriKind.Absolute);
}
