using System.Windows;
using SelectiveVpn.Services;

namespace SelectiveVpn;

public partial class SettingsWindow : Window
{
    public SettingsWindow(bool launchWithWindows, bool connectOnLaunch, string language)
    {
        InitializeComponent();
        LaunchBox.IsChecked = launchWithWindows;
        ConnectBox.IsChecked = connectOnLaunch;
        LanguageBox.ItemsSource = AppLanguage.Options
            .Select(option => new LanguageChoice(option.Code, Loc.T(option.TitleKey)))
            .ToList();
        LanguageBox.SelectedValue = AppLanguage.Normalize(language);
        if (LanguageBox.SelectedItem is null && LanguageBox.Items.Count > 0)
            LanguageBox.SelectedIndex = 0;

        SourceInitialized += (_, _) => TitleBarTheme.ApplyFromResources(this);
        Loaded += (_, _) => TitleBarTheme.ApplyFromResources(this);
    }

    public bool LaunchWithWindows => LaunchBox.IsChecked == true;

    public bool ConnectOnLaunch => ConnectBox.IsChecked == true;

    public string SelectedLanguage => LanguageBox.SelectedValue as string ?? AppLanguage.Auto;

    private void SaveClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class LanguageChoice
    {
        public LanguageChoice(string code, string title)
        {
            Code = code;
            Title = title;
        }

        public string Code { get; }

        public string Title { get; }

        public override string ToString() => Title;
    }
}
