using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SelectiveVpn.Models;

namespace SelectiveVpn.Engine;

public static class MihomoProfileWriter
{
    public static string Build(
        AmneziaWgProfile profile,
        IReadOnlyList<RouteTarget> targets,
        int apiPort,
        string apiSecret,
        int vpnCheckPort = 0,
        bool exceptListed = false)
    {
        var builder = new StringBuilder();
        AppendPreamble(builder, apiPort, apiSecret, vpnCheckPort, outboundProxy: "awg");
        AppendDns(builder, Nameservers(profile), targets, exceptListed);
        builder.AppendLine("proxies:");
        builder.AppendLine("  - name: awg");
        builder.AppendLine("    type: wireguard");
        builder.AppendLine("    private-key: " + Quote(profile.PrivateKey));
        builder.AppendLine("    server: " + Quote(profile.EndpointHost));
        builder.AppendLine("    port: " + profile.EndpointPort.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("    ip: " + Quote(profile.AddressV4));
        if (profile.AddressV6 is not null)
            builder.AppendLine("    ipv6: " + Quote(profile.AddressV6));
        builder.AppendLine("    public-key: " + Quote(profile.PublicKey));
        if (profile.PresharedKey is not null)
            builder.AppendLine("    pre-shared-key: " + Quote(profile.PresharedKey));
        builder.AppendLine("    allowed-ips: [" + string.Join(", ", profile.AllowedIps.Select(Quote)) + "]");
        builder.AppendLine("    udp: true");
        if (profile.Mtu is int mtu)
            builder.AppendLine("    mtu: " + mtu.ToString(CultureInfo.InvariantCulture));
        if (profile.PersistentKeepalive is int keepalive)
            builder.AppendLine("    persistent-keepalive: " + keepalive.ToString(CultureInfo.InvariantCulture));

        AppendAmnezia(builder, profile);
        builder.AppendLine("proxy-groups:");
        builder.AppendLine("  - name: VPN");
        builder.AppendLine("    type: select");
        builder.AppendLine("    proxies:");
        builder.AppendLine("      - awg");
        AppendRules(builder, targets, exceptListed);
        return builder.ToString();
    }

    public static string BuildSubscription(
        SubscriptionProfile subscription,
        IReadOnlyList<RouteTarget> targets,
        int apiPort,
        string apiSecret,
        string providerCachePath,
        string hardwareId,
        int vpnCheckPort = 0,
        bool exceptListed = false)
    {
        var builder = new StringBuilder();
        AppendPreamble(builder, apiPort, apiSecret, vpnCheckPort, outboundProxy: "VPN");
        AppendDns(builder, ["udp://1.1.1.1", "udp://8.8.8.8"], targets, exceptListed);
        builder.AppendLine("proxy-providers:");
        builder.AppendLine("  subscription:");
        builder.AppendLine("    type: http");
        builder.AppendLine("    url: " + Quote(subscription.Url));
        builder.AppendLine("    path: " + Quote(providerCachePath.Replace('\\', '/')));
        builder.AppendLine("    interval: 3600");
        builder.AppendLine("    health-check:");
        builder.AppendLine("      enable: true");
        builder.AppendLine("      url: https://www.gstatic.com/generate_204");
        builder.AppendLine("      interval: 300");
        builder.AppendLine("      lazy: true");
        builder.AppendLine("    header:");
        builder.AppendLine("      User-Agent:");
        builder.AppendLine("        - " + Quote("SelectiveVpn/" + MihomoRelease.Version));
        builder.AppendLine("      x-hwid:");
        builder.AppendLine("        - " + Quote(hardwareId));
        builder.AppendLine("      x-device-os:");
        builder.AppendLine("        - " + Quote("Windows"));
        builder.AppendLine("      x-ver-os:");
        builder.AppendLine("        - " + Quote(Environment.OSVersion.Version.ToString()));
        builder.AppendLine("      x-device-model:");
        builder.AppendLine("        - " + Quote("SelectiveVpn"));
        builder.AppendLine("proxy-groups:");
        builder.AppendLine("  - name: " + Quote(SubscriptionProfile.AutoNodeName));
        builder.AppendLine("    type: url-test");
        builder.AppendLine("    use:");
        builder.AppendLine("      - subscription");
        builder.AppendLine("    url: https://www.gstatic.com/generate_204");
        builder.AppendLine("    interval: 300");
        builder.AppendLine("    tolerance: 100");
        builder.AppendLine("  - name: VPN");
        builder.AppendLine("    type: select");
        builder.AppendLine("    proxies:");
        builder.AppendLine("      - " + Quote(SubscriptionProfile.AutoNodeName));
        builder.AppendLine("    use:");
        builder.AppendLine("      - subscription");
        AppendRules(builder, targets, exceptListed);
        return builder.ToString();
    }

    private static void AppendPreamble(
        StringBuilder builder,
        int apiPort,
        string apiSecret,
        int vpnCheckPort,
        string outboundProxy)
    {
        builder.AppendLine("allow-lan: false");
        builder.AppendLine("mode: rule");
        builder.AppendLine("log-level: info");
        builder.AppendLine("find-process-mode: always");
        builder.AppendLine("ipv6: true");
        builder.AppendLine("external-controller: " + Quote($"127.0.0.1:{apiPort.ToString(CultureInfo.InvariantCulture)}"));
        builder.AppendLine("secret: " + Quote(apiSecret));
        if (vpnCheckPort > 0)
        {
            builder.AppendLine("listeners:");
            builder.AppendLine("  - name: vpn-ip-check");
            builder.AppendLine("    type: mixed");
            builder.AppendLine("    listen: 127.0.0.1");
            builder.AppendLine("    port: " + vpnCheckPort.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("    proxy: " + outboundProxy);
        }

        builder.AppendLine("tun:");
        builder.AppendLine("  enable: true");
        builder.AppendLine("  stack: mixed");
        builder.AppendLine("  auto-route: true");
        builder.AppendLine("  auto-detect-interface: true");
        builder.AppendLine("  strict-route: false");
        builder.AppendLine("  dns-hijack:");
        builder.AppendLine("    - any:53");

        // Domain lists need SNI/Host sniffing: browsers often use DoH and connect by IP.
        builder.AppendLine("sniffer:");
        builder.AppendLine("  enable: true");
        builder.AppendLine("  force-dns-mapping: true");
        builder.AppendLine("  parse-pure-ip: true");
        builder.AppendLine("  override-destination: true");
        builder.AppendLine("  sniff:");
        builder.AppendLine("    HTTP:");
        builder.AppendLine("      ports: [80, 8080-8880]");
        builder.AppendLine("    TLS:");
        builder.AppendLine("      ports: [443, 8443]");
        builder.AppendLine("    QUIC:");
        builder.AppendLine("      ports: [443]");
    }

    private static void AppendDns(
        StringBuilder builder,
        IEnumerable<string> nameservers,
        IReadOnlyList<RouteTarget> targets,
        bool exceptListed)
    {
        var upstreams = nameservers.ToArray();
        builder.AppendLine("dns:");
        builder.AppendLine("  enable: true");
        builder.AppendLine("  ipv6: false");
        builder.AppendLine("  enhanced-mode: fake-ip");
        builder.AppendLine("  fake-ip-range: 198.18.0.1/16");
        builder.AppendLine("  fake-ip-filter:");
        builder.AppendLine("    - '*.lan'");
        builder.AppendLine("    - '*.local'");
        builder.AppendLine("    - localhost");
        builder.AppendLine("    - '*.localhost'");
        builder.AppendLine("    - '+.localdomain'");
        builder.AppendLine("  respect-rules: true");
        builder.AppendLine("  default-nameserver:");
        builder.AppendLine("    - 1.1.1.1");
        builder.AppendLine("    - 8.8.8.8");
        builder.AppendLine("  nameserver:");
        foreach (var server in upstreams)
            builder.AppendLine("    - " + Quote(server));

        // respect-rules routes the DNS transport by the resolver IP, not by the queried domain.
        // Bind included domains to VPN so split tunneling cannot send their DNS
        // through MATCH,DIRECT while the eventual HTTPS connection is supposed to use VPN.
        var domains = targets
            .Where(target => target.EffectiveKind == RouteKind.Domain)
            .Select(target => target.Path.Trim().TrimEnd('.').ToLowerInvariant())
            .Where(domain => domain.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (domains.Length > 0 && !exceptListed)
        {
            var policyUpstreams = upstreams.Select(server => WithDnsPolicy(server, "VPN")).ToArray();
            builder.AppendLine("  nameserver-policy:");
            foreach (var domain in domains)
            {
                builder.AppendLine("    " + Quote("+." + domain) + ":");
                foreach (var server in policyUpstreams)
                    builder.AppendLine("      - " + Quote(server));
            }
        }

        // Proxy endpoint bootstrap must stay independent of the VPN-bound domain policy.
        builder.AppendLine("  proxy-server-nameserver:");
        builder.AppendLine("    - 1.1.1.1");
        builder.AppendLine("    - 8.8.8.8");
    }

    private static string WithDnsPolicy(string nameserver, string policy)
    {
        var separator = nameserver.IndexOf('#');
        if (separator < 0)
            return nameserver + "#" + policy;

        var endpoint = nameserver[..separator];
        var parameters = nameserver[(separator + 1)..].Split('&');
        // A bare first fragment is a proxy/interface selector; key=value fragments are options.
        var options = parameters
            .Where((parameter, index) => parameter.Length > 0 && (index > 0 || parameter.Contains('=')))
            .ToArray();
        return endpoint + "#" + policy + (options.Length == 0 ? "" : "&" + string.Join("&", options));
    }

    private static void AppendRules(
        StringBuilder builder,
        IReadOnlyList<RouteTarget> targets,
        bool exceptListed)
    {
        builder.AppendLine("rules:");
        if (targets.Count == 0)
        {
            builder.AppendLine("  - MATCH,VPN");
            return;
        }

        var listed = exceptListed ? "DIRECT" : "VPN";
        foreach (var target in targets)
        {
            switch (target.EffectiveKind)
            {
                case RouteKind.Folder:
                    builder.AppendLine("  - " + YamlSingle(FolderRule(target.Path, listed)));
                    break;
                case RouteKind.Domain:
                    builder.AppendLine("  - " + YamlSingle($"DOMAIN-SUFFIX,{target.Path},{listed}"));
                    break;
                case RouteKind.Ip:
                    builder.AppendLine("  - " + YamlSingle(
                        (target.Path.Contains(':') ? "IP-CIDR6," : "IP-CIDR,") + target.Path + "," + listed));
                    break;
                default:
                    // Exact EXE plus its install folder so helpers next to the launcher match immediately.
                    builder.AppendLine("  - " + YamlSingle($"PROCESS-PATH,{target.Path},{listed}"));
                    var parent = Path.GetDirectoryName(target.Path);
                    if (!string.IsNullOrWhiteSpace(parent))
                        builder.AppendLine("  - " + YamlSingle(FolderRule(parent, listed)));
                    break;
            }
        }

        builder.AppendLine(exceptListed ? "  - MATCH,VPN" : "  - MATCH,DIRECT");
    }

    private static void AppendAmnezia(StringBuilder builder, AmneziaWgProfile profile)
    {
        if (!profile.HasAmneziaOptions)
            return;

        builder.AppendLine("    amnezia-wg-option:");
        if (profile.IsVersion3)
            builder.AppendLine("      version: 3");

        AppendInt(builder, "jc", profile.Jc);
        AppendInt(builder, "jmin", profile.Jmin);
        AppendInt(builder, "jmax", profile.Jmax);
        AppendInt(builder, "s1", profile.S1);
        AppendInt(builder, "s2", profile.S2);
        AppendInt(builder, "s3", profile.S3);
        AppendInt(builder, "s4", profile.S4);
        AppendText(builder, "h1", profile.H1);
        AppendText(builder, "h2", profile.H2);
        AppendText(builder, "h3", profile.H3);
        AppendText(builder, "h4", profile.H4);
        AppendText(builder, "i1", profile.I1);
        AppendText(builder, "i2", profile.I2);
        AppendText(builder, "i3", profile.I3);
        AppendText(builder, "i4", profile.I4);
        AppendText(builder, "i5", profile.I5);

        if (!profile.IsVersion3)
        {
            AppendText(builder, "j1", profile.J1);
            AppendText(builder, "j2", profile.J2);
            AppendText(builder, "j3", profile.J3);
            AppendInt(builder, "itime", profile.ITime);
        }

        AppendText(builder, "header-protection-key", profile.HeaderProtectionKey);
        AppendText(builder, "content-padding-addition", profile.ContentPaddingAddition);
        AppendText(builder, "rekey-after-time", profile.RekeyAfterTime);
        AppendText(builder, "rekey-timeout", profile.RekeyTimeout);
        AppendText(builder, "reject-after-time", profile.RejectAfterTime);
        AppendText(builder, "keepalive-timeout", profile.KeepaliveTimeout);
        AppendText(builder, "max-handshake-attempts", profile.MaxHandshakeAttempts);
        AppendBool(builder, "random-trailers", profile.RandomTrailers);
        AppendBool(builder, "disable-cookies", profile.DisableCookies);
    }

    private static IEnumerable<string> Nameservers(AmneziaWgProfile profile)
    {
        if (profile.DnsServers.Count == 0)
        {
            yield return "udp://1.1.1.1";
            yield return "udp://8.8.8.8";
            yield break;
        }

        foreach (var server in profile.DnsServers)
        {
            yield return server.Contains("://", StringComparison.Ordinal)
                ? server
                : "udp://" + server;
        }
    }

    private static void AppendInt(StringBuilder builder, string name, int? value)
    {
        if (value is int number)
            builder.AppendLine($"      {name}: {number.ToString(CultureInfo.InvariantCulture)}");
    }

    private static void AppendText(StringBuilder builder, string name, string? value)
    {
        if (value is not null)
            builder.AppendLine($"      {name}: {Quote(value)}");
    }

    private static void AppendBool(StringBuilder builder, string name, bool? value)
    {
        if (value is bool flag)
            builder.AppendLine($"      {name}: {(flag ? "true" : "false")}");
    }

    private static string Quote(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        return "\"" + escaped + "\"";
    }

    private static string FolderRule(string path, string policy)
    {
        var folder = path.TrimEnd('\\', '/');
        var escaped = Regex.Escape(folder + "\\");
        return $"PROCESS-PATH-REGEX,(?i)^{escaped}.*,{policy}";
    }

    private static string YamlSingle(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
