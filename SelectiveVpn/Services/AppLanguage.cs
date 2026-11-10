using System.Globalization;
using System.Windows;

namespace SelectiveVpn.Services;

internal static class AppLanguage
{
    public const string Auto = "auto";
    public const string Russian = "ru";
    public const string English = "en";
    public const string Spanish = "es";
    public const string Portuguese = "pt";
    public const string Chinese = "zh";

    private const string DictionaryTag = "SelectiveVpn.Lang";

    public static event EventHandler? LanguageChanged;

    public static string CurrentCode { get; private set; } = English;

    public static IReadOnlyList<LanguageOption> Options { get; } =
    [
        new(Auto, "Lang.Auto"),
        new(Russian, "Lang.Russian"),
        new(English, "Lang.English"),
        new(Spanish, "Lang.Spanish"),
        new(Portuguese, "Lang.Portuguese"),
        new(Chinese, "Lang.Chinese")
    ];

    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return Auto;

        var value = language.Trim().ToLowerInvariant();
        return value switch
        {
            Auto or "system" or "default" => Auto,
            Russian or "ru-ru" => Russian,
            English or "en-us" or "en-gb" => English,
            Spanish or "es-es" or "es-mx" => Spanish,
            Portuguese or "pt-br" or "pt-pt" => Portuguese,
            Chinese or "zh-cn" or "zh-hans" or "zh-tw" or "zh-hant" => Chinese,
            _ => Auto
        };
    }

    public static string Resolve(string? language)
    {
        var normalized = Normalize(language);
        if (normalized != Auto)
            return normalized;

        var culture = CultureInfo.CurrentUICulture;
        while (true)
        {
            var name = culture.TwoLetterISOLanguageName.ToLowerInvariant();
            if (name is Russian or English or Spanish or Portuguese or Chinese)
                return name;

            if (culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return Chinese;

            if (Equals(culture, culture.Parent) || string.IsNullOrEmpty(culture.Parent.Name))
                break;

            culture = culture.Parent;
        }

        return English;
    }

    public static void Apply(string? language)
    {
        if (Application.Current is null)
            return;

        var resolved = Resolve(language);
        var uri = new Uri($"pack://application:,,,/Localization/Lang.{resolved}.xaml", UriKind.Absolute);
        var dictionary = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            if (Equals(merged[i][DictionaryTag], true))
                merged.RemoveAt(i);
        }

        dictionary[DictionaryTag] = true;
        merged.Add(dictionary);
        CurrentCode = resolved;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public sealed record LanguageOption(string Code, string TitleKey)
    {
        public string Title => Loc.T(TitleKey);
    }
}
