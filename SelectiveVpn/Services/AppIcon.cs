using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace SelectiveVpn.Services;

internal static class AppIcon
{
    private static readonly Uri ResourceUri = new(
        "pack://application:,,,/SelectiveVpn;component/Assets/SelectiveVpn.ico",
        UriKind.Absolute);

    public static Icon LoadTrayIcon()
    {
        var resource = Application.GetResourceStream(ResourceUri)
            ?? throw new InvalidOperationException("The application icon resource is missing.");
        using var stream = resource.Stream;
        using var icon = new Icon(stream, Forms.SystemInformation.SmallIconSize);
        // Detach the returned icon from the resource stream; the window owns its lifetime.
        return (Icon)icon.Clone();
    }
}
