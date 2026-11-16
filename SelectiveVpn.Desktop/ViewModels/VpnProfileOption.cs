using SelectiveVpn.Desktop.Services;
using SelectiveVpn.Engine;

namespace SelectiveVpn.Desktop.ViewModels;

/// <summary>Avalonia port of SelectiveVpn.ViewModels.VpnProfileOption (AmneziaWG + subscription).</summary>
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
        ?? (SubscriptionProfile.IsSubscriptionPath(ConfigPath) ? UiLoc.T("Orbit.Subscription") : "AmneziaWG");

    public bool IsSubscription => SubscriptionProfile.IsSubscriptionPath(ConfigPath);

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
