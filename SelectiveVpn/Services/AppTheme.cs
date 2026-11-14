using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SelectiveVpn.Services;

internal static class AppTheme
{
    // The looks offered in settings. All share the Orbit layout and differ in colour.
    public const string Orbit = "orbit";
    public const string Aurora = "aurora";
    public const string Ocean = "ocean";
    public const string Plum = "plum";
    public const string Sunset = "sunset";
    public const string Graphite = "graphite";

    // Identifiers saved by releases that had other looks. Normalize maps them to Orbit.
    public const string Light = "light";
    public const string Dark = "dark";
    public const string Green = "green";
    public const string Oled = "oled";
    public const string Amber = "amber";
    public const string Indigo = "indigo";
    public const string Depth = "depth";
    public const string Retro = "retro";

    public static readonly string[] All = [Orbit, Aurora, Ocean, Plum, Sunset, Graphite];

    public static string CurrentTheme { get; private set; } = Orbit;

    private static readonly Dictionary<string, Dictionary<string, string>> Palettes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Cyan and violet on deep navy: the original look.
        [Orbit] = CreateLook(
            background: "#0B0E1C", panel: "#151A31", foreground: "#E8ECFF", muted: "#9AA3C7", border: "#2A3358", soft: "#1B2241",
            accent: "#7FE3FF", onAccent: "#071022", accentSoft: "#17304C", accent2: "#8C6EFF", accent2Light: "#B9A6FF",
            edge: "#2B5C8F", primaryBorder: "#3B4FA8", glassFrom: "#CC1B2244", glassTo: "#99121733",
            glow1: "#5A4BD6", glow2: "#2B9EC4", glowShadow: "#50B4FF", idle: "#8A93B5", busy: "#FFC46E", avatar3: "#FFC46E", star: "#DDE4FF"),
        // Mint and teal on a dark forest green.
        [Aurora] = CreateLook(
            background: "#08120F", panel: "#10201B", foreground: "#E6F5EE", muted: "#93B0A4", border: "#23413A", soft: "#162B25",
            accent: "#6FE8B0", onAccent: "#04130D", accentSoft: "#143A2E", accent2: "#2FB8C9", accent2Light: "#8FE3F0",
            edge: "#1F6B52", primaryBorder: "#2E8C6C", glassFrom: "#CC14302A", glassTo: "#990E211C",
            glow1: "#1FA37A", glow2: "#3B6FD6", glowShadow: "#3FD6A6", idle: "#7F8C87", busy: "#FFC46E", avatar3: "#FFC46E", star: "#DDF5EA"),
        // Sky and cobalt blue on a deep sea background.
        [Ocean] = CreateLook(
            background: "#071019", panel: "#0F1D2E", foreground: "#E4EEFA", muted: "#92A8C2", border: "#213A57", soft: "#14263C",
            accent: "#5AB8FF", onAccent: "#04101E", accentSoft: "#123250", accent2: "#3D7BFF", accent2Light: "#9DBEFF",
            edge: "#1F5C99", primaryBorder: "#2F6FC0", glassFrom: "#CC132A45", glassTo: "#990D1C30",
            glow1: "#2456D6", glow2: "#1C8FB8", glowShadow: "#3F9BFF", idle: "#8499B3", busy: "#FFC46E", avatar3: "#FFC46E", star: "#DCE9FF"),
        // Pink and purple on a dark plum.
        [Plum] = CreateLook(
            background: "#140A18", panel: "#21122A", foreground: "#F6E9FA", muted: "#B79DC2", border: "#452A55", soft: "#2C1838",
            accent: "#F58AD8", onAccent: "#1E0718", accentSoft: "#45173F", accent2: "#A26BFF", accent2Light: "#C9A8FF",
            edge: "#8C3A7A", primaryBorder: "#A0489A", glassFrom: "#CC2B1638", glassTo: "#991C0E26",
            glow1: "#9A3BD6", glow2: "#D63B8F", glowShadow: "#D97BE8", idle: "#A68FB0", busy: "#FFC46E", avatar3: "#FFC46E", star: "#F3E1FF"),
        // Amber and coral on a warm dark brown.
        [Sunset] = CreateLook(
            background: "#160D0A", panel: "#251712", foreground: "#FBEFE6", muted: "#C2A593", border: "#55362A", soft: "#301E17",
            accent: "#FFB25E", onAccent: "#1F0E03", accentSoft: "#4A2A14", accent2: "#FF6B6B", accent2Light: "#FFA59E",
            edge: "#A3601F", primaryBorder: "#B8742E", glassFrom: "#CC33201A", glassTo: "#99221410",
            glow1: "#D65A2B", glow2: "#C4902B", glowShadow: "#FF9457", idle: "#B39A8B", busy: "#FFF0B0", avatar3: "#F7E08A", star: "#FFEBDD"),
        // Neutral greys with an ice-white accent.
        [Graphite] = CreateLook(
            background: "#0D0E10", panel: "#18191D", foreground: "#ECEDEF", muted: "#9EA2AB", border: "#33363D", soft: "#202227",
            accent: "#D5DCE8", onAccent: "#0D0E10", accentSoft: "#2A2E36", accent2: "#8E99AD", accent2Light: "#B8C0CF",
            edge: "#5B6475", primaryBorder: "#707A8C", glassFrom: "#CC1E2025", glassTo: "#99141518",
            glow1: "#3A3F4B", glow2: "#2C3340", glowShadow: "#AEB8C8", idle: "#7E828B", busy: "#FFC46E", avatar3: "#FFC46E", star: "#E6E8EC")
    };

    private static Dictionary<string, string> CreateLook(
        string background, string panel, string foreground, string muted, string border, string soft,
        string accent, string onAccent, string accentSoft, string accent2, string accent2Light,
        string edge, string primaryBorder, string glassFrom, string glassTo,
        string glow1, string glow2, string glowShadow, string idle, string busy, string avatar3, string star)
    {
        var p = CreatePalette(true, background, panel, foreground, muted, border, soft, accent, onAccent, accentSoft);
        p["PrimaryBorder"] = primaryBorder;
        p["PrimaryEdge"] = edge;
        p["StatusConnectedForeground"] = accent;
        p["StatusConnectedBackground"] = accentSoft;
        p["StatusConnectedBorder"] = edge;
        p["HeroIdle"] = idle;
        p["HeroActive"] = accent;
        p["HeroActive2"] = accent2;
        p["HeroBusy"] = busy;
        // Second colour of the look: outgoing traffic, the second avatar, gradients.
        p["Accent2"] = accent2Light;
        p["Avatar3"] = avatar3;
        p["GlassFrom"] = glassFrom;
        p["GlassTo"] = glassTo;
        p["BackgroundGlow1"] = glow1;
        p["BackgroundGlow2"] = glow2;
        p["BackgroundStar"] = star;
        p["SelectionGlow"] = glowShadow;
        return p;
    }
    private static Dictionary<string, string> CreatePalette(
        bool dark, string background, string panel, string foreground, string muted,
        string border, string soft, string accent, string accentForeground, string accentSoft)
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
            ["PrimaryForeground"] = accentForeground,
            ["PrimaryBorder"] = accent,
            ["PrimaryEdge"] = accent,
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
            ["StatusConnectedBackground"] = dark ? "#18372D" : "#E2F5EC",
            ["StatusConnectedBorder"] = dark ? "#2F5B48" : "#BFDACB",
            ["StatusConnectedForeground"] = dark ? "#85CBAA" : "#26765B",
            ["StatusConnectingBackground"] = accentSoft,
            ["StatusConnectingBorder"] = border,
            ["StatusConnectingForeground"] = accent,
            ["StatusErrorBackground"] = dark ? "#3C232B" : "#FCEBF0",
            ["StatusErrorBorder"] = dark ? "#75404B" : "#E8BDCB",
            ["StatusErrorForeground"] = dark ? "#F0A8B5" : "#A33651",
            ["SidebarBackground"] = background,
            ["SidebarBorder"] = border,
            ["NavActiveBackground"] = accentSoft,
            ["NavActiveForeground"] = accent,
            ["ListHoverBackground"] = soft,
            ["ListSelectedBackground"] = accentSoft,
            ["DangerForeground"] = dark ? "#F0A8B5" : "#A33651",
            ["DangerBackground"] = dark ? "#9A3B50" : "#B33D59",
            ["DangerBorder"] = dark ? "#B95C70" : "#A33651",
            ["EyebrowForeground"] = muted,
            ["OrbitSoftBackground"] = soft,
            ["OrbitAccentSoftBackground"] = accentSoft,
            ["OrbitAccentForeground"] = accent,
            ["HeroIdle"] = muted,
            ["HeroActive"] = accent,
            ["HeroActive2"] = accent,
            ["HeroBusy"] = "#FFC46E",
            ["PanelBackground"] = panel
        };
    }

    /// <summary>Returns a supported look: the given one, or Orbit for anything unknown or retired.</summary>
    public static string Normalize(string? theme)
    {
        var value = theme?.Trim();
        return All.FirstOrDefault(known => known.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? Orbit;
    }

    public static bool UsesDarkChrome(string? theme) => true;

    public static event EventHandler<string>? ThemeChanged;

    public static IReadOnlyDictionary<string, string> ColorsFor(string? theme)
        => Palettes[Normalize(theme)];

    public static bool TryGetColor(string? theme, string key, out Color color)
    {
        if (!Palettes[Normalize(theme)].TryGetValue(key, out var hex))
        {
            color = default;
            return false;
        }

        color = (Color)ColorConverter.ConvertFromString(hex);
        return true;
    }

    public static Color GetColor(string key)
        => TryGetColor(CurrentTheme, key, out var color) ? color : Colors.Gray;

    public static void Apply(string? theme)
    {
        var normalized = Normalize(theme);
        CurrentTheme = normalized;
        if (Application.Current is null)
            return;

        foreach (var (key, hex) in Palettes[normalized])
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            Application.Current.Resources[key] = brush;
        }

        ApplyChromeResources(normalized);

        foreach (Window window in Application.Current.Windows)
            TitleBarTheme.Apply(window, UsesDarkChrome(normalized));

        ThemeChanged?.Invoke(null, normalized);
    }

    private static void ApplyChromeResources(string theme)
    {
        var resources = Application.Current.Resources;
        resources["IsRetroTheme"] = false;
        resources["ThemeFontFamily"] = new FontFamily("Segoe UI");
        resources["ThemeMonoFontFamily"] = new FontFamily("Cascadia Code, Consolas");
        resources["ThemeDialogMonoFontFamily"] = new FontFamily("Consolas, Cascadia Mono, Segoe UI");
        resources["ButtonCornerRadius"] = new CornerRadius(11);
        resources["FieldCornerRadius"] = new CornerRadius(9);
        resources["CardCornerRadius"] = new CornerRadius(18);
        resources["DialogCornerRadius"] = new CornerRadius(14);
        resources["SmallCornerRadius"] = new CornerRadius(6);
        resources["BadgeCornerRadius"] = new CornerRadius(6);
        resources["PushButtonCornerRadius"] = new CornerRadius(18);

        resources["ThemeHeaderBackground"] = Brushes.Transparent;
        resources["ThemeHeaderForeground"] = resources["WindowForeground"];
        resources["ThemeHeaderMutedForeground"] = resources["MutedForeground"];
        resources["ThemeCaptionBackground"] = resources["WindowBackground"];
        resources["ThemeCaptionForeground"] = resources["WindowForeground"];
        resources["ThemeWorkAreaBackground"] = Brushes.Transparent;
        resources["BevelHighlight"] = Brushes.White;
        resources["BevelLight"] = FrozenBrush(0xDF, 0xDF, 0xDF);
        resources["BevelShadow"] = Brushes.Gray;
        resources["BevelDark"] = Brushes.Black;
        resources["WorkAreaOuterBorderBrush"] = Brushes.Transparent;
        resources["WorkAreaInnerBorderBrush"] = Brushes.Transparent;
        resources["WorkAreaOuterBorderThickness"] = new Thickness(0);
        resources["WorkAreaInnerBorderThickness"] = new Thickness(0);
        resources["WorkAreaPadding"] = new Thickness(0);

        resources["CardEffect"] = Freeze(new DropShadowEffect { Color = Colors.Black, BlurRadius = 36, ShadowDepth = 14, Direction = 270, Opacity = 0.55 });
        var palette = Palettes[theme];
        var accent = (Color)ColorConverter.ConvertFromString(palette["Accent"]);
        var accentDeep = (Color)ColorConverter.ConvertFromString(palette["HeroActive2"]);
        resources["PushEffect"] = Freeze(new DropShadowEffect { Color = accent, BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = 0.45 });
        resources["PanelBorderThickness"] = new Thickness(1);
        resources["PushBackground"] = FrozenGradient(palette["Accent"], palette["HeroActive2"]);

        var glass = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(palette["GlassFrom"]),
            (Color)ColorConverter.ConvertFromString(palette["GlassTo"]), 60);
        glass.Freeze();
        resources["PanelSurface"] = glass;

        // Tints of the accent, so the markup never has to hard-code a colour of one look.
        resources["AccentWash"] = Tint(accent, 0x1A);
        resources["AccentWashStrong"] = Tint(accent, 0x26);
        resources["AccentLine"] = Tint(accent, 0x33);
        resources["AccentLineStrong"] = Tint(accent, 0x40);
        resources["AccentLineBright"] = Tint(accent, 0x73);
        resources["AccentBright"] = Tint(Color.FromRgb(Lighten(accent.R), Lighten(accent.G), Lighten(accent.B)), 0xFF);
        var selected = new LinearGradientBrush(Color.FromArgb(0x2E, accent.R, accent.G, accent.B),
            Color.FromArgb(0x1F, accentDeep.R, accentDeep.G, accentDeep.B), new Point(0, 0), new Point(1, 1));
        selected.Freeze();
        resources["ProfileSelectedBackground"] = selected;
        resources["ProfileSelectedEffect"] = Freeze(new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString(palette["SelectionGlow"]), BlurRadius = 26, ShadowDepth = 8, Direction = 270, Opacity = 0.55 });
        resources["RunDotEffect"] = Freeze(new DropShadowEffect { Color = accent, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9 });
    }

    private static SolidColorBrush Tint(Color color, byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static byte Lighten(byte channel) => (byte)(channel + (255 - channel) * 0.6);

    private static Effect Freeze(Effect effect)
    {
        effect.Freeze();
        return effect;
    }

    private static LinearGradientBrush FrozenGradient(string from, string to)
    {
        var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(from),
            (Color)ColorConverter.ConvertFromString(to), new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush FrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
