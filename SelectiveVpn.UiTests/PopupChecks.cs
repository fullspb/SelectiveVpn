using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using SelectiveVpn.Models;
using SelectiveVpn.Services;
using SelectiveVpn.ViewModels;

namespace SelectiveVpn.UiTests;

internal static class PopupChecks
{
    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
    [
        ("colour schemes are chosen in settings, not in the main window", NoThemeSelectorAsync),
        ("both routing tabs remain visible and selectable when their lists are empty", EmptyModeSelectorAsync),
        ("routing tabs restore their own populated rule lists", PopulatedRoutingTabsAsync),
        ("routing tabs prevent mode changes during a pending connection", BusyRoutingTabsAsync),
        ("connection duration replaces the hero configuration button without duplicate rule actions", ConnectionDurationHeroAsync),
        ("profile, import and rule popups open in every theme", AllMenusAsync)
    ];

    internal static XDocument LoadWindowSource()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SelectiveVpn", "MainWindow.xaml")))
            root = root.Parent;
        Check.True(root is not null, "Run UI checks from the built repository so the actual window source is available.");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = XDocument.Load(Path.Combine(root!.FullName, "SelectiveVpn", "MainWindow.xaml"));
        source.Root!.Attribute(x + "Class")?.Remove();
        source.Root.Attribute("Icon")?.Remove();
        var namespaceMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attribute in source.Root.DescendantsAndSelf().Attributes().ToArray())
        {
            if (attribute.Name == x + "Shared" || attribute.Name.LocalName == "Click") attribute.Remove();
            else if (attribute.IsNamespaceDeclaration && attribute.Value.StartsWith("clr-namespace:SelectiveVpn", StringComparison.Ordinal)
                     && !attribute.Value.Contains(";assembly=", StringComparison.Ordinal))
            {
                var previous = attribute.Value;
                attribute.Value += ";assembly=SelectiveVpn";
                namespaceMap[previous] = attribute.Value;
            }
        }
        foreach (var element in source.Root.DescendantsAndSelf())
        {
            if (namespaceMap.TryGetValue(element.Name.NamespaceName, out var qualified))
                element.Name = XName.Get(element.Name.LocalName, qualified);
            foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).ToArray())
            {
                if (!namespaceMap.TryGetValue(attribute.Name.NamespaceName, out var attributeNamespace))
                    continue;
                element.Add(new XAttribute(XName.Get(attribute.Name.LocalName, attributeNamespace), attribute.Value));
                attribute.Remove();
            }
        }
        return source;
    }

    internal static Window CreateIsolatedWindow(XDocument source, TestFixture fixture)
    {
        var window = (Window)XamlReader.Parse(source.ToString(), new ParserContext
        {
            BaseUri = new Uri("pack://application:,,,/SelectiveVpn;component/")
        });
        window.DataContext = fixture.Model;
        window.ShowInTaskbar = false;
        window.Opacity = 0;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        return window;
    }

    private static async Task NoThemeSelectorAsync()
    {
        var source = LoadWindowSource();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        Check.True(!source.Descendants(presentation + "Button")
                .Any(button => (string?)button.Attribute("Content") == "{Binding ThemeDisplayName}"),
            "The old header theme button must stay removed.");
        await using var fixture = new TestFixture();
        var window = CreateIsolatedWindow(source, fixture);
        try
        {
            window.Show();
            window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Check.True(!Descendants(window).OfType<ComboBox>().Any(combo =>
                    BindingOperations.GetBinding(combo, Selector.SelectedValueProperty)?.Path?.Path == nameof(fixture.Model.SelectedThemeId)),
                "Colour schemes live in settings: the main window must not offer a theme dropdown.");
            Check.Equal(AppTheme.All.Length, fixture.Model.ThemeOptions.Count, "The view model should know every colour scheme.");
            Check.Equal(AppTheme.Orbit, fixture.Model.ThemeOptions[0].Code, "Orbit stays the first, default colour scheme.");
            Check.Equal(0, fixture.DialogCount, "Showing the main window must not open settings or any other dialog.");
        }
        finally
        {
            window.Close();
            AppTheme.Apply(AppTheme.Orbit);
        }
    }

    private static async Task AllMenusAsync()
    {
        var source = LoadWindowSource();

        foreach (var theme in ThemeOption.CreateOptions().Select(option => option.Code))
        {
            await using var fixture = new TestFixture();
            fixture.Model.SelectedThemeId = theme;
            var window = CreateIsolatedWindow(source, fixture);
            try
            {
                window.Show();
                window.UpdateLayout();
                var buttons = Descendants(window).OfType<Button>().Where(button => button.ContextMenu is not null).ToArray();
                Check.True(buttons.Length >= 4, "The actual window must include its profile, import, and remaining routing menu triggers.");
                var separators = 0;
                var submenus = 0;
                foreach (var button in buttons)
                {
                    var menu = button.ContextMenu!;
                    menu.DataContext = fixture.Model;
                    menu.PlacementTarget = button;
                    menu.Placement = PlacementMode.Bottom;
                    menu.Opacity = 0;
                    menu.StaysOpen = true;
                    var opened = false;
                    RoutedEventHandler recordOpening = (_, _) => opened = true;
                    menu.Opened += recordOpening;
                    try
                    {
                        menu.IsOpen = true;
                        menu.ApplyTemplate();
                        menu.Measure(new Size(400, 1000));
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        menu.UpdateLayout();
                        Check.True(opened && menu.ActualWidth > 0,
                            "The popup must actually generate its native window and item containers. Theme: " + theme
                            + "; trigger: " + button.ToolTip + "; opened: " + opened + "; width: " + menu.ActualWidth);
                        separators += menu.Items.OfType<Separator>().Count();
                        foreach (var submenu in menu.Items.OfType<MenuItem>().Where(item => item.HasItems))
                        {
                            if (!menu.IsOpen) menu.IsOpen = true;
                            submenu.IsSubmenuOpen = true;
                            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            Check.True(submenu.IsSubmenuOpen, "The advanced submenu should open.");
                            submenus++;
                            submenu.IsSubmenuOpen = false;
                        }
                    }
                    finally { menu.Opened -= recordOpening; menu.IsOpen = false; }
                }
                Check.True(separators > 0 && submenus > 0, "The ellipsis popup separator and advanced submenu must both be covered.");
            }
            finally { window.Close(); }
        }
        AppTheme.Apply(AppTheme.Dark);
    }

    private static async Task EmptyModeSelectorAsync()
    {
        await using var fixture = new TestFixture(emptyRules: true);
        var window = CreateIsolatedWindow(LoadWindowSource(), fixture);
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var selector = GetRoutingTabs(window);
            var tabs = CheckVisibleRoutingTabs(selector);
            Check.True(!Descendants(window).OfType<ComboBox>().Any(combo =>
                BindingOperations.GetBinding(combo, Selector.SelectedIndexProperty)?.Path?.Path == nameof(MainViewModel.RoutingModeIndex)),
                "Routing modes must be exposed as separate tabs rather than a dropdown.");
            SelectTab(tabs[1]);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check.True(fixture.Model.ExceptListed && !fixture.Model.HasApps,
                "Choosing the exclusion mode should load its own empty list.");
            Check.True(selector.IsVisible && selector.IsEnabled,
                "An empty exclusion list must not hide the tabs and trap the user in that mode.");
            CheckVisibleRoutingTabs(selector);
            Check.Equal(Loc.T("Orbit.Direct"), fixture.Model.ListedRouteLabel,
                "An empty exclusion card must describe direct exceptions.");
            SelectTab(tabs[0]);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check.True(!fixture.Model.ExceptListed && selector.IsVisible,
                "The user must be able to return to the include list without adding a rule.");
        }
        finally { window.Close(); }
    }

    private static async Task ConnectionDurationHeroAsync()
    {
        var time = new ManualConnectionTimeProvider();
        await using var fixture = new TestFixture(timeProvider: time);
        var window = CreateIsolatedWindow(LoadWindowSource(), fixture);
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var textBlocks = Descendants(window).OfType<TextBlock>().ToArray();
            var duration = textBlocks.Single(text =>
                BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path?.Path == nameof(MainViewModel.ConnectionDuration));
            Check.Equal("ConnectionDurationText", duration.Name, "The connection hero must expose its elapsed-time binding.");
            Check.True(duration.IsVisible && duration.Text == "00:00:00",
                "The hero should show zero connected time while disconnected.");
            var incoming = textBlocks.Single(text =>
                BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path?.Path == nameof(MainViewModel.IncomingTraffic));
            var durationAt = duration.TranslatePoint(default, window);
            var incomingAt = incoming.TranslatePoint(default, window);
            Check.True(durationAt.X < incomingAt.X && Math.Abs(durationAt.Y - incomingAt.Y) < 24,
                "Connection duration should sit beside the traffic counters in the same row of the connection panel.");

            var buttons = Descendants(window).OfType<Button>().ToArray();
            Check.True(!buttons.Any(button =>
                BindingOperations.GetBinding(button, Button.CommandProperty)?.Path?.Path == nameof(MainViewModel.EditConfigCommand)),
                "The former hero configuration button must be removed.");
            Check.True(buttons.SelectMany(button => button.ContextMenu?.Items.OfType<MenuItem>() ?? [])
                .Any(item => BindingOperations.GetBinding(item, MenuItem.CommandProperty)?.Path?.Path == nameof(MainViewModel.EditConfigCommand)),
                "Configuration editing must remain available in the profile menu.");
            Check.Equal(1, buttons.Count(button => button.ContextMenu?.Items.OfType<MenuItem>().Any(item =>
                BindingOperations.GetBinding(item, MenuItem.CommandProperty)?.Path?.Path == nameof(MainViewModel.AddAddressListCommand)) == true),
                "Only the lower add-rule button should open the rule menu after removing the duplicate top plus.");

            await fixture.Model.ConnectFromUserAsync();
            time.Advance(TimeSpan.FromSeconds(125));
            await Check.UntilAsync(() => duration.Text == "00:02:05",
                "The visible duration binding must update itself while connected, without a traffic refresh.", milliseconds: 4000);
            await fixture.Model.DisconnectFromUserAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check.Equal("00:00:00", duration.Text, "The hero duration must clear when the connection actually stops.");
        }
        finally { window.Close(); }
    }

    private static async Task PopulatedRoutingTabsAsync()
    {
        await using var fixture = new TestFixture();
        var window = CreateIsolatedWindow(LoadWindowSource(), fixture);
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var selector = GetRoutingTabs(window);
            var tabs = CheckVisibleRoutingTabs(selector);
            Check.True(tabs[0].IsSelected && fixture.Model.Apps.Single().ExecutablePath == fixture.Executable,
                "The include tab must initially show the profile's saved application rule.");

            SelectTab(tabs[1]);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check.True(fixture.Model.ExceptListed && !fixture.Model.HasApps,
                "Selecting the exclusion tab must open its independent empty list.");
            fixture.Model.AddFolderCommand.Execute(null);
            Check.True(fixture.Model.Apps.Single().ExecutablePath == fixture.Folder,
                "Adding a rule from the selected exclusion tab must add it to that tab's list.");

            SelectTab(tabs[0]);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check.True(!fixture.Model.ExceptListed && fixture.Model.Apps.Count == 1
                && fixture.Model.Apps.Single().ExecutablePath == fixture.Executable,
                "Returning to the include tab must restore its application without the exclusion folder.");
            SelectTab(tabs[1]);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check.True(fixture.Model.ExceptListed && fixture.Model.Apps.Count == 1
                && fixture.Model.Apps.Single().ExecutablePath == fixture.Folder,
                "Returning to the exclusion tab must restore its folder without the include application.");

            fixture.Model.RoutingModeIndex = 0;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check.True(tabs[0].IsSelected && !tabs[1].IsSelected,
                "The native tab selection must follow changes made through the real view model.");
            CheckVisibleRoutingTabs(selector);
        }
        finally { window.Close(); }
    }

    private static async Task BusyRoutingTabsAsync()
    {
        await using var fixture = new TestFixture();
        var window = CreateIsolatedWindow(LoadWindowSource(), fixture);
        Task? connecting = null;
        fixture.Engine.StartBehavior = async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return OperationResult.Ok("Unreachable after test cancellation");
        };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var selector = GetRoutingTabs(window);
            var tabs = CheckVisibleRoutingTabs(selector);
            connecting = fixture.Model.ConnectFromUserAsync();
            await Check.UntilAsync(() => fixture.Model.IsBusy, "The isolated connection did not enter its pending state.");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check.True(!selector.IsEnabled && tabs.All(tab => !tab.IsEnabled),
                "Both routing tabs must be disabled while a connection is pending.");
            var blocked = false;
            try { SelectTab(tabs[1]); }
            catch (ElementNotEnabledException) { blocked = true; }
            Check.True(blocked && tabs[0].IsSelected && !fixture.Model.ExceptListed,
                "Native accessibility selection must not change a disabled routing tab.");

            await fixture.Model.DisconnectFromUserAsync();
            await connecting;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check.True(selector.IsEnabled && tabs.All(tab => tab.IsEnabled),
                "Cancelling the pending connection must make both routing tabs available again.");
            SelectTab(tabs[1]);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check.True(fixture.Model.ExceptListed && tabs[1].IsSelected,
                "The exclusion tab must work after cancelling a pending connection.");
        }
        finally
        {
            if (connecting is not null)
            {
                await fixture.Model.DisconnectFromUserAsync();
                await connecting;
            }
            window.Close();
        }
    }

    private static TabControl GetRoutingTabs(Window window)
    {
        var tabs = Descendants(window).OfType<TabControl>().Single(control =>
            BindingOperations.GetBinding(control, Selector.SelectedIndexProperty)?.Path?.Path == nameof(MainViewModel.RoutingModeIndex));
        Check.Equal("RoutingModeTabs", tabs.Name, "The real window should expose its native routing tabs by name.");
        return tabs;
    }

    private static TabItem[] CheckVisibleRoutingTabs(TabControl selector)
    {
        selector.ApplyTemplate();
        selector.UpdateLayout();
        var tabs = selector.Items.OfType<TabItem>().ToArray();
        Check.Equal(2, tabs.Length, "Both routing modes must be represented by native tab items.");
        var titles = new[] { Loc.T("Orbit.ModeInclude"), Loc.T("Orbit.ModeExclude") };
        for (var index = 0; index < tabs.Length; index++)
        {
            var tab = tabs[index];
            tab.ApplyTemplate();
            tab.UpdateLayout();
            Check.True(tab.IsVisible && tab.ActualWidth > 0 && tab.ActualHeight > 0,
                "Every routing tab must remain visibly present, including the inactive mode.");
            Check.True(Descendants(tab).OfType<TextBlock>().Any(text => text.IsVisible && text.Text == titles[index]),
                "The routing tab must render its complete localized mode title: " + titles[index]);
        }
        return tabs;
    }

    private static void SelectTab(TabItem tab)
    {
        var owner = ItemsControl.ItemsControlFromItemContainer(tab) as TabControl
            ?? throw new InvalidOperationException("The routing tab must belong to its native TabControl.");
        var ownerPeer = UIElementAutomationPeer.CreatePeerForElement(owner) as TabControlAutomationPeer
            ?? new TabControlAutomationPeer(owner);
        var peer = new TabItemAutomationPeer(tab, ownerPeer);
        var selection = peer.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider
            ?? throw new InvalidOperationException("The native routing tab must expose its selection automation pattern.");
        selection.Select();
    }

    private static void CheckRenderedCaption(ComboBox selector, string expectedTitle)
    {
        selector.ApplyTemplate();
        selector.UpdateLayout();
        Check.True(Descendants(selector).OfType<TextBlock>().Any(text => text.IsVisible && text.Text == expectedTitle),
            "The closed theme dropdown must render the localized theme title. Expected: " + expectedTitle
            + "; SelectedItem: " + selector.SelectedItem
            + "; rendered: " + string.Join(" | ", Descendants(selector).OfType<TextBlock>().Select(text => text.Text)));
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
