namespace SelectiveVpn.Desktop.Models;

/// <summary>One executable offered by the process or desktop-app picker.</summary>
public sealed record ProcessAppInfo(string Name, string ExecutablePath)
{
    public string DetailText => ExecutablePath;
}
