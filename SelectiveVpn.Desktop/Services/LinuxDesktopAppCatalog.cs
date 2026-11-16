using SelectiveVpn.Desktop.Models;
using SelectiveVpn.Engine;

namespace SelectiveVpn.Desktop.Services;

/// <summary>
/// Collects desktop applications from <c>.desktop</c> files (user + system), resolving
/// <c>Exec=</c> to an absolute binary path for PROCESS-PATH rules.
/// </summary>
internal static class LinuxDesktopAppCatalog
{
    public static IReadOnlyList<ProcessAppInfo> Load()
    {
        var apps = new Dictionary<string, ProcessAppInfo>(HostPlatform.PathComparer);
        foreach (var root in DesktopRoots())
            CollectFromDirectory(root, apps);

        return apps.Values
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(app => app.ExecutablePath, HostPlatform.PathComparer)
            .ToList();
    }

    private static IEnumerable<string> DesktopRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            yield return Path.Combine(home, ".local", "share", "applications");

        yield return "/usr/local/share/applications";
        yield return "/usr/share/applications";

        var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        if (string.IsNullOrWhiteSpace(dataDirs))
            yield break;

        foreach (var part in dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dir = Path.Combine(part, "applications");
            if (dir is not ("/usr/share/applications" or "/usr/local/share/applications"))
                yield return dir;
        }
    }

    private static void CollectFromDirectory(string root, IDictionary<string, ProcessAppInfo> apps)
    {
        if (!Directory.Exists(root))
            return;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.desktop", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            try
            {
                if (!TryParseDesktop(file, out var name, out var exec))
                    continue;

                var path = ResolveExecutable(exec);
                if (path is null || !File.Exists(path))
                    continue;

                path = Path.GetFullPath(path);
                if (IsNoise(path) || apps.ContainsKey(path))
                    continue;

                apps[path] = new ProcessAppInfo(name, path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }
    }

    private static bool TryParseDesktop(string file, out string name, out string exec)
    {
        name = "";
        exec = "";
        var inDesktop = false;
        var noDisplay = false;
        var hidden = false;
        var type = "Application";
        string? nameEn = null;
        string? nameAny = null;
        string? tryExec = null;

        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inDesktop = line.Equals("[Desktop Entry]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inDesktop)
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (key.Equals("Type", StringComparison.OrdinalIgnoreCase))
                type = value;
            else if (key.Equals("NoDisplay", StringComparison.OrdinalIgnoreCase))
                noDisplay = value.Equals("true", StringComparison.OrdinalIgnoreCase);
            else if (key.Equals("Hidden", StringComparison.OrdinalIgnoreCase))
                hidden = value.Equals("true", StringComparison.OrdinalIgnoreCase);
            else if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
                nameAny = value;
            else if (key.Equals("Name[en]", StringComparison.OrdinalIgnoreCase)
                     || key.Equals("Name[en_US]", StringComparison.OrdinalIgnoreCase))
                nameEn = value;
            else if (key.Equals("Exec", StringComparison.OrdinalIgnoreCase))
                exec = value;
            else if (key.Equals("TryExec", StringComparison.OrdinalIgnoreCase))
                tryExec = value;
        }

        if (!type.Equals("Application", StringComparison.OrdinalIgnoreCase) || noDisplay || hidden)
            return false;

        if (string.IsNullOrWhiteSpace(exec) && !string.IsNullOrWhiteSpace(tryExec))
            exec = tryExec;

        name = (nameEn ?? nameAny ?? Path.GetFileNameWithoutExtension(file)).Trim();
        return !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(exec);
    }

    private static string? ResolveExecutable(string execLine)
    {
        var tokens = Tokenize(execLine);
        if (tokens.Count == 0)
            return null;

        var i = 0;
        while (i < tokens.Count)
        {
            var token = tokens[i];
            if (token.Equals("env", StringComparison.Ordinal))
            {
                i++;
                while (i < tokens.Count && tokens[i].Contains('=', StringComparison.Ordinal) && !tokens[i].StartsWith('-'))
                    i++;
                continue;
            }

            if (token.StartsWith('-'))
            {
                i++;
                continue;
            }

            break;
        }

        if (i >= tokens.Count)
            return null;

        var candidate = tokens[i];
        if (candidate.StartsWith('%'))
            return null;

        if (candidate.Contains('/') && File.Exists(candidate))
            return candidate;

        return FindOnPath(candidate);
    }

    private static string? FindOnPath(string command)
    {
        if (command.Contains('/'))
            return File.Exists(command) ? command : null;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir, command);
                if (File.Exists(full))
                    return full;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
            }
        }

        return null;
    }

    private static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quote != '\0')
            {
                if (ch == quote)
                    quote = '\0';
                else
                    current.Append(ch);
                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    private static bool IsNoise(string path)
    {
        var file = Path.GetFileName(path);
        return file.Equals("gnome-terminal", StringComparison.OrdinalIgnoreCase)
               && path.Contains("/libexec/", StringComparison.Ordinal);
    }
}
