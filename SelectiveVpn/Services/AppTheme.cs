using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SelectiveVpn.Services;

internal static class AppTheme
{
    // The three supported looks.
    public const string Orbit = "orbit";

    // Identifiers saved by earlier releases. They are no longer offered in the UI and are
    // migrated to the closest new look by Normalize.
    public const string Light = "light";
    public const string Dark = "dark";
    public const string Green = "green";
    public const string Oled = "oled";
    public const string Amber = "amber";
    public const string Indigo = "indigo";
    public const string Ocean = "ocean";
    public const string Plum = "plum";
    public const string Aurora = "aurora";
    public const string Depth = "depth";
    public const string Retro = "retro";

    public static readonly string[] All = [Orbit];

    public static string CurrentTheme { get; private set; } = Orbit;

    private static readonly Dictionary<string, Dictionary<string, string>> Palettes = new(StringComparer.OrdinalIgnoreCase)
    {
        [Orbit] = CreateOrbitPalette()
    };

    private static Dictionary<string, string> CreateOrbitPalette()
    {
        var p = CreatePalette(true, "#0B0E1C", "#151A31", "#E8ECFF", "#9AA3C7",
            "#2A3358", "#1B2241", "#7FE3FF", "#071022", "#17304C");
        p["PrimaryBackground"] = "#7FE3FF";
        p["PrimaryBorder"] = "#3B4FA8";
        p["PrimaryEdge"] = "#2B5C8F";
        p["StatusConnectedForeground"] = "#7FE3FF";
        p["StatusConnectedBackground"] = "#123047";
        p["StatusConnectedBorder"] = "#2B5C8F";
        p["HeroIdle"] = "#8A93B5";
        p["HeroActive"] = "#7FE3FF";
        p["HeroActive2"] = "#8C6EFF";
        p["HeroBusy"] = "#FFC46E";
        p["PanelBackground"] = "#141A33";
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

    // Earlier releases offered several looks; every saved identifier now maps to Orbit.
    public static string Normalize(string? theme) => Orbit;

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
        resources["PushEffect"] = Freeze(new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString("#7FE3FF"), BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = 0.45 });
        resources["PanelBorderThickness"] = new Thickness(1);
        resources["PushBackground"] = FrozenGradient("#7FE3FF", "#8C6EFF");

        var glass = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#CC1B2244"),
            (Color)ColorConverter.ConvertFromString("#99121733"), 60);
        glass.Freeze();
        resources["PanelSurface"] = glass;
    }

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
