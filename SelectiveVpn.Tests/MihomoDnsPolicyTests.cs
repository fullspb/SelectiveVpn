using System.Text.Json;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;

namespace SelectiveVpn.Tests;

public sealed class MihomoDnsPolicyTests
{
    [Fact]
    public void Included_youtube_domains_send_dns_through_vpn_without_replacing_profile_resolvers()
    {
        var targets = DomainListPresets.YouTubeGoogle
            .Select(domain => new RouteTarget(domain, Kind: RouteKind.Domain))
            .ToArray();

        var yaml = BuildWireGuard(targets, ["1.1.1.1", "9.9.9.9"]);
        var policies = ReadDnsPolicies(yaml);

        Assert.Equal(DomainListPresets.YouTubeGoogle.Count, policies.Count);
        foreach (var domain in new[] { "youtube.com", "googlevideo.com", "ytimg.com", "ggpht.com", "youtubei.googleapis.com" })
            Assert.Equal(["udp://1.1.1.1#VPN", "udp://9.9.9.9#VPN"], policies["+." + domain]);
        Assert.Equal(["udp://1.1.1.1", "udp://9.9.9.9"], ReadDnsList(yaml, "nameserver"));
        Assert.Equal(["1.1.1.1", "8.8.8.8"], ReadDnsList(yaml, "default-nameserver"));
        Assert.Equal(["1.1.1.1", "8.8.8.8"], ReadDnsList(yaml, "proxy-server-nameserver"));
        Assert.Contains("'DOMAIN-SUFFIX,googlevideo.com,VPN'", yaml);
        Assert.Contains("MATCH,DIRECT", yaml);
    }

    [Fact]
    public void Excluded_domains_preserve_profile_dns_without_forcing_private_resolvers_direct()
    {
        var yaml = BuildWireGuard([new("youtube.com", Kind: RouteKind.Domain)],
            ["10.9.9.1", "https://dns.example/dns-query"], exceptListed: true);

        Assert.Empty(ReadDnsPolicies(yaml));
        Assert.DoesNotContain("nameserver-policy:", yaml);
        Assert.Equal(["udp://10.9.9.1", "https://dns.example/dns-query"], ReadDnsList(yaml, "nameserver"));
        Assert.Equal(["1.1.1.1", "8.8.8.8"], ReadDnsList(yaml, "proxy-server-nameserver"));
        Assert.Contains("'DOMAIN-SUFFIX,youtube.com,DIRECT'", yaml);
        Assert.Contains("MATCH,VPN", yaml);
    }

    [Fact]
    public void Included_domains_keep_tunnel_private_dns_reachable_through_vpn()
    {
        var yaml = BuildWireGuard([new("youtube.com", Kind: RouteKind.Domain)], ["10.9.9.1"]);

        Assert.Equal(["udp://10.9.9.1#VPN"], ReadDnsPolicies(yaml)["+.youtube.com"]);
        Assert.Equal(["udp://10.9.9.1"], ReadDnsList(yaml, "nameserver"));
        Assert.Equal(["1.1.1.1", "8.8.8.8"], ReadDnsList(yaml, "proxy-server-nameserver"));
    }

    [Theory]
    [InlineData(false, "VPN")]
    [InlineData(true, "DIRECT")]
    public void Subscription_domain_dns_policy_is_added_only_for_include_mode(bool exceptListed, string policy)
    {
        var subscription = new SubscriptionProfile { Url = "https://example.invalid/subscription", Title = "Synthetic subscription" };
        var yaml = MihomoProfileWriter.BuildSubscription(subscription,
            [new("youtube.com", Kind: RouteKind.Domain), new("googlevideo.com", Kind: RouteKind.Domain)],
            17990, "synthetic-controller-secret", "provider.yaml", "synthetic-hardware-id", exceptListed: exceptListed);

        var policies = ReadDnsPolicies(yaml);
        if (exceptListed)
            Assert.Empty(policies);
        else
        {
            Assert.Equal(["udp://1.1.1.1#VPN", "udp://8.8.8.8#VPN"], policies["+.youtube.com"]);
            Assert.Equal(["udp://1.1.1.1#VPN", "udp://8.8.8.8#VPN"], policies["+.googlevideo.com"]);
        }
        Assert.Equal(["udp://1.1.1.1", "udp://8.8.8.8"], ReadDnsList(yaml, "nameserver"));
        Assert.Equal(["1.1.1.1", "8.8.8.8"], ReadDnsList(yaml, "proxy-server-nameserver"));
        Assert.Contains("'DOMAIN-SUFFIX,youtube.com," + policy + "'", yaml);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Non_domain_targets_do_not_add_a_domain_dns_policy(bool exceptListed)
    {
        var yaml = BuildWireGuard(
            [new(@"C:\Synthetic\browser.exe"), new(@"C:\Synthetic\Apps", IsFolder: true), new("203.0.113.0/24", Kind: RouteKind.Ip)],
            ["1.1.1.1"], exceptListed);

        Assert.Empty(ReadDnsPolicies(yaml));
        Assert.DoesNotContain("nameserver-policy:", yaml);
        Assert.Equal(["udp://1.1.1.1"], ReadDnsList(yaml, "nameserver"));
    }

    [Fact]
    public void Full_tunnel_and_empty_subscription_keep_the_original_default_dns_configuration()
    {
        var wireGuard = BuildWireGuard([], []);
        var subscription = MihomoProfileWriter.BuildSubscription(
            new SubscriptionProfile { Url = "https://example.invalid/subscription" }, [],
            17990, "synthetic-controller-secret", "provider.yaml", "synthetic-hardware-id");

        foreach (var yaml in new[] { wireGuard, subscription })
        {
            Assert.Empty(ReadDnsPolicies(yaml));
            Assert.Equal(["udp://1.1.1.1", "udp://8.8.8.8"], ReadDnsList(yaml, "nameserver"));
            Assert.Equal(["1.1.1.1", "8.8.8.8"], ReadDnsList(yaml, "proxy-server-nameserver"));
            Assert.Contains("MATCH,VPN", yaml);
        }
    }

    [Fact]
    public void Duplicate_domain_targets_produce_one_case_insensitive_suffix_policy()
    {
        var yaml = BuildWireGuard(
            [new("YouTube.COM", Kind: RouteKind.Domain), new("youtube.com", Kind: RouteKind.Domain),
             new("GOOGLEVIDEO.COM", Kind: RouteKind.Domain), new("googlevideo.com", Kind: RouteKind.Domain)],
            ["1.1.1.1"]);

        var policies = ReadDnsPolicies(yaml);
        Assert.Equal(2, policies.Count);
        Assert.Equal(["udp://1.1.1.1#VPN"], policies["+.youtube.com"]);
        Assert.Equal(["udp://1.1.1.1#VPN"], policies["+.googlevideo.com"]);
    }

    [Theory]
    [InlineData("https://dns.example/dns-query#OldProxy&h3=true&ecs=203.0.113.0/24",
        "https://dns.example/dns-query#VPN&h3=true&ecs=203.0.113.0/24")]
    [InlineData("https://dns.example/dns-query#DIRECT&h3=true", "https://dns.example/dns-query#VPN&h3=true")]
    [InlineData("udp://1.1.1.1#Ethernet", "udp://1.1.1.1#VPN")]
    [InlineData("https://dns.example/dns-query#h3=true&ecs-override=true",
        "https://dns.example/dns-query#VPN&h3=true&ecs-override=true")]
    [InlineData("https://dns.example/dns-query#&h3=true", "https://dns.example/dns-query#VPN&h3=true")]
    [InlineData("udp://1.1.1.1#", "udp://1.1.1.1#VPN")]
    public void Dns_route_selector_replaces_an_existing_fragment_and_preserves_options(
        string upstream, string expected)
    {
        var yaml = BuildWireGuard([new("youtube.com", Kind: RouteKind.Domain)], [upstream]);
        var routed = Assert.Single(ReadDnsPolicies(yaml)["+.youtube.com"]);

        Assert.Equal(expected, routed);
        Assert.Equal(1, routed.Count(character => character == '#'));
        Assert.Equal([upstream], ReadDnsList(yaml, "nameserver"));
    }

    private static string BuildWireGuard(IReadOnlyList<RouteTarget> targets, IReadOnlyList<string> dns, bool exceptListed = false)
        => MihomoProfileWriter.Build(new AmneziaWgProfile
        {
            PrivateKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            PublicKey = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=",
            AddressV4 = "10.9.9.2",
            EndpointHost = "203.0.113.10",
            EndpointPort = 51820,
            DnsServers = dns
        }, targets, 17990, "synthetic-controller-secret", exceptListed: exceptListed);

    private static Dictionary<string, List<string>> ReadDnsPolicies(string yaml)
    {
        var policies = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<string>? current = null;
        foreach (var line in DnsSectionLines(yaml, "nameserver-policy"))
        {
            if (line.StartsWith("      - ", StringComparison.Ordinal))
                current!.Add(JsonSerializer.Deserialize<string>(line[8..])!);
            else if (line.StartsWith("    ", StringComparison.Ordinal) && line.EndsWith(':'))
            {
                var domain = JsonSerializer.Deserialize<string>(line[4..^1])!;
                current = [];
                policies.Add(domain, current);
            }
        }
        return policies;
    }

    private static string[] ReadDnsList(string yaml, string key)
        => DnsSectionLines(yaml, key).Select(line => line[6..])
            .Select(value => value.StartsWith('"') ? JsonSerializer.Deserialize<string>(value)! : value)
            .ToArray();

    private static IEnumerable<string> DnsSectionLines(string yaml, string key)
    {
        var reading = false;
        foreach (var line in yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (!reading)
            {
                reading = line == "  " + key + ":";
                continue;
            }
            if (!line.StartsWith("    ", StringComparison.Ordinal))
                yield break;
            yield return line;
        }
    }
}
