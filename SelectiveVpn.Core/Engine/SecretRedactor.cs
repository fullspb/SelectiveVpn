namespace SelectiveVpn.Engine;

public static class SecretRedactor
{
    public static string Redact(string text, IEnumerable<string> secrets)
    {
        foreach (var secret in secrets
                     .Where(static value => !string.IsNullOrWhiteSpace(value) && value.Length >= 8)
                     .OrderByDescending(static value => value.Length))
        {
            text = text.Replace(secret, CoreText.T("Core.Redacted", "[скрыто]"), StringComparison.Ordinal);
        }

        return text;
    }
}
