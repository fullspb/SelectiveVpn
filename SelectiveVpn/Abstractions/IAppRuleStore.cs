using SelectiveVpn.Models;

namespace SelectiveVpn.Abstractions;

/// <summary>
/// Будущие правила приложений для маршрутизации процессов через TUN.
/// Текущая реализация — заглушка: список только в памяти окна,
/// системные правила не создаются.
/// </summary>
public interface IAppRuleStore
{
    IReadOnlyList<AppRule> GetRules();

    void Clear();

    OperationResult AddExecutable(string executablePath);

    OperationResult AddFolder(string folderPath);

    OperationResult AddAddress(string address);

    OperationResult AddAddressList(
        string name,
        IReadOnlyList<string> entries,
        IReadOnlyList<string> resolvedIps,
        bool skipDomainResolve = false);

    OperationResult ReplaceAddressList(
        string currentName,
        string name,
        IReadOnlyList<string> entries,
        IReadOnlyList<string> resolvedIps,
        bool skipDomainResolve = false);

    OperationResult UpdateAddressListResolved(string name, IReadOnlyList<string> resolvedIps);

    OperationResult RemoveExecutable(string executablePath);

    Task<OperationResult> ApplyAsync(CancellationToken cancellationToken = default);
}
