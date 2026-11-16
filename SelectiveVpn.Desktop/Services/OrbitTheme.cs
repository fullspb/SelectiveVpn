using Avalonia;
using Avalonia.Media;

namespace SelectiveVpn.Desktop.Services;

/// <summary>
/// Avalonia port of SelectiveVpn.Services.AppTheme (Orbit palettes).
/// DropShadowEffect / BlurEffect → BoxShadow or omitted where Avalonia cannot match WPF.
/// </summary>
internal static class OrbitTheme
{
    public const string Orbit = "orbit";
    public const string Aurora = "aurora";
    public const string Ocean = "ocean";
    public const string Plum = "plum";
    public const string Sunset = "sunset";
    public const string Graphite = "graphite";

    public static readonly string[] All = [Orbit, Aurora, Ocean, Plum, Sunset, Graphite];

    public static string CurrentTheme { get; private set; } = Orbit;

    private static readonly Dictionary<string, Dictionary<string, string>> Palettes = new(StringComparer.OrdinalIgnoreCase)
    {
        [Orbit] = CreateLook(
            background: "#0B0E1C", panel: "#151A31", foreground: "#E8ECFF", muted: "#9AA3C7", border: "#2A3358", soft: "#1B2241",
            accent: "#7FE3FF", onAccent: "#071022", accentSoft: "#17304C", accent2: "#8C6EFF", accent2Light: "#B9A6FF",
            edge: "#2B5C8F", primaryBorder: "#3B4FA8", glassFrom: "#CC1B2244", glassTo: "#99121733",
            glow1: "#5A4BD6", glow2: "#2B9EC4", glowShadow: "#50B4FF", idle: "#8A93B5", busy: "#FFC46E", avatar3: "#FFC46E", star: "#DDE4FF"),
        [Aurora] = CreateLook(
            background: "#08120F", panel: "#10201B", foreground: "#E6F5EE", muted: "#93B0A4", border: "#23413A", soft: "#162B25",
            accent: "#6FE8B0", onAccent: "#04130D", accentSoft: "#143A2E", accent2: "#2FB8C9", accent2Light: "#8FE3F0",
            edge: "#1F6B52", primaryBorder: "#2E8C6C", glassFrom: "#CC14302A", glassTo: "#990E211C",
            glow1: "#1FA37A", glow2: "#3B6FD6", glowShadow: "#3FD6A6", idle: "#7F8C87", busy: "#FFC46E", avatar3: "#FFC46E", star: "#DDF5EA"),
        [Ocean] = CreateLook(
            background: "#071019", panel: "#0F1D2E", foreground: "#E4EEFA", muted: "#92A8C2", border: "#213A57", soft: "#14263C",
            accent: "#5AB8FF", onAccent: "#04101E", accentSoft: "#123250", accent2: "#3D7BFF", accent2Light: "#9DBEFF",
            edge: "#1F5C99", primaryBorder: "#2F6FC0", glassFrom: "#CC132A45", glassTo: "#990D1C30",
            glow1: "#2456D6", glow2: "#1C8FB8", glowShadow: "#3F9BFF", idle: "#8499B3", busy: "#FFC46E", avatar3: "#FFC46E", star: "#DCE9FF"),
        [Plum] = CreateLook(
            background: "#140A18", panel: "#21122A", foreground: "#F6E9FA", muted: "#B79DC2", border: "#452A55", soft: "#2C1838",
            accent: "#F58AD8", onAccent: "#1E0718", accentSoft: "#45173F", accent2: "#A26BFF", accent2Light: "#C9A8FF",
            edge: "#8C3A7A", primaryBorder: "#A0489A", glassFrom: "#CC2B1638", glassTo: "#991C0E26",
            glow1: "#9A3BD6", glow2: "#D63B8F", glowShadow: "#D97BE8", idle: "#A68FB0", busy: "#FFC46E", avatar3: "#FFC46E", star: "#F3E1FF"),
        [Sunset] = CreateLook(
            background: "#160D0A", panel: "#251712", foreground: "#FBEFE6", muted: "#C2A593", border: "#55362A", soft: "#301E17",
            accent: "#FFB25E", onAccent: "#1F0E03", accentSoft: "#4A2A14", accent2: "#FF6B6B", accent2Light: "#FFA59E",
            edge: "#A3601F", primaryBorder: "#B8742E", glassFrom: "#CC33201A", glassTo: "#99221410",
            glow1: "#D65A2B", glow2: "#C4902B", glowShadow: "#FF9457", idle: "#B39A8B", busy: "#FFF0B0", avatar3: "#F7E08A", star: "#FFEBDD"),
        [Graphite] = CreateLook(
            background: "#0D0E10", panel: "#18191D", foreground: "#ECEDEF", muted: "#9EA2AB", border: "#33363D", soft: "#202227",
            accent: "#D5DCE8", onAccent: "#0D0E10", accentSoft: "#2A2E36", accent2: "#8E99AD", accent2Light: "#B8C0CF",
            edge: "#5B6475", primaryBorder: "#707A8C", glassFrom: "#CC1E2025", glassTo: "#99141518",
            glow1: "#3A3F4B", glow2: "#2C3340", glowShadow: "#AEB8C8", idle: "#7E828B", busy: "#FFC46E", avatar3: "#FFC46E", star: "#E6E8EC")
    };

    public static string Normalize(string? theme)
    {
        var value = theme?.Trim();
        return All.FirstOrDefault(known => known.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? Orbit;
    }

    public static IReadOnlyDictionary<string, string> ColorsFor(string? theme)
        => Palettes[Normalize(theme)];

    public static void Apply(Avalonia.Application? app, string? theme)
    {
        if (app is null)
            return;

        var normalized = Normalize(theme);
        CurrentTheme = normalized;
        var palette = Palettes[normalized];

        foreach (var (key, hex) in palette)
            app.Resources[key] = Brush(hex);

        var accent = Color.Parse(palette["Accent"]);
        var accentDeep = Color.Parse(palette["HeroActive2"]);

        app.Resources["ThemeFontFamily"] = new FontFamily("Inter, Segoe UI, Ubuntu, sans-serif");
        app.Resources["ThemeMonoFontFamily"] = new FontFamily("Cascadia Code, Consolas, monospace");
        app.Resources["AccentWash"] = Tint(accent, 0x1A);
        app.Resources["AccentWashStrong"] = Tint(accent, 0x26);
        app.Resources["AccentLine"] = Tint(accent, 0x33);
        app.Resources["AccentLineStrong"] = Tint(accent, 0x40);
        app.Resources["AccentLineBright"] = Tint(accent, 0x73);
        app.Resources["AccentBright"] = Tint(Color.FromRgb(Lighten(accent.R), Lighten(accent.G), Lighten(accent.B)), 0xFF);
        app.Resources["OrbitAccentSoftBackground"] = Brush(palette["OrbitAccentSoftBackground"]);
        app.Resources["PushBackground"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse(palette["Accent"]), 0),
                new GradientStop(Color.Parse(palette["HeroActive2"]), 1)
            ]
        };
        app.Resources["ProfileSelectedBackground"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.FromArgb(0x2E, accent.R, accent.G, accent.B), 0),
                new GradientStop(Color.FromArgb(0x1F, accentDeep.R, accentDeep.G, accentDeep.B), 1)
            ]
        };
        // Avalonia substitute for WPF DropShadowEffect on plates / push button.
        app.Resources["OrbitPlateShadow"] = new BoxShadows(new BoxShadow
        {
            Blur = 40,
            OffsetY = 18,
            Color = Color.FromArgb(0x99, 0, 0, 0)
        });
        app.Resources["PushButtonShadow"] = new BoxShadows(new BoxShadow
        {
            Blur = 24,
            OffsetY = 4,
            Color = Color.FromArgb(0x73, accent.R, accent.G, accent.B)
        });
    }

    private static Dictionary<string, string> CreateLook(
        string background, string panel, string foreground, string muted, string border, string soft,
        string accent, string onAccent, string accentSoft, string accent2, string accent2Light,
        string edge, string primaryBorder, string glassFrom, string glassTo,
        string glow1, string glow2, string glowShadow, string idle, string busy, string avatar3, string star)
    {
        return new Dictionary<string, string>
        {
            ["WindowBackground"] = background,
            ["WindowForeground"] = foreground,
            ["MutedForeground"] = muted,
            ["CardBackground"] = panel,
            ["CardBorder"] = border,
            ["Accent"] = accent,
            ["ButtonBackground"] = soft,
            ["ButtonForeground"] = foreground,
            ["ButtonBorder"] = border,
            ["PrimaryBackground"] = accent,
            ["PrimaryForeground"] = onAccent,
            ["PrimaryBorder"] = primaryBorder,
            ["PrimaryEdge"] = edge,
            ["FieldBackground"] = panel,
            ["FieldBorder"] = border,
            ["InfoBackground"] = accentSoft,
            ["InfoBorder"] = border,
            ["InfoForeground"] = accent,
            ["EmptyForeground"] = muted,
            ["PathForeground"] = muted,
            ["StatusIdleBackground"] = soft,
            ["StatusIdleBorder"] = border,
            ["StatusIdleForeground"] = muted,
            ["StatusConnectedBackground"] = accentSoft,
            ["StatusConnectedBorder"] = edge,
            ["StatusConnectedForeground"] = accent,
            ["StatusConnectingBackground"] = accentSoft,
            ["StatusConnectingBorder"] = border,
            ["StatusConnectingForeground"] = accent,
            ["StatusErrorBackground"] = "#3C232B",
            ["StatusErrorBorder"] = "#75404B",
            ["StatusErrorForeground"] = "#F0A8B5",
            ["DangerForeground"] = "#F0A8B5",
            ["DangerBackground"] = "#9A3B50",
            ["DangerBorder"] = "#B95C70",
            ["EyebrowForeground"] = muted,
            ["OrbitSoftBackground"] = soft,
            ["OrbitAccentSoftBackground"] = accentSoft,
            ["OrbitAccentForeground"] = accent,
            ["HeroIdle"] = idle,
            ["HeroActive"] = accent,
            ["HeroActive2"] = accent2,
            ["HeroBusy"] = busy,
            ["Accent2"] = accent2Light,
            ["Avatar3"] = avatar3,
            ["GlassFrom"] = glassFrom,
            ["GlassTo"] = glassTo,
            ["BackgroundGlow1"] = glow1,
            ["BackgroundGlow2"] = glow2,
            ["BackgroundStar"] = star,
            ["SelectionGlow"] = glowShadow,
            ["PanelBackground"] = panel
        };
    }

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    private static IBrush Tint(Color color, byte alpha)
        => new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));

    private static byte Lighten(byte channel) => (byte)(channel + (255 - channel) * 0.6);
}
