using System.Windows;
using System.Windows.Media;

namespace SelectiveVpn.Services;

internal static class AppTheme
{
    public const string Light = "light";
    public const string Dark = "dark";
    public const string Green = "green";
    public const string Oled = "oled";
    public const string Amber = "amber";
    public const string Indigo = "indigo";

    private static readonly Dictionary<string, string> LightColors = new()
    {
        ["WindowBackground"] = "#F3F5F8",
        ["WindowForeground"] = "#1C2430",
        ["MutedForeground"] = "#5C677A",
        ["CardBackground"] = "#FFFFFF",
        ["CardBorder"] = "#E2E6ED",
        ["Accent"] = "#1F4E8C",
        ["ButtonBackground"] = "#FFFFFF",
        ["ButtonForeground"] = "#1C2430",
        ["ButtonBorder"] = "#D0D5DD",
        ["PrimaryBackground"] = "#1F4E8C",
        ["PrimaryForeground"] = "#FFFFFF",
        ["PrimaryBorder"] = "#173E70",
        ["FieldBackground"] = "#F7F8FA",
        ["FieldBorder"] = "#E2E6ED",
        ["InfoBackground"] = "#F3F7FC",
        ["InfoBorder"] = "#D6E2F5",
        ["InfoForeground"] = "#1F4E8C",
        ["EmptyForeground"] = "#8B95A5",
        ["PathForeground"] = "#667085",
        ["StatusIdleBackground"] = "#FFF7EB",
        ["StatusIdleBorder"] = "#F5D7A8",
        ["StatusIdleForeground"] = "#8A5A00",
        ["StatusConnectedBackground"] = "#E8F6EE",
        ["StatusConnectedBorder"] = "#B7E0C6",
        ["StatusConnectedForeground"] = "#1F7A45",
        ["StatusConnectingBackground"] = "#EEF3FA",
        ["StatusConnectingBorder"] = "#D3DDED",
        ["StatusConnectingForeground"] = "#1F4E8C",
        ["StatusErrorBackground"] = "#FDECEC",
        ["StatusErrorBorder"] = "#F3C1C1",
        ["StatusErrorForeground"] = "#A33B3B",
        ["SidebarBackground"] = "#F7F8FB",
        ["SidebarBorder"] = "#E2E6ED",
        ["NavActiveBackground"] = "#E8EEF8",
        ["NavActiveForeground"] = "#1F4E8C",
        ["ListHoverBackground"] = "#E4EAF3",
        ["ListSelectedBackground"] = "#D3E3FA",
        ["DangerForeground"] = "#C24141",
        ["DangerBackground"] = "#C24141",
        ["DangerBorder"] = "#9A3030",
        ["EyebrowForeground"] = "#5B6B8A"
    };

    private static readonly Dictionary<string, string> DarkColors = new()
    {
        ["WindowBackground"] = "#0E131C",
        ["WindowForeground"] = "#E8EEF6",
        ["MutedForeground"] = "#8B97A8",
        ["CardBackground"] = "#141C2A",
        ["CardBorder"] = "#243044",
        ["Accent"] = "#3B82F6",
        ["ButtonBackground"] = "#1A2433",
        ["ButtonForeground"] = "#E8EEF6",
        ["ButtonBorder"] = "#2A384C",
        ["PrimaryBackground"] = "#2F6BFF",
        ["PrimaryForeground"] = "#FFFFFF",
        ["PrimaryBorder"] = "#2456D6",
        ["FieldBackground"] = "#0E1522",
        ["FieldBorder"] = "#243044",
        ["InfoBackground"] = "#1A2740",
        ["InfoBorder"] = "#2A4068",
        ["InfoForeground"] = "#9EC1FF",
        ["EmptyForeground"] = "#7E8B9C",
        ["PathForeground"] = "#8B98A8",
        ["StatusIdleBackground"] = "#3A2E1A",
        ["StatusIdleBorder"] = "#6B5424",
        ["StatusIdleForeground"] = "#F0C36A",
        ["StatusConnectedBackground"] = "#123226",
        ["StatusConnectedBorder"] = "#1C5C40",
        ["StatusConnectedForeground"] = "#3DDC97",
        ["StatusConnectingBackground"] = "#1A2740",
        ["StatusConnectingBorder"] = "#2A4068",
        ["StatusConnectingForeground"] = "#9EC1FF",
        ["StatusErrorBackground"] = "#3A1E1E",
        ["StatusErrorBorder"] = "#6B3030",
        ["StatusErrorForeground"] = "#F0A0A0",
        ["SidebarBackground"] = "#0B0F16",
        ["SidebarBorder"] = "#1A2230",
        ["NavActiveBackground"] = "#17243A",
        ["NavActiveForeground"] = "#E8EEF6",
        ["ListHoverBackground"] = "#162033",
        ["ListSelectedBackground"] = "#1B3F7A",
        ["DangerForeground"] = "#FF6B6B",
        ["DangerBackground"] = "#E3534A",
        ["DangerBorder"] = "#C13E36",
        ["EyebrowForeground"] = "#7E8BA0"
    };

    private static readonly Dictionary<string, string> GreenColors = new()
    {
        ["WindowBackground"] = "#101612",
        ["WindowForeground"] = "#E7EEE8",
        ["MutedForeground"] = "#8E998F",
        ["CardBackground"] = "#1A221C",
        ["CardBorder"] = "#2C3830",
        ["Accent"] = "#6FCBAA",
        ["ButtonBackground"] = "#1E2822",
        ["ButtonForeground"] = "#E7EEE8",
        ["ButtonBorder"] = "#334038",
        ["PrimaryBackground"] = "#6FCBAA",
        ["PrimaryForeground"] = "#0C1612",
        ["PrimaryBorder"] = "#5BB896",
        ["FieldBackground"] = "#141C18",
        ["FieldBorder"] = "#2C3830",
        ["InfoBackground"] = "#163028",
        ["InfoBorder"] = "#2C5044",
        ["InfoForeground"] = "#B7E8D4",
        ["EmptyForeground"] = "#7A857C",
        ["PathForeground"] = "#8E998F",
        ["StatusIdleBackground"] = "#2A261C",
        ["StatusIdleBorder"] = "#5A4E32",
        ["StatusIdleForeground"] = "#E4C98A",
        ["StatusConnectedBackground"] = "#163228",
        ["StatusConnectedBorder"] = "#2C5C48",
        ["StatusConnectedForeground"] = "#7DDFC0",
        ["StatusConnectingBackground"] = "#1A2C28",
        ["StatusConnectingBorder"] = "#2C5048",
        ["StatusConnectingForeground"] = "#B7E8D4",
        ["StatusErrorBackground"] = "#3A1E1E",
        ["StatusErrorBorder"] = "#6B3030",
        ["StatusErrorForeground"] = "#F0A0A0",
        ["SidebarBackground"] = "#0E1410",
        ["SidebarBorder"] = "#232C26",
        ["NavActiveBackground"] = "#24302A",
        ["NavActiveForeground"] = "#E7EEE8",
        ["ListHoverBackground"] = "#1C2822",
        ["ListSelectedBackground"] = "#1E4A3A",
        ["DangerForeground"] = "#FF8A84",
        ["DangerBackground"] = "#C44B45",
        ["DangerBorder"] = "#A33B36",
        ["EyebrowForeground"] = "#6FCBAA"
    };

    private static readonly Dictionary<string, string> OledColors = new()
    {
        ["WindowBackground"] = "#000000",
        ["WindowForeground"] = "#F5F5F5",
        ["MutedForeground"] = "#8D8D8D",
        ["CardBackground"] = "#101010",
        ["CardBorder"] = "#2A2A2A",
        ["Accent"] = "#FFFFFF",
        ["ButtonBackground"] = "#161616",
        ["ButtonForeground"] = "#F5F5F5",
        ["ButtonBorder"] = "#2E2E2E",
        ["PrimaryBackground"] = "#FFFFFF",
        ["PrimaryForeground"] = "#000000",
        ["PrimaryBorder"] = "#E6E6E6",
        ["FieldBackground"] = "#000000",
        ["FieldBorder"] = "#2A2A2A",
        ["InfoBackground"] = "#101010",
        ["InfoBorder"] = "#2A2A2A",
        ["InfoForeground"] = "#D0D0D0",
        ["EmptyForeground"] = "#6E6E6E",
        ["PathForeground"] = "#8D8D8D",
        ["StatusIdleBackground"] = "#1A160E",
        ["StatusIdleBorder"] = "#5A4A28",
        ["StatusIdleForeground"] = "#E4C98A",
        ["StatusConnectedBackground"] = "#0E2418",
        ["StatusConnectedBorder"] = "#1E4A32",
        ["StatusConnectedForeground"] = "#3DDC97",
        ["StatusConnectingBackground"] = "#101010",
        ["StatusConnectingBorder"] = "#2A2A2A",
        ["StatusConnectingForeground"] = "#D0D0D0",
        ["StatusErrorBackground"] = "#2A1010",
        ["StatusErrorBorder"] = "#5A2828",
        ["StatusErrorForeground"] = "#F0A0A0",
        ["SidebarBackground"] = "#000000",
        ["SidebarBorder"] = "#1A1A1A",
        ["NavActiveBackground"] = "#FFFFFF",
        ["NavActiveForeground"] = "#111111",
        ["ListHoverBackground"] = "#1A1A1A",
        ["ListSelectedBackground"] = "#333333",
        ["DangerForeground"] = "#E85D5D",
        ["DangerBackground"] = "#3A1618",
        ["DangerBorder"] = "#6B2A2E",
        ["EyebrowForeground"] = "#A3A3A3"
    };

    private static readonly Dictionary<string, string> AmberColors = new()
    {
        ["WindowBackground"] = "#16120C",
        ["WindowForeground"] = "#F3EBDD",
        ["MutedForeground"] = "#A89880",
        ["CardBackground"] = "#241C14",
        ["CardBorder"] = "#3A2E22",
        ["Accent"] = "#E0A045",
        ["ButtonBackground"] = "#2A2218",
        ["ButtonForeground"] = "#F3EBDD",
        ["ButtonBorder"] = "#4A3C2C",
        ["PrimaryBackground"] = "#E0A045",
        ["PrimaryForeground"] = "#1A1208",
        ["PrimaryBorder"] = "#C48A30",
        ["FieldBackground"] = "#120E0A",
        ["FieldBorder"] = "#3A2E22",
        ["InfoBackground"] = "#2A2214",
        ["InfoBorder"] = "#5A4A28",
        ["InfoForeground"] = "#F0D7A0",
        ["EmptyForeground"] = "#8A7A66",
        ["PathForeground"] = "#A89880",
        ["StatusIdleBackground"] = "#2A2214",
        ["StatusIdleBorder"] = "#6B5424",
        ["StatusIdleForeground"] = "#F0C36A",
        ["StatusConnectedBackground"] = "#1E2A18",
        ["StatusConnectedBorder"] = "#3A5430",
        ["StatusConnectedForeground"] = "#B7E08A",
        ["StatusConnectingBackground"] = "#2A2214",
        ["StatusConnectingBorder"] = "#5A4A28",
        ["StatusConnectingForeground"] = "#F0D7A0",
        ["StatusErrorBackground"] = "#3A1E1E",
        ["StatusErrorBorder"] = "#6B3030",
        ["StatusErrorForeground"] = "#F0A0A0",
        ["SidebarBackground"] = "#120E0A",
        ["SidebarBorder"] = "#2A2218",
        ["NavActiveBackground"] = "#3A2C1C",
        ["NavActiveForeground"] = "#F3EBDD",
        ["ListHoverBackground"] = "#2A2218",
        ["ListSelectedBackground"] = "#4A3418",
        ["DangerForeground"] = "#FF8A78",
        ["DangerBackground"] = "#A84838",
        ["DangerBorder"] = "#8A382C",
        ["EyebrowForeground"] = "#E0A045"
    };

    private static readonly Dictionary<string, string> IndigoColors = new()
    {
        ["WindowBackground"] = "#12101C",
        ["WindowForeground"] = "#EEEAF8",
        ["MutedForeground"] = "#9A94B0",
        ["CardBackground"] = "#1C1830",
        ["CardBorder"] = "#322C4A",
        ["Accent"] = "#8B7CFF",
        ["ButtonBackground"] = "#221E38",
        ["ButtonForeground"] = "#EEEAF8",
        ["ButtonBorder"] = "#3A3460",
        ["PrimaryBackground"] = "#8B7CFF",
        ["PrimaryForeground"] = "#FFFFFF",
        ["PrimaryBorder"] = "#7466E0",
        ["FieldBackground"] = "#0E0C18",
        ["FieldBorder"] = "#322C4A",
        ["InfoBackground"] = "#221C3A",
        ["InfoBorder"] = "#3A3460",
        ["InfoForeground"] = "#C8C0FF",
        ["EmptyForeground"] = "#7E7894",
        ["PathForeground"] = "#9A94B0",
        ["StatusIdleBackground"] = "#2A2418",
        ["StatusIdleBorder"] = "#5A4E30",
        ["StatusIdleForeground"] = "#E4C98A",
        ["StatusConnectedBackground"] = "#14281E",
        ["StatusConnectedBorder"] = "#245C40",
        ["StatusConnectedForeground"] = "#7DDFC0",
        ["StatusConnectingBackground"] = "#221C3A",
        ["StatusConnectingBorder"] = "#3A3460",
        ["StatusConnectingForeground"] = "#C8C0FF",
        ["StatusErrorBackground"] = "#3A1E1E",
        ["StatusErrorBorder"] = "#6B3030",
        ["StatusErrorForeground"] = "#F0A0A0",
        ["SidebarBackground"] = "#0E0C16",
        ["SidebarBorder"] = "#241E38",
        ["NavActiveBackground"] = "#2A2448",
        ["NavActiveForeground"] = "#EEEAF8",
        ["ListHoverBackground"] = "#221C38",
        ["ListSelectedBackground"] = "#3A3270",
        ["DangerForeground"] = "#FF8A9A",
        ["DangerBackground"] = "#A84858",
        ["DangerBorder"] = "#8A3848",
        ["EyebrowForeground"] = "#8B7CFF"
    };

    public static string Normalize(string? theme)
    {
        if (string.Equals(theme, Dark, StringComparison.OrdinalIgnoreCase))
            return Dark;

        if (string.Equals(theme, Green, StringComparison.OrdinalIgnoreCase))
            return Green;

        if (string.Equals(theme, Oled, StringComparison.OrdinalIgnoreCase))
            return Oled;

        if (string.Equals(theme, Amber, StringComparison.OrdinalIgnoreCase))
            return Amber;

        if (string.Equals(theme, Indigo, StringComparison.OrdinalIgnoreCase))
            return Indigo;

        return Light;
    }

    public static bool UsesDarkChrome(string? theme)
    {
        var normalized = Normalize(theme);
        return normalized is Dark or Green or Oled or Amber or Indigo;
    }

    public static void Apply(string? theme)
    {
        if (Application.Current is null)
            return;

        var normalized = Normalize(theme);
        var colors = normalized switch
        {
            Dark => DarkColors,
            Green => GreenColors,
            Oled => OledColors,
            Amber => AmberColors,
            Indigo => IndigoColors,
            _ => LightColors
        };
        var resources = Application.Current.Resources;
        foreach (var (key, hex) in colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            resources[key] = brush;
        }

        foreach (Window window in Application.Current.Windows)
            TitleBarTheme.Apply(window, UsesDarkChrome(normalized));
    }
}
