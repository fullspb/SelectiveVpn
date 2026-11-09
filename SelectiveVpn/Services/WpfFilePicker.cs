using Microsoft.Win32;
using SelectiveVpn.Abstractions;

namespace SelectiveVpn.Services;

/// <summary>
/// Диалоги выбора файла. Возвращают путь и не открывают поток файла.
/// </summary>
public sealed class WpfFilePicker : IFilePicker
{
    public string? PickAmneziaConfig()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Конфигурация AmneziaWG",
            Filter = "Конфигурация AmneziaWG (*.conf;*.awg)|*.conf;*.awg|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            DereferenceLinks = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Исполняемый файл приложения",
            Filter = "Приложения (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false,
            DereferenceLinks = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Папка приложений",
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickEngine()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Исполняемый файл Mihomo",
            Filter = "Mihomo (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false,
            DereferenceLinks = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
