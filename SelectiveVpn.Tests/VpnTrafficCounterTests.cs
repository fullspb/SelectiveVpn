using SelectiveVpn.Engine;

namespace SelectiveVpn.Tests;

public class EngineLogFilterTests
{
    [Fact]
    public void Hides_routine_vpn_dial_warnings()
    {
        const string line = "time=\"2026-11-07T20:32:33+03:00\" level=warning msg=\"[TCP] dial VPN (match ProcessPathRegex/(?i)^C:\\\\Program\\\\ Files\\\\Google\"";

        Assert.False(EngineLogFilter.ShouldShow(line));
        Assert.False(EngineLogFilter.ShouldShow("[TUN] default interface changed by monitor"));
        Assert.False(EngineLogFilter.ShouldShow(
            "time=\"2026-11-07T21:15:09+03:00\" level=warning msg=\"[TCP] dial VPN (match Match/) 198.18.0.1:50989(Grok Bot.exe) --> 198.18.0.12:443 error\""));
    }

    [Fact]
    public void Shows_real_engine_failures()
    {
        Assert.True(EngineLogFilter.ShouldShow("level=error msg=\"handshake failed\""));
        Assert.True(EngineLogFilter.ShouldShow("level=fatal msg=\"tun start failed\""));
        Assert.False(EngineLogFilter.ShouldShow("level=warning msg=\"bind: address already in use\""));
    }
}

public class VpnTrafficCounterTests
{
    [Fact]
    public void Counts_only_vpn_connections_and_keeps_closed_ones()
    {
        var counter = new VpnTrafficCounter();
        counter.Apply("""
            {
              "connections": [
                {"id":"direct","upload":9000,"download":8000,"chains":["DIRECT"]},
                {"id":"vpn","upload":100,"download":400,"chains":["awg","VPN"]}
              ]
            }
            """);

        Assert.Equal(400, counter.IncomingBytes);
        Assert.Equal(100, counter.OutgoingBytes);

        counter.Apply("""
            {
              "connections": [
                {"id":"vpn","upload":150,"download":900,"chains":["awg","VPN"]}
              ]
            }
            """);

        Assert.Equal(900, counter.IncomingBytes);
        Assert.Equal(150, counter.OutgoingBytes);

        counter.Apply("""{"connections":[]}""");

        Assert.Equal(900, counter.IncomingBytes);
        Assert.Equal(150, counter.OutgoingBytes);
    }

    [Fact]
    public void Reset_clears_the_session()
    {
        var counter = new VpnTrafficCounter();
        counter.Apply("""{"connections":[{"id":"vpn","upload":10,"download":20,"chains":["awg"]}]}""");
        counter.Reset();

        Assert.Equal(0, counter.IncomingBytes);
        Assert.Equal(0, counter.OutgoingBytes);
    }
}
