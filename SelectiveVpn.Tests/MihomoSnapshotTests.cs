using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;

namespace SelectiveVpn.Tests;

public sealed class MihomoSnapshotTests
{
    [Fact]
    public async Task Child_process_reload_keeps_the_running_configuration_when_profile_file_is_edited()
    {
        var parent = Path.Combine(Path.GetTempPath(), "SelectiveVpn.Snapshot.Tests");
        var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var engine = new MihomoEngine(root);
        using var current = Process.GetCurrentProcess();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var input = Path.Combine(root, "input.conf");
            var runtime = Path.Combine(root, "runtime.yaml");
            var text = "[Interface]\nPrivateKey = AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\nAddress = 10.9.9.2/32\n[Peer]\nPublicKey = AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\nEndpoint = original.example:51820\nAllowedIPs = 0.0.0.0/0\n";
            await File.WriteAllTextAsync(input, text, timeout.Token);
            var parsed = AmneziaWgConfigReader.ReadFile(input);
            Assert.True(parsed.Succeeded);
            Set("_activeWireGuardProfile", parsed.Profile);
            Set("_configPath", runtime);
            Set("_apiPort", ((IPEndPoint)listener.LocalEndpoint).Port);
            Set("_apiSecret", "synthetic-test-secret");
            // Only IsRunning is queried; this test never calls StartAsync/StopAsync or kills a process.
            Set("_process", current);
            await File.WriteAllTextAsync(input, text.Replace("original.example", "unapplied.example"), timeout.Token);
            var server = ReplyAsync();
            var reload = typeof(MihomoEngine).GetMethod("ReloadExpandedConfigAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var request = new MihomoStartRequest(input, [new RouteTarget(Path.Combine(root, "parent.exe"))]);
            var result = await (Task<bool>)reload.Invoke(engine, [request, new[] { Path.Combine(root, "child.exe") }, timeout.Token])!;
            await server;
            Assert.True(result);
            var yaml = await File.ReadAllTextAsync(runtime, timeout.Token);
            Assert.Contains("original.example", yaml);
            Assert.DoesNotContain("unapplied.example", yaml);
            Assert.Contains("child.exe", yaml);
        }
        finally
        {
            Set("_process", null);
            var resolved = Path.GetFullPath(root);
            Assert.StartsWith(Path.GetFullPath(parent) + Path.DirectorySeparatorChar, resolved, StringComparison.OrdinalIgnoreCase);
            Directory.Delete(resolved, recursive: true);
        }

        void Set(string name, object? value) => typeof(MihomoEngine).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(engine, value);

        async Task ReplyAsync()
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 }) { }
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), timeout.Token);
        }
    }
}
