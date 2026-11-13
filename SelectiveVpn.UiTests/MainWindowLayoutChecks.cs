using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using SelectiveVpn.Models;
using SelectiveVpn.Services;
using SelectiveVpn.ViewModels;

namespace SelectiveVpn.UiTests;

internal static class MainWindowLayoutChecks
{
    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
    [
        ("fixed main window keeps subscription controls visible and scrolls only the rule list in every theme", FixedWindowAcrossThemesAsync),
        ("opening the journal overlays the body without moving its controls", JournalOverlayAsync),
        ("lists disabled by a pending disconnect keep the dark theme instead of a white system background", BusyListsStayDarkAsync)
    ];

    private static async Task BusyListsStayDarkAsync()
    {
        await using var fixture = CreatePopulatedSubscription();
        await fixture.Model.ConnectFromUserAsync();
        var pending = new TaskCompletionSource<SelectiveVpn.Models.OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Engine.StopBehavior = _ => pending.Task;
        var window = PopupChecks.CreateIsolatedWindow(PopupChecks.LoadWindowSource(), fixture);
        try
        {
            window.Show();
            var disconnect = fixture.Model.DisconnectFromUserAsync();
            await Check.UntilAsync(() => fixture.Model.IsBusy, "The synthetic disconnect did not enter its pending state.");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var disabled = Descendants(window).OfType<ListBox>().Where(list => !list.IsEnabled).ToArray();
            Check.True(disabled.Contains(Named<ListBox>(window, "RulesList")) && disabled.Length >= 2,
                "The profile and rule lists should be disabled while a disconnect is pending.");
            // This harness does not load App.xaml, so scroll bars keep their light system look here.
            var scrollBarParts = disabled.SelectMany(Descendants).OfType<System.Windows.Controls.Primitives.ScrollBar>()
                .SelectMany(Descendants).ToHashSet();
            foreach (var border in disabled.SelectMany(Descendants).OfType<Border>().Where(border => !scrollBarParts.Contains(border)))
            {
                var light = border.Background is SolidColorBrush { Color: { A: > 200, R: > 230, G: > 230, B: > 230 } };
                Check.True(!light, "A disabled list must not paint an opaque light background over the dark theme. Element: "
                    + (string.IsNullOrEmpty(border.Name) ? "(unnamed border)" : border.Name));
            }
            pending.SetResult(new SelectiveVpn.Models.OperationResult(false, "Synthetic stop refusal"));
            await disconnect;
        }
        finally { window.Close(); }
    }

    private static async Task FixedWindowAcrossThemesAsync()
    {
        var source = PopupChecks.LoadWindowSource();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        Check.True(!source.Descendants(presentation + "ScrollViewer").Any(),
            "The authored main window must not contain an outer page ScrollViewer; list controls may provide their own internal scrolling.");
        await using var fixture = CreatePopulatedSubscription();
        await fixture.Model.ConnectFromUserAsync();
        fixture.Model.AddFolderCommand.Execute(null);
        Check.True(fixture.Model.HasPendingChanges,
            "The layout stress check should include a real pending-routing notice after editing the connected profile.");
        foreach (var theme in fixture.Model.ThemeOptions.Select(option => option.Code))
        {
            fixture.Model.SelectedThemeId = theme;
            var window = PopupChecks.CreateIsolatedWindow(source, fixture);
            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                Check.Equal(1140d, window.Width, "The main window should use the chosen fixed width. Theme: " + theme);
                Check.Equal(830d, window.Height, "The main window should use the chosen fixed height. Theme: " + theme);
                Check.Equal(ResizeMode.CanMinimize, window.ResizeMode, "The fixed layout should permit minimizing without resizing or maximizing. Theme: " + theme);
                var client = (FrameworkElement)window.Content;
                var body = Named<FrameworkElement>(window, "MainBody");
                var rules = Named<ListBox>(window, "RulesList");
                var add = Named<Button>(window, "AddTargetsButton");
                var servers = Named<Button>(window, "SubscriptionServersButton");
                var uptime = Named<TextBlock>(window, "ConnectionDurationText");
                var tabs = Named<TabControl>(window, "RoutingModeTabs");
                var notice = Descendants(body).OfType<Border>().Single(border =>
                    BindingOperations.GetBinding(border, UIElement.VisibilityProperty)?.Path?.Path == nameof(MainViewModel.ShowRoutingNotice));
                var reconnect = Descendants(notice).OfType<Button>().Single();
                CheckWithin(body, client, "The main body should fit inside the native client area. Theme: " + theme);
                CheckWithin(servers, body, "The subscription server action must remain fully visible without scrolling. Theme: " + theme);
                Check.True(servers.IsEnabled, "The subscription server action should remain usable after connecting. Theme: " + theme);
                CheckWithin(uptime, body, "The connected-time display must remain fully visible without scrolling. Theme: " + theme);
                CheckWithin(add, body, "The add-rule action must remain below the internally scrolling list. Theme: " + theme);
                CheckWithin(tabs, body, "Both routing tabs must remain inside the fixed body. Theme: " + theme);
                CheckWithin(notice, body, "The pending-routing notice must fit alongside a long rule list. Theme: " + theme);
                CheckWithin(reconnect, notice, "The pending-routing reconnect action must remain visible. Theme: " + theme);
                CheckWithin(rules, body, "Many rules must not expand the list beyond the body. Theme: " + theme);
                Check.Equal(61, rules.Items.Count, "The scrolling check must retain all 60 saved rules and the new pending folder. Theme: " + theme);

                rules.ApplyTemplate();
                rules.UpdateLayout();
                var scroll = Descendants(rules).OfType<ScrollViewer>().Single();
                Check.True(scroll.ScrollableHeight > 0 && scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible,
                    "A long rule list should provide its own visible vertical scrollbar. Theme: " + theme);
                var addBeforeScroll = Bounds(add, body);
                rules.ScrollIntoView(rules.Items[rules.Items.Count - 1]);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                rules.UpdateLayout();
                Check.True(scroll.VerticalOffset > 0, "The internal rule scrollbar must reach later saved rules. Theme: " + theme);
                Check.Equal(addBeforeScroll, Bounds(add, body), "Scrolling rules must leave the add action in place. Theme: " + theme);
                CheckWithin(servers, body, "Scrolling rules must not move subscription controls off screen. Theme: " + theme);
            }
            finally { window.Close(); }
        }
        AppTheme.Apply(AppTheme.Orbit);
    }

    private static async Task JournalOverlayAsync()
    {
        await using var fixture = CreatePopulatedSubscription();
        await fixture.Model.ConnectFromUserAsync();
        foreach (var theme in AppTheme.All)
        {
            fixture.Model.SelectedThemeId = theme;
            var window = PopupChecks.CreateIsolatedWindow(PopupChecks.LoadWindowSource(), fixture);
            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                var client = (FrameworkElement)window.Content;
                var body = Named<FrameworkElement>(window, "MainBody");
                var journal = Named<FrameworkElement>(window, "LogPanel");
                var servers = Named<Button>(window, "SubscriptionServersButton");
                var uptime = Named<TextBlock>(window, "ConnectionDurationText");
                var add = Named<Button>(window, "AddTargetsButton");
                Check.Equal(Visibility.Collapsed, journal.Visibility, "The journal should start closed. Theme: " + theme);
                var before = new[] { Bounds(body, client), Bounds(servers, client), Bounds(uptime, client), Bounds(add, client) };
                // The isolated source has no code-behind: this is the same visibility change
                // performed by the production journal toggle, without constructing MainWindow.
                journal.Visibility = Visibility.Visible;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                CheckWithin(journal, client, "The opened journal must fit inside the fixed client area. Theme: " + theme);
                Check.True(before.SequenceEqual(new[] { Bounds(body, client), Bounds(servers, client), Bounds(uptime, client), Bounds(add, client) }),
                    "Opening the journal must overlay content without shrinking the body or moving its controls. Theme: " + theme);
                journal.Visibility = Visibility.Collapsed;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check.True(before.SequenceEqual(new[] { Bounds(body, client), Bounds(servers, client), Bounds(uptime, client), Bounds(add, client) }),
                    "Closing the journal must preserve the fixed positions too. Theme: " + theme);
            }
            finally { window.Close(); }
        }
        AppTheme.Apply(AppTheme.Orbit);
    }

    private static TestFixture CreatePopulatedSubscription() => new(subscriptions: true, configureSettings: settings =>
    {
        foreach (var profile in settings.Profiles)
        {
            profile.ExceptListed = false;
            profile.IncludeTargets = Enumerable.Range(1, 60)
                .Select(index => new SavedTarget { Kind = "domain", Path = $"service-{index:D2}.example.invalid" }).ToList();
            profile.ExcludeTargets = [];
            profile.Targets = [];
        }
    });

    private static T Named<T>(Window window, string name) where T : FrameworkElement
        => window.FindName(name) as T ?? throw new InvalidOperationException("The actual layout is missing " + name + ".");

    private static Rect Bounds(FrameworkElement element, FrameworkElement host)
        => element.TransformToAncestor(host).TransformBounds(new Rect(element.RenderSize));

    private static void CheckWithin(FrameworkElement element, FrameworkElement host, string message)
    {
        var bounds = Bounds(element, host);
        Check.True(element.IsVisible && bounds.Width > 0 && bounds.Height > 0
                   && bounds.Left >= -.5 && bounds.Top >= -.5
                   && bounds.Right <= host.ActualWidth + .5 && bounds.Bottom <= host.ActualHeight + .5,
            message + " Bounds: " + bounds + "; viewport: " + host.ActualWidth + " × " + host.ActualHeight + ".");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
