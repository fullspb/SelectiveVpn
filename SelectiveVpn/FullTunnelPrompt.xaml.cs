using System.Windows;
using SelectiveVpn.Services;

namespace SelectiveVpn;

public partial class FullTunnelPrompt : Window
{
    public FullTunnelPrompt()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => TitleBarTheme.ApplyFromResources(this);
        Loaded += (_, _) => TitleBarTheme.ApplyFromResources(this);
    }

    public bool DontAskAgain => RememberBox.IsChecked == true;

    private void ContinueClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
