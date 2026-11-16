using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SelectiveVpn.Desktop.Services;
using SelectiveVpn.Engine;

namespace SelectiveVpn.Desktop.Views;

public partial class SubscriptionImportWindow : Window
{
    private bool _busy;
    private CancellationTokenSource? _importCts;

    public SubscriptionImportWindow()
    {
        InitializeComponent();
        Opened += (_, _) => UrlBox.Focus();
    }

    public string? SavedPath { get; private set; }

    public string ResultMessage { get; private set; } = "";

    private async void ImportClick(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (!ConfigSubscriptionImporter.TryValidateInput(UrlBox.Text, out var urlError))
        {
            ShowError(urlError);
            return;
        }

        var input = UrlBox.Text ?? "";
        await RunImportAsync(() => ConfigSubscriptionImporter.ImportAsync(
            input,
            NameBox.Text,
            hardwareId: DeviceIdentity.GetHardwareId(AppPaths.RootDirectory),
            profilesDirectory: Path.Combine(AppPaths.RootDirectory, "profiles"),
            cancellationToken: _importCts!.Token)).ConfigureAwait(true);
    }

    private async void FileClick(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import subscription or config file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Subscription / Clash / text")
                {
                    Patterns = ["*.sub.json", "*.json", "*.yaml", "*.yml", "*.txt", "*.conf", "*.awg"]
                },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        // Existing SelectiveVPN subscription JSON — register as-is.
        if (SubscriptionProfile.IsSubscriptionPath(path))
        {
            if (!SubscriptionProfile.TryRead(path, out _, out var error))
            {
                ShowError(error);
                return;
            }

            SavedPath = Path.GetFullPath(path);
            ResultMessage = "Subscription file selected.";
            Close(true);
            return;
        }

        string payload;
        try
        {
            payload = await File.ReadAllTextAsync(path).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Could not read the selected file.");
            return;
        }

        var displayName = string.IsNullOrWhiteSpace(NameBox.Text)
            ? Path.GetFileNameWithoutExtension(path)
            : NameBox.Text;

        await RunImportAsync(() => Task.FromResult(ConfigSubscriptionImporter.ImportFromPayload(
            payload,
            displayName,
            sourceHint: Path.GetFileName(path),
            profilesDirectory: Path.Combine(AppPaths.RootDirectory, "profiles")))).ConfigureAwait(true);
    }

    private async Task RunImportAsync(Func<Task<ConfigImportResult>> import)
    {
        _busy = true;
        _importCts = new CancellationTokenSource();
        ImportButton.IsEnabled = false;
        FileButton.IsEnabled = false;
        UrlBox.IsEnabled = false;
        NameBox.IsEnabled = false;
        ErrorText.IsVisible = false;
        SetStatus("Loading…");

        try
        {
            var result = await import().ConfigureAwait(true);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.SavedPath))
            {
                ShowError(result.Message);
                return;
            }

            SavedPath = result.SavedPath;
            ResultMessage = result.Message;
            Close(true);
        }
        catch (OperationCanceledException)
        {
            ShowError("Import cancelled.");
        }
        catch (Exception)
        {
            ShowError("Import failed.");
        }
        finally
        {
            _importCts.Dispose();
            _importCts = null;
            _busy = false;
            ImportButton.IsEnabled = true;
            FileButton.IsEnabled = true;
            UrlBox.IsEnabled = true;
            NameBox.IsEnabled = true;
        }
    }

    private void CancelClick(object? sender, RoutedEventArgs e)
    {
        if (_busy && _importCts is not null)
        {
            SetStatus("Cancelling…");
            _importCts.Cancel();
            return;
        }

        Close(false);
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        StatusText.IsVisible = true;
        ErrorText.IsVisible = false;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
        StatusText.IsVisible = false;
    }
}
