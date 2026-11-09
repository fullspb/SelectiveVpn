using SelectiveVpn.Models;

namespace SelectiveVpn.Abstractions;

/// <summary>
/// Будущее управление службой Windows.
/// Текущая реализация — заглушка: не обращается к диспетчеру служб,
/// не устанавливает и не запускает службу.
/// </summary>
public interface IWindowsServiceController
{
    string PlannedServiceName { get; }

    Task<OperationResult> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> InstallAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> UninstallAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> StartAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> StopAsync(CancellationToken cancellationToken = default);
}
