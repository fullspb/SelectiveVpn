using System.IO;
using SelectiveVpn.Engine;

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
