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

        if (!ConfigSubscriptionImporter.TryValidateInput(UrlBox.Text, out var urlError))
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
        SetStatus(Loc.T("SubImport.Loading"));

        try
        {
            var result = await ConfigSubscriptionImporter.ImportAsync(
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
            ShowError(Loc.T("SubImport.Canceled"));
        }
        catch (Exception)
        {
            ShowError(Loc.T("SubImport.Fail"));
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

    /// <summary>
    /// The link box takes several lines so a pasted list of servers stays intact, but a plain
    /// Enter still starts the import, as it did when the box held a single URL.
    /// </summary>
    private void UrlBoxPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter
            || System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.None)
        {
            return;
        }

        e.Handled = true;
        ImportClick(sender, e);
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        if (_busy && _importCts is not null)
        {
            SetStatus(Loc.T("AddressList.Canceling"));
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
