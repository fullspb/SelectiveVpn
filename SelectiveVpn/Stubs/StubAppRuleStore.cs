using System.IO;
using SelectiveVpn.Abstractions;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;
using SelectiveVpn.Services;

namespace SelectiveVpn.Stubs;

/// <summary>
/// Список приложений текущего окна. В системный firewall он ничего не пишет:
/// пути попадают в правила PROCESS-PATH профиля Mihomo при подключении.
/// </summary>
public sealed class StubAppRuleStore : IAppRuleStore
{
    private readonly List<AppRule> _rules = [];

    public IReadOnlyList<AppRule> GetRules() => _rules.ToArray();

    public void Clear() => _rules.Clear();

    public OperationResult AddExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return OperationResult.Fail(Loc.T("Msg.NeedAppPath"));

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(executablePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return OperationResult.Fail(Loc.T("Msg.BadAppPath"));
        }

        if (!File.Exists(fullPath))
            return OperationResult.Fail(Loc.T("Msg.AppNotFound"));

        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
            return OperationResult.Fail(Loc.T("Msg.NeedExe"));

        if (_rules.Exists(rule => string.Equals(rule.ExecutablePath, fullPath, StringComparison.OrdinalIgnoreCase)))
            return OperationResult.Fail(Loc.T("Msg.AppExists"));

        _rules.Add(new AppRule(fullPath));
        return OperationResult.Ok(Loc.Tf("Msg.AppAdded", Path.GetFileName(fullPath)));
    }

    public OperationResult AddFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return OperationResult.Fail(Loc.T("Msg.NeedFolderPath"));

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(folderPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return OperationResult.Fail(Loc.T("Msg.BadFolderPath"));
        }

        if (fullPath.Contains(',') || fullPath.Contains('\''))
            return OperationResult.Fail(Loc.T("Msg.BadFolderChar"));

        if (!Directory.Exists(fullPath))
            return OperationResult.Fail(Loc.T("Msg.FolderNotFound"));

        fullPath = fullPath.TrimEnd('\\');
        if (_rules.Exists(rule => string.Equals(rule.ExecutablePath, fullPath, StringComparison.OrdinalIgnoreCase)))
            return OperationResult.Fail(Loc.T("Msg.FolderExists"));

        _rules.Add(new AppRule(fullPath, true));
        return OperationResult.Ok(Loc.Tf("Msg.FolderAdded", new DirectoryInfo(fullPath).Name));
    }

    public OperationResult AddAddress(string address)
    {
        if (!RouteAddress.TryParse(address, out var normalized, out var kind, out var error))
            return OperationResult.Fail(error);

        if (_rules.Exists(rule => string.Equals(rule.ExecutablePath, normalized, StringComparison.OrdinalIgnoreCase)))
            return OperationResult.Fail(Loc.T("Msg.AddressExists"));

        _rules.Add(new AppRule(normalized, false, kind));
        return kind == RouteKind.Ip
            ? OperationResult.Ok(Loc.Tf("Msg.ListAdded", normalized, Loc.T("Kind.Ip")))
            : OperationResult.Ok(Loc.Tf("Msg.ListAdded", normalized, Loc.T("Kind.Domain")));
    }

    public OperationResult AddAddressList(
        string name,
        IReadOnlyList<string> entries,
        IReadOnlyList<string> resolvedIps,
        bool skipDomainResolve = false)
    {
        var title = string.IsNullOrWhiteSpace(name) ? "Список адресов" : name.Trim();
        if (title.Contains(',') || title.Contains('\''))
            return OperationResult.Fail("Название списка содержит недопустимый символ.");

        if (entries.Count == 0)
            return OperationResult.Fail("Список пуст. Добавьте домены или IP-адреса.");

        if (_rules.Exists(rule =>
                rule.EffectiveKind == RouteKind.AddressList
                && string.Equals(rule.ExecutablePath, title, StringComparison.OrdinalIgnoreCase)))
            return OperationResult.Fail("Список с таким названием уже есть.");

        var ips = skipDomainResolve ? Array.Empty<string>() : resolvedIps.ToArray();
        _rules.Add(new AppRule(title, false, RouteKind.AddressList, entries.ToArray(), ips, skipDomainResolve));
        return OperationResult.Ok(
            Loc.Tf("Msg.ListAdded", title, AppRule.FormatListSummary(entries, ips)));
    }

    public OperationResult ReplaceAddressList(
        string currentName,
        string name,
        IReadOnlyList<string> entries,
        IReadOnlyList<string> resolvedIps,
        bool skipDomainResolve = false)
    {
        var index = _rules.FindIndex(rule =>
            rule.EffectiveKind == RouteKind.AddressList
            && string.Equals(rule.ExecutablePath, currentName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return OperationResult.Fail("Список адресов не найден.");

        var title = string.IsNullOrWhiteSpace(name) ? "Список адресов" : name.Trim();
        if (title.Contains(',') || title.Contains('\''))
            return OperationResult.Fail("Название списка содержит недопустимый символ.");

        if (entries.Count == 0)
            return OperationResult.Fail("Список пуст. Добавьте домены или IP-адреса.");

        if (_rules.Exists(rule =>
                rule.EffectiveKind == RouteKind.AddressList
                && !string.Equals(rule.ExecutablePath, currentName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(rule.ExecutablePath, title, StringComparison.OrdinalIgnoreCase)))
            return OperationResult.Fail("Список с таким названием уже есть.");

        var ips = skipDomainResolve ? Array.Empty<string>() : resolvedIps.ToArray();
        _rules[index] = new AppRule(title, false, RouteKind.AddressList, entries.ToArray(), ips, skipDomainResolve);
        return OperationResult.Ok(
            Loc.Tf("Msg.ListUpdated", title, AppRule.FormatListSummary(entries, ips)));
    }

    public OperationResult UpdateAddressListResolved(string name, IReadOnlyList<string> resolvedIps)
    {
        var index = _rules.FindIndex(rule =>
            rule.EffectiveKind == RouteKind.AddressList
            && string.Equals(rule.ExecutablePath, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return OperationResult.Fail("Список адресов не найден.");

        var current = _rules[index];
        _rules[index] = current with { ResolvedIps = resolvedIps.ToArray() };
        return OperationResult.Ok($"Обновлены IP списка «{current.ExecutablePath}».");
    }

    public OperationResult RemoveExecutable(string executablePath)
    {
        var removed = _rules.RemoveAll(rule =>
            string.Equals(rule.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));

        if (removed == 0)
            return OperationResult.Fail("Приложение не найдено в списке.");

        return OperationResult.Ok("Цель убрана из списка. Уже запущенный туннель от этого сам не меняется.");
    }

    public Task<OperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var details = _rules.Count == 0
            ? "Список приложений пуст."
            : $"В списке приложений: {_rules.Count}.";

        return Task.FromResult(OperationResult.Ok(
            details + " Они будут записаны в профиль Mihomo при подключении."));
    }
}
