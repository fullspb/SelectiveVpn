using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;
using SelectiveVpn.Services;

namespace SelectiveVpn;

public partial class AddressListPrompt : Window
{
    private bool _busy;
    private CancellationTokenSource? _resolveCts;

    public AddressListPrompt(AppRule? existing = null)
    {
        InitializeComponent();
        if (existing is not null && existing.IsAddressList)
        {
            Title = "Редактировать список";
            NameBox.Text = existing.ExecutablePath;
            AddressesBox.Text = string.Join(Environment.NewLine, existing.EntryList);
            SkipResolveBox.IsChecked = existing.SkipDomainResolve;
            AddButton.Content = "Сохранить";
        }

        UpdateLineCount();
        SourceInitialized += (_, _) => TitleBarTheme.ApplyFromResources(this);
        Loaded += (_, _) =>
        {
            TitleBarTheme.ApplyFromResources(this);
            AddressesBox.Focus();
            AddressesBox.CaretIndex = AddressesBox.Text.Length;
            UpdateLineCount();
        };
    }

    public string ListName { get; private set; } = "Список адресов";

    public IReadOnlyList<string> Entries { get; private set; } = [];

    public IReadOnlyList<string> ResolvedIps { get; private set; } = [];

    public bool SkipDomainResolve { get; private set; }

    public string ResultMessage { get; private set; } = "";

    private void AddressesChanged(object sender, TextChangedEventArgs e) => UpdateLineCount();

    private void UpdateLineCount()
    {
        var count = CountLines(AddressesBox.Text);
        LineCountText.Text = FormatDomainCount(count);
    }

    private static int CountLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var count = 0;
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.None))
        {
            if (line.Trim().Length > 0)
                count++;
        }

        return count;
    }

    private static string FormatDomainCount(int count)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;
        var word = mod100 is >= 11 and <= 14
            ? "доменов"
            : mod10 == 1
                ? "домен"
                : mod10 is >= 2 and <= 4
                    ? "домена"
                    : "доменов";
        return $"{count} {word}";
    }

    private async void AddClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        SetStatus("Читаю список…");
        var parsed = AddressListResolver.Parse(AddressesBox.Text);
        if (!parsed.Succeeded)
        {
            ShowError(parsed.Message);
            return;
        }

        ListName = string.IsNullOrWhiteSpace(NameBox.Text) ? "Список адресов" : NameBox.Text.Trim();
        SkipDomainResolve = SkipResolveBox.IsChecked == true;
        Entries = parsed.Entries;

        if (SkipDomainResolve)
        {
            ResolvedIps = [];
            ResultMessage = $"Домены не проверялись. В списке: {AppRule.FormatListSummary(parsed.Entries, [])}.";
            DialogResult = true;
            return;
        }

        _busy = true;
        _resolveCts = new CancellationTokenSource();
        AddButton.IsEnabled = false;
        NameBox.IsEnabled = false;
        AddressesBox.IsEnabled = false;
        SkipResolveBox.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;

        var progress = new Progress<string>(message =>
            Dispatcher.BeginInvoke(() => SetStatus(message), DispatcherPriority.Background));

        try
        {
            var resolved = await AddressListResolver.ResolveAsync(parsed.Entries, progress, _resolveCts.Token)
                .ConfigureAwait(true);
            Entries = resolved.Entries;
            ResolvedIps = resolved.ResolvedIps;
            ResultMessage = resolved.Message;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            ShowError("Разрешение доменов отменено.");
        }
        catch (Exception)
        {
            ShowError("Не удалось разрешить домены.");
        }
        finally
        {
            _resolveCts.Dispose();
            _resolveCts = null;
            _busy = false;
            AddButton.IsEnabled = true;
            NameBox.IsEnabled = true;
            AddressesBox.IsEnabled = true;
            SkipResolveBox.IsEnabled = true;
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        if (_busy && _resolveCts is not null)
        {
            SetStatus("Отмена…");
            _resolveCts.Cancel();
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
