using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SelectiveVpn.Models;
using SelectiveVpn.Services;

namespace SelectiveVpn;

public partial class AppPickerPrompt : Window
{
    private readonly Func<string?> _pickExecutable;
    private IReadOnlyList<InstalledAppInfo> _apps = [];
    private bool _loaded;

    public AppPickerPrompt(Func<string?> pickExecutable)
    {
        InitializeComponent();
        _pickExecutable = pickExecutable;
        SourceInitialized += (_, _) => TitleBarTheme.ApplyFromResources(this);
        Loaded += OnLoaded;
    }

    public string? SelectedExecutablePath { get; private set; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        TitleBarTheme.ApplyFromResources(this);
        if (_loaded)
            return;

        _loaded = true;
        SearchBox.Focus();
        await LoadAppsAsync().ConfigureAwait(true);
    }

    private async Task LoadAppsAsync()
    {
        EmptyText.Visibility = Visibility.Visible;
        EmptyText.Text = Loc.T("Msg.AppsLoading");
        AppList.IsEnabled = false;
        AddButton.IsEnabled = false;

        IReadOnlyList<InstalledAppInfo> apps = [];
        try
        {
            apps = await Task.Run(InstalledAppCatalog.Load).ConfigureAwait(true);
        }
        catch (Exception)
        {
            EmptyText.Text = Loc.T("Msg.AppsLoadFail");
            SetStatus(Loc.T("Msg.AppsUnavailable"));
        }

        _apps = apps;
        AppList.IsEnabled = true;
        AddButton.IsEnabled = true;
        ApplyFilter(SearchBox.Text);
        SetStatus(_apps.Count > 0
            ? Loc.Tf("Msg.AppsFound", _apps.Count)
            : Loc.T("Msg.AppsEmpty"));
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyFilter(SearchBox.Text);
    }

    private void ApplyFilter(string? query)
    {
        var text = query?.Trim() ?? "";
        IEnumerable<InstalledAppInfo> filtered = _apps;
        if (text.Length > 0)
        {
            filtered = _apps.Where(app =>
                app.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                || app.ExecutablePath.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();
        AppList.ItemsSource = list;
        if (list.Count == 0)
        {
            EmptyText.Visibility = Visibility.Visible;
            EmptyText.Text = _apps.Count == 0
                ? Loc.T("Msg.AppsNone")
                : Loc.T("Msg.AppsNoMatch");
        }
        else
        {
            EmptyText.Visibility = Visibility.Collapsed;
            if (AppList.SelectedItem is null)
                AppList.SelectedIndex = 0;
        }
    }

    private void BrowseExeClick(object sender, RoutedEventArgs e)
    {
        var path = _pickExecutable();
        if (string.IsNullOrWhiteSpace(path))
            return;

        SelectedExecutablePath = path;
        DialogResult = true;
    }

    private void AddClick(object sender, RoutedEventArgs e)
        => AcceptSelection();

    private void AppList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        => AcceptSelection();

    private void AcceptSelection()
    {
        if (AppList.SelectedItem is not InstalledAppInfo app)
        {
            SetStatus(Loc.T("Msg.AppsPick"));
            return;
        }

        SelectedExecutablePath = app.ExecutablePath;
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }
}
