using System.IO;
using SelectiveVpn.Engine;
using SelectiveVpn.Services;

namespace SelectiveVpn.ViewModels;

public sealed class VpnProfileOption : ViewModelBase
{
    private bool _isActive;
    private bool _isRunning;

    private readonly string? _linkProtocols;

    public VpnProfileOption(string configPath)
    {
        ConfigPath = configPath;
        if (SubscriptionProfile.TryRead(configPath, out var subscription, out _))
        {
            Name = subscription.Title;
            // A profile made from pasted links is named after its protocol, not "Subscription".
            if (subscription.IsInline && subscription.Protocols.Length > 0)
                _linkProtocols = string.Join(" / ", subscription.Protocols).ToUpperInvariant();
        }
        else
        {
            Name = Path.GetFileNameWithoutExtension(configPath);
        }
    }

    public string ConfigPath { get; }

    public string Name { get; }

    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();

    public string KindLabel => _linkProtocols
        ?? (SubscriptionProfile.IsSubscriptionPath(ConfigPath) ? Loc.T("Orbit.Subscription") : "AmneziaWG");

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
