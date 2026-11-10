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
            Title = Loc.T("Ui.Edit") + " — " + Loc.T("AddressList.Title");
            NameBox.Text = existing.ExecutablePath;
            AddressesBox.Text = string.Join(Environment.NewLine, existing.EntryList);
            SkipResolveBox.IsChecked = existing.SkipDomainResolve;
            AddButton.Content = Loc.T("Ui.Save");
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

    public string ListName { get; private set; } = Loc.T("AddressList.DefaultName");

    public IReadOnlyList<string> Entries { get; private set; } = [];

    public IReadOnlyList<string> ResolvedIps { get; private set; } = [];

    public bool SkipDomainResolve { get; private set; }

    public string ResultMessage { get; private set; } = "";

    private void AddressesChanged(object sender, TextChangedEventArgs e) => UpdateLineCount();

    private void YouTubePresetClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var preset = DomainListPresets.YouTubeGoogle;
        var existing = AddressesBox.Text?
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        var merged = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in existing.Concat(preset))
        {
            if (line.Length == 0 || !seen.Add(line))
                continue;
            merged.Add(line);
        }

        AddressesBox.Text = string.Join(Environment.NewLine, merged);
        if (string.IsNullOrWhiteSpace(NameBox.Text)
            || string.Equals(NameBox.Text.Trim(), Loc.T("AddressList.DefaultName"), StringComparison.OrdinalIgnoreCase))
        {
            NameBox.Text = Loc.T("AddressList.PresetYouTubeName");
        }

        // Domain-suffix + sniffer is enough; resolving googlevideo CDN IPs is noisy and stale.
        SkipResolveBox.IsChecked = true;
        SetStatus(Loc.Tf("AddressList.PresetApplied", Loc.T("AddressList.PresetYouTubeName"), preset.Count));
        UpdateLineCount();
        AddressesBox.CaretIndex = AddressesBox.Text.Length;
        AddressesBox.Focus();
    }

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
            ? Loc.T("AddressList.DomainMany")
            : mod10 == 1
                ? Loc.T("AddressList.Domain1")
                : mod10 is >= 2 and <= 4
                    ? Loc.T("AddressList.DomainFew")
                    : Loc.T("AddressList.DomainMany");
        return $"{count} {word}";
    }

    private async void AddClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        SetStatus(Loc.T("AddressList.Reading"));
        var parsed = AddressListResolver.Parse(AddressesBox.Text);
        if (!parsed.Succeeded)
        {
            ShowError(parsed.Message);
            return;
        }

        ListName = string.IsNullOrWhiteSpace(NameBox.Text)
            ? Loc.T("AddressList.DefaultName")
            : NameBox.Text.Trim();
        SkipDomainResolve = SkipResolveBox.IsChecked == true;
        Entries = parsed.Entries;

        if (SkipDomainResolve)
        {
            ResolvedIps = [];
            ResultMessage = Loc.Tf("AddressList.SkipResolveResult", AppRule.FormatListSummary(parsed.Entries, []));
            DialogResult = true;
            return;
        }

        _busy = true;
        _resolveCts = new CancellationTokenSource();
        AddButton.IsEnabled = false;
        YouTubePresetButton.IsEnabled = false;
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
            ShowError(Loc.T("AddressList.ResolveCanceled"));
        }
        catch (Exception)
        {
            ShowError(Loc.T("AddressList.ResolveFail"));
        }
        finally
        {
            _resolveCts.Dispose();
            _resolveCts = null;
            _busy = false;
            AddButton.IsEnabled = true;
            YouTubePresetButton.IsEnabled = true;
            NameBox.IsEnabled = true;
            AddressesBox.IsEnabled = true;
            SkipResolveBox.IsEnabled = true;
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        if (_busy && _resolveCts is not null)
        {
            SetStatus(Loc.T("AddressList.Canceling"));
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
