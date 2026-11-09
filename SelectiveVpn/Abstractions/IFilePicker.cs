namespace SelectiveVpn.Abstractions;

/// <summary>
/// Выбор файлов в интерфейсе. Реализация возвращает только путь и не читает файл.
/// </summary>
public interface IFilePicker
{
    string? PickAmneziaConfig();

    string? PickExecutable();

    string? PickFolder();

    string? PickEngine();
}
