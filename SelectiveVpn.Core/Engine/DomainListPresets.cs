namespace SelectiveVpn.Engine;

/// <summary>
/// Built-in domain packs for address-list routing (DOMAIN-SUFFIX).
/// Kept intentionally compact: CDN hostnames change often; sniffer + fake-ip cover the rest.
/// </summary>
public static class DomainListPresets
{
    public const string YouTubeGoogleId = "youtube-google";

    public static IReadOnlyList<string> YouTubeGoogle { get; } =
    [
        "youtube.com",
        "youtu.be",
        "youtube-nocookie.com",
        "youtubekids.com",
        "googlevideo.com",
        "ytimg.com",
        "ggpht.com",
        "gvt1.com",
        "gvt2.com",
        "gvt3.com",
        "googleapis.com",
        "youtube.googleapis.com",
        "youtubei.googleapis.com",
        "gstatic.com",
        "googleusercontent.com",
        "google.com",
        "withyoutube.com"
    ];
}
