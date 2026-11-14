using System.Globalization;

namespace SelectiveVpn;

/// <summary>
/// User-facing text produced by the engine library. The library has no UI resources of its own,
/// so the app plugs in <see cref="Lookup"/> for the selected language. Every call also carries
/// the Russian text, which is used when no lookup is set (tests, tools) or a key is missing.
/// </summary>
public static class CoreText
{
    private static volatile Func<string, string?>? _lookup;

    /// <summary>Returns the translation for a key, or null when there is none. Must be thread-safe.</summary>
    public static Func<string, string?>? Lookup
    {
        get => _lookup;
        set => _lookup = value;
    }

    public static string T(string key, string fallback)
    {
        var value = _lookup?.Invoke(key);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    public static string F(string key, string fallback, params object?[] args)
    {
        try
        {
            return string.Format(CultureInfo.InvariantCulture, T(key, fallback), args);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.InvariantCulture, fallback, args);
        }
    }

    /// <summary>
    /// Picks the word form for a count by the plural rule of the current language (List.Plural):
    /// "slavic" has one/few/many, "simple" has one/many, "none" always uses many.
    /// </summary>
    public static string Plural(int count, string key, string one, string few, string many)
    {
        switch (T("List.Plural", "slavic"))
        {
            case "none":
                return T(key + ".Many", many);
            case "simple":
                return count == 1 ? T(key + ".One", one) : T(key + ".Many", many);
        }

        var mod100 = Math.Abs(count) % 100;
        var mod10 = Math.Abs(count) % 10;
        if (mod100 is >= 11 and <= 14)
            return T(key + ".Many", many);
        return mod10 switch
        {
            1 => T(key + ".One", one),
            2 or 3 or 4 => T(key + ".Few", few),
            _ => T(key + ".Many", many)
        };
    }
}
