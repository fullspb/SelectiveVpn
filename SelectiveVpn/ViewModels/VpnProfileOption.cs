using System.IO;

namespace SelectiveVpn.ViewModels;

public sealed class VpnProfileOption : ViewModelBase
{
    private bool _isActive;
    private bool _isRunning;

    public VpnProfileOption(string configPath)
    {
        ConfigPath = configPath;
        Name = Path.GetFileNameWithoutExtension(configPath);
    }

    public string ConfigPath { get; }

    public string Name { get; }

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
