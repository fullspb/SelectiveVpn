using System.Windows;
using SelectiveVpn.Models;
using SelectiveVpn.Services;

namespace SelectiveVpn.ViewModels;

/// <summary>
/// Desktop side effects used by the view model. Defaults preserve the normal application behavior;
/// an isolated host can substitute these boundaries without touching a user's settings or session.
/// </summary>
internal sealed class MainViewModelEnvironment
{
    public Func<(UserSettings Settings, bool Existed)> LoadSettings { get; init; } = static () =>
    {
        var settings = UserSettingsStore.Load(out var existed);
        return (settings, existed);
    };

    public Action<UserSettings> SaveSettings { get; init; } = UserSettingsStore.Save;

    public Func<bool> IsElevated { get; init; } = WindowsElevation.IsElevated;

    public Func<Window, bool?> ShowDialog { get; init; } = static window => window.ShowDialog();

    public Func<bool, (bool Succeeded, string Message)> ApplyStartup { get; init; } = static enabled =>
    {
        var succeeded = WindowsStartup.TryApply(enabled, out var message);
        return (succeeded, message);
    };

    public Func<string, (bool Succeeded, string Message)> Relaunch { get; init; } = static arguments =>
    {
        var succeeded = WindowsElevation.TryRelaunch(arguments, out var message);
        return (succeeded, message);
    };

    public Action<string, IReadOnlyList<AppRule>, bool> SaveConnectSession { get; init; } = ConnectSessionStore.Save;

    public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } = static (delay, token) => Task.Delay(delay, token);

    public Func<string, CancellationToken, Task<string?>> LookupIpCountryAsync { get; init; } = IpCountryLookup.LookupAsync;

    public TimeProvider TimeProvider { get; init; } = System.TimeProvider.System;

    public bool EnableTrafficPolling { get; init; } = true;
}
