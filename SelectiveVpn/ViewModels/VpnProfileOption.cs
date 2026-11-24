using System.IO;
using SelectiveVpn.Engine;
using SelectiveVpn.Services;

namespace SelectiveVpn.ViewModels;

public sealed class VpnProfileOption : ViewModelBase
{
    private bool _isActive;
    private bool _isRunning;

    private readonly string? _linkProtocols;
    private readonly string? _tunnelProtocol;

    private string _name;

    public VpnProfileOption(string configPath, string? displayName = null)
    {
        ConfigPath = configPath;
        if (SubscriptionProfile.TryRead(configPath, out var subscription, out _))
        {
            OriginalName = subscription.Title;
            // A profile made from pasted links is named after its protocol, not "Subscription".
            if (subscription.IsInline && subscription.Protocols.Length > 0)
                _linkProtocols = string.Join(" / ", subscription.Protocols).ToUpperInvariant();
        }
        else
        {
            OriginalName = Path.GetFileNameWithoutExtension(configPath);
            _tunnelProtocol = TunnelProtocol(configPath);
        }

        _name = string.IsNullOrWhiteSpace(displayName) ? OriginalName : displayName.Trim();
    }

    public string ConfigPath { get; }

    /// <summary>Name from the file or subscription, used when the user has not named the profile.</summary>
    public string OriginalName { get; } = "";

    /// <summary>Name shown in the list: the user's own name, or <see cref="OriginalName"/>.</summary>
    public string Name
    {
        get => _name;
        set
        {
            var name = string.IsNullOrWhiteSpace(value) ? OriginalName : value.Trim();
            if (SetProperty(ref _name, name))
                OnPropertyChanged(nameof(Initial));
        }
    }

    public bool HasCustomName => !string.Equals(_name, OriginalName, StringComparison.Ordinal);

    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();

    public string KindLabel => _linkProtocols
        ?? (SubscriptionProfile.IsSubscriptionPath(ConfigPath) ? Loc.T("Orbit.Subscription") : _tunnelProtocol ?? "AmneziaWG");

    /// <summary>WireGuard or AmneziaWG, read from the file; null when it cannot be read.</summary>
    private static string? TunnelProtocol(string configPath)
    {
        try
        {
            var read = AmneziaWgConfigReader.ReadFile(configPath);
            return read.Succeeded ? read.Profile?.ProtocolName : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
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
