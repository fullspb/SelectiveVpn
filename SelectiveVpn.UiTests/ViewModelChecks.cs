using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;
using SelectiveVpn.Services;
using SelectiveVpn.ViewModels;

namespace SelectiveVpn.UiTests;

internal static class ViewModelChecks
{
    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
    [
        ("settings and startup boundaries remain isolated", SettingsAreIsolatedAsync),
        ("theme selection applies and persists before any save action", ThemeSelectionPersistsImmediatelyAsync),
        ("settings theme applies immediately and survives cancel", SettingsThemeSurvivesCancelAsync),
        ("every colour scheme is complete, distinct and readable", ThemePalettesAreCompleteAndReadableAsync),
        ("profile selection keeps the actual running profile visible", SelectedVersusRunningAsync),
        ("empty rules always route all traffic through VPN", EmptyRulesAsync),
        ("connect and disconnect update real command states", ConnectDisconnectAsync),
        ("connect cancellation leaves no running tunnel", CancelConnectAsync),
        ("late successful start after cancellation is cleaned up", LateSuccessAfterCancellationAsync),
        ("start exception resets busy state and permits retry", FailedStartCanRetryAsync),
        ("stop failure preserves connected state and can retry", FailedStopCanRetryAsync),
        ("stop exception resets busy state and permits retry", ThrowingStopCanRetryAsync),
        ("changed routes reconnect using the latest configuration", ReconnectDirtyRoutesAsync),
        ("YouTube domain list reaches the engine after explicit reconnect", ReconnectYouTubeDomainsAsync),
        ("another selected profile cannot reconnect a running tunnel", ReconnectOtherProfileIsDisabledAsync),
        ("rule add and remove commands mark live changes", AddRemoveRulesAsync),
        ("inactive subscription selection does not touch the live node", InactiveSubscriptionNodeAsync),
        ("active subscription selection reaches the engine", ActiveSubscriptionNodeAsync),
        ("real server command uses an intercepted picker safely", ManageServersCommandAsync),
        ("full tunnel confirmation supports cancel and remember", FullTunnelConfirmationAsync),
        ("declined elevation never reaches the real session or engine", DeclinedElevationAsync),
        ("recovery stays disabled by default", RecoveryDisabledAsync),
        ("recovery restarts the running profile, not the edited one", RecoveryUsesRunningProfileAsync),
        ("recovery cancellation prevents a later restart", RecoveryCanBeCancelledAsync),
        ("recovery stops after three failed attempts", RecoveryIsBoundedAsync),
        ("late IP response cannot restore an address after disconnect", LateIpAfterDisconnectAsync),
        ("old session cannot replace a new session IP", OldSessionIpAsync),
        ("late server response cannot repopulate a disconnected session", LateServerAsync),
        ("a profile made from pasted server links is named after its protocol", PastedLinkProfileAsync)
    ];

    private static async Task PastedLinkProfileAsync()
    {
        await using var fixture = new TestFixture(subscriptions: true);
        const string link = "vless://11111111-2222-3333-4444-555555555555@203.0.113.10:443?type=tcp&security=tls&sni=example.com#Amsterdam";
        var imported = ConfigSubscriptionImporter.ImportShareLinks(link, profilesDirectory: fixture.Root);
        Check.True(imported.Succeeded && imported.SavedPath is not null, "A pasted vless link should import without a network request: " + imported.Message);

        var pasted = new VpnProfileOption(imported.SavedPath!);
        Check.Equal("Amsterdam", pasted.Name, "The profile takes its name from the link.");
        Check.Equal("VLESS", pasted.KindLabel, "A pasted-link profile shows its protocol instead of the subscription label.");
        Check.Equal(Loc.T("Orbit.Subscription"), new VpnProfileOption(fixture.AlphaPath).KindLabel,
            "A subscription by URL keeps the subscription label.");
        Check.True(SubscriptionProfile.IsSubscriptionPath(imported.SavedPath),
            "Pasted links reuse the subscription profile machinery, including the server picker.");
    }

    private static async Task SettingsAreIsolatedAsync()
    {
        await using var fixture = new TestFixture();
        Check.Equal(0, fixture.StartupRequests.Count, "Constructing the isolated model must not change Windows startup.");
        fixture.Model.SaveSettings();
        Check.True(fixture.SaveCount > 0, "SaveSettings must use the injected in-memory persistence boundary.");
        fixture.DialogChoice = dialog =>
        {
            Check.True(dialog is SettingsWindow, "The real settings command should construct SettingsWindow.");
            var themeBox = (ComboBox)dialog.FindName("ThemeBox");
            Check.True(themeBox.IsVisible || themeBox.Visibility == Visibility.Visible, "The colour scheme must be offered in settings.");
            Check.Equal(AppTheme.All.Length, themeBox.Items.Count, "Settings should offer every colour scheme.");
            themeBox.SelectedValue = AppTheme.Ocean;
            ((CheckBox)dialog.FindName("AutoReconnectBox")).IsChecked = true;
            return true;
        };
        fixture.Model.OpenSettingsCommand.Execute(null);
        Check.Equal(AppTheme.Ocean, fixture.Model.ThemeId, "The selected theme should apply through the real settings command.");
        Check.Equal(AppTheme.Ocean, fixture.Settings.Theme, "The selected theme should persist to injected settings.");
        Check.True(fixture.Settings.AutoReconnect && fixture.Model.AutoReconnect, "Recovery should be saved only when explicitly enabled in settings.");
        Check.Equal(0, fixture.StartupRequests.Count, "Saving unchanged startup flags must not alter Windows startup.");
    }

    private static async Task ThemeSelectionPersistsImmediatelyAsync()
    {
        await using var fixture = new TestFixture();
        try
        {
            Check.Equal(AppTheme.Orbit, fixture.Model.ThemeId, "A theme saved by a release with other looks must fall back to Orbit.");
            var originalSaveCount = fixture.SaveCount;
            var notifications = new HashSet<string>();
            fixture.Model.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is { } property) notifications.Add(property);
            };

            fixture.Model.SelectedThemeId = AppTheme.Ocean;

            Check.Equal(AppTheme.Ocean, fixture.Model.ThemeId, "Selecting a theme must apply it immediately.");
            Check.Equal(AppTheme.Ocean, fixture.Model.SelectedThemeId, "The selected theme property must follow the applied theme.");
            Check.Equal(AppTheme.Ocean, fixture.Settings.Theme, "Theme selection must update the persisted settings before any dialog is saved.");
            Check.True(fixture.SaveCount > originalSaveCount, "Selecting a theme must invoke persistence immediately.");
            Check.True(notifications.Contains(nameof(MainViewModel.SelectedThemeId)), "Theme selection must notify bound controls.");
            Check.True(AppTheme.TryGetColor(AppTheme.Ocean, "WindowBackground", out var expectedBackground), "The selected theme must have a window background.");
            Check.Equal(expectedBackground, ((SolidColorBrush)Application.Current.Resources["WindowBackground"]).Color,
                "The actual WPF resource palette must change before a save action.");
            Check.Equal(fixture.Model.ThemeOptions.Single(option => option.Code == AppTheme.Ocean).Title, fixture.Model.ThemeDisplayName,
                "The reported theme name must be the selected one.");
            Check.Equal(AppTheme.All.Length, fixture.Model.ThemeOptions.Count, "Every colour scheme must be offered.");
            Check.True(fixture.Model.ThemeOptions.All(option => !string.IsNullOrWhiteSpace(option.Title) && !option.Title.StartsWith("Orbit.", StringComparison.Ordinal)),
                "Every colour scheme needs a readable localized title.");

            foreach (var retired in new[] { AppTheme.Dark, AppTheme.Light, AppTheme.Retro, AppTheme.Indigo, "no-such-theme" })
                Check.Equal(AppTheme.Orbit, AppTheme.Normalize(retired), "A retired or unknown theme must fall back to Orbit: " + retired);
            Check.Equal(AppTheme.Plum, AppTheme.Normalize(" PLUM "), "Theme identifiers are matched without regard to case or spaces.");
            Check.Equal(0, fixture.Engine.Starts.Count, "Selecting a theme must not connect the VPN.");
            Check.Equal(0, fixture.StartupRequests.Count, "Selecting a theme must not modify Windows startup.");
        }
        finally { AppTheme.Apply(AppTheme.Orbit); }
    }

    private static async Task SettingsThemeSurvivesCancelAsync()
    {
        await using var fixture = new TestFixture();
        try
        {
            fixture.Model.SelectedThemeId = AppTheme.Aurora;
            ComboBox? dismissedThemeBox = null;
            fixture.DialogChoice = dialog =>
            {
                Check.True(dialog is SettingsWindow, "The settings command must construct the actual settings window.");
                var themeBox = (ComboBox)dialog.FindName("ThemeBox");
                dismissedThemeBox = themeBox;
                Check.Equal(AppTheme.Aurora, themeBox.SelectedValue as string, "Settings must start with the current theme selected.");
                var previousSaveCount = fixture.SaveCount;
                themeBox.SelectedValue = AppTheme.Ocean;
                Check.Equal(AppTheme.Ocean, fixture.Model.ThemeId, "The theme must apply while the settings dialog is still open.");
                Check.Equal(AppTheme.Ocean, fixture.Settings.Theme, "The new theme must persist before ShowDialog returns.");
                Check.True(fixture.SaveCount > previousSaveCount, "An open-dialog theme change must persist immediately.");

                previousSaveCount = fixture.SaveCount;
                themeBox.SelectedValue = AppTheme.Plum;
                Check.Equal(AppTheme.Plum, fixture.Model.SelectedThemeId, "A second open-dialog selection must update the selection.");
                Check.Equal(AppTheme.Plum, fixture.Settings.Theme, "Each open-dialog theme change must persist independently.");
                Check.True(fixture.SaveCount > previousSaveCount, "The second theme selection must also be persisted before dismissing settings.");
                ((CheckBox)dialog.FindName("LaunchBox")).IsChecked = true;
                ((CheckBox)dialog.FindName("ConnectBox")).IsChecked = true;
                return false;
            };

            fixture.Model.OpenSettingsCommand.Execute(null);

            Check.Equal(AppTheme.Plum, fixture.Model.ThemeId, "Cancel must not revert an already applied theme.");
            Check.Equal(AppTheme.Plum, fixture.Settings.Theme, "Cancel must retain the persisted theme.");
            Check.True(!fixture.Settings.LaunchWithWindows && !fixture.Settings.ConnectOnLaunch,
                "Cancel must still discard unrelated startup and automatic connection edits.");
            Check.Equal(0, fixture.StartupRequests.Count, "Discarded startup edits must not reach Windows startup.");
            Check.True(dismissedThemeBox is not null, "The settings theme control should have been intercepted.");
            var savedAfterCancel = fixture.SaveCount;
            dismissedThemeBox!.SelectedValue = AppTheme.Ocean;
            Check.Equal(AppTheme.Plum, fixture.Model.ThemeId, "A dismissed settings window must no longer change the active theme.");
            Check.Equal(savedAfterCancel, fixture.SaveCount, "The scoped settings theme event must detach after dialog dismissal.");
        }
        finally { AppTheme.Apply(AppTheme.Orbit); }
    }

    private static Task ThemePalettesAreCompleteAndReadableAsync()
    {
        var reference = AppTheme.ColorsFor(AppTheme.Orbit).Keys.Order().ToArray();
        var accents = new HashSet<Color>();
        foreach (var theme in AppTheme.All)
        {
            Check.True(reference.SequenceEqual(AppTheme.ColorsFor(theme).Keys.Order()), "Every theme must define the same colours as Orbit: " + theme);
            Color Get(string key) => AppTheme.TryGetColor(theme, key, out var color) ? color : throw new InvalidOperationException(key);
            var background = Get("WindowBackground");
            var panel = Get("CardBackground");
            Check.True(accents.Add(Get("Accent")), "Each theme needs its own accent colour: " + theme);
            Check.True(Contrast(Get("WindowForeground"), background) >= 7 && Contrast(Get("WindowForeground"), panel) >= 7,
                "Body text must be clearly readable on the window and on panels: " + theme);
            Check.True(Contrast(Get("MutedForeground"), background) >= 4.5 && Contrast(Get("MutedForeground"), panel) >= 4.5,
                "Secondary text must stay readable: " + theme);
            Check.True(Contrast(Get("Accent"), background) >= 4.5 && Contrast(Get("Accent2"), panel) >= 4.5,
                "Accent-coloured text must stay readable: " + theme);
            Check.True(Contrast(Get("PrimaryForeground"), Get("Accent")) >= 4.5 && Contrast(Get("PrimaryForeground"), Get("HeroActive2")) >= 3,
                "The label of the main button must be readable across its gradient: " + theme);
            Check.True(Distance(Get("HeroActive"), Get("HeroIdle")) >= 80 && Distance(Get("HeroBusy"), Get("HeroActive")) >= 80 && Distance(Get("HeroBusy"), Get("HeroIdle")) >= 80,
                "Idle, connecting and connected must look different: " + theme);

            AppTheme.Apply(theme);
            foreach (var key in new[] { "AccentWash", "AccentLine", "AccentLineBright", "AccentBright", "ProfileSelectedBackground", "PushBackground", "PanelSurface" })
                Check.True(Application.Current.Resources[key] is Brush, $"Theme {theme} must publish the {key} brush used by the main window.");
            Check.Equal(Get("Accent"), ((SolidColorBrush)Application.Current.Resources["Accent"]).Color, "Applying a theme must publish its accent: " + theme);
        }
        AppTheme.Apply(AppTheme.Orbit);
        return Task.CompletedTask;
    }

    // Plain RGB distance: states of the globe differ by hue as much as by brightness.
    private static double Distance(Color a, Color b)
        => Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));

    private static double Contrast(Color a, Color b)
    {
        static double Luminance(Color color)
        {
            static double Channel(byte value)
            {
                var c = value / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        }
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static async Task SelectedVersusRunningAsync()
    {
        await using var fixture = new TestFixture();
        await fixture.Model.ConnectFromUserAsync();
        fixture.Model.SelectedProfile = fixture.Model.Profiles.Single(profile => profile.ConfigPath == fixture.BetaPath);
        Check.Equal(fixture.BetaPath, fixture.Model.ConfigPath, "The edited profile should change.");
        Check.Equal("Alpha", fixture.Model.ConnectionProfileName, "The connection card must still identify the running profile.");
        Check.True(fixture.Model.IsOtherProfileRunning && !fixture.Model.IsSelectedProfileRunning, "Running and selected states must remain distinct.");
        Check.Equal(1, fixture.Engine.Starts.Count, "Selecting a profile must not silently restart the tunnel.");
        Check.Equal(0, fixture.Engine.Stops, "Selecting a profile must not silently stop the tunnel.");
    }

    private static async Task EmptyRulesAsync()
    {
        await using var fixture = new TestFixture(emptyRules: true);
        fixture.Model.RoutingModeIndex = 1;
        Check.True(!fixture.Model.HasApps, "The fixture should have no rules.");
        Check.Equal(Loc.T("Orbit.AllTraffic"), fixture.Model.RoutingSummary, "An empty exclusion list still means full tunnel.");
        Check.Equal(Loc.T("Orbit.Direct"), fixture.Model.ListedRouteLabel, "An empty exclusion list should still identify its rules as direct-route exceptions.");
        await fixture.Model.ConnectFromUserAsync();
        Check.Equal(0, fixture.Engine.Starts.Single().Targets.Count, "The engine should receive an empty target list for full tunnel.");
    }

    private static async Task ConnectDisconnectAsync()
    {
        await using var fixture = new TestFixture();
        Check.True(fixture.Model.ConnectCommand.CanExecute(null), "Connect must initially be available.");
        await fixture.Model.ConnectFromUserAsync();
        Check.True(fixture.Model.IsConnected && fixture.Engine.IsRunning, "A successful start should establish connected state.");
        Check.Equal("connected", fixture.Model.StatusKind, "The status should be connected.");
        Check.True(!fixture.Model.ConnectCommand.CanExecute(null), "Connect must be disabled while connected.");
        Check.True(ReferenceEquals(fixture.Model.ConnectionActionCommand, fixture.Model.DisconnectCommand), "The power button should disconnect.");
        await fixture.Model.DisconnectFromUserAsync();
        Check.True(!fixture.Model.IsConnected && !fixture.Engine.IsRunning && !fixture.Model.IsBusy, "Disconnect should leave a settled, stopped model.");
        Check.Equal("disconnected", fixture.Model.StatusKind, "The status should be disconnected.");
        Check.True(fixture.Model.ConnectCommand.CanExecute(null), "Connect should become available again.");
    }

    private static async Task CancelConnectAsync()
    {
        await using var fixture = new TestFixture();
        fixture.Engine.StartBehavior = async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return OperationResult.Ok("unreachable");
        };
        var connection = fixture.Model.ConnectFromUserAsync();
        await Check.UntilAsync(() => fixture.Engine.Starts.Count == 1, "The fake start did not begin.");
        Check.True(fixture.Model.IsBusy, "A pending start should show busy state.");
        Check.True(fixture.Model.ConnectionActionCommand.CanExecute(null), "The main action must remain cancellable while connecting.");
        await fixture.Model.DisconnectFromUserAsync();
        await connection;
        Check.True(!fixture.Model.IsConnected && !fixture.Engine.IsRunning && !fixture.Model.IsBusy, "Cancellation must leave no live tunnel or stuck busy state.");
    }

    private static async Task LateSuccessAfterCancellationAsync()
    {
        await using var fixture = new TestFixture();
        var gate = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Engine.StartBehavior = (_, _) => gate.Task;
        var connection = fixture.Model.ConnectFromUserAsync();
        try
        {
            await Check.UntilAsync(() => fixture.Engine.Starts.Count == 1, "The delayed fake start did not begin.");
            var cancellation = fixture.Model.DisconnectFromUserAsync();
            gate.TrySetResult(OperationResult.Ok("Start completed after cancellation"));
            await cancellation;
            await connection;
            Check.True(!fixture.Model.IsConnected && !fixture.Engine.IsRunning, "A late successful start must be stopped after cancellation.");
            Check.True(fixture.Engine.Stops > 0, "The late engine process must be cleaned up explicitly.");
        }
        finally
        {
            gate.TrySetResult(OperationResult.Ok("cleanup"));
        }
    }

    private static async Task FailedStartCanRetryAsync()
    {
        await using var fixture = new TestFixture();
        fixture.Engine.StartBehavior = (_, _) => throw new InvalidOperationException("Synthetic start failure");
        await fixture.Model.ConnectFromUserAsync();
        Check.True(!fixture.Model.IsConnected && !fixture.Model.IsBusy, "A start exception must reset the connection operation.");
        Check.Equal("error", fixture.Model.StatusKind, "A start exception should be visible as an error.");
        fixture.Engine.StartBehavior = null;
        await fixture.Model.ConnectFromUserAsync();
        Check.True(fixture.Model.IsConnected, "A subsequent connection attempt must work.");
    }

    private static async Task FailedStopCanRetryAsync()
    {
        await using var fixture = new TestFixture();
        await fixture.Model.ConnectFromUserAsync();
        fixture.Engine.StopBehavior = _ => Task.FromResult(OperationResult.Fail("Synthetic stop refusal"));
        await fixture.Model.DisconnectFromUserAsync();
        Check.True(fixture.Model.IsConnected && fixture.Engine.IsRunning && !fixture.Model.IsBusy, "A refused stop must not claim that a live engine disconnected.");
        Check.True(fixture.Model.DisconnectCommand.CanExecute(null), "A failed stop must remain retryable.");
        fixture.Engine.StopBehavior = null;
        await fixture.Model.DisconnectFromUserAsync();
        Check.True(!fixture.Model.IsConnected, "Retrying stop should disconnect.");
    }

    private static async Task ThrowingStopCanRetryAsync()
    {
        await using var fixture = new TestFixture();
        await fixture.Model.ConnectFromUserAsync();
        fixture.Engine.StopBehavior = _ => throw new InvalidOperationException("Synthetic stop exception");
        await fixture.Model.DisconnectFromUserAsync();
        Check.True(fixture.Model.IsConnected && !fixture.Model.IsBusy, "A stop exception must preserve reality and reset busy state.");
        fixture.Engine.StopBehavior = null;
        await fixture.Model.DisconnectFromUserAsync();
        Check.True(!fixture.Model.IsConnected, "Stop must be retryable after an exception.");
    }

    private static async Task ReconnectDirtyRoutesAsync()
    {
        await using var fixture = new TestFixture();
        await fixture.Model.ConnectFromUserAsync();
        fixture.Model.RoutingModeIndex = 1;
        Check.True(fixture.Model.HasPendingChanges && fixture.Model.ReconnectCommand.CanExecute(null), "Changing live routes should enable reconnection.");
        await fixture.Model.ReconnectFromUserAsync();
        Check.Equal(2, fixture.Engine.Starts.Count, "Reconnect should perform exactly one fresh start.");
        Check.Equal(1, fixture.Engine.Stops, "Reconnect should stop the previous tunnel once.");
        Check.True(fixture.Engine.Starts.Last().ExceptListed, "Reconnect must send the updated routing mode.");
        Check.True(fixture.Model.IsConnected && !fixture.Model.HasPendingChanges, "Successful reconnect should clear pending changes.");
    }

    private static async Task ReconnectYouTubeDomainsAsync()
    {
        await using var fixture = new TestFixture();
        await fixture.Model.ConnectFromUserAsync();
        Check.True(!fixture.Engine.Starts.Single().Targets.Any(target => target.EffectiveKind == RouteKind.Domain),
            "The initial connection must not already contain the YouTube preset.");
        fixture.DialogChoice = dialog =>
        {
            Check.True(dialog is AddressListPrompt, "The real add-list command must construct the address-list dialog.");
            var prompt = (AddressListPrompt)dialog;
            typeof(AddressListPrompt).GetProperty(nameof(AddressListPrompt.ListName))!.SetValue(prompt, "YouTube");
            typeof(AddressListPrompt).GetProperty(nameof(AddressListPrompt.Entries))!.SetValue(prompt, DomainListPresets.YouTubeGoogle.ToArray());
            typeof(AddressListPrompt).GetProperty(nameof(AddressListPrompt.ResolvedIps))!.SetValue(prompt, Array.Empty<string>());
            typeof(AddressListPrompt).GetProperty(nameof(AddressListPrompt.SkipDomainResolve))!.SetValue(prompt, true);
            return true;
        };

        fixture.Model.AddAddressListCommand.Execute(null);
        await Check.UntilAsync(() => fixture.Model.Apps.Any(rule => rule.IsAddressList) && fixture.Model.HasPendingChanges,
            "The actual add-list command must add the YouTube preset and mark it for reconnection.");
        Check.Equal(1, fixture.DialogCount, "Adding the preset should use one intercepted address-list dialog.");
        Check.Equal(1, fixture.Engine.Starts.Count, "Adding a list must leave the existing connection intact until explicit reconnect.");
        var storedList = fixture.Model.Apps.Single(rule => rule.IsAddressList);
        Check.True(storedList.SkipDomainResolve && storedList.ResolvedIpList.Count == 0,
            "The YouTube preset must retain domain routing without cached CDN IPs.");
        Check.True(fixture.Model.ReconnectCommand.CanExecute(null), "The new live domain list must make reconnect available.");

        await fixture.Model.ReconnectFromUserAsync();

        Check.Equal(2, fixture.Engine.Starts.Count, "Explicit reconnect must start the engine once with the updated list.");
        Check.Equal(1, fixture.Engine.Stops, "Explicit reconnect must stop the previous connection once.");
        var request = fixture.Engine.Starts.Last();
        Check.Equal(fixture.AlphaPath, request.ConfigFilePath, "Reconnection must preserve the selected/running profile.");
        Check.True(!request.ExceptListed, "The YouTube domains must be sent in include mode so they route through VPN.");
        var domains = request.Targets.Where(target => target.EffectiveKind == RouteKind.Domain)
            .Select(target => target.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Check.True(domains.Contains("youtube.com") && domains.Contains("googlevideo.com") && domains.Contains("ytimg.com"),
            "The updated engine request must include the YouTube site, video CDN, and image CDN as domain targets.");
        Check.True(domains.SetEquals(DomainListPresets.YouTubeGoogle),
            "Every domain in the actual YouTube preset must survive dialog, persistence, and reconnect expansion.");
        Check.True(!request.Targets.Any(target => target.EffectiveKind is RouteKind.AddressList or RouteKind.Ip),
            "The engine must receive expanded domain targets without an unexpanded list or stale CDN IPs.");
        Check.True(request.Targets.Any(target => target.EffectiveKind == RouteKind.File && target.Path == fixture.Executable),
            "Adding a YouTube list must preserve the profile's existing application route.");
        Check.True(fixture.Model.IsConnected && !fixture.Model.HasPendingChanges,
            "Successful reconnect must settle as connected with the domain edits applied.");
    }

    private static async Task ReconnectOtherProfileIsDisabledAsync()
    {
        await using var fixture = new TestFixture();
        await fixture.Model.ConnectFromUserAsync();
        fixture.Model.ExceptListed = true;
        fixture.Model.SelectedProfile = fixture.Model.Profiles.Single(profile => profile.ConfigPath == fixture.BetaPath);
        Check.True(!fixture.Model.ReconnectCommand.CanExecute(null), "A selected inactive profile must not reconnect another profile's live tunnel.");
        await fixture.Model.ReconnectFromUserAsync();
        Check.Equal(1, fixture.Engine.Starts.Count, "Disabled reconnect must not start a different profile.");
        fixture.Model.SelectedProfile = fixture.Model.Profiles.Single(profile => profile.ConfigPath == fixture.AlphaPath);
        Check.True(fixture.Model.HasPendingChanges, "Pending edits should remain associated with their original profile.");
    }

    private static async Task AddRemoveRulesAsync()
    {
        await using var fixture = new TestFixture();
        await fixture.Model.ConnectFromUserAsync();
        fixture.Model.AddFolderCommand.Execute(null);
        Check.Equal(2, fixture.Model.TargetCount, "The actual add-folder command should add a fixture directory.");
        Check.True(fixture.Model.HasPendingChanges, "Adding a live routing target requires reconnect.");
        fixture.Model.SelectedApp = fixture.Model.Apps.Single(rule => rule.IsFolder);
        fixture.Model.RemoveCommand.Execute(null);
        Check.Equal(1, fixture.Model.TargetCount, "The actual remove command should remove the selected target.");
    }

    private static async Task InactiveSubscriptionNodeAsync()
    {
        await using var fixture = new TestFixture(subscriptions: true);
        await fixture.Model.ConnectFromUserAsync();
        fixture.Model.SelectedProfile = fixture.Model.Profiles.Single(profile => profile.ConfigPath == fixture.BetaPath);
        await ApplyNodeAsync(fixture.Model, fixture.BetaPath, "Node B");
        Check.Equal(0, fixture.Engine.SelectedNodes.Count, "Selecting a server for an inactive subscription must not alter the running profile.");
        Check.True(SubscriptionProfile.TryRead(fixture.BetaPath, out var saved, out _), "The selected subscription should remain readable.");
        Check.Equal("Node B", saved.EffectiveSelectedNode, "The inactive profile's choice should be saved for its next connection.");
        Check.Equal("Alpha", fixture.Model.ConnectionProfileName, "The running profile label must stay Alpha.");
    }

    private static async Task ActiveSubscriptionNodeAsync()
    {
        await using var fixture = new TestFixture(subscriptions: true);
        await fixture.Model.ConnectFromUserAsync();
        await ApplyNodeAsync(fixture.Model, fixture.AlphaPath, "Node B");
        Check.Equal("Node B", fixture.Engine.SelectedNodes.Single(), "The active subscription should select the node on the actual controller boundary.");
        Check.Equal(1, fixture.Engine.Starts.Count, "Changing a live subscription node must not restart the whole engine.");
    }

    private static async Task ManageServersCommandAsync()
    {
        await using var fixture = new TestFixture(subscriptions: true);
        await fixture.Model.ConnectFromUserAsync();
        fixture.DialogChoice = dialog =>
        {
            Check.True(dialog is ServerPickerPrompt, "Manage servers must create the actual server picker.");
            typeof(ServerPickerPrompt).GetProperty(nameof(ServerPickerPrompt.SelectedNodeName))!.SetValue(dialog, "Node B");
            return true;
        };
        fixture.Model.ManageServersCommand.Execute(null);
        await Check.UntilAsync(() => fixture.Engine.SelectedNodes.Contains("Node B"), "The real server command did not apply the intercepted selection.");
        Check.Equal(1, fixture.DialogCount, "Exactly one picker should be intercepted.");
    }

    private static async Task FullTunnelConfirmationAsync()
    {
        await using var fixture = new TestFixture(emptyRules: true, warnFullTunnel: true);
        fixture.DialogChoice = _ => false;
        await fixture.Model.ConnectFromUserAsync();
        Check.Equal(0, fixture.Engine.Starts.Count, "Cancelling full-tunnel confirmation must not start the engine.");
        fixture.DialogChoice = dialog =>
        {
            Check.True(dialog is FullTunnelPrompt, "Full tunnel requires its actual confirmation prompt.");
            ((CheckBox)dialog.FindName("RememberBox")).IsChecked = true;
            return true;
        };
        await fixture.Model.ConnectFromUserAsync();
        Check.True(fixture.Model.IsConnected && fixture.Settings.SkipFullTunnelWarning, "Confirm-and-remember should connect and save only injected settings.");
    }

    private static async Task DeclinedElevationAsync()
    {
        await using var fixture = new TestFixture();
        fixture.IsElevated = false;
        await fixture.Model.ConnectFromUserAsync();
        Check.Equal(1, fixture.ElevationRequests, "The elevation request should use the injected boundary.");
        Check.Equal(1, fixture.SessionSaveCount, "The session handoff should use the injected in-memory boundary.");
        Check.Equal(fixture.AlphaPath, fixture.SavedSessionPath, "The intended profile should be passed to the session boundary.");
        Check.Equal(0, fixture.Engine.Starts.Count, "Declined elevation must not start the engine.");
        Check.True(!fixture.Model.IsBusy && !fixture.Model.IsConnected, "Declined elevation should leave a settled model.");
    }

    private static async Task RecoveryDisabledAsync()
    {
        await using var fixture = new TestFixture();
        Check.True(!fixture.Settings.AutoReconnect, "Automatic recovery must be opt-in.");
        await fixture.Model.ConnectFromUserAsync();
        fixture.Engine.Crash();
        await Task.Delay(10);
        Check.Equal(1, fixture.Engine.Starts.Count, "A crash must not restart while recovery is disabled.");
        Check.True(!fixture.Model.IsConnected && !fixture.Model.IsRecovering, "The connection should settle as stopped/error.");
    }

    private static async Task RecoveryUsesRunningProfileAsync()
    {
        await using var fixture = new TestFixture();
        fixture.Settings.AutoReconnect = true;
        await fixture.Model.ConnectFromUserAsync();
        fixture.Model.SelectedProfile = fixture.Model.Profiles.Single(profile => profile.ConfigPath == fixture.BetaPath);
        fixture.Engine.Crash();
        await Check.UntilAsync(() => fixture.Engine.Starts.Count == 2 && fixture.Model.IsConnected, "Recovery did not restore the tunnel.");
        Check.Equal(fixture.AlphaPath, fixture.Engine.Starts.Last().ConfigFilePath, "Recovery must restart the lost running profile rather than the edited profile.");
        Check.Equal(fixture.BetaPath, fixture.Model.ConfigPath, "Recovery should preserve the profile being edited.");
        Check.Equal("Alpha", fixture.Model.ConnectionProfileName, "The connection card must identify the recovered profile.");
    }

    private static async Task RecoveryCanBeCancelledAsync()
    {
        await using var fixture = new TestFixture();
        fixture.Settings.AutoReconnect = true;
        await fixture.Model.ConnectFromUserAsync();
        var delayEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DelayChoice = async (_, token) =>
        {
            delayEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        fixture.Engine.Crash();
        await delayEntered.Task;
        Check.True(fixture.Model.IsRecovering && fixture.Model.ConnectionActionCommand.CanExecute(null), "The recovery action should remain cancellable.");
        await fixture.Model.DisconnectFromUserAsync();
        await Check.UntilAsync(() => !fixture.Model.IsRecovering && !fixture.Model.IsBusy, "Cancelling recovery left a stuck operation.");
        Check.Equal(1, fixture.Engine.Starts.Count, "Cancelled recovery must not start another engine.");
    }

    private static async Task RecoveryIsBoundedAsync()
    {
        await using var fixture = new TestFixture();
        fixture.Settings.AutoReconnect = true;
        await fixture.Model.ConnectFromUserAsync();
        fixture.Engine.StartBehavior = (_, _) => Task.FromResult(OperationResult.Fail("Synthetic recovery failure"));
        fixture.Engine.Crash();
        await Check.UntilAsync(() => !fixture.Model.IsRecovering && !fixture.Model.IsBusy, "Recovery did not stop after its attempt budget.");
        Check.Equal(4, fixture.Engine.Starts.Count, "Recovery should make exactly three additional attempts.");
        Check.True(!fixture.Model.IsConnected && fixture.Model.ConnectCommand.CanExecute(null), "Exhausted recovery should leave manual connection available.");
    }

    private static Task ApplyNodeAsync(MainViewModel model, string configPath, string nodeName)
    {
        var method = typeof(MainViewModel).GetMethod("ApplySelectedNodeAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("The selected-node application boundary was not found.");
        return (Task)method.Invoke(model, [configPath, nodeName])!;
    }

    private static Task RefreshIpAsync(MainViewModel model)
        => (Task)typeof(MainViewModel).GetMethod("RefreshIpAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(model, [false])!;

    private static async Task LateIpAfterDisconnectAsync()
    {
        await using var fixture = new TestFixture();
        await fixture.Model.ConnectFromUserAsync();
        var pending = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Engine.IpBehavior = _ => pending.Task;
        var refresh = RefreshIpAsync(fixture.Model);
        await fixture.Model.DisconnectFromUserAsync();
        pending.SetResult(OperationResult.Ok("203.0.113.99"));
        await refresh;
        Check.Equal("—", fixture.Model.ExternalIp, "A late response must not repopulate the disconnected address.");
        Check.True(!fixture.Model.IsConnected, "The IP probe must never reconnect.");
    }

    private static async Task OldSessionIpAsync()
    {
        await using var fixture = new TestFixture();
        await fixture.Model.ConnectFromUserAsync();
        var pending = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Engine.IpBehavior = _ => pending.Task;
        var oldRefresh = RefreshIpAsync(fixture.Model);
        await fixture.Model.DisconnectFromUserAsync();
        await fixture.Model.ConnectFromUserAsync();
        fixture.Engine.IpBehavior = _ => Task.FromResult(OperationResult.Ok("198.51.100.24"));
        await RefreshIpAsync(fixture.Model);
        pending.SetResult(OperationResult.Ok("203.0.113.99"));
        await oldRefresh;
        Check.Equal("198.51.100.24", fixture.Model.ExternalIp, "Only the current session's result may be displayed.");
    }

    private static async Task LateServerAsync()
    {
        await using var fixture = new TestFixture(subscriptions: true);
        await fixture.Model.ConnectFromUserAsync();
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Engine.NodeBehavior = _ => pending.Task;
        var refresh = (Task)typeof(MainViewModel).GetMethod("RefreshConnectedServerAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(fixture.Model, null)!;
        await fixture.Model.DisconnectFromUserAsync();
        pending.SetResult("Old node");
        await refresh;
        Check.Equal("", fixture.Model.ConnectedServer, "A late server response must not leak into the disconnected state.");
    }
}
