using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;
using SelectiveVpn.Services;
using SelectiveVpn.ViewModels;

namespace SelectiveVpn.UiTests;

/// <summary>
/// Renders the README screenshots from demo data, without a real VPN or user settings.
/// Run with SVPN_README_SHOTS set to the output folder, for example docs\screenshots.
/// </summary>
internal static class ReadmeShots
{
    public static async Task<int> RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SelectiveVpn;component/Localization/Lang.en.xaml")
        });
        var time = new ManualConnectionTimeProvider();
        await using var fixture = new TestFixture(timeProvider: time, enableTrafficPolling: true, configureSettings: settings =>
        {
            var root = Path.GetDirectoryName(settings.ConfigPath)!;
            var config = File.ReadAllText(settings.ConfigPath!);
            string Conf(string name)
            {
                var path = Path.Combine(root, name + ".conf");
                File.WriteAllText(path, config);
                return path;
            }
            var home = Conf("Home VPN");
            // AmneziaWG obfuscation settings, so the card reads AmneziaWG; Work stays plain WireGuard.
            File.WriteAllText(home, config.Replace("[Peer]", "Jc = 4\nJmin = 40\nJmax = 70\nS1 = 15\nS2 = 30\nH1 = 1\nH2 = 2\nH3 = 3\nH4 = 4\n[Peer]"));
            var work = Conf("Work");
            var travel = Path.Combine(root, "Travel" + SubscriptionProfile.FileExtension);
            SubscriptionProfile.SaveToPath(travel, new SubscriptionProfile { Url = "https://example.invalid/not-fetched", Title = "Travel", NodeCount = 12 });
            List<SavedTarget> Demo() =>
            [
                new() { Path = @"C:\Program Files\Google\Chrome\Application\chrome.exe", Kind = "file" },
                new() { Path = @"C:\Windows\System32\OpenSSH\ssh.exe", Kind = "file" },
                new() { Path = @"C:\Windows\System32\curl.exe", Kind = "file" },
                new() { Path = @"C:\Program Files\dotnet", Kind = "folder" },
                new() { Path = "youtube.com", Kind = "domain" },
                new() { Path = "github.com", Kind = "domain" }
            ];
            settings.ConfigPath = home;
            settings.Language = AppLanguage.English;
            settings.Theme = AppTheme.Default;
            settings.Profiles =
            [
                new SavedProfile { ConfigPath = home, Targets = Demo(), IncludeTargets = Demo(), ExcludeTargets = [] },
                new SavedProfile { ConfigPath = work, DisplayName = "Office", Targets = [], IncludeTargets = [], ExcludeTargets = [] },
                new SavedProfile { ConfigPath = travel, Targets = [], IncludeTargets = [], ExcludeTargets = [] }
            ];
        });
        fixture.DelayChoice = (delay, token) => Task.Delay(delay, token);
        fixture.Engine.IpBehavior = _ => Task.FromResult(OperationResult.Ok("203.0.113.24"));
        fixture.CountryLookupChoice = (_, _) => Task.FromResult<string?>("NL");
        fixture.Engine.TrafficBehavior = _ =>
        {
            var (down, up) = DemoTotals(TrafficSample.Now);
            return Task.FromResult(new VpnTraffic(down, up));
        };

        var window = PopupChecks.CreateIsolatedWindow(PopupChecks.LoadWindowSource(), fixture);
        try
        {
            window.Show();
            await Settle(window);
            Save(window, Path.Combine(directory, "main-disconnected.jpg"));

            await fixture.Model.ConnectFromUserAsync();
            await (Task)typeof(MainViewModel).GetMethod("RefreshIpAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(fixture.Model, [false])!;
            await Check.UntilAsync(() => fixture.Model.ExternalIpCountryCode == "NL", "Demo country lookup did not finish.");
            time.Advance(new TimeSpan(1, 24, 37));
            await Task.Delay(1500);
            AddDemoTraffic(fixture.Model);
            await Task.Delay(800);
            await Settle(window);
            Console.WriteLine($"connected={fixture.Model.IsConnected} ip={fixture.Model.ExternalIp} time={fixture.Model.ConnectionDuration} in={fixture.Model.IncomingTraffic} rules={fixture.Model.Apps.Count}");
            Save(window, Path.Combine(directory, "main-connected.jpg"));

            fixture.Model.SelectedThemeId = AppTheme.Orbit;
            await Task.Delay(800);
            await Settle(window);
            Save(window, Path.Combine(directory, "main-orbit.jpg"));
            fixture.Model.SelectedThemeId = AppTheme.Default;
        }
        finally { window.Close(); }

        var settingsWindow = new SettingsWindow(true, false, AppLanguage.English, AppTheme.Default, true)
        {
            ShowInTaskbar = false, Opacity = 0, WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000
        };
        try
        {
            settingsWindow.Show();
            await Settle(settingsWindow);
            Save(settingsWindow, Path.Combine(directory, "settings.jpg"));
        }
        finally { settingsWindow.Close(); }
        return 0;
    }

    private static readonly double DemoStart = TrafficSample.Now;

    /// <summary>
    /// Running VPN byte totals at a moment: a smooth, always growing curve with a burst, so the
    /// engine's totals and the graph's history describe the same traffic.
    /// </summary>
    private static (long Down, long Up) DemoTotals(double seconds)
    {
        var t = seconds - DemoStart + 600;
        var burst = Math.Clamp((t - 575) / 6, 0, 1) * 9_000_000;
        var down = 315L * 1024 * 1024 + 620_000 * t - 2_700_000 * Math.Cos(t / 7) - 900_000 * Math.Cos(t / 2.3) + burst;
        var up = 492L * 1024 * 1024 + 75_000 * t - 300_000 * Math.Cos(t / 6) - 60_000 * Math.Cos(t / 1.7);
        return ((long)down, (long)up);
    }

    /// <summary>The minute before now, so the load graph is full when the shot is taken.</summary>
    private static void AddDemoTraffic(MainViewModel model)
    {
        var now = TrafficSample.Now;
        for (var second = 60; second >= 1; second--)
        {
            var (down, up) = DemoTotals(now - second);
            model.AddTrafficTotals(now - second, down, up);
        }
    }

    private static async Task Settle(Window window)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static void Save(Window window, string path)
    {
        const double scale = 2;
        var content = (FrameworkElement)window.Content;
        var width = (int)Math.Round(content.ActualWidth * scale);
        var height = (int)Math.Round(content.ActualHeight * scale);
        var layer = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        layer.Render(content);
        // The content is drawn over the window background, which the window itself paints.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            dc.DrawImage(layer, new Rect(0, 0, width, height));
        }
        var final = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        final.Render(visual);
        var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
        encoder.Frames.Add(BitmapFrame.Create(final));
        using var stream = File.Create(path);
        encoder.Save(stream);
        Console.WriteLine($"saved {path} {width}x{height}");
    }
}
