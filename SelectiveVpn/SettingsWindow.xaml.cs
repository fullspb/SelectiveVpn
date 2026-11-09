using System.Windows;
using SelectiveVpn.Services;

namespace SelectiveVpn;

public partial class SettingsWindow : Window
{
    public SettingsWindow(bool launchWithWindows, bool connectOnLaunch)
    {
        InitializeComponent();
        LaunchBox.IsChecked = launchWithWindows;
        ConnectBox.IsChecked = connectOnLaunch;
        SourceInitialized += (_, _) => TitleBarTheme.ApplyFromResources(this);
        Loaded += (_, _) => TitleBarTheme.ApplyFromResources(this);
    }

    public bool LaunchWithWindows => LaunchBox.IsChecked == true;

    public bool ConnectOnLaunch => ConnectBox.IsChecked == true;

    private void SaveClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
