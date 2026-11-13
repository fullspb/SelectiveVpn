using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SelectiveVpn.Services;

internal static class TitleBarTheme
{
    private const int CaptionColor = 35;
    private const int TextColor = 36;
    private const int BorderColor = 34;

    public static void ApplyFromResources(Window window)
    {
        if (AppTheme.CurrentTheme == AppTheme.Retro)
        {
            Apply(window, false);
            return;
        }

        var dark = true;
        if (Application.Current?.Resources["WindowBackground"] is SolidColorBrush background)
        {
            var color = background.Color;
            var luminance = (color.R * 299) + (color.G * 587) + (color.B * 114);
            dark = luminance < 160000;
        }

        Apply(window, dark);
    }

    public static void Apply(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var enabled = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));

        if ((Application.Current?.Resources["ThemeCaptionBackground"] ?? Application.Current?.Resources["WindowBackground"]) is SolidColorBrush background)
        {
            var color = ToColorRef(background.Color);
            _ = DwmSetWindowAttribute(handle, CaptionColor, ref color, sizeof(int));
        }

        if (Application.Current?.Resources["WindowBackground"] is SolidColorBrush border)
        {
            var color = ToColorRef(border.Color);
            _ = DwmSetWindowAttribute(handle, BorderColor, ref color, sizeof(int));
        }

        if ((Application.Current?.Resources["ThemeCaptionForeground"] ?? Application.Current?.Resources["WindowForeground"]) is SolidColorBrush foreground)
        {
            var color = ToColorRef(foreground.Color);
            _ = DwmSetWindowAttribute(handle, TextColor, ref color, sizeof(int));
        }
    }

    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
