namespace SelectiveVpn.Models;

public sealed record InstalledAppInfo(string Name, string ExecutablePath)
{
    public string DetailText => ExecutablePath;
}
