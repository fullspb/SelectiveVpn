namespace SelectiveVpn.Engine;

public static class EngineLogFilter
{
    public static bool ShouldShow(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || IsRoutineNoise(line))
            return false;

        return line.Contains("panic", StringComparison.OrdinalIgnoreCase)
            || line.Contains("FATA", StringComparison.OrdinalIgnoreCase)
            || line.Contains("level=fatal", StringComparison.OrdinalIgnoreCase)
            || line.Contains("level=error", StringComparison.OrdinalIgnoreCase)
            || line.Contains("ERRO", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRoutineNoise(string line)
    {
        if (line.Contains("default interface changed", StringComparison.OrdinalIgnoreCase))
            return true;

        return line.Contains("dial VPN", StringComparison.OrdinalIgnoreCase)
            || line.Contains("dial DIRECT", StringComparison.OrdinalIgnoreCase);
    }
}
