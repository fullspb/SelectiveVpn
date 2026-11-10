using System.Text;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;

namespace SelectiveVpn.Tests;

public class ConfigSubscriptionImporterTests
{
    private const string PrivateKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string PublicKey = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=";

    private static string SampleConf() => $"""
        [Interface]
        PrivateKey = {PrivateKey}
        Address = 10.9.9.2/32
        DNS = 1.1.1.1

        [Peer]
        PublicKey = {PublicKey}
        Endpoint = vpn.example.com:51820
        AllowedIPs = 0.0.0.0/0
        """;

    [Fact]
    public void Materializes_raw_conf()
    {
        Assert.True(ConfigSubscriptionImporter.TryMaterializeConfig(SampleConf(), out var conf, out var message));
        Assert.Contains("[Interface]", conf);
        Assert.Contains("AmneziaWG/WireGuard", message);
    }

    [Fact]
    public void Materializes_base64_conf()
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(SampleConf()));
        Assert.True(ConfigSubscriptionImporter.TryMaterializeConfig(encoded, out var conf, out var message));
        Assert.Contains("Endpoint = vpn.example.com:51820", conf);
        Assert.Contains("Base64", message);
    }

    [Fact]
    public void Materializes_subscription_lines_and_takes_first()
    {
        var first = Convert.ToBase64String(Encoding.UTF8.GetBytes(SampleConf()));
        var second = Convert.ToBase64String(Encoding.UTF8.GetBytes(SampleConf().Replace("10.9.9.2", "10.9.9.3")));
        var payload = first + Environment.NewLine + second;

        Assert.True(ConfigSubscriptionImporter.TryMaterializeConfig(payload, out var conf, out var message));
        Assert.Contains("10.9.9.2", conf);
        Assert.Contains("2 из 2", message);
    }

    [Fact]
    public void Materializes_clash_wireguard_yaml()
    {
        var yaml = $"""
            proxies:
              - name: Node A
                type: wireguard
                server: vpn.example.com
                port: 51820
                ip: 10.9.9.2
                private-key: {PrivateKey}
                public-key: {PublicKey}
                dns:
                  - 1.1.1.1
                mtu: 1420
                udp: true
            """;

        Assert.True(ConfigSubscriptionImporter.TryMaterializeConfig(yaml, out var conf, out var message));
        Assert.Contains("PrivateKey = " + PrivateKey, conf);
        Assert.Contains("Endpoint = vpn.example.com:51820", conf);
        Assert.Contains("Node A", message);

        var parsed = AmneziaWgConfigReader.Read(conf);
        Assert.True(parsed.Succeeded, parsed.Message);
    }

    [Fact]
    public void Rejects_unrelated_payload()
    {
        Assert.False(ConfigSubscriptionImporter.TryMaterializeConfig("hello world", out _, out var message));
        Assert.Contains("не распознана", message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://example.com/a.conf")]
    [InlineData("not-a-url")]
    public void Rejects_bad_urls(string url)
    {
        Assert.False(ConfigSubscriptionImporter.TryNormalizeUrl(url, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Saves_imported_profile_to_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SelectiveVpnTests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = ConfigSubscriptionImporter.ImportFromPayload(
                SampleConf(),
                displayName: "Demo Node",
                sourceHint: "example.com",
                profilesDirectory: dir);

            Assert.True(result.Succeeded, result.Message);
            Assert.True(File.Exists(result.SavedPath));
            Assert.Contains("Demo Node", Path.GetFileName(result.SavedPath));
            Assert.Contains("vpn.example.com", result.ProfileDescription);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Inspects_vless_subscription()
    {
        var payload = """
            vless://e414c8ce-20c2-4bcf-87ea-4881566e25ec@vbr.keepsecure.network:443?encryption=none&flow=xtls-rprx-vision&type=tcp&security=tls#RU
            vless://e414c8ce-20c2-4bcf-87ea-4881566e25ec@vbr.keepsecure.network:443?encryption=none&type=tcp&security=tls#EE
            """;

        Assert.True(ConfigSubscriptionImporter.TryInspectShareSubscription(payload, out var inspection));
        Assert.Equal(2, inspection.UsableCount);
        Assert.Contains("vless", inspection.Protocols);
    }

    [Fact]
    public void Detects_placeholder_only_subscription()
    {
        var payload = """
            vless://00000000-0000-0000-0000-000000000000@0.0.0.0:1?encryption=none&type=tcp&security=none#HWID
            vless://00000000-0000-0000-0000-000000000000@0.0.0.0:1?encryption=none&type=tcp&security=none#limit
            """;

        Assert.True(ConfigSubscriptionImporter.TryInspectShareSubscription(payload, out var inspection));
        Assert.Equal(2, inspection.TotalCount);
        Assert.Equal(0, inspection.UsableCount);
    }

    [Fact]
    public void Saves_uri_subscription_profile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SelectiveVpnTests", Guid.NewGuid().ToString("N"));
        try
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("""
                vless://e414c8ce-20c2-4bcf-87ea-4881566e25ec@node.example.com:443?encryption=none&type=tcp&security=tls#Node
                trojan://secret@node.example.com:443?security=tls#Trojan
                """));

            var result = ConfigSubscriptionImporter.ImportFromPayload(
                encoded,
                displayName: "Keep Secure",
                subscriptionUrl: "https://sub.example.com/sub/abc",
                profilesDirectory: dir);

            Assert.True(result.Succeeded, result.Message);
            Assert.True(SubscriptionProfile.IsSubscriptionPath(result.SavedPath));
            Assert.True(SubscriptionProfile.TryRead(result.SavedPath!, out var profile, out var error), error);
            Assert.Equal("https://sub.example.com/sub/abc", profile.Url);
            Assert.Equal("Keep Secure", profile.Title);
            Assert.Equal(2, profile.NodeCount);
            Assert.Contains("vless", profile.Protocols);
            Assert.Contains("trojan", profile.Protocols);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Inspects_clash_yaml_subscription_and_skips_placeholders()
    {
        var yaml = """
            proxies:
              - name: Dead
                type: vless
                server: 0.0.0.0
                port: 1
              - name: Live
                type: vless
                server: node.example.com
                port: 443
            proxy-groups:
              - name: Proxy
                type: select
                proxies:
                  - Live
            """;

        Assert.True(ConfigSubscriptionImporter.TryInspectClashSubscription(yaml, out var inspection));
        Assert.Equal(2, inspection.TotalCount);
        Assert.Equal(1, inspection.UsableCount);
        Assert.Contains("vless", inspection.Protocols);
    }

    [Fact]
    public void Saves_clash_yaml_as_subscription_profile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SelectiveVpnTests", Guid.NewGuid().ToString("N"));
        try
        {
            var yaml = """
                proxies:
                  - name: Live
                    type: trojan
                    server: node.example.com
                    port: 443
                """;

            var result = ConfigSubscriptionImporter.ImportFromPayload(
                yaml,
                displayName: "Clash Sub",
                subscriptionUrl: "https://sub.example.com/clash",
                profilesDirectory: dir);

            Assert.True(result.Succeeded, result.Message);
            Assert.True(SubscriptionProfile.IsSubscriptionPath(result.SavedPath));
            Assert.Contains("trojan", result.Message);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Builds_subscription_mihomo_yaml_with_provider_and_hwid()
    {
        var subscription = new SubscriptionProfile
        {
            Url = "https://sub.example.com/sub/abc",
            Title = "Demo",
            NodeCount = 3,
            Protocols = ["vless"]
        };

        var yaml = MihomoProfileWriter.BuildSubscription(
            subscription,
            [],
            17990,
            "secret",
            @"C:\temp\providers\subscription.yaml",
            "abc123hwid",
            vpnCheckPort: 17991);

        Assert.Contains("proxy-providers:", yaml);
        Assert.Contains("type: http", yaml);
        Assert.Contains("https://sub.example.com/sub/abc", yaml);
        Assert.Contains("x-hwid:", yaml);
        Assert.Contains("abc123hwid", yaml);
        Assert.Contains("type: url-test", yaml);
        Assert.Contains("type: select", yaml);
        Assert.Contains("use:", yaml);
        Assert.Contains("subscription", yaml);
        Assert.Contains("proxy: VPN", yaml);
        Assert.Contains("MATCH,VPN", yaml);
        Assert.Contains(SubscriptionProfile.AutoNodeName, yaml);
    }

    [Fact]
    public void Extracts_share_link_node_names()
    {
        var payload = """
            vless://id@node.example.com:443?encryption=none#Russia
            trojan://secret@node.example.com:443#Estonia
            """;
        var names = SubscriptionNodeCatalog.ExtractNames(payload);
        Assert.Contains("Russia", names);
        Assert.Contains("Estonia", names);
    }

    [Fact]
    public void Sorts_nodes_by_delay_keeping_auto_first()
    {
        var nodes = new[]
        {
            new VpnNodeInfo(SubscriptionProfile.AutoNodeName, SubscriptionProfile.AutoNodeDisplayName, true),
            new VpnNodeInfo("Slow", "Slow", false, 300),
            new VpnNodeInfo("Fast", "Fast", false, 40),
            new VpnNodeInfo("Dead", "Dead", false, 0)
        };

        var sorted = SubscriptionNodeCatalog.ApplyDelays(
            nodes,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Slow"] = 300,
                ["Fast"] = 40,
                ["Dead"] = 0
            },
            sortByDelay: true);

        Assert.Equal(SubscriptionProfile.AutoNodeName, sorted[0].Name);
        Assert.Equal("Fast", sorted[1].Name);
        Assert.Equal("Slow", sorted[2].Name);
        Assert.Equal("Dead", sorted[3].Name);
    }
}
