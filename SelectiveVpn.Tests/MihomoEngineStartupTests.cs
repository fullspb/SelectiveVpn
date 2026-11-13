using SelectiveVpn.Engine;
using SelectiveVpn.Models;

namespace SelectiveVpn.Tests;

public sealed class MihomoEngineStartupTests
{
    [Fact]
    public async Task Invalid_engine_executable_returns_failure_and_removes_generated_secrets()
    {
        var root = Path.Combine(Path.GetTempPath(), "SelectiveVpn.Engine.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var enginePath = Path.Combine(root, "invalid-engine.exe");
            await File.WriteAllTextAsync(enginePath, "This is deliberately not an executable.");
            var profilePath = Path.Combine(root, "synthetic.conf");
            await File.WriteAllTextAsync(profilePath, SyntheticConfig);
            var engine = new MihomoEngine(root);

            var result = await engine.StartAsync(new MihomoStartRequest(profilePath, [], enginePath));

            Assert.False(result.Succeeded);
            Assert.False(engine.IsRunning);
            Assert.DoesNotContain("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", result.Message);
            Assert.False(File.Exists(Path.Combine(root, "runtime", "config.yaml")));
            Assert.True(File.Exists(profilePath));
        }
        finally
        {
            DeleteFixture(root);
        }
    }

    [Fact]
    public async Task Cancelled_start_leaves_no_generated_runtime_configuration()
    {
        var root = Path.Combine(Path.GetTempPath(), "SelectiveVpn.Engine.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var enginePath = Path.Combine(root, "invalid-engine.exe");
            await File.WriteAllTextAsync(enginePath, "This is deliberately not an executable.");
            var profilePath = Path.Combine(root, "synthetic.conf");
            await File.WriteAllTextAsync(profilePath, SyntheticConfig);
            var engine = new MihomoEngine(root);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var result = await engine.StartAsync(new MihomoStartRequest(profilePath, [], enginePath), cancellation.Token);

            Assert.False(result.Succeeded);
            Assert.Contains("отменено", result.Message);
            Assert.False(engine.IsRunning);
            Assert.False(File.Exists(Path.Combine(root, "runtime", "config.yaml")));
            Assert.True(File.Exists(profilePath));
        }
        finally
        {
            DeleteFixture(root);
        }
    }

    private static void DeleteFixture(string root)
    {
        var resolved = Path.GetFullPath(root);
        var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SelectiveVpn.Engine.Tests")) + Path.DirectorySeparatorChar;
        Assert.StartsWith(allowed, resolved, StringComparison.OrdinalIgnoreCase);
        Directory.Delete(resolved, recursive: true);
    }

    private const string SyntheticConfig = """
        [Interface]
        PrivateKey = AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
        Address = 10.9.9.2/32
        DNS = 1.1.1.1

        [Peer]
        PublicKey = BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=
        Endpoint = vpn.example.com:51820
        AllowedIPs = 0.0.0.0/0
        """;
}
