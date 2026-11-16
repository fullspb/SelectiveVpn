using Avalonia.Controls;
using Avalonia.Interactivity;
using SelectiveVpn.Desktop.Services;

namespace SelectiveVpn.Desktop.Views;

public partial class AddressListWindow : Window
{
    public AddressListWindow()
    {
        InitializeComponent();
        Title = UiLoc.T("Ui.AddressPromptTitle");
        TitleText.Text = UiLoc.T("Ui.AddressPromptTitle");
        AddressBox.Watermark = UiLoc.T("Ui.AddressWatermark");
        AddButton.Content = UiLoc.T("Ui.Add");
        CancelButton.Content = UiLoc.T("Ui.Cancel");
    }

    public string AddressText => AddressBox.Text ?? "";

    private void AddClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AddressBox.Text))
        {
            ErrorText.Text = UiLoc.T("Orbit.EmptyRules");
            return;
        }

        Close(true);
    }

    private void CancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
