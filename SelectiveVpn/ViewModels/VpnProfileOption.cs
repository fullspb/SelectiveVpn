using System.IO;
using SelectiveVpn.Engine;
using SelectiveVpn.Services;

namespace SelectiveVpn.ViewModels;

public sealed class VpnProfileOption : ViewModelBase
{
    private bool _isActive;
    private bool _isRunning;

    public VpnProfileOption(string configPath)
    {
        ConfigPath = configPath;
        Name = ResolveName(configPath);
    }

    public string ConfigPath { get; }

    public string Name { get; }

    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();

    public string KindLabel => SubscriptionProfile.IsSubscriptionPath(ConfigPath) ? Loc.T("Orbit.Subscription") : "AmneziaWG";

    private static string ResolveName(string configPath)
    {
        if (SubscriptionProfile.TryRead(configPath, out var subscription, out _))
            return subscription.Title;

        return Path.GetFileNameWithoutExtension(configPath);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }
}
