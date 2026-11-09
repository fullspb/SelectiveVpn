using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SelectiveVpn.Services;

internal static class InstanceShutdown
{
    public static string EventName
    {
        get
        {
            var path = (Environment.ProcessPath ?? "").ToLowerInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
            return @"Local\SelectiveVpn.Shutdown." + hash[..16];
        }
    }

    public static EventWaitHandle CreateListener()
        => new(false, EventResetMode.AutoReset, EventName, out _);

    public static void SignalAndWait()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(EventName);
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return;
        }

        var deadline = Environment.TickCount64 + 25_000;
        while (Environment.TickCount64 < deadline)
        {
            if (!OtherCopyRunning())
                return;

            Thread.Sleep(200);
        }
    }

    private static bool OtherCopyRunning()
    {
        var current = Environment.ProcessId;
        var mine = Environment.ProcessPath;
        var found = false;
        foreach (var process in Process.GetProcessesByName("SelectiveVpn"))
        {
            try
            {
                if (process.Id == current || string.IsNullOrWhiteSpace(mine))
                    continue;

                if (string.Equals(process.MainModule?.FileName, mine, StringComparison.OrdinalIgnoreCase))
                    found = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            finally
            {
                process.Dispose();
            }

            if (found)
                return true;
        }

        return false;
    }
}
