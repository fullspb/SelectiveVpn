using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SelectiveVpn.Desktop.Models;
using SelectiveVpn.Desktop.Services;

namespace SelectiveVpn.Desktop.Views;

public enum AppPickerSource
{
    DesktopApps,
    RunningProcesses
}

public partial class AppPickerWindow : Window
{
    private readonly Func<IReadOnlyList<ProcessAppInfo>> _load;
    private IReadOnlyList<ProcessAppInfo> _apps = [];
    private bool _loading;

    public AppPickerWindow()
        : this(AppPickerSource.RunningProcesses)
    {
    }

    public AppPickerWindow(AppPickerSource source, Func<IReadOnlyList<ProcessAppInfo>>? load = null)
    {
        InitializeComponent();
        Source = source;
        _load = load ?? (source == AppPickerSource.RunningProcesses
            ? LinuxRunningProcessCatalog.Load
            : LinuxDesktopAppCatalog.Load);

        if (source == AppPickerSource.RunningProcesses)
        {
            Title = "Add process";
            HeadingText.Text = "Choose a running process";
            HintText.Text = "Select a program that is running now. The rule binds to its file and stays after restart.";
            RefreshButton.IsVisible = true;
        }
        else
        {
            Title = "Add application";
            HeadingText.Text = "Choose a desktop application";
            HintText.Text = "Applications come from .desktop entries. The rule uses the resolved binary path.";
        }

        Opened += async (_, _) => await LoadAppsAsync().ConfigureAwait(true);
    }

    public AppPickerSource Source { get; }

    public string? SelectedExecutablePath { get; private set; }

    private async Task LoadAppsAsync()
    {
        if (_loading)
            return;

        _loading = true;
        var previous = (AppList.SelectedItem as ProcessAppInfo)?.ExecutablePath;
        EmptyText.IsVisible = true;
        EmptyText.Text = Source == AppPickerSource.RunningProcesses
            ? "Collecting processes…"
            : "Collecting applications…";
        AppList.IsEnabled = false;
        AddButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;

        IReadOnlyList<ProcessAppInfo> apps = [];
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
            EmptyText.Text = "Could not load the list.";
            StatusText.Text = "List unavailable.";
            return;
        }

        StatusText.Text = _apps.Count > 0
            ? (Source == AppPickerSource.RunningProcesses
                ? $"Running programs: {_apps.Count}."
                : $"Applications found: {_apps.Count}.")
            : (Source == AppPickerSource.RunningProcesses
                ? "No readable process paths."
                : "No desktop applications with a resolvable Exec=.");
    }

    private void ApplyFilter(string? query, string? keepSelected = null)
    {
        var text = query?.Trim() ?? "";
        IEnumerable<ProcessAppInfo> filtered = _apps;
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
            EmptyText.IsVisible = true;
            EmptyText.Text = _apps.Count == 0
                ? (Source == AppPickerSource.RunningProcesses ? "No processes." : "No applications.")
                : "Nothing matches the search.";
        }
        else
        {
            EmptyText.IsVisible = false;
            AppList.SelectedItem = list.FirstOrDefault(app =>
                string.Equals(app.ExecutablePath, keepSelected, StringComparison.Ordinal));
            if (AppList.SelectedItem is null)
                AppList.SelectedIndex = 0;
        }
    }

    private async void RefreshClick(object? sender, RoutedEventArgs e)
        => await LoadAppsAsync().ConfigureAwait(true);

    private void SearchBox_OnTextChanged(object? sender, TextChangedEventArgs e)
        => ApplyFilter(SearchBox.Text, (AppList.SelectedItem as ProcessAppInfo)?.ExecutablePath);

    private void AddClick(object? sender, RoutedEventArgs e) => AcceptSelection();

    private void AppList_OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
        => AcceptSelection();

    private void AcceptSelection()
    {
        if (AppList.SelectedItem is not ProcessAppInfo app)
            return;

        SelectedExecutablePath = app.ExecutablePath;
        Close(true);
    }

    private async void BrowseClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose executable",
            AllowMultiple = false
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        SelectedExecutablePath = Path.GetFullPath(path);
        Close(true);
    }

    private void CancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
