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
            Title = Loc.T("File.ConfigTitle"),
            Filter = Loc.T("File.ConfigTitle") + " (*.conf;*.awg)|*.conf;*.awg|" + Loc.T("File.AllFiles") + " (*.*)|*.*",
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
            Title = Loc.T("File.ExeTitle"),
            Filter = Loc.T("File.ExeFilter") + " (*.exe)|*.exe",
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
            Title = Loc.T("File.FolderTitle"),
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickEngine()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("File.EngineTitle"),
            Filter = "Mihomo (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false,
            DereferenceLinks = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
