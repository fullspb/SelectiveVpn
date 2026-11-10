namespace SelectiveVpn.Models;

public sealed record VpnNodeInfo(string Name, string DisplayName, bool IsAuto, int? DelayMs = null)
{
    public string DelayText => DelayMs switch
    {
        null => "—",
        <= 0 => "нет",
        _ => $"{DelayMs} мс"
    };

    public VpnNodeInfo WithDelay(int? delayMs) => this with { DelayMs = delayMs };
}
