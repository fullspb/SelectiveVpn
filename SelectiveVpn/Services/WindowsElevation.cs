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
                message = "Не удалось определить путь к SelectiveVpn.exe.";
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
                message = "Запрос прав администратора не выполнен. Туннель не запускался.";
                return false;
            }

            message = "Запрошены права администратора для TUN.";
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            message = "Запрос прав администратора отменён. Туннель не запускался.";
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            message = "Не удалось запросить права администратора. Туннель не запускался.";
            return false;
        }
    }
}
