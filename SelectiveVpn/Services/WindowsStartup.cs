using System.IO;
using System.Security;
using Microsoft.Win32;

namespace SelectiveVpn.Services;

internal static class WindowsStartup
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SelectiveVpn";

    public static bool TryApply(bool enabled, out string message)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunPath, writable: true);
            if (key is null)
            {
                message = Loc.T("Msg.Startup.OpenFail");
                return false;
            }

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                message = Loc.T("Msg.Startup.Off");
                return true;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                message = Loc.T("Msg.Startup.NoPath");
                return false;
            }

            key.SetValue(ValueName, $"\"{executable}\"");
            message = Loc.T("Msg.Startup.On");
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            message = Loc.T("Msg.Startup.ChangeFail");
            return false;
        }
    }
}
