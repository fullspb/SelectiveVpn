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
}
