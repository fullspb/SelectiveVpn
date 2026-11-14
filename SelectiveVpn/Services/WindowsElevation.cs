using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace SelectiveVpn.Services;

internal static class WindowsElevation
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool TryRelaunch(string arguments, out string message)
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                message = Loc.T("Msg.Elev.NoPath");
                return false;
            }

            var started = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas"
            });

            if (started is null)
            {
                message = Loc.T("Msg.Elev.NotDone");
                return false;
            }

            message = Loc.T("Msg.Elev.Requested");
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            message = Loc.T("Msg.Elev.Canceled");
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            message = Loc.T("Msg.Elev.Fail");
            return false;
        }
    }
}
