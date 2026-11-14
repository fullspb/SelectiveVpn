using System.Text;
using SelectiveVpn.Engine;

namespace SelectiveVpn.Tests;

public class ShareLinkImportTests
{
    private const string Reality =
        "vless://11111111-2222-3333-4444-555555555555@203.0.113.10:443?type=tcp&security=reality&pbk=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&fp=chrome&sni=example.com&sid=0123abcd&flow=xtls-rprx-vision#Amsterdam%201";

    private const string Trojan = "trojan://secret-pass@node.example.com:8443?security=tls&sni=node.example.com#Trojan";

    [Fact]
    public void Imports_a_single_vless_link_as_an_inline_profile()
    {
        WithDirectory(dir =>
        {
            var result = ConfigSubscriptionImporter.ImportShareLinks(Reality, profilesDirectory: dir);

            Assert.True(result.Succeeded, result.Message);
            Assert.True(SubscriptionProfile.IsSubscriptionPath(result.SavedPath));
            Assert.True(SubscriptionProfile.TryRead(result.SavedPath!, out var profile, out var error), error);
            Assert.True(profile.IsInline);
            Assert.Equal("", profile.Url);
            Assert.Equal([Reality], profile.Links!);
            Assert.Equal("Amsterdam 1", profile.Title);
            Assert.Equal(1, profile.NodeCount);
            Assert.Equal(["vless"], profile.Protocols);
        });
    }

    [Fact]
    public void Imports_several_links_once_each_and_keeps_the_given_name()
    {
        WithDirectory(dir =>
        {
            var input = $"\uFEFF# my servers\r\n{Reality}\r\n\r\n  {Trojan}  \r\n{Reality}\r\n";

            var result = ConfigSubscriptionImporter.ImportShareLinks(input, "Home", dir);

            Assert.True(result.Succeeded, result.Message);
            Assert.True(SubscriptionProfile.TryRead(result.SavedPath!, out var profile, out var error), error);
            Assert.Equal("Home", profile.Title);
            Assert.Equal([Reality, Trojan], profile.Links!);
            Assert.Equal(2, profile.NodeCount);
            Assert.Equal(["trojan", "vless"], profile.Protocols);
        });
    }

    [Theory]
    [InlineData("vless://11111111-2222-3333-4444-555555555555@203.0.113.10?type=tcp#no-port")]
    [InlineData("vless://203.0.113.10:443?type=tcp#no-id")]
    [InlineData("vless://11111111-2222-3333-4444-555555555555@203.0.113.10:443#ok\nhello world")]
    [InlineData("vless://00000000-0000-0000-0000-000000000000@0.0.0.0:1?type=tcp#placeholder")]
    public void Rejects_incomplete_mixed_and_placeholder_input(string input)
    {
        WithDirectory(dir =>
        {
            var result = ConfigSubscriptionImporter.ImportShareLinks(input, profilesDirectory: dir);

            Assert.False(result.Succeeded);
            Assert.False(string.IsNullOrWhiteSpace(result.Message));
            Assert.False(Directory.Exists(dir) && Directory.EnumerateFiles(dir).Any());
        });
    }

    [Fact]
    public void Dialog_validation_accepts_server_links_and_http_urls_only()
    {
        Assert.True(ConfigSubscriptionImporter.TryValidateInput(Reality, out _));
        Assert.True(ConfigSubscriptionImporter.TryValidateInput("\n  " + Trojan, out _));
        Assert.True(ConfigSubscriptionImporter.TryValidateInput("https://sub.example.com/abc", out _));
        Assert.False(ConfigSubscriptionImporter.TryValidateInput("", out var empty));
        Assert.False(string.IsNullOrWhiteSpace(empty));
        Assert.False(ConfigSubscriptionImporter.TryValidateInput("ftp://example.com/a.conf", out var ftp));
        Assert.Contains("vless://", ftp);
    }

    [Fact]
    public async Task ImportAsync_does_not_touch_the_network_for_server_links()
    {
        var dir = NewDirectory();
        try
        {
            var handler = new ThrowingHandler();

            var result = await ConfigSubscriptionImporter.ImportAsync(Reality, handler: handler, profilesDirectory: dir);

            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(0, handler.Requests);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Inline_profile_becomes_a_file_provider_with_base64_links()
    {
        var profile = new SubscriptionProfile { Url = "", Links = [Reality, Trojan], Title = "Home", Protocols = ["trojan", "vless"] };

        var yaml = MihomoProfileWriter.BuildSubscription(profile, [], 17990, "secret", @"C:\temp\providers\subscription.yaml", "hwid", 17991);

        Assert.Contains("type: file", yaml);
        Assert.Contains("C:/temp/providers/subscription.yaml", yaml);
        Assert.DoesNotContain("type: http", yaml);
        Assert.DoesNotContain("url: \"\"", yaml);
        Assert.DoesNotContain("x-hwid", yaml);
        Assert.Contains("health-check:", yaml);
        Assert.Contains(SubscriptionProfile.AutoNodeName, yaml);
        // The links themselves, with their identifiers, never go into the main config.
        Assert.DoesNotContain("11111111-2222-3333-4444-555555555555", yaml);

        var payload = profile.BuildProviderPayload();
        Assert.Equal(Reality + "\n" + Trojan, Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        Assert.Equal(["Amsterdam 1", "Trojan"], SubscriptionNodeCatalog.ExtractNames(payload));
        Assert.Equal(["11111111-2222-3333-4444-555555555555", "secret-pass"], profile.SecretValues());
    }

    [Fact]
    public void Subscription_by_url_still_reads_and_ignores_empty_links()
    {
        WithDirectory(dir =>
        {
            var path = Path.Combine(dir, "sub" + SubscriptionProfile.FileExtension);
            SubscriptionProfile.SaveToPath(path, new SubscriptionProfile { Url = "https://sub.example.com/abc", Links = [], Title = "Sub" });

            Assert.True(SubscriptionProfile.TryRead(path, out var profile, out var error), error);
            Assert.False(profile.IsInline);
            Assert.Equal("https://sub.example.com/abc", profile.Url);

            File.WriteAllText(path, """{"kind":"subscription","url":"","links":["not a link"],"title":"Broken"}""");
            Assert.False(SubscriptionProfile.TryRead(path, out _, out _));
        });
    }

    private static void WithDirectory(Action<string> body)
    {
        var dir = NewDirectory();
        try
        {
            body(dir);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static string NewDirectory()
        => Path.Combine(Path.GetTempPath(), "SelectiveVpnTests", Guid.NewGuid().ToString("N"));

    private static void Cleanup(string dir)
    {
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            throw new HttpRequestException("Server links must not be fetched.");
        }
    }
}
