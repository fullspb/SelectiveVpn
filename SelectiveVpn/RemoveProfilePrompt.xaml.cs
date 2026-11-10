using System.Windows;
using SelectiveVpn.Services;

namespace SelectiveVpn;

public partial class RemoveProfilePrompt : Window
{
    public RemoveProfilePrompt(string profileName)
    {
        InitializeComponent();
        HintText.Text = Loc.Tf("RemoveProfile.Hint", profileName);
        SourceInitialized += (_, _) => TitleBarTheme.ApplyFromResources(this);
        Loaded += (_, _) => TitleBarTheme.ApplyFromResources(this);
    }

    private void RemoveClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
