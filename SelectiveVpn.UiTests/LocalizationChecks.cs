using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml.Linq;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;

namespace SelectiveVpn.UiTests;

internal static class LocalizationChecks
{
    private static readonly string[] Languages = ["ru", "en", "es", "pt", "zh"];
    private static readonly Regex Cyrillic = new("[Ѐ-ӿ]", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(@"\{\d+\}", RegexOptions.Compiled);

    // Not user-facing text: an uninstaller-name filter and a stub that nothing calls.
    private static readonly string[] AllowedFiles = ["InstalledAppCatalog.cs", "StubWindowsServiceController.cs"];

    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
    [
        ("every language file defines the same keys with the same placeholders", SameKeysEverywhereAsync),
        ("no user-facing Russian text is hard-coded outside the language files", NoHardCodedRussianAsync),
        ("engine messages and list summaries follow the selected language", EngineTextFollowsLanguageAsync)
    ];

    private static Task SameKeysEverywhereAsync()
    {
        var all = Languages.ToDictionary(language => language, LoadLanguage);
        var reference = all["ru"];
        foreach (var language in Languages.Where(language => language != "ru"))
        {
            var current = all[language];
            var missing = reference.Keys.Except(current.Keys).Order().ToArray();
            var extra = current.Keys.Except(reference.Keys).Order().ToArray();
            Check.True(missing.Length == 0, $"Lang.{language}.xaml is missing keys: {string.Join(", ", missing.Take(12))}");
            Check.True(extra.Length == 0, $"Lang.{language}.xaml has keys the Russian file lacks: {string.Join(", ", extra.Take(12))}");
            foreach (var (key, text) in reference)
            {
                var expected = Placeholders(text);
                var actual = Placeholders(current[key]);
                Check.True(expected.SetEquals(actual), $"Lang.{language}.xaml key {key} must use the same placeholders as the Russian text.");
            }
        }
        return Task.CompletedTask;
    }

    private static Task NoHardCodedRussianAsync()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();
        foreach (var project in new[] { "SelectiveVpn", "SelectiveVpn.Core" })
        {
            var files = Directory.EnumerateFiles(Path.Combine(root, project), "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains(@"\bin\") && !path.Contains(@"\obj\") && !path.Contains(@"\Localization\"))
                .Where(path => !AllowedFiles.Contains(Path.GetFileName(path)));
            foreach (var file in files)
            {
                var number = 0;
                foreach (var raw in File.ReadLines(file))
                {
                    number++;
                    var line = raw.Trim();
                    if (line.StartsWith("//") || line.StartsWith('*') || line.StartsWith("<!--"))
                        continue;
                    var comment = line.IndexOf(" //", StringComparison.Ordinal);
                    if (comment >= 0)
                        line = line[..comment];
                    if (!Cyrillic.IsMatch(line))
                        continue;
                    // The engine library carries its Russian text as the fallback of a keyed lookup.
                    if (line.Contains("CoreText.") || line.Contains("const string AutoNodeDisplayName"))
                        continue;
                    offenders.Add($"{Path.GetFileName(file)}:{number}");
                }
            }
        }
        Check.True(offenders.Count == 0,
            "User-facing text must come from the language files. Hard-coded Russian found at: " + string.Join(", ", offenders.Take(15)));
        return Task.CompletedTask;
    }

    private static Task EngineTextFollowsLanguageAsync()
    {
        var english = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SelectiveVpn;component/Localization/Lang.en.xaml")
        };
        var texts = english.Keys.OfType<string>().Where(key => english[key] is string).ToDictionary(key => key, key => (string)english[key]);
        var list = new AppRule("YouTube", false, RouteKind.AddressList, ["youtube.com", "ytimg.com", "203.0.113.0/24"], [], true);
        Check.Equal("2 домена · 1 IP · без проверки IP", list.DetailText, "The list summary should be Russian with the Russian strings loaded.");
        Check.True(!RouteAddress.TryParse("", out _, out _, out var russianError) && Cyrillic.IsMatch(russianError),
            "Without a lookup the engine falls back to its Russian text.");

        Application.Current.Resources.MergedDictionaries.Add(english);
        CoreText.Lookup = key => texts.GetValueOrDefault(key);
        try
        {
            Check.Equal("2 domains · 1 IP · IPs not checked", list.DetailText, "The list summary must follow the selected language.");
            Check.Equal("1 domain", AppRule.FormatDomainCount(1), "English uses the singular only for one.");
            Check.Equal("21 domains", AppRule.FormatDomainCount(21), "English must not apply the Russian plural rule.");
            Check.True(!RouteAddress.TryParse("", out _, out _, out var englishError), "An empty address is still invalid.");
            Check.Equal(texts["Core.Address.Required"], englishError, "Engine validation messages must follow the selected language.");
            Check.Equal(texts["Core.Engine.Canceled"], MihomoEngine.ConnectCanceledMessage, "The cancellation marker must be the translated text.");
            Check.True(!Cyrillic.IsMatch(new VpnNodeInfo("a", "a", false, 0).DelayText + new VpnNodeInfo("a", "a", false, 42).DelayText),
                "Server latency labels must follow the selected language.");
        }
        finally
        {
            CoreText.Lookup = null;
            Application.Current.Resources.MergedDictionaries.Remove(english);
        }
        return Task.CompletedTask;
    }

    private static Dictionary<string, string> LoadLanguage(string language)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Load(Path.Combine(RepositoryRoot(), "SelectiveVpn", "Localization", $"Lang.{language}.xaml"));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in document.Root!.Elements())
        {
            var key = (string?)element.Attribute(x + "Key");
            if (key is null)
                continue;
            Check.True(result.TryAdd(key, element.Value), $"Lang.{language}.xaml defines {key} twice.");
        }
        return result;
    }

    private static HashSet<string> Placeholders(string text)
        => Placeholder.Matches(text).Select(match => match.Value).ToHashSet(StringComparer.Ordinal);

    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SelectiveVpn", "MainWindow.xaml")))
            root = root.Parent;
        return root?.FullName ?? throw new InvalidOperationException("Run UI checks from the built repository.");
    }
}
