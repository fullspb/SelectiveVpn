namespace SelectiveVpn.Models;

/// <summary>
/// Запрос запуска Mihomo. Содержит путь к файлу и цели маршрутизации, но не текст конфигурации.
/// </summary>
public sealed record MihomoStartRequest(
    string ConfigFilePath,
    IReadOnlyList<RouteTarget> Targets,
    string? CustomEnginePath = null,
    bool ExceptListed = false);
