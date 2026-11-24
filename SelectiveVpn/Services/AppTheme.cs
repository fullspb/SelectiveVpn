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
    // Looks with raised, glossy 3D controls.
    public const string Neon = "neon";
    public const string Ruby = "ruby";
    public const string Gold = "gold";

    /// <summary>The look of a fresh install, and of any unknown or retired identifier.</summary>
    public const string Default = Neon;

    // Identifiers saved by releases that had other looks. Normalize maps them to the default.
    public const string Light = "light";
    public const string Dark = "dark";
    public const string Green = "green";
    public const string Oled = "oled";
    public const string Amber = "amber";
    public const string Indigo = "indigo";
    public const string Depth = "depth";
    public const string Retro = "retro";

    public static readonly string[] All = [Neon, Ruby, Gold, Orbit, Aurora, Ocean, Plum, Sunset, Graphite];

    /// <summary>Looks whose buttons, switches and cards are drawn raised, with an edge, a gloss and a shadow.</summary>
    private static readonly HashSet<string> Volumetric = new(StringComparer.OrdinalIgnoreCase) { Neon, Ruby, Gold };

    public static bool IsVolumetric(string? theme) => Volumetric.Contains(Normalize(theme));

    public static string CurrentTheme { get; private set; } = Default;

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
            glow1: "#3A3F4B", glow2: "#2C3340", glowShadow: "#AEB8C8", idle: "#7E828B", busy: "#FFC46E", avatar3: "#FFC46E", star: "#E6E8EC"),
        // 3D: hot magenta and electric cyan on a violet night.
        [Neon] = CreateLook(
            background: "#08051A", panel: "#140D2E", foreground: "#F4EEFF", muted: "#A89CCB", border: "#33275E", soft: "#1D1440",
            accent: "#FF5CDB", onAccent: "#1A0418", accentSoft: "#3D1446", accent2: "#21E6FF", accent2Light: "#6FF0FF",
            edge: "#8E1F7C", primaryBorder: "#C23AA8", glassFrom: "#CC1E1446", glassTo: "#99100A2A",
            glow1: "#B21FD6", glow2: "#1F8FE0", glowShadow: "#FF5CDB", idle: "#9A90BD", busy: "#FFD36E", avatar3: "#FFD36E", star: "#F0E6FF"),
        // 3D: ruby red and coral on a deep wine background.
        [Ruby] = CreateLook(
            background: "#12050A", panel: "#220C14", foreground: "#FFEDF0", muted: "#C49AA4", border: "#4E1F2C", soft: "#2E111B",
            accent: "#FF4D6D", onAccent: "#1E0208", accentSoft: "#4A1222", accent2: "#FF8A5B", accent2Light: "#FFB08F",
            edge: "#8A1430", primaryBorder: "#B8264A", glassFrom: "#CC2E1220", glassTo: "#991C0A12",
            glow1: "#C21F45", glow2: "#7A2BC4", glowShadow: "#FF4D6D", idle: "#B3939B", busy: "#FFD36E", avatar3: "#FFD36E", star: "#FFE6EA"),
        // 3D: polished gold and amber on midnight blue.
        [Gold] = CreateLook(
            background: "#070A12", panel: "#121726", foreground: "#FFF8EA", muted: "#B4AC98", border: "#3A3A3C", soft: "#1A1F30",
            accent: "#FFCF5C", onAccent: "#1A1203", accentSoft: "#3D3214", accent2: "#E8892E", accent2Light: "#FFB46B",
            edge: "#9A6A12", primaryBorder: "#C9962A", glassFrom: "#CC1C2133", glassTo: "#99101422",
            glow1: "#2B4AC4", glow2: "#C48A1F", glowShadow: "#FFCF5C", idle: "#A8A293", busy: "#9FE6FF", avatar3: "#9FE6FF", star: "#FFF3D6")
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

    /// <summary>Returns a supported look: the given one, or the default for anything unknown or retired.</summary>
    public static string Normalize(string? theme)
    {
        var value = theme?.Trim();
        return All.FirstOrDefault(known => known.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? Default;
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

        ApplySurfaceResources(palette, Volumetric.Contains(theme));
    }

    /// <summary>
    /// Surfaces of buttons, the mode switch and cards. Flat looks keep soft translucent fills;
    /// volumetric looks raise each control on a coloured edge, light its top rim, add a gloss and
    /// cast a shadow, and sink the mode switch into the panel.
    /// </summary>
    private static void ApplySurfaceResources(IReadOnlyDictionary<string, string> palette, bool raised)
    {
        var resources = Application.Current.Resources;
        resources["IsVolumetricTheme"] = raised;
        if (!raised)
        {
            resources["GhostFace"] = Tint(Colors.White, 0x0F);
            resources["GhostFaceHover"] = Tint(Colors.White, 0x1F);
            resources["GhostEdge"] = Brushes.Transparent;
            resources["GhostRim"] = Brushes.Transparent;
            resources["GhostGloss"] = Brushes.Transparent;
            resources["GhostFaceMargin"] = new Thickness(0);
            resources["GhostPressedMargin"] = new Thickness(0);
            resources["GhostEffect"] = null;
            resources["PushGloss"] = Brushes.Transparent;
            resources["PushRim"] = Brushes.Transparent;
            resources["SegmentTrack"] = Tint(Colors.Black, 0x40);
            resources["SegmentTrackBorder"] = Brushes.Transparent;
            resources["SegmentSelectedBackground"] = resources["OrbitAccentSoftBackground"];
            resources["SegmentSelectedBorder"] = resources["Accent"];
            resources["SegmentSelectedEffect"] = null;
            resources["GlassFill"] = Vertical(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF), new Point(0, 0), new Point(0.6, 1));
            resources["GlassBorder"] = Tint(Colors.White, 0x17);
            resources["ProfileCardBackground"] = Vertical(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x06, 0xFF, 0xFF, 0xFF), new Point(0, 0), new Point(1, 1));
            resources["ProfileCardBorder"] = Tint(Colors.White, 0x17);
            resources["ProfileCardEffect"] = null;
            return;
        }

        var panel = Parse(palette["CardBackground"]);
        var accent = Parse(palette["Accent"]);
        var accentSoft = Parse(palette["OrbitAccentSoftBackground"]);
        var background = Parse(palette["WindowBackground"]);

        resources["GhostFace"] = Vertical(Mix(panel, Colors.White, 0.13, 0xF2), Mix(panel, Colors.Black, 0.18, 0xF2));
        resources["GhostFaceHover"] = Vertical(Mix(Mix(panel, accent, 0.16), Colors.White, 0.14, 0xF6), Mix(Mix(panel, accent, 0.10), Colors.Black, 0.12, 0xF6));
        resources["GhostEdge"] = Vertical(Mix(accent, background, 0.62), Mix(accent, Colors.Black, 0.80));
        resources["GhostRim"] = Rim(0x5A);
        resources["GhostGloss"] = Gloss(0x26, 0.48);
        resources["GhostFaceMargin"] = new Thickness(0, 0, 0, 4);
        resources["GhostPressedMargin"] = new Thickness(0, 3, 0, 1);
        resources["GhostEffect"] = Freeze(new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 6, Direction = 270, Opacity = 0.55 });

        resources["PushGloss"] = Gloss(0x6A, 0.55);
        resources["PushRim"] = Rim(0x9A);
        resources["PushEffect"] = Freeze(new DropShadowEffect { Color = accent, BlurRadius = 34, ShadowDepth = 6, Direction = 270, Opacity = 0.6 });

        resources["SegmentTrack"] = Vertical(Mix(background, Colors.Black, 0.55, 0xC8), Mix(panel, Colors.Black, 0.25, 0xA0));
        resources["SegmentTrackBorder"] = Vertical(Color.FromArgb(0x70, 0, 0, 0), Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        resources["SegmentSelectedBackground"] = Vertical(Mix(accentSoft, Colors.White, 0.16), Mix(accentSoft, Colors.Black, 0.22));
        resources["SegmentSelectedBorder"] = Vertical(Mix(accent, Colors.White, 0.35), Mix(accent, Colors.Black, 0.45));
        resources["SegmentSelectedEffect"] = Freeze(new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 4, Direction = 270, Opacity = 0.6 });

        resources["GlassFill"] = Vertical(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x06, 0xFF, 0xFF, 0xFF), new Point(0, 0), new Point(0.4, 1));
        resources["GlassBorder"] = Bevel(0x4A, 0x40);
        resources["ProfileCardBackground"] = Vertical(Mix(panel, Colors.White, 0.10, 0xD8), Mix(panel, Colors.Black, 0.20, 0xD8));
        resources["ProfileCardBorder"] = Bevel(0x50, 0x55);
        resources["ProfileCardEffect"] = Freeze(new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 7, Direction = 270, Opacity = 0.5 });
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Color Mix(Color from, Color to, double amount, byte alpha = 0xFF) => Color.FromArgb(alpha,
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));

    private static LinearGradientBrush Vertical(Color top, Color bottom)
        => Vertical(top, bottom, new Point(0, 0), new Point(0, 1));

    private static LinearGradientBrush Vertical(Color top, Color bottom, Point start, Point end)
    {
        var brush = new LinearGradientBrush(top, bottom, start, end);
        brush.Freeze();
        return brush;
    }

    /// <summary>A light rim along the top edge that fades out before the middle.</summary>
    private static LinearGradientBrush Rim(byte alpha) => Stops(
        (Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF), 0),
        (Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.45));

    /// <summary>A soft reflection on the upper part of a raised face.</summary>
    private static LinearGradientBrush Gloss(byte alpha, double stop) => Stops(
        (Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF), 0),
        (Color.FromArgb((byte)(alpha / 3), 0xFF, 0xFF, 0xFF), stop * 0.8),
        (Color.FromArgb(0, 0xFF, 0xFF, 0xFF), stop));

    /// <summary>Card outline lit from above: bright top edge, darker lower edge.</summary>
    private static LinearGradientBrush Bevel(byte light, byte shade) => Stops(
        (Color.FromArgb(light, 0xFF, 0xFF, 0xFF), 0),
        (Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 0.35),
        (Color.FromArgb(shade, 0, 0, 0), 1));

    private static LinearGradientBrush Stops(params (Color Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush(
            new GradientStopCollection(stops.Select(stop => new GradientStop(stop.Color, stop.Offset))),
            new Point(0, 0), new Point(0, 1));
        brush.Freeze();
        return brush;
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
