using System.Reflection;

namespace SelectiveVpn.Services;

internal static class AppVersion
{
    public static string Current { get; } = Read();

    private static string Read()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
            return "0.0.0";

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? informational[..plus] : informational;
    }
}
