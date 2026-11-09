using System.IO;
using System.Text.Json;
using SelectiveVpn.Models;

namespace SelectiveVpn.Services;

internal static class ConnectSessionStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SelectiveVpn",
        "session.json");

    public static void Save(string configPath, IReadOnlyList<AppRule> rules, bool exceptListed)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var targets = rules
            .Select(rule => new SessionTarget(
                rule.ExecutablePath,
                rule.IsFolder,
                rule.StoredKind,
                rule.EntryList.ToArray(),
                rule.ResolvedIpList.ToArray(),
                rule.SkipDomainResolve))
            .ToArray();
        var payload = JsonSerializer.Serialize(new Session(configPath, targets, exceptListed));
        File.WriteAllText(FilePath, payload);
    }

    public static bool TryTake(out string configPath, out IReadOnlyList<SessionTarget> targets, out bool exceptListed)
    {
        configPath = "";
        targets = [];
        exceptListed = false;
        if (!File.Exists(FilePath))
            return false;

        try
        {
            var session = JsonSerializer.Deserialize<Session>(File.ReadAllText(FilePath));
            if (session is null || string.IsNullOrWhiteSpace(session.ConfigPath))
                return false;

            configPath = session.ConfigPath;
            targets = session.Targets ?? [];
            exceptListed = session.ExceptListed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch (IOException)
            {
            }
        }
    }

    internal sealed record SessionTarget(
        string Path,
        bool IsFolder,
        string? Kind = null,
        string[]? Entries = null,
        string[]? ResolvedIps = null,
        bool SkipDomainResolve = false);

    private sealed record Session(string ConfigPath, SessionTarget[] Targets, bool ExceptListed = false);
}
