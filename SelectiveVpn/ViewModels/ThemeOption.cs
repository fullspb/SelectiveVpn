using SelectiveVpn.Services;

namespace SelectiveVpn.ViewModels;

public sealed class ThemeOption(string code, string title) : ViewModelBase
{
    private string _title = title;

    public string Code { get; } = code;

    public string Title
    {
        get => _title;
        internal set => SetProperty(ref _title, value);
    }

    public override string ToString() => Title;

    internal static IReadOnlyList<ThemeOption> CreateOptions() =>
    [
        new(AppTheme.Neon, Loc.T("Orbit.ThemeNeon")),
        new(AppTheme.Ruby, Loc.T("Orbit.ThemeRuby")),
        new(AppTheme.Gold, Loc.T("Orbit.ThemeGold")),
        new(AppTheme.Orbit, Loc.T("Orbit.ThemeOrbit")),
        new(AppTheme.Aurora, Loc.T("Orbit.ThemeAurora")),
        new(AppTheme.Ocean, Loc.T("Orbit.ThemeOcean")),
        new(AppTheme.Plum, Loc.T("Orbit.ThemePlum")),
        new(AppTheme.Sunset, Loc.T("Orbit.ThemeSunset")),
        new(AppTheme.Graphite, Loc.T("Orbit.ThemeGraphite"))
    ];
}
