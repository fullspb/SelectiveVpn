using System.IO;
using System.Text;
using System.Windows;
using SelectiveVpn.Engine;
using SelectiveVpn.Services;

namespace SelectiveVpn;

public partial class ConfigEditPrompt : Window
{
    private readonly string _path;

    public ConfigEditPrompt(string path, string content)
    {
        InitializeComponent();
        _path = path;
        PathText.Text = path;
        ConfigBox.Text = content;
        SourceInitialized += (_, _) => TitleBarTheme.ApplyFromResources(this);
        Loaded += (_, _) =>
        {
            TitleBarTheme.ApplyFromResources(this);
            ConfigBox.Focus();
            ConfigBox.CaretIndex = 0;
        };
    }

    public string SavedContent { get; private set; } = "";

    public string ResultMessage { get; private set; } = "";

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        var text = ConfigBox.Text ?? "";
        var parsed = AmneziaWgConfigReader.Read(text);
        if (!parsed.Succeeded || parsed.Profile is null)
        {
            ShowError(string.IsNullOrWhiteSpace(parsed.Message)
                ? "Конфигурация не прошла проверку."
                : parsed.Message);
            return;
        }

        try
        {
            File.WriteAllText(_path, NormalizeNewlines(text), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            SavedContent = text;
            ResultMessage = $"Конфиг сохранён: {parsed.Profile.Describe()}.";
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Не удалось сохранить файл конфигурации.");
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static string NormalizeNewlines(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);
}
