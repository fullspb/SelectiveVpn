using System.Diagnostics;

namespace SelectiveVpn.Engine;

/// <summary>
/// Best-effort Linux teardown for Mihomo TUN leftovers (Meta iface + policy rules).
/// Mihomo's POST /shutdown returns 404 on v1.19.x — rely on process kill + this cleanup.
/// </summary>
public static class LinuxTunnelCleanup
{
    public static readonly int[] PolicyRulePrefs = [9000, 9001, 9002, 9010];

    public static void TryCleanup()
    {
        if (!OperatingSystem.IsLinux())
            return;

        TryRun("ip", "link", "delete", "Meta");
        foreach (var pref in PolicyRulePrefs)
        {
            // Pref may be stacked; delete until ip rule fails.
            for (var i = 0; i < 8; i++)
            {
                if (!TryRun("ip", "rule", "del", "pref", pref.ToString()))
                    break;
            }
        }

        // Best-effort DNS restore: flush stub cache after Meta/rules are gone (no root needed).
        TryRun("resolvectl", "flush-caches");
        TryRun("resolvectl", "reset-server-features");
    }

    public static void TryHardKillExecutable(string executablePath)
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(executablePath))
            return;

        TryRun("pkill", "-9", "-f", executablePath);
    }

    private static bool TryRun(string fileName, params string[] args)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            foreach (var arg in args)
                process.StartInfo.ArgumentList.Add(arg);

            if (!process.Start())
                return false;

            process.WaitForExit(3000);
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                   or System.ComponentModel.Win32Exception
                                   or IOException)
        {
            return false;
        }
    }
}
