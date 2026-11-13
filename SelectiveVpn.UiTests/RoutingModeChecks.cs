using System.Text.Json;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;
using SelectiveVpn.Services;

namespace SelectiveVpn.UiTests;

internal static class RoutingModeChecks
{
    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
    [
        ("legacy routing lists migrate only to their saved mode with independent data", LegacyMigrationAsync),
        ("saved empty routing lists never resurrect a legacy list", ModernEmptyListsAsync),
        ("routing modes keep separate editable domain and application lists", SwitchModesAsync),
        ("each profile retains both routing lists and its selected mode", ProfilesRemainIndependentAsync),
        ("both routing lists and the selected mode survive saving and reopening", ReopenSavedModesAsync),
        ("an empty exclusion list starts a full VPN tunnel", EmptyExclusionsAsync),
        ("live mode changes reconnect with only the selected mode's rules", ReconnectSelectedModeAsync),
        ("elevation restoration replaces one mode snapshot without merging the other", RestoreSessionSelectedModeAsync),
        ("elevation captures only the selected mode and leaves the other list intact", ElevationSelectedModeAsync)
    ];

    private static Task LegacyMigrationAsync()
    {
        foreach (var exceptListed in new[] { false, true })
        {
            var legacy = new SavedTarget
            {
                Path = "Legacy sites", Kind = "address-list",
                Entries = ["youtube.com", "googlevideo.com"], ResolvedIps = ["203.0.113.24/32"]
            };
            var profile = new SavedProfile { ConfigPath = "synthetic.conf", ExceptListed = exceptListed, Targets = [legacy] };
            var settings = new UserSettings { ConfigPath = profile.ConfigPath, Profiles = [profile] };

            UserSettingsStore.Normalize(settings);

            var active = exceptListed ? profile.ExcludeTargets! : profile.IncludeTargets!;
            var other = exceptListed ? profile.IncludeTargets! : profile.ExcludeTargets!;
            Check.Equal(1, active.Count, "The legacy list should belong to its saved mode.");
            Check.Equal(0, other.Count, "The second mode should begin with its own empty list.");
            Check.True(active.Single().Entries.SequenceEqual(new[] { "youtube.com", "googlevideo.com" }),
                "Migration must retain every domain rather than converting it to a stale IP route.");
            Check.True(active.Single().ResolvedIps.SequenceEqual(new[] { "203.0.113.24/32" }),
                "Migration must retain the list's explicit resolved-IP metadata.");

            legacy.Entries.Add("must-not-appear.example");
            legacy.ResolvedIps.Clear();
            Check.Equal(2, active.Single().Entries.Count, "Migrated entries must not share the original mutable list.");
            Check.Equal(1, active.Single().ResolvedIps.Count, "Migrated IP metadata must not share the original mutable list.");
            profile.Targets.Single().Entries.Add("compatibility-only.example");
            Check.Equal(2, active.Single().Entries.Count, "The legacy compatibility mirror must not mutate the saved mode list.");

            UserSettingsStore.Normalize(settings);
            Check.Equal(1, (exceptListed ? profile.ExcludeTargets : profile.IncludeTargets)!.Count,
                "Repeated normalization must not duplicate migrated rules.");
            Check.Equal(0, (exceptListed ? profile.IncludeTargets : profile.ExcludeTargets)!.Count,
                "Repeated normalization must not copy rules into the other mode.");
        }
        return Task.CompletedTask;
    }

    private static Task ModernEmptyListsAsync()
    {
        var settings = new UserSettings
        {
            Profiles =
            [
                new SavedProfile
                {
                    ConfigPath = "empty.conf", ExceptListed = true,
                    Targets = [new SavedTarget { Path = "stale.example", Kind = "domain" }],
                    IncludeTargets = [], ExcludeTargets = []
                },
                new SavedProfile
                {
                    ConfigPath = "partial.conf", ExceptListed = true,
                    Targets = [new SavedTarget { Path = "stale.example", Kind = "domain" }],
                    IncludeTargets = [], ExcludeTargets = null
                }
            ]
        };

        UserSettingsStore.Normalize(settings);
        var restored = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(settings))!;
        UserSettingsStore.Normalize(restored);

        foreach (var profile in restored.Profiles)
        {
            Check.Equal(0, profile.IncludeTargets!.Count, "An explicitly empty modern include list must remain empty.");
            Check.Equal(0, profile.ExcludeTargets!.Count, "A modern exclusion list must not be refilled from stale compatibility data.");
            Check.Equal(0, profile.Targets.Count, "The compatibility mirror must reflect the active modern list.");
        }
        return Task.CompletedTask;
    }

    private static async Task SwitchModesAsync()
    {
        await using var fixture = new TestFixture();
        await AddListAsync(fixture, "YouTube", DomainListPresets.YouTubeGoogle);
        ExpectApps(fixture, fixture.Executable, "YouTube");

        fixture.Model.RoutingModeIndex = 1;
        Check.True(fixture.Model.ExceptListed, "The mode selector must activate the exclusion list.");
        ExpectApps(fixture);
        fixture.Model.AddFolderCommand.Execute(null);
        ExpectApps(fixture, fixture.Folder);

        fixture.Model.RoutingModeIndex = 0;
        ExpectApps(fixture, fixture.Executable, "YouTube");
        var restoredYouTube = fixture.Model.Apps.Single(rule => rule.IsAddressList);
        Check.True(restoredYouTube.EntryList.SequenceEqual(DomainListPresets.YouTubeGoogle) && restoredYouTube.SkipDomainResolve,
            "Switching back must retain all domain-list contents and the skip-resolution flag.");
        fixture.Model.SelectedApp = restoredYouTube;
        fixture.Model.RemoveCommand.Execute(null);
        ExpectApps(fixture, fixture.Executable);

        fixture.Model.RoutingModeIndex = 1;
        ExpectApps(fixture, fixture.Folder);
        fixture.Model.SelectedApp = fixture.Model.Apps.Single();
        fixture.Model.RemoveCommand.Execute(null);
        ExpectApps(fixture);
        fixture.Model.RoutingModeIndex = 0;
        ExpectApps(fixture, fixture.Executable);
    }

    private static async Task ProfilesRemainIndependentAsync()
    {
        await using var fixture = new TestFixture();
        await AddListAsync(fixture, "Alpha YouTube", DomainListPresets.YouTubeGoogle);
        fixture.Model.RoutingModeIndex = 1;
        fixture.Model.AddFolderCommand.Execute(null);

        SelectProfile(fixture, fixture.BetaPath);
        Check.True(!fixture.Model.ExceptListed, "A newly selected profile should restore its own saved include mode.");
        ExpectApps(fixture, fixture.Executable);
        fixture.Model.RoutingModeIndex = 1;
        ExpectApps(fixture);
        await AddListAsync(fixture, "Beta direct sites", ["example.org"]);

        SelectProfile(fixture, fixture.AlphaPath);
        Check.True(fixture.Model.ExceptListed, "Returning to Alpha should restore Alpha's last selected mode.");
        ExpectApps(fixture, fixture.Folder);
        fixture.Model.RoutingModeIndex = 0;
        ExpectApps(fixture, fixture.Executable, "Alpha YouTube");

        SelectProfile(fixture, fixture.BetaPath);
        Check.True(fixture.Model.ExceptListed, "Returning to Beta should restore Beta's independent last selected mode.");
        ExpectApps(fixture, "Beta direct sites");
        fixture.Model.RoutingModeIndex = 0;
        ExpectApps(fixture, fixture.Executable);
    }

    private static async Task ReopenSavedModesAsync()
    {
        await using var fixture = new TestFixture(configureSettings: settings =>
        {
            var active = settings.Profiles[0];
            active.IncludeTargets = active.Targets.ToList();
            active.ExcludeTargets =
            [
                new SavedTarget
                {
                    Path = "Local sites", Kind = "address-list", Entries = ["example.org"],
                    ResolvedIps = ["203.0.113.7/32"], SkipDomainResolve = false
                }
            ];
            active.ExceptListed = true;
        });
        Check.True(fixture.Model.ExceptListed, "Initial startup must restore the saved exclusion mode.");
        ExpectApps(fixture, "Local sites");
        fixture.Model.AddFolderCommand.Execute(null);
        fixture.Model.RoutingModeIndex = 0;
        await AddListAsync(fixture, "YouTube", DomainListPresets.YouTubeGoogle);
        fixture.Model.RoutingModeIndex = 1;

        await fixture.ReopenFromSavedSettingsAsync();

        Check.True(fixture.Model.ExceptListed, "The selected exclusion mode must survive a full JSON save and model recreation.");
        ExpectApps(fixture, "Local sites", fixture.Folder);
        var restoredList = fixture.Model.Apps.Single(rule => rule.IsAddressList);
        Check.True(restoredList.EntryList.SequenceEqual(new[] { "example.org" })
            && restoredList.ResolvedIpList.SequenceEqual(new[] { "203.0.113.7/32" }) && !restoredList.SkipDomainResolve,
            "Reopening must retain all exclusion-domain metadata.");
        fixture.Model.RoutingModeIndex = 0;
        ExpectApps(fixture, fixture.Executable, "YouTube");
        Check.True(fixture.SavedSettingsJson!.Contains("\"IncludeTargets\"", StringComparison.Ordinal)
            && fixture.SavedSettingsJson.Contains("\"ExcludeTargets\"", StringComparison.Ordinal),
            "Persistence must write both mode lists, including the inactive list.");

        await fixture.ReopenFromSavedSettingsAsync();
        Check.True(!fixture.Model.ExceptListed, "Saving from include mode must restore include mode on the next startup.");
        ExpectApps(fixture, fixture.Executable, "YouTube");
        fixture.Model.RoutingModeIndex = 1;
        ExpectApps(fixture, "Local sites", fixture.Folder);
    }

    private static async Task EmptyExclusionsAsync()
    {
        await using var fixture = new TestFixture();
        fixture.Model.RoutingModeIndex = 1;
        ExpectApps(fixture);

        await fixture.Model.ConnectFromUserAsync();

        var request = fixture.Engine.Starts.Single();
        Check.True(request.ExceptListed && request.Targets.Count == 0,
            "An empty exclusion list must connect with zero excluded targets, independently of the non-empty include list.");
        var parsed = AmneziaWgConfigReader.ReadFile(fixture.AlphaPath);
        Check.True(parsed.Succeeded && parsed.Profile is not null, "The synthetic profile must remain readable.");
        var yaml = MihomoProfileWriter.Build(parsed.Profile!, request.Targets, 17990, "synthetic-test-secret", exceptListed: request.ExceptListed);
        Check.True(yaml.Contains("MATCH,VPN", StringComparison.Ordinal) && !yaml.Contains("PROCESS-PATH,", StringComparison.Ordinal),
            "The selected empty exclusion list must generate full VPN routing rather than inheriting the include application's rule.");
        fixture.Model.RoutingModeIndex = 0;
        ExpectApps(fixture, fixture.Executable);
    }

    private static async Task ReconnectSelectedModeAsync()
    {
        await using var fixture = new TestFixture();
        await fixture.Model.ConnectFromUserAsync();
        await AddListAsync(fixture, "YouTube", DomainListPresets.YouTubeGoogle);
        await fixture.Model.ReconnectFromUserAsync();
        var includeRequest = fixture.Engine.Starts.Last();
        Check.True(!includeRequest.ExceptListed && includeRequest.Targets.Any(target => target.Path == "youtube.com"),
            "The include list must reach the engine before switching to the independent exclusion list.");

        fixture.Model.RoutingModeIndex = 1;
        fixture.Model.AddFolderCommand.Execute(null);
        Check.True(fixture.Model.IsConnected && fixture.Model.HasPendingChanges,
            "Changing modes must preserve the live connection and expose unapplied changes.");
        Check.True(ReferenceEquals(includeRequest, fixture.Engine.Starts.Last()),
            "Editing the other mode must not mutate or silently replace the running request.");
        await fixture.Model.ReconnectFromUserAsync();
        var excludeRequest = fixture.Engine.Starts.Last();
        Check.True(excludeRequest.ExceptListed && excludeRequest.Targets.Count == 1
            && excludeRequest.Targets.Single().EffectiveKind == RouteKind.Folder
            && excludeRequest.Targets.Single().Path == fixture.Folder,
            "Reconnect must send only the selected exclusion list, without include-mode domains or applications.");
        Check.True(fixture.Model.IsConnected && !fixture.Model.HasPendingChanges,
            "Successful exclusion-mode reconnection must clear pending changes.");

        fixture.Model.RoutingModeIndex = 0;
        await fixture.Model.ReconnectFromUserAsync();
        var returnedRequest = fixture.Engine.Starts.Last();
        Check.True(!returnedRequest.ExceptListed && returnedRequest.Targets.Any(target => target.Path == "youtube.com")
            && returnedRequest.Targets.Any(target => target.Path == fixture.Executable)
            && !returnedRequest.Targets.Any(target => target.Path == fixture.Folder),
            "Switching back and reconnecting must restore the include list without leaking exclusion rules.");
        Check.Equal(fixture.AlphaPath, returnedRequest.ConfigFilePath, "Mode changes must not switch profiles.");
    }

    private static async Task ElevationSelectedModeAsync()
    {
        await using var fixture = new TestFixture();
        fixture.IsElevated = false;
        fixture.Model.RoutingModeIndex = 1;
        fixture.Model.AddFolderCommand.Execute(null);

        await fixture.Model.ConnectFromUserAsync();

        Check.True(fixture.SavedSessionExceptListed, "Elevation handoff must capture the currently selected exclusion mode.");
        Check.True(fixture.SavedSessionRules.Count == 1 && fixture.SavedSessionRules.Single().IsFolder
            && fixture.SavedSessionRules.Single().ExecutablePath == fixture.Folder,
            "Elevation handoff must carry only the exclusion folder, not the include application.");
        var excludedSnapshot = fixture.SavedSessionRules;
        fixture.Model.RoutingModeIndex = 0;
        await fixture.Model.ConnectFromUserAsync();
        Check.True(!fixture.SavedSessionExceptListed, "A later include-mode handoff must capture include mode.");
        Check.True(fixture.SavedSessionRules.Count == 1 && fixture.SavedSessionRules.Single().ExecutablePath == fixture.Executable,
            "The include handoff must contain its own saved application rule.");
        Check.True(excludedSnapshot.Single().ExecutablePath == fixture.Folder,
            "Switching modes must not alter a previously captured elevation session snapshot.");
        Check.Equal(0, fixture.Engine.Starts.Count, "The declined test elevation must never start a real or simulated tunnel.");
        fixture.Model.RoutingModeIndex = 1;
        ExpectApps(fixture, fixture.Folder);
    }

    private static async Task RestoreSessionSelectedModeAsync()
    {
        await using var fixture = new TestFixture();
        await AddListAsync(fixture, "YouTube", DomainListPresets.YouTubeGoogle);
        SelectProfile(fixture, fixture.BetaPath);
        fixture.Model.AddFolderCommand.Execute(null);

        fixture.Model.RestoreSavedSessionTargets(fixture.AlphaPath,
            [new ConnectSessionStore.SessionTarget(fixture.Folder, true, "folder")], exceptListed: true);

        Check.Equal(fixture.AlphaPath, fixture.Model.ConfigPath, "A saved session must restore its own profile.");
        Check.True(fixture.Model.ExceptListed, "A saved exclusion session must restore exclusion mode.");
        ExpectApps(fixture, fixture.Folder);
        fixture.Model.RoutingModeIndex = 0;
        ExpectApps(fixture, fixture.Executable, "YouTube");

        fixture.Model.RestoreSavedSessionTargets(fixture.AlphaPath,
            [new ConnectSessionStore.SessionTarget("Session sites", false, "address-list",
                ["youtube.com", "googlevideo.com"], [], true)], exceptListed: false);

        ExpectApps(fixture, "Session sites");
        Check.True(fixture.Model.Apps.Single().SkipDomainResolve
            && fixture.Model.Apps.Single().EntryList.SequenceEqual(new[] { "youtube.com", "googlevideo.com" }),
            "The exact incoming session snapshot must retain its domain metadata without merging previous include targets.");
        fixture.Model.RoutingModeIndex = 1;
        ExpectApps(fixture, fixture.Folder);

        fixture.Model.RestoreSavedSessionTargets(fixture.AlphaPath, [], exceptListed: true);
        ExpectApps(fixture);
        fixture.Model.RoutingModeIndex = 0;
        ExpectApps(fixture, "Session sites");
        SelectProfile(fixture, fixture.BetaPath);
        ExpectApps(fixture, fixture.Executable, fixture.Folder);
        Check.Equal(0, fixture.Engine.Starts.Count, "Restoring a snapshot through the test seam must not start a tunnel or access the real session file.");
    }

    private static void SelectProfile(TestFixture fixture, string path)
        => fixture.Model.SelectedProfile = fixture.Model.Profiles.Single(profile => profile.ConfigPath == path);

    private static void ExpectApps(TestFixture fixture, params string[] expected)
        => Check.True(fixture.Model.Apps.Select(rule => rule.ExecutablePath).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expected),
            "The visible list must contain only the current profile and routing mode's saved rules. Expected: "
            + string.Join(", ", expected) + "; actual: " + string.Join(", ", fixture.Model.Apps.Select(rule => rule.ExecutablePath)));

    private static async Task AddListAsync(TestFixture fixture, string name, IReadOnlyList<string> entries)
    {
        fixture.DialogChoice = dialog =>
        {
            Check.True(dialog is AddressListPrompt, "The real add-list command should construct AddressListPrompt.");
            var prompt = (AddressListPrompt)dialog;
            typeof(AddressListPrompt).GetProperty(nameof(AddressListPrompt.ListName))!.SetValue(prompt, name);
            typeof(AddressListPrompt).GetProperty(nameof(AddressListPrompt.Entries))!.SetValue(prompt, entries.ToArray());
            typeof(AddressListPrompt).GetProperty(nameof(AddressListPrompt.ResolvedIps))!.SetValue(prompt, Array.Empty<string>());
            typeof(AddressListPrompt).GetProperty(nameof(AddressListPrompt.SkipDomainResolve))!.SetValue(prompt, true);
            return true;
        };
        fixture.Model.AddAddressListCommand.Execute(null);
        await Check.UntilAsync(() => fixture.Model.Apps.Any(rule => rule.IsAddressList && rule.ExecutablePath == name),
            "The add-list command did not add the intercepted domain list.");
    }
}
