namespace SelectiveVpn.Models;

public sealed record VpnNodeInfo(string Name, string DisplayName, bool IsAuto, int? DelayMs = null)
{
    public string DelayText => DelayMs switch
    {
        null => "—",
        <= 0 => CoreText.T("Core.Delay.None", "нет"),
        _ => CoreText.F("Unit.Ms", "{0} мс", DelayMs)
    };

    public VpnNodeInfo WithDelay(int? delayMs) => this with { DelayMs = delayMs };
}
