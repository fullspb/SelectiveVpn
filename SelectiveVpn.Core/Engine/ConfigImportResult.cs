namespace SelectiveVpn.Engine;

public sealed record ConfigImportResult(
    bool Succeeded,
    string Message,
    string? SavedPath = null,
    string? ProfileDescription = null)
{
    public static ConfigImportResult Ok(string savedPath, string description, string message)
        => new(true, message, savedPath, description);

    public static ConfigImportResult Fail(string message)
        => new(false, message);
}
