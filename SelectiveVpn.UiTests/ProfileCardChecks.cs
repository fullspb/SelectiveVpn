using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using SelectiveVpn.Services;
using SelectiveVpn.ViewModels;

namespace SelectiveVpn.UiTests;

internal static class ProfileCardChecks
{
    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
    [
        ("each profile card carries its own actions button under the avatar", CardMenuButtonAsync),
        ("a profile can be renamed, the name is kept, and an empty name restores the original", RenameProfileAsync),
        ("profile cards tell plain WireGuard from AmneziaWG", ProtocolLabelAsync)
    ];

    private static async Task CardMenuButtonAsync()
    {
        var source = PopupChecks.LoadWindowSource();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var menuButtons = source.Descendants(presentation + "Button")
            .Where(button => (string?)button.Attribute("ContextMenu") == "{StaticResource ProfileMenu}").ToList();
        Check.True(menuButtons.Count == 1 && (string?)menuButtons[0].Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "ProfileMenuButton",
            "The profile actions open only from the button inside each card, not from the list header.");
        var menu = source.Descendants(presentation + "ContextMenu").Single(item => (string?)item.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "ProfileMenu");
        Check.Equal("{Binding RenameProfileCommand}", (string?)menu.Elements(presentation + "MenuItem").First().Attribute("Command"),
            "Renaming is the first profile action.");

        await using var fixture = new TestFixture();
        var window = PopupChecks.CreateIsolatedWindow(source, fixture);
        try
        {
            window.Show();
            window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            var buttons = Descendants(window).OfType<Button>().Where(button => button.Name == "ProfileMenuButton").ToList();
            Check.Equal(fixture.Model.Profiles.Count, buttons.Count, "Every profile card has its own actions button.");
            foreach (var button in buttons)
            {
                Check.True(button is { IsVisible: true, ActualWidth: > 0 } && button.Tag is VpnProfileOption, "The button is shown and knows its profile.");
                var card = (FrameworkElement)VisualTreeHelper.GetParent(button);
                var avatar = card is Panel panel ? panel.Children.OfType<Border>().First() : null;
                Check.True(avatar is not null && button.TranslatePoint(new Point(0, 0), avatar).Y >= avatar.ActualHeight,
                    "The actions button sits under the profile letter.");
            }
        }
        finally { window.Close(); }
    }

    private static async Task RenameProfileAsync()
    {
        await using var fixture = new TestFixture();
        var model = fixture.Model;
        var alpha = model.Profiles.Single(profile => profile.ConfigPath == fixture.AlphaPath);
        model.SelectedProfile = alpha;
        fixture.DialogChoice = dialog =>
        {
            var prompt = (RenameProfilePrompt)dialog;
            prompt.ResultName = "  Home\trouter  ";
            return true;
        };
        Check.True(model.RenameProfileCommand.CanExecute(null), "Renaming is available for the selected profile.");
        model.RenameProfileCommand.Execute(null);
        Check.Equal("Home router", alpha.Name, "The new name is cleaned to one line.");
        Check.Equal("H", alpha.Initial, "The letter follows the new name.");
        Check.Equal("Home router", model.ProfileName, "The rules card uses the new name.");
        Check.True(File.Exists(fixture.AlphaPath), "Renaming never touches the configuration file.");

        await fixture.ReopenFromSavedSettingsAsync();
        model = fixture.Model;
        alpha = model.Profiles.Single(profile => profile.ConfigPath == fixture.AlphaPath);
        Check.Equal("Home router", alpha.Name, "The name is saved with the profile.");
        model.SelectedProfile = alpha;

        fixture.DialogChoice = dialog =>
        {
            ((RenameProfilePrompt)dialog).ResultName = "";
            return true;
        };
        model.RenameProfileCommand.Execute(null);
        Check.Equal(Path.GetFileNameWithoutExtension(fixture.AlphaPath), alpha.Name, "An empty name brings back the file name.");
        Check.True(!alpha.HasCustomName, "Nothing custom is left to save.");

        fixture.DialogChoice = _ => false;
        model.RenameProfileCommand.Execute(null);
        Check.True(!alpha.HasCustomName, "Cancelling changes nothing.");
        Check.Equal(new string('x', RenameProfilePrompt.MaxNameLength), RenameProfilePrompt.Clean(new string('x', 200)), "Names are kept short.");
    }

    private static async Task ProtocolLabelAsync()
    {
        await using var fixture = new TestFixture();
        Check.Equal("WireGuard", fixture.Model.Profiles.Single(profile => profile.ConfigPath == fixture.AlphaPath).KindLabel,
            "A config without obfuscation settings is plain WireGuard.");
        var amnezia = Path.Combine(Path.GetDirectoryName(fixture.AlphaPath)!, "Amnezia.conf");
        File.WriteAllText(amnezia, File.ReadAllText(fixture.AlphaPath).Replace("Address = 10.66.0.5/32\n", "Address = 10.66.0.5/32\nJc = 4\nJmin = 40\nJmax = 70\nS1 = 15\nS2 = 30\n"));
        Check.Equal("AmneziaWG", new VpnProfileOption(amnezia).KindLabel, "Jc, S1 and similar settings mean AmneziaWG.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }
}
