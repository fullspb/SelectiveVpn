using Avalonia.Controls;
using Avalonia.Interactivity;
using SelectiveVpn.Desktop.Models;
using SelectiveVpn.Desktop.Services;
using SelectiveVpn.Engine;

namespace SelectiveVpn.Desktop.Views;

public partial class AddressListWindow : Window
{
    public AddressListWindow()
        : this(null)
    {
    }

    public AddressListWindow(RouteRuleItem? existing)
    {
        InitializeComponent();
        ApplyLabels();
        if (existing is { IsAddressList: true })
        {
            Title = UiLoc.T("Ui.Edit") + " — " + UiLoc.T("AddressList.Title");
            TitleText.Text = UiLoc.T("AddressList.Heading");
            NameBox.Text = existing.Path;
            AddressBox.Text = string.Join(Environment.NewLine, existing.Entries);
            AddButton.Content = UiLoc.T("Ui.Save");
        }

        UpdateLineCount();
        AddressBox.TextChanged += (_, _) => UpdateLineCount();
    }

    public string ListName { get; private set; } = "";

    public string AddressText => AddressBox.Text ?? "";

    public IReadOnlyList<string> Entries { get; private set; } = [];

    public bool SkipDomainResolve { get; private set; } = true;

    private void ApplyLabels()
    {
        Title = UiLoc.T("AddressList.Title");
        TitleText.Text = UiLoc.T("AddressList.Heading");
        HintText.Text = UiLoc.T("AddressList.Hint");
        NameLabel.Text = UiLoc.T("AddressList.Name");
        NameBox.Text = UiLoc.T("AddressList.DefaultName");
        AddressesLabel.Text = UiLoc.T("AddressList.Addresses");
        AddressBox.Watermark = UiLoc.T("Ui.AddressWatermark");
        YouTubePresetButton.Content = UiLoc.T("AddressList.PresetYouTube");
        ToolTip.SetTip(YouTubePresetButton, UiLoc.T("AddressList.PresetYouTubeHint"));
        AddButton.Content = UiLoc.T("Ui.Add");
        CancelButton.Content = UiLoc.T("Ui.Cancel");
        StatusText.Text = "";
        ErrorText.Text = "";
    }

    private void YouTubePresetClick(object? sender, RoutedEventArgs e)
    {
        var preset = DomainListPresets.YouTubeGoogle;
        var existing = AddressBox.Text?
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

        AddressBox.Text = string.Join(Environment.NewLine, merged);
        if (string.IsNullOrWhiteSpace(NameBox.Text)
            || string.Equals(NameBox.Text.Trim(), UiLoc.T("AddressList.DefaultName"), StringComparison.OrdinalIgnoreCase))
        {
            NameBox.Text = UiLoc.T("AddressList.PresetYouTubeName");
        }

        // Domain-suffix + sniffer is enough; resolving CDN IPs is noisy/stale (same as WPF).
        SkipDomainResolve = true;
        StatusText.Text = UiLoc.Tf(
            "AddressList.PresetApplied",
            UiLoc.T("AddressList.PresetYouTubeName"),
            preset.Count);
        ErrorText.Text = "";
        UpdateLineCount();
        AddressBox.CaretIndex = AddressBox.Text?.Length ?? 0;
        AddressBox.Focus();
    }

    private void AddClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AddressBox.Text))
        {
            ErrorText.Text = UiLoc.T("Orbit.EmptyRules");
            StatusText.Text = "";
            return;
        }

        var parsed = AddressListResolver.Parse(AddressBox.Text);
        if (!parsed.Succeeded)
        {
            ErrorText.Text = parsed.Message;
            StatusText.Text = "";
            return;
        }

        ListName = string.IsNullOrWhiteSpace(NameBox.Text)
            ? UiLoc.T("AddressList.DefaultName")
            : NameBox.Text.Trim();
        Entries = parsed.Entries;
        SkipDomainResolve = true;
        Close(true);
    }

    private void CancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void UpdateLineCount()
    {
        var count = 0;
        foreach (var line in (AddressBox.Text ?? "").Split(['\r', '\n'], StringSplitOptions.None))
        {
            if (line.Trim().Length > 0)
                count++;
        }

        LineCountText.Text = count == 0
            ? ""
            : RouteRuleItem.FormatDomainCount(count);
    }
}
