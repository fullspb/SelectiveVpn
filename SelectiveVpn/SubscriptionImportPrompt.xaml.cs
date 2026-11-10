using System.Windows;
using SelectiveVpn.Engine;
using SelectiveVpn.Services;

namespace SelectiveVpn;

public partial class SubscriptionImportPrompt : Window
{
    private bool _busy;
    private CancellationTokenSource? _importCts;

    public SubscriptionImportPrompt()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => TitleBarTheme.ApplyFromResources(this);
        Loaded += (_, _) =>
        {
            TitleBarTheme.ApplyFromResources(this);
            UrlBox.Focus();
        };
    }

    public string? SavedPath { get; private set; }

    public string ResultMessage { get; private set; } = "";

    private async void ImportClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (!ConfigSubscriptionImporter.TryNormalizeUrl(UrlBox.Text, out _, out var urlError))
        {
            ShowError(urlError);
            return;
        }

        _busy = true;
        _importCts = new CancellationTokenSource();
        ImportButton.IsEnabled = false;
        UrlBox.IsEnabled = false;
        NameBox.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        SetStatus("Загружаю ссылку…");

        try
        {
            var result = await ConfigSubscriptionImporter.ImportFromUrlAsync(
                    UrlBox.Text,
                    NameBox.Text,
                    cancellationToken: _importCts.Token)
                .ConfigureAwait(true);

            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.SavedPath))
            {
                ShowError(result.Message);
                return;
            }

            SavedPath = result.SavedPath;
            ResultMessage = result.Message;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            ShowError("Импорт отменён.");
        }
        catch (Exception)
        {
            ShowError("Не удалось импортировать профиль по ссылке.");
        }
        finally
        {
            _importCts.Dispose();
            _importCts = null;
            _busy = false;
            ImportButton.IsEnabled = true;
            UrlBox.IsEnabled = true;
            NameBox.IsEnabled = true;
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        if (_busy && _importCts is not null)
        {
            SetStatus("Отмена…");
            _importCts.Cancel();
            return;
        }

        DialogResult = false;
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Collapsed;
    }
}
