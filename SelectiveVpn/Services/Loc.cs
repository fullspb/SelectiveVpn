using System.Windows;

namespace SelectiveVpn.Services;

internal static class Loc
{
    public static string T(string key)
    {
        if (Application.Current?.TryFindResource(key) is string value && !string.IsNullOrEmpty(value))
            return value;

        return key;
    }

    public static string Tf(string key, params object?[] args)
    {
        var format = T(key);
        try
        {
            return string.Format(format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }
}
