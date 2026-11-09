using SelectiveVpn.Abstractions;
using SelectiveVpn.Models;

namespace SelectiveVpn.Stubs;

/// <summary>
/// ЗАГЛУШКА. Не вызывает диспетчер служб, sc.exe и ServiceController.
/// Служба не устанавливается, не запускается и не останавливается.
/// </summary>
public sealed class StubWindowsServiceController : IWindowsServiceController
{
    public string PlannedServiceName => "SelectiveVpn";

    public Task<OperationResult> GetStatusAsync(CancellationToken cancellationToken = default)
        => Refuse("запрос состояния", cancellationToken);

    public Task<OperationResult> InstallAsync(CancellationToken cancellationToken = default)
        => Refuse("установка", cancellationToken);

    public Task<OperationResult> UninstallAsync(CancellationToken cancellationToken = default)
        => Refuse("удаление", cancellationToken);

    public Task<OperationResult> StartAsync(CancellationToken cancellationToken = default)
        => Refuse("запуск", cancellationToken);

    public Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
        => Refuse("остановка", cancellationToken);

    private Task<OperationResult> Refuse(string action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(OperationResult.Fail(
            $"Служба Windows «{PlannedServiceName}»: {action} не выполняется. Управление службой — заглушка."));
    }
}
