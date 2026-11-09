namespace SelectiveVpn.Engine;

public sealed record AmneziaConfigReadResult(bool Succeeded, AmneziaWgProfile? Profile, string Message)
{
    public static AmneziaConfigReadResult Ok(AmneziaWgProfile profile) => new(true, profile, "");

    public static AmneziaConfigReadResult Fail(string message) => new(false, null, message);
}
