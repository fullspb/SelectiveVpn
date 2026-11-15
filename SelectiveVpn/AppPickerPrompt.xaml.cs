using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SelectiveVpn.Models;
using SelectiveVpn.Services;

namespace SelectiveVpn;

/// <summary>Where the picker takes its list from.</summary>
public enum AppPickerSource
{
    InstalledApps,
    RunningProcesses
}

public partial class AppPickerPrompt : Window
{
    private readonly Func<string?> _pickExecutable;
    private readonly Func<IReadOnlyList<InstalledAppInfo>> _load;
    private IReadOnlyList<InstalledAppInfo> _apps = [];
    private bool _loaded;
    private bool _loading;

    public AppPickerPrompt(Func<string?> pickExecutable)
        : this(pickExecutable, AppPickerSource.InstalledApps)
    {
    }

    internal AppPickerPrompt(
        Func<string?> pickExecutable,
        AppPickerSource source,
        Func<IReadOnlyList<InstalledAppInfo>>? load = null)
    {
        InitializeComponent();
        _pickExecutable = pickExecutable;
        Source = source;
        _load = load ?? (source == AppPickerSource.RunningProcesses ? RunningProcessCatalog.Load : InstalledAppCatalog.Load);
        if (source == AppPickerSource.RunningProcesses)
        {
            SetResourceReference(TitleProperty, "ProcessPicker.Title");
            HeadingText.SetResourceReference(TextBlock.TextProperty, "ProcessPicker.Heading");
            HintText.SetResourceReference(TextBlock.TextProperty, "ProcessPicker.Hint");
            // Processes come and go while the dialog is open; installed programs do not.
            RefreshButton.Visibility = Visibility.Visible;
        }

        SourceInitialized += (_, _) => TitleBarTheme.ApplyFromResources(this);
        Loaded += OnLoaded;
    }

    public AppPickerSource Source { get; }

    public string? SelectedExecutablePath { get; internal set; }

    private bool Running => Source == AppPickerSource.RunningProcesses;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        TitleBarTheme.ApplyFromResources(this);
        if (_loaded)
            return;

        _loaded = true;
        SearchBox.Focus();
        await LoadAppsAsync().ConfigureAwait(true);
    }

    internal async Task LoadAppsAsync()
    {
        if (_loading)
            return;

        _loading = true;
        var previous = (AppList.SelectedItem as InstalledAppInfo)?.ExecutablePath;
        EmptyText.Visibility = Visibility.Visible;
        EmptyText.Text = Loc.T(Running ? "Msg.ProcessesLoading" : "Msg.AppsLoading");
        AppList.IsEnabled = false;
        AddButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;

        IReadOnlyList<InstalledAppInfo> apps = [];
        var failed = false;
        try
        {
            apps = await Task.Run(_load).ConfigureAwait(true);
        }
        catch (Exception)
        {
            failed = true;
        }

        _apps = apps;
        _loading = false;
        AppList.IsEnabled = true;
        AddButton.IsEnabled = true;
        RefreshButton.IsEnabled = true;
        ApplyFilter(SearchBox.Text, previous);
        if (failed)
        {
            EmptyText.Text = Loc.T("Msg.AppsLoadFail");
            SetStatus(Loc.T("Msg.AppsUnavailable"));
            return;
        }

        SetStatus(_apps.Count > 0
            ? Loc.Tf(Running ? "Msg.ProcessesFound" : "Msg.AppsFound", _apps.Count)
            : Loc.T(Running ? "Msg.ProcessesEmpty" : "Msg.AppsEmpty"));
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyFilter(SearchBox.Text, (AppList.SelectedItem as InstalledAppInfo)?.ExecutablePath);
    }

    private void ApplyFilter(string? query, string? keepSelected = null)
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
                ? Loc.T(Running ? "Msg.ProcessesEmpty" : "Msg.AppsNone")
                : Loc.T("Msg.AppsNoMatch");
        }
        else
        {
            EmptyText.Visibility = Visibility.Collapsed;
            AppList.SelectedItem = list.FirstOrDefault(app =>
                string.Equals(app.ExecutablePath, keepSelected, StringComparison.OrdinalIgnoreCase));
            if (AppList.SelectedItem is null)
                AppList.SelectedIndex = 0;
        }
    }

    private async void RefreshClick(object sender, RoutedEventArgs e)
        => await LoadAppsAsync().ConfigureAwait(true);

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
            SetStatus(Loc.T(Running ? "Msg.ProcessesPick" : "Msg.AppsPick"));
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
