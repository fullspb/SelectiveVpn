using System.Globalization;
using System.Net;
using System.Net.Sockets;
using SelectiveVpn.Models;

namespace SelectiveVpn.Engine;

public static class RouteAddress
{
    public static bool TryParse(string? input, out string normalized, out RouteKind kind, out string error)
    {
        normalized = "";
        kind = RouteKind.Domain;
        var text = input?.Trim() ?? "";
        if (text.Length == 0)
        {
            error = "Укажите домен или IP-адрес.";
            return false;
        }

        if (text.Any(character => char.IsWhiteSpace(character) || character is ',' or '\'' or '"' or '\\'))
        {
            error = "Адрес содержит недопустимый символ.";
            return false;
        }

        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            text = text["http://".Length..];
        else if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            text = text["https://".Length..];

        var slash = text.IndexOf('/');
        string? prefixText = null;
        if (slash >= 0)
        {
            var tail = text[(slash + 1)..];
            if (tail.Length == 0 || tail.Any(character => !char.IsAsciiDigit(character)))
                text = text[..slash];
            else
            {
                prefixText = tail;
                text = text[..slash];
            }
        }

        var query = text.IndexOfAny(['?', '#']);
        if (query >= 0)
            text = text[..query];

        text = StripPort(text);
        if (text.Length == 0)
        {
            error = "Укажите домен или IP-адрес.";
            return false;
        }

        if (IPAddress.TryParse(text, out var address))
        {
            var maxPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            var prefix = maxPrefix;
            if (prefixText is not null
                && (!int.TryParse(prefixText, NumberStyles.None, CultureInfo.InvariantCulture, out prefix)
                    || prefix < 0
                    || prefix > maxPrefix))
            {
                error = "Некорректная маска сети.";
                return false;
            }

            normalized = address.ToString() + "/" + prefix.ToString(CultureInfo.InvariantCulture);
            kind = RouteKind.Ip;
            error = "";
            return true;
        }

        if (prefixText is not null)
        {
            error = "Некорректный IP-адрес.";
            return false;
        }

        if (text.StartsWith("*.", StringComparison.Ordinal))
            text = text[2..];
        text = text.Trim('.').ToLowerInvariant();
        try
        {
            text = new IdnMapping().GetAscii(text);
        }
        catch (ArgumentException)
        {
            error = "Некорректный домен. Пример: example.com.";
            return false;
        }

        if (!IsDomain(text))
        {
            error = "Некорректный домен. Пример: example.com.";
            return false;
        }

        normalized = text;
        kind = RouteKind.Domain;
        error = "";
        return true;
    }

    private static string StripPort(string host)
    {
        if (host.StartsWith('['))
        {
            var end = host.IndexOf(']');
            return end > 1 ? host[1..end] : host;
        }

        var colon = host.LastIndexOf(':');
        if (colon <= 0 || host.IndexOf(':') != colon)
            return host;

        var port = host[(colon + 1)..];
        return port.Length is > 0 and <= 5 && port.All(char.IsAsciiDigit) ? host[..colon] : host;
    }

    private static bool IsDomain(string host)
    {
        if (host.Length is 0 or > 253 || !host.Contains('.'))
            return false;

        var labels = host.Split('.');
        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63)
                return false;
            if (label.StartsWith('-') || label.EndsWith('-'))
                return false;
            if (label.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
                return false;
        }

        return true;
    }
}
