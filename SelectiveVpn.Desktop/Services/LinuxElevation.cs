using System.Diagnostics;

namespace SelectiveVpn.Desktop.Services;

/// <summary>
/// Single clear privilege path for Linux TUN: setcap on mihomo (preferred) or document pkexec/sudo.
/// Never loop run0/polkit from an agent.
/// </summary>
internal static class LinuxElevation
{
    // Avoid Mono.Unix dependency — parse CapEff from /proc/self/status.
    public static bool HasNetAdminCapability()
    {
        if (!OperatingSystem.IsLinux())
            return false;

        try
        {
            foreach (var line in File.ReadLines("/proc/self/status"))
            {
                if (!line.StartsWith("CapEff:", StringComparison.Ordinal))
                    continue;

                var hex = line["CapEff:".Length..].Trim();
                if (!ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var mask))
                    return false;

                const int capNetAdmin = 12;
                return (mask & (1UL << capNetAdmin)) != 0;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return false;
    }

    public static bool CanConfigureTun()
    {
        if (!OperatingSystem.IsLinux())
            return true;

        if (Environment.IsPrivilegedProcess || HasNetAdminCapability())
            return true;

        if (MihomoBinaryHasNetAdmin(AppPaths.EngineBinaryPath))
            return true;

        // Root via geteuid without Mono.Unix:
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "id",
                ArgumentList = { "-u" },
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
                return false;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);
            return output == "0";
        }
        catch
        {
            return false;
        }
    }

    public static bool MihomoBinaryHasNetAdmin(string binaryPath)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists(binaryPath))
            return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "getcap",
                ArgumentList = { binaryPath },
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
                return false;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            return output.Contains("cap_net_admin", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static string PrivilegeHint
        => "TUN needs CAP_NET_ADMIN. Preferred one-shot: Helpers/apply-mihomo-caps.sh (sudo/pkexec once after each mihomo download). "
           + "Alternative: run the app once with sudo. Do not spam run0/polkit from agents.";

    /// <summary>
    /// Runs apply-mihomo-caps.sh via pkexec once (GUI password prompt). Returns false if cancelled/missing.
    /// </summary>
    public static (bool Succeeded, string Message) TryApplyMihomoCapabilities(string? mihomoPath = null)
    {
        var script = AppPaths.ApplyCapsScript;
        if (!File.Exists(script))
            return (false, $"Helper script missing: {script}");

        var binary = mihomoPath ?? AppPaths.EngineBinaryPath;
        if (!File.Exists(binary))
            return (false, $"Mihomo binary not found yet: {binary}. Connect once to download, then apply caps.");

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pkexec",
                ArgumentList = { script, binary },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process is null)
                return (false, "Failed to start pkexec.");

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0)
                return (true, string.IsNullOrWhiteSpace(stdout) ? "Capabilities applied to mihomo." : stdout.Trim());

            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return (false, $"pkexec/setcap failed (exit {process.ExitCode}): {detail.Trim()}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return (false, $"Could not run pkexec: {ex.Message}. Run manually: sudo {script} {binary}");
        }
    }

    public static (bool Succeeded, string Message) TryEmergencyTeardown()
    {
        var script = AppPaths.TeardownScript;
        if (File.Exists(script))
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "pkexec",
                    ArgumentList = { script },
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                if (process is not null)
                {
                    process.WaitForExit(15000);
                    if (process.ExitCode == 0)
                        return (true, "Emergency teardown completed via helper.");
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // fall through to best-effort user cleanup
                _ = ex;
            }
        }

        SelectiveVpn.Engine.LinuxTunnelCleanup.TryCleanup();
        return (true, "Best-effort teardown ran (may need sudo if Meta was created as root).");
    }
}
