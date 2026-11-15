using System.ComponentModel;
using System.Diagnostics;

namespace SelectiveVpn.Services;

/// <summary>Web pages of the project, opened in the user's browser.</summary>
internal static class AppLinks
{
    public const string GitHub = "https://github.com/fullspb/SelectiveVpn";

    /// <summary>Replaced in checks so that nothing is launched.</summary>
    internal static Func<string, bool> Opener { get; set; } = OpenInBrowser;

    public static bool Open(string url) => Opener(url);

    private static bool OpenInBrowser(string url)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
