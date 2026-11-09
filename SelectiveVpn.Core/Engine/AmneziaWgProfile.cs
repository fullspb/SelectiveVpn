namespace SelectiveVpn.Engine;

public sealed class AmneziaWgProfile
{
    public required string PrivateKey { get; init; }

    public required string AddressV4 { get; init; }

    public string? AddressV6 { get; init; }

    public required string PublicKey { get; init; }

    public string? PresharedKey { get; init; }

    public required string EndpointHost { get; init; }

    public required int EndpointPort { get; init; }

    public IReadOnlyList<string> AllowedIps { get; init; } = [];

    public IReadOnlyList<string> DnsServers { get; init; } = [];

    public int? Mtu { get; init; }

    public int? PersistentKeepalive { get; init; }

    public int? Jc { get; init; }

    public int? Jmin { get; init; }

    public int? Jmax { get; init; }

    public int? S1 { get; init; }

    public int? S2 { get; init; }

    public int? S3 { get; init; }

    public int? S4 { get; init; }

    public string? H1 { get; init; }

    public string? H2 { get; init; }

    public string? H3 { get; init; }

    public string? H4 { get; init; }

    public string? I1 { get; init; }

    public string? I2 { get; init; }

    public string? I3 { get; init; }

    public string? I4 { get; init; }

    public string? I5 { get; init; }

    public string? J1 { get; init; }

    public string? J2 { get; init; }

    public string? J3 { get; init; }

    public int? ITime { get; init; }

    public string? HeaderProtectionKey { get; init; }

    public string? ContentPaddingAddition { get; init; }

    public string? RekeyAfterTime { get; init; }

    public string? RekeyTimeout { get; init; }

    public string? RejectAfterTime { get; init; }

    public string? KeepaliveTimeout { get; init; }

    public string? MaxHandshakeAttempts { get; init; }

    public bool? RandomTrailers { get; init; }

    public bool? DisableCookies { get; init; }

    public IReadOnlyList<string> IgnoredParameters { get; init; } = [];

    public bool IsVersion3 =>
        HeaderProtectionKey is not null
        || ContentPaddingAddition is not null
        || RekeyAfterTime is not null
        || RekeyTimeout is not null
        || RejectAfterTime is not null
        || KeepaliveTimeout is not null
        || MaxHandshakeAttempts is not null
        || RandomTrailers is not null
        || DisableCookies is not null;

    public bool IsVersion31 => IsVersion3 && (RandomTrailers is not null || DisableCookies is not null);

    public bool HasAmneziaOptions =>
        IsVersion3
        || Jc is not null
        || Jmin is not null
        || Jmax is not null
        || S1 is not null
        || S2 is not null
        || S3 is not null
        || S4 is not null
        || H1 is not null
        || H2 is not null
        || H3 is not null
        || H4 is not null
        || I1 is not null
        || I2 is not null
        || I3 is not null
        || I4 is not null
        || I5 is not null
        || J1 is not null
        || J2 is not null
        || J3 is not null
        || ITime is not null;

    public string Describe()
    {
        var kind = IsVersion31
            ? "AmneziaWG 3.1"
            : IsVersion3
                ? "AmneziaWG 3.0"
                : HasAmneziaOptions
                    ? "AmneziaWG"
                    : "WireGuard";

        return $"{kind}, конечная точка {EndpointHost}:{EndpointPort.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    public IEnumerable<string> SecretValues()
    {
        yield return PrivateKey;
        yield return PublicKey;

        if (PresharedKey is not null)
            yield return PresharedKey;

        if (HeaderProtectionKey is not null)
            yield return HeaderProtectionKey;

        foreach (var packet in new[] { I1, I2, I3, I4, I5, J1, J2, J3 })
        {
            if (packet is not null)
                yield return packet;
        }
    }
}
