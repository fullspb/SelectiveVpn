using SelectiveVpn.Models;

namespace SelectiveVpn.Abstractions;

/// <summary>
/// Управление процессом Mihomo: AmneziaWG 3.1 и правила процессов через TUN.
/// </summary>
public interface IMihomoController
{
    event EventHandler<string>? EngineMessage;

    event EventHandler? EngineStopped;

    string EngineId { get; }

    bool IsEngineAvailable { get; }

    bool IsRunning { get; }

    Task<OperationResult> StartAsync(
        MihomoStartRequest request,
        CancellationToken cancellationToken = default);

    Task<OperationResult> StopAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> QueryExitIpAsync(CancellationToken cancellationToken = default);

    Task<VpnTraffic> QueryVpnTrafficAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> SelectVpnNodeAsync(string nodeName, CancellationToken cancellationToken = default);

    /// <summary>Текущий листовой узел группы VPN (с раскрытием AUTO), или null.</summary>
    Task<string?> GetActiveVpnNodeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VpnNodeInfo>> ListVpnNodesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, int>> MeasureVpnNodeDelaysAsync(
        IEnumerable<string> nodeNames,
        CancellationToken cancellationToken = default);

    string? ProviderCachePath { get; }
}
