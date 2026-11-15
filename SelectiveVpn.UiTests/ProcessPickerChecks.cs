using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using System.Xml.Linq;
using SelectiveVpn.Models;
using SelectiveVpn.Services;
using SelectiveVpn.ViewModels;

namespace SelectiveVpn.UiTests;

internal static class ProcessPickerChecks
{
    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
    [
        ("running programs are listed once each, with real paths and without the app itself", RunningProcessesAreListedAsync),
        ("the process picker loads, filters and refreshes its own list", ProcessPickerDialogAsync),
        ("adding a process from the rule menu creates an ordinary app rule", AddProcessCommandAsync),
        ("settings link to the project page on GitHub", SettingsGitHubLinkAsync)
    ];

    private static Task RunningProcessesAreListedAsync()
    {
        var running = RunningProcessCatalog.Load();
        Check.True(running.Count > 0, "Windows always has running programs whose path can be read.");
        Check.True(running.All(app => Path.IsPathFullyQualified(app.ExecutablePath) && File.Exists(app.ExecutablePath)),
            "Every listed process must point at an existing executable file.");
        Check.Equal(running.Count, running.Select(app => app.ExecutablePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "A program with several processes must be listed once.");
        Check.True(running.All(app => app.Name.StartsWith(Path.GetFileName(app.ExecutablePath), StringComparison.OrdinalIgnoreCase)),
            "Each entry is titled by its file name so it can be recognised and searched.");
        var own = Path.GetFullPath(AppContext.BaseDirectory);
        Check.True(!running.Any(app => app.ExecutablePath.StartsWith(own, StringComparison.OrdinalIgnoreCase)),
            "Programs from the app's own folder must not be offered: routing the app or its engine would loop.");
        return Task.CompletedTask;
    }

    private static async Task ProcessPickerDialogAsync()
    {
        var loads = 0;
        IReadOnlyList<InstalledAppInfo> Fake()
        {
            loads++;
            return loads == 1
                ? [new("browser.exe — Demo Browser", @"C:\Demo\browser.exe"), new("game.exe", @"C:\Games\game.exe")]
                : [new("browser.exe — Demo Browser", @"C:\Demo\browser.exe"), new("game.exe", @"C:\Games\game.exe"), new("chat.exe", @"C:\Demo\chat.exe")];
        }

        var dialog = Hidden(new AppPickerPrompt(() => null, AppPickerSource.RunningProcesses, Fake));
        try
        {
            dialog.Show();
            var list = (ListBox)dialog.FindName("AppList");
            var search = (TextBox)dialog.FindName("SearchBox");
            var refresh = (Button)dialog.FindName("RefreshButton");
            await Check.UntilAsync(() => list.Items.Count == 2 && list.IsEnabled, "The picker did not load the running programs.");
            Check.Equal(Loc.T("ProcessPicker.Title"), dialog.Title, "The process picker has its own title.");
            Check.Equal(Loc.T("ProcessPicker.Heading"), ((TextBlock)dialog.FindName("HeadingText")).Text, "The process picker has its own heading.");
            Check.True(refresh.Visibility == Visibility.Visible, "Processes change while the dialog is open, so the list can be refreshed.");
            Check.Equal(Loc.Tf("Msg.ProcessesFound", 2), ((TextBlock)dialog.FindName("StatusText")).Text, "The status reports how many programs are running.");

            search.Text = "game";
            Check.Equal(1, list.Items.Count, "Search narrows the list by name.");
            search.Text = @"c:\demo";
            Check.Equal(1, list.Items.Count, "Search also matches the path, in any case.");
            search.Text = "";
            list.SelectedIndex = 1;

            refresh.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Check.UntilAsync(() => list.Items.Count == 3 && list.IsEnabled, "Refresh did not reload the running programs.");
            Check.Equal(@"C:\Games\game.exe", (list.SelectedItem as InstalledAppInfo)?.ExecutablePath, "Refreshing keeps the selected program selected.");
        }
        finally { dialog.Close(); }

        var installed = Hidden(new AppPickerPrompt(() => null, AppPickerSource.InstalledApps, () => [new("Demo", @"C:\Demo\demo.exe")]));
        try
        {
            installed.Show();
            await Check.UntilAsync(() => ((ListBox)installed.FindName("AppList")).Items.Count == 1, "The installed-app picker did not load.");
            Check.Equal(Loc.T("AppPicker.Title"), installed.Title, "The installed-app picker keeps its title.");
            Check.True(((Button)installed.FindName("RefreshButton")).Visibility == Visibility.Collapsed, "Installed programs need no refresh button.");
        }
        finally { installed.Close(); }
    }

    private static async Task AddProcessCommandAsync()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var menuItems = PopupChecks.LoadWindowSource().Descendants(presentation + "MenuItem")
            .Select(item => (string?)item.Attribute("Command")).ToArray();
        var appIndex = Array.IndexOf(menuItems, "{Binding AddAppCommand}");
        Check.True(appIndex >= 0 && menuItems.ElementAtOrDefault(appIndex + 1) == "{Binding AddProcessCommand}",
            "The rule menu offers a process right after an application.");

        await using var fixture = new TestFixture(emptyRules: true);
        Check.Equal(0, fixture.Model.Apps.Count, "The check starts without rules.");
        fixture.DialogChoice = dialog =>
        {
            Check.True(dialog is AppPickerPrompt { Source: AppPickerSource.RunningProcesses }, "The command must open the picker of running processes.");
            ((AppPickerPrompt)dialog).SelectedExecutablePath = fixture.Executable;
            return true;
        };
        var saves = fixture.SaveCount;
        Check.True(fixture.Model.AddProcessCommand.CanExecute(null), "Adding a process is available when nothing is busy.");
        fixture.Model.AddProcessCommand.Execute(null);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

        Check.Equal(1, fixture.Model.Apps.Count, "The chosen process becomes one rule.");
        var rule = fixture.Model.Apps[0];
        Check.True(rule.EffectiveKind == RouteKind.File && string.Equals(rule.ExecutablePath, fixture.Executable, StringComparison.OrdinalIgnoreCase),
            "A process rule is an ordinary app rule tied to the executable file.");
        Check.True(fixture.SaveCount > saves, "The new rule is saved.");

        fixture.DialogChoice = dialog =>
        {
            Check.True(dialog is AppPickerPrompt { Source: AppPickerSource.InstalledApps }, "Adding an application still opens the installed-app picker.");
            return false;
        };
        fixture.Model.AddAppCommand.Execute(null);
        Check.Equal(1, fixture.Model.Apps.Count, "Cancelling a picker adds nothing.");
    }

    private static Task SettingsGitHubLinkAsync()
    {
        var opened = new List<string>();
        var original = AppLinks.Opener;
        AppLinks.Opener = url => { opened.Add(url); return true; };
        var settings = Hidden(new SettingsWindow(false, false, "ru", AppTheme.Orbit));
        try
        {
            settings.Show();
            settings.UpdateLayout();
            var link = (Button)settings.FindName("GitHubLink");
            Check.True(link is { IsVisible: true, ActualWidth: > 0 }, "Settings must show the link to the project.");
            Check.Equal(AppLinks.GitHub, link.ToolTip as string, "The link shows where it leads.");
            link.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check.True(opened.SequenceEqual(new[] { "https://github.com/fullspb/SelectiveVpn" }), "The link opens the project page on GitHub.");
        }
        finally
        {
            settings.Close();
            AppLinks.Opener = original;
        }
        return Task.CompletedTask;
    }

    private static T Hidden<T>(T window) where T : Window
    {
        window.ShowInTaskbar = false;
        window.Opacity = 0;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        return window;
    }
}
