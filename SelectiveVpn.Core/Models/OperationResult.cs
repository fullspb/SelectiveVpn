namespace SelectiveVpn.Models;

public sealed record OperationResult(bool Succeeded, string Message)
{
    public static OperationResult Ok(string message) => new(true, message);

    public static OperationResult Fail(string message) => new(false, message);
}
