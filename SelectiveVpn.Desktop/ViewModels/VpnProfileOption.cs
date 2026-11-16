namespace SelectiveVpn.Desktop.ViewModels;

/// <summary>Avalonia port of SelectiveVpn.ViewModels.VpnProfileOption (AmneziaWG / file profiles).</summary>
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

    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();

    public string KindLabel => "AmneziaWG";

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
