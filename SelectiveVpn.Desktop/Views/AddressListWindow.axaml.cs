using Avalonia.Controls;
using Avalonia.Interactivity;
using SelectiveVpn.Desktop.Services;
using SelectiveVpn.Engine;

namespace SelectiveVpn.Desktop.Views;

public partial class AddressListWindow : Window
{
    public AddressListWindow()
    {
        InitializeComponent();
        ApplyLabels();
        UpdateLineCount();
        AddressBox.TextChanged += (_, _) => UpdateLineCount();
    }

    public string AddressText => AddressBox.Text ?? "";

    private void ApplyLabels()
    {
        Title = UiLoc.T("AddressList.Title");
        TitleText.Text = UiLoc.T("AddressList.Heading");
        HintText.Text = UiLoc.T("AddressList.Hint");
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
            : UiLoc.Tf("AddressList.LineCount", count);
    }
}
