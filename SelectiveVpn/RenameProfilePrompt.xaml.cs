using System.Windows;
using SelectiveVpn.Services;

namespace SelectiveVpn;

/// <summary>Asks for the name a profile is shown under. An empty name brings back the original one.</summary>
public partial class RenameProfilePrompt : Window
{
    public const int MaxNameLength = 60;

    public RenameProfilePrompt(string currentName, string originalName)
    {
        InitializeComponent();
        NameBox.MaxLength = MaxNameLength;
        NameBox.Text = currentName;
        HintText.Text = Loc.Tf("RenameProfile.Hint", originalName);
        SourceInitialized += (_, _) => TitleBarTheme.ApplyFromResources(this);
        Loaded += (_, _) =>
        {
            TitleBarTheme.ApplyFromResources(this);
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    /// <summary>The new name, trimmed; empty when the original name should be used again.</summary>
    public string ResultName { get; internal set; } = "";

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        ResultName = Clean(NameBox.Text);
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>One line, no control characters, at most <see cref="MaxNameLength"/> characters.</summary>
    internal static string Clean(string? text)
    {
        var chars = (text ?? "").Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray();
        var name = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return name.Length > MaxNameLength ? name[..MaxNameLength].TrimEnd() : name;
    }
}
