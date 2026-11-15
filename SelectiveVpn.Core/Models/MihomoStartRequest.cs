namespace SelectiveVpn.Models;

/// <summary>
/// Запрос запуска Mihomo. Содержит путь к файлу и цели маршрутизации, но не текст конфигурации.
/// </summary>
public sealed record MihomoStartRequest(
    string ConfigFilePath,
    IReadOnlyList<RouteTarget> Targets,
    string? CustomEnginePath = null,
    bool ExceptListed = false,
    /// <summary>
    /// When true (Windows default), an empty target list means MATCH,VPN (full tunnel).
    /// Linux Avalonia MVP passes false so empty include keeps MATCH,DIRECT and browsing works.
    /// </summary>
    bool FullTunnelWhenEmpty = true);
