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
                message = "Не удалось открыть автозагрузку Windows.";
                return false;
            }

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                message = "Автозапуск с Windows выключен.";
                return true;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                message = "Не удалось определить путь к SelectiveVpn.exe. Автозапуск не записан.";
                return false;
            }

            key.SetValue(ValueName, $"\"{executable}\"");
            message = "Автозапуск с Windows включён. При входе откроется приложение, без подключения VPN.";
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            message = "Не удалось изменить автозапуск Windows.";
            return false;
        }
    }
}
