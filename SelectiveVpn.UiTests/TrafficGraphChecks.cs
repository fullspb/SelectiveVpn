using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SelectiveVpn.Controls;
using SelectiveVpn.Models;
using SelectiveVpn.Services;
using SelectiveVpn.ViewModels;

namespace SelectiveVpn.UiTests;

internal static class TrafficGraphChecks
{
    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
    [
        ("VPN byte totals turn into a minute of speed samples", SpeedSamplesAsync),
        ("the routing card shows a live VPN load graph instead of decorative lanes", GraphInWindowAsync)
    ];

    private static async Task SpeedSamplesAsync()
    {
        await using var fixture = new TestFixture();
        var model = fixture.Model;
        model.AddTrafficTotals(100, 1_000, 500);
        Check.True(!model.HasTrafficSamples, "The first totals only set the starting point.");

        model.AddTrafficTotals(102, 1_000 + 2 * 2048, 500 + 2 * 512);
        var sample = model.TrafficSamples.Single();
        Check.True(Math.Abs(sample.DownPerSecond - 2048) < 0.01 && Math.Abs(sample.UpPerSecond - 512) < 0.01,
            "Speed is the growth of the totals divided by the time between them.");
        Check.Equal("2 " + Loc.T("Unit.KB") + Loc.T("Unit.PerSecond"), model.DownloadRate, "Download speed is shown with its unit per second.");
        Check.Equal("512 " + Loc.T("Unit.B") + Loc.T("Unit.PerSecond"), model.UploadRate, "Upload speed is shown with its unit per second.");

        for (var second = 103; second <= 200; second++)
            model.AddTrafficTotals(second, 1_000 + second * 4096L, 500 + second * 100L);
        Check.True(model.TrafficSamples.First().Seconds >= 200 - MainViewModel.TrafficWindowSeconds - 2,
            "The graph keeps about a minute of samples, not the whole session.");
        Check.True(model.TrafficSamples.Zip(model.TrafficSamples.Skip(1)).All(pair => pair.First.Seconds < pair.Second.Seconds),
            "Samples stay in time order.");

        model.AddTrafficTotals(201, 10, 10);
        Check.Equal(200d, model.TrafficSamples[^1].Seconds, "Totals that go down give no sample.");
        model.AddTrafficTotals(202, 10 + 1024, 10);
        Check.True(model.TrafficSamples[^1] is { Seconds: 202, DownPerSecond: 1024, UpPerSecond: 0 }
            && model.TrafficSamples.All(item => item.DownPerSecond >= 0 && item.UpPerSecond >= 0),
            "Totals that start over do not produce a negative or huge speed.");
    }

    private static async Task GraphInWindowAsync()
    {
        var source = PopupChecks.LoadWindowSource();
        Check.True(!source.ToString().Contains("LaneRun", StringComparison.Ordinal),
            "The running dots that showed nothing real are gone.");

        await using var fixture = new TestFixture();
        var window = PopupChecks.CreateIsolatedWindow(source, fixture);
        try
        {
            window.Show();
            window.UpdateLayout();
            var graph = (TrafficGraph)window.FindName("VpnTrafficGraph");
            var idle = (TextBlock)window.FindName("TrafficIdleText");
            Check.True(graph is { IsVisible: true, ActualWidth: > 100, ActualHeight: > 40 }, "The VPN load graph has room in the routing card.");
            Check.True(idle.IsVisible && !graph.IsAnimating, "Before connecting the graph is still and explains when it fills.");

            await fixture.Model.ConnectFromUserAsync();
            var now = TrafficSample.Now;
            fixture.Model.AddTrafficTotals(now - 3, 0, 0);
            fixture.Model.AddTrafficTotals(now - 2, 300_000, 20_000);
            fixture.Model.AddTrafficTotals(now - 1, 900_000, 60_000);
            await Dispatcher.Yield(DispatcherPriority.Render);
            window.UpdateLayout();
            Check.True(!idle.IsVisible, "The idle hint hides once connected.");
            Check.True(graph.IsAnimating, "The graph scrolls while there are samples to show.");
            Check.True(((TextBlock)window.FindName("DownloadRateText")).IsVisible, "The current speed is shown above the graph.");
            await Check.UntilAsync(() => graph.Scale > 300_000, "The scale must grow to fit the busiest second.");

            await fixture.Model.DisconnectFromUserAsync();
            await Dispatcher.Yield(DispatcherPriority.Render);
            Check.True(!fixture.Model.HasTrafficSamples && !graph.IsAnimating && idle.IsVisible,
                "Disconnecting clears the graph and stops redrawing it.");
        }
        finally { window.Close(); }
    }
}
