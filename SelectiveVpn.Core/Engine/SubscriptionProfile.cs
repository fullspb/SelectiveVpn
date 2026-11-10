using System.Text.Json;
using System.Text.Json.Serialization;

namespace SelectiveVpn.Engine;

public sealed class SubscriptionProfile
{
    public const string FileExtension = ".sub.json";
    public const string KindValue = "subscription";
    public const string AutoNodeName = "AUTO";
    public const string AutoNodeDisplayName = "Авто (лучший)";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Kind { get; set; } = KindValue;

    public string Url { get; set; } = "";

    public string Title { get; set; } = "Подписка";

    public string? ProfileTitleHeader { get; set; }

    public int NodeCount { get; set; }

    public string[] Protocols { get; set; } = [];

    /// <summary>Имя узла в группе VPN. null/пусто/AUTO — автоматический выбор.</summary>
    public string? SelectedNode { get; set; }

    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;

    public string EffectiveSelectedNode
        => string.IsNullOrWhiteSpace(SelectedNode) ? AutoNodeName : SelectedNode.Trim();

    public static string DisplayNodeName(string? nodeName)
        => string.IsNullOrWhiteSpace(nodeName) || nodeName.Equals(AutoNodeName, StringComparison.OrdinalIgnoreCase)
            ? AutoNodeDisplayName
            : nodeName;

    public string Describe()
    {
        var protocols = Protocols.Length == 0 ? "узлы" : string.Join("/", Protocols);
        var nodes = NodeCount <= 0 ? "подписка" : $"{NodeCount} {FormatNodes(NodeCount)}";
        return $"{Title}: {nodes} ({protocols})";
    }

    public static bool IsSubscriptionPath(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase);

    public static bool TryRead(string path, out SubscriptionProfile profile, out string error)
    {
        profile = null!;
        error = "";
        try
        {
            if (!File.Exists(path))
            {
                error = "Файл подписки не найден.";
                return false;
            }

            var parsed = JsonSerializer.Deserialize<SubscriptionProfile>(File.ReadAllText(path), JsonOptions);
            if (parsed is null
                || !string.Equals(parsed.Kind, KindValue, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(parsed.Url))
            {
                error = "Файл подписки повреждён.";
                return false;
            }

            if (!Uri.TryCreate(parsed.Url.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                error = "В файле подписки некорректная ссылка.";
                return false;
            }

            parsed.Url = uri.AbsoluteUri;
            if (string.IsNullOrWhiteSpace(parsed.Title))
                parsed.Title = "Подписка";
            profile = parsed;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = "Не удалось прочитать файл подписки.";
            return false;
        }
    }

    public static string Write(string directory, SubscriptionProfile profile, string? preferredStem = null)
    {
        Directory.CreateDirectory(directory);
        var stem = SanitizeStem(string.IsNullOrWhiteSpace(preferredStem) ? profile.Title : preferredStem);
        if (stem.Length == 0)
            stem = "subscription";

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(directory, $"{stem}-{stamp}{FileExtension}");
        SaveToPath(path, profile);
        return path;
    }

    public static void SaveToPath(string path, SubscriptionProfile profile)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        profile.SavedAt = DateTimeOffset.UtcNow;
        File.WriteAllText(path, JsonSerializer.Serialize(profile, JsonOptions));
    }

    public static bool TrySaveSelectedNode(string path, string? nodeName, out string error)
    {
        error = "";
        if (!TryRead(path, out var profile, out error))
            return false;

        profile.SelectedNode = string.IsNullOrWhiteSpace(nodeName) || nodeName.Equals(AutoNodeName, StringComparison.OrdinalIgnoreCase)
            ? AutoNodeName
            : nodeName.Trim();

        try
        {
            SaveToPath(path, profile);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Не удалось сохранить выбранный сервер.";
            return false;
        }
    }

    private static string SanitizeStem(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(ch => invalid.Contains(ch) || ch is ':' or '?' or '#' ? '-' : ch).ToArray();
        var cleaned = new string(chars).Trim(' ', '.', '-');
        while (cleaned.Contains("--", StringComparison.Ordinal))
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        return cleaned.Length > 48 ? cleaned[..48].TrimEnd('-', '.') : cleaned;
    }

    private static string FormatNodes(int count)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;
        if (mod100 is >= 11 and <= 14)
            return "узлов";
        return mod10 switch
        {
            1 => "узел",
            2 or 3 or 4 => "узла",
            _ => "узлов"
        };
    }
}
