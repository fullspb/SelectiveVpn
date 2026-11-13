using System.Net.Http;
using System.Net;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SelectiveVpn.Models;
using SelectiveVpn.Services;
using SelectiveVpn.ViewModels;

namespace SelectiveVpn.UiTests;

internal static class IpCountryChecks
{
    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
    [
        ("IP country lookup uses the measured VPN exit address and clears on disconnect", MeasuredExitAddressAsync),
        ("an old IP-country response cannot replace a newer refresh", NewerRefreshWinsAsync),
        ("an old country response cannot leak across VPN sessions", NewSessionWinsAsync),
        ("delayed or failed country lookup never blocks the VPN or erases its IP", CountryFailureDoesNotBlockAsync),
        ("invalid country responses never produce a broken flag or caption", InvalidCountryResponsesAsync),
        ("the external-IP row renders bundled country images and hides them on disconnect", NativeFlagImageAsync),
        ("the country service queries the explicit exit IP and ignores mismatched or failed responses", CountryServiceBoundaryAsync)
    ];

    private static async Task MeasuredExitAddressAsync()
    {
        await using var fixture = new TestFixture();
        Check.Equal(0, fixture.CountryLookupRequests.Count, "Country lookup must not run before there is an active VPN exit address.");
        fixture.Engine.IpBehavior = _ => Task.FromResult(OperationResult.Ok("198.51.100.27"));
        fixture.CountryLookupChoice = (_, _) => Task.FromResult<string?>("NL");
        await fixture.Model.ConnectFromUserAsync();
        await RefreshIpAsync(fixture.Model);
        await Check.UntilAsync(() => fixture.Model.ExternalIpCountryCode == "NL", "The measured exit IP did not receive its country.");
        Check.True(fixture.CountryLookupRequests.All(ip => ip == "198.51.100.27"),
            "The lookup must use the measured external address, not the server endpoint, profile name or local public IP.");
        Check.Equal("198.51.100.27", fixture.Model.ExternalIp, "Country metadata must preserve the measured address.");
        Check.True(!string.IsNullOrWhiteSpace(fixture.Model.ExternalIpCountryName), "A valid country should expose a readable country tooltip.");
        await fixture.Model.DisconnectFromUserAsync();
        Check.Equal("", fixture.Model.ExternalIpCountryCode, "Disconnecting must hide the previous exit flag.");
        Check.Equal("", fixture.Model.ExternalIpCountryName, "Disconnecting must also clear the old country tooltip.");
    }

    private static async Task NewerRefreshWinsAsync()
    {
        await using var fixture = new TestFixture();
        await fixture.Model.ConnectFromUserAsync();
        var oldCountry = PendingCountry();
        fixture.CountryLookupChoice = (ip, _) => ip == "198.51.100.30" ? oldCountry.Task : Task.FromResult<string?>("DE");
        fixture.Engine.IpBehavior = _ => Task.FromResult(OperationResult.Ok("198.51.100.30"));
        var oldRefresh = RefreshIpAsync(fixture.Model);
        await Check.UntilAsync(() => fixture.CountryLookupRequests.Contains("198.51.100.30"), "The delayed country lookup did not begin.");
        try
        {
            fixture.Engine.IpBehavior = _ => Task.FromResult(OperationResult.Ok("198.51.100.31"));
            await RefreshIpAsync(fixture.Model);
            await Check.UntilAsync(() => fixture.Model.ExternalIpCountryCode == "DE", "The newer country lookup did not apply.");
            oldCountry.SetResult("JP"); // Intentionally ignores the cancelled request's token.
            await oldRefresh;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check.Equal("198.51.100.31", fixture.Model.ExternalIp, "An older country request must not overwrite the refreshed address.");
            Check.Equal("DE", fixture.Model.ExternalIpCountryCode, "An ignored cancellation must not restore a flag for an older IP.");
        }
        finally { oldCountry.TrySetResult(null); await oldRefresh; }
    }

    private static async Task NewSessionWinsAsync()
    {
        await using var fixture = new TestFixture();
        await fixture.Model.ConnectFromUserAsync();
        var oldCountry = PendingCountry();
        fixture.CountryLookupChoice = (ip, _) => ip == "198.51.100.40" ? oldCountry.Task : Task.FromResult<string?>("FR");
        fixture.Engine.IpBehavior = _ => Task.FromResult(OperationResult.Ok("198.51.100.40"));
        var oldRefresh = RefreshIpAsync(fixture.Model);
        await Check.UntilAsync(() => fixture.CountryLookupRequests.Contains("198.51.100.40"), "The previous-session country lookup did not begin.");
        try
        {
            await fixture.Model.DisconnectFromUserAsync();
            Check.Equal("", fixture.Model.ExternalIpCountryCode, "Disconnecting while country metadata is pending must leave no old flag.");
            fixture.Engine.IpBehavior = _ => Task.FromResult(OperationResult.Ok("198.51.100.41"));
            await fixture.Model.ConnectFromUserAsync();
            await RefreshIpAsync(fixture.Model);
            await Check.UntilAsync(() => fixture.Model.ExternalIpCountryCode == "FR", "The new session did not receive its own country.");
            oldCountry.SetResult("JP");
            await oldRefresh;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check.Equal("198.51.100.41", fixture.Model.ExternalIp, "An old session must not change the new exit IP.");
            Check.Equal("FR", fixture.Model.ExternalIpCountryCode, "An old country response must not cross a disconnect/reconnect boundary.");
        }
        finally { oldCountry.TrySetResult(null); await oldRefresh; }
    }

    private static async Task CountryFailureDoesNotBlockAsync()
    {
        await using var fixture = new TestFixture();
        var delayedCountry = PendingCountry();
        fixture.CountryLookupChoice = (_, _) => delayedCountry.Task;
        fixture.Engine.IpBehavior = _ => Task.FromResult(OperationResult.Ok("198.51.100.50"));
        var connection = fixture.Model.ConnectFromUserAsync();
        await connection;
        var refresh = RefreshIpAsync(fixture.Model);
        try
        {
            await Check.UntilAsync(() => fixture.Model.IsConnected && fixture.CountryLookupRequests.Count > 0,
                "The VPN did not connect while country lookup was delayed.");
            Check.True(connection.IsCompleted && refresh.IsCompleted && !fixture.Model.IsBusy,
                "Country metadata must not hold the VPN connection or IP refresh command open.");
            Check.Equal("198.51.100.50", fixture.Model.ExternalIp, "The external IP must appear before optional country metadata arrives.");
            delayedCountry.SetException(new HttpRequestException("Synthetic country service unavailable"));
            await connection;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check.True(fixture.Model.IsConnected && fixture.Engine.Starts.Count == 1 && fixture.Engine.Stops == 0,
                "A failed country lookup must not stop or reconnect the VPN.");
            Check.Equal("198.51.100.50", fixture.Model.ExternalIp, "A country-service failure must never erase an otherwise valid IP.");
            Check.Equal("", fixture.Model.ExternalIpCountryCode, "Unavailable country metadata should simply leave the flag hidden.");

            fixture.CountryLookupChoice = (_, _) => Task.FromResult<string?>("NL");
            await RefreshIpAsync(fixture.Model);
            await Check.UntilAsync(() => fixture.Model.ExternalIpCountryCode == "NL", "A later country lookup should still succeed after a failure.");
        }
        finally { delayedCountry.TrySetResult(null); await connection; await refresh; }
    }

    private static async Task InvalidCountryResponsesAsync()
    {
        await using var fixture = new TestFixture();
        await fixture.Model.ConnectFromUserAsync();
        fixture.CountryLookupChoice = (_, _) => Task.FromResult<string?>("NL");
        await RefreshIpAsync(fixture.Model);
        await Check.UntilAsync(() => fixture.Model.ExternalIpCountryCode == "NL", "The country flag must be visible before testing invalid metadata.");
        foreach (var invalid in new string?[] { null, "", "ZZ", "<html>", "123", "Netherlands" })
        {
            fixture.CountryLookupChoice = (_, _) => Task.FromResult(invalid);
            await RefreshIpAsync(fixture.Model);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check.Equal("", fixture.Model.ExternalIpCountryCode, "Invalid country metadata must not select a broken image: " + invalid);
            Check.Equal("", fixture.Model.ExternalIpCountryName, "Invalid metadata must not be displayed as a tooltip: " + invalid);
            Check.True(fixture.Model.IsConnected && fixture.Model.ExternalIp != "—", "Optional invalid metadata must preserve the working VPN and IP.");
        }
    }

    private static async Task NativeFlagImageAsync()
    {
        foreach (var code in new[] { "NL", "RU", "US" })
        {
            var image = CountryFlags.GetImage(code);
            Check.True(image is { PixelWidth: >= 24, PixelHeight: >= 16, IsFrozen: true },
                "Country flags must be locally decoded bitmap images that work without platform emoji support: " + code);
            Check.True(ReferenceEquals(image, CountryFlags.GetImage(code.ToLowerInvariant())),
                "Repeated UI bindings should reuse the normalized cached country image: " + code);
        }
        Check.True(CountryFlags.GetImage("ZZ") is null, "An unknown country must not request an absent image.");

        await using var fixture = new TestFixture();
        fixture.CountryLookupChoice = (_, _) => Task.FromResult<string?>("nl");
        const string longIpv6 = "2001:4860:4860:1234:5678:9abc:def0:1234";
        fixture.Engine.IpBehavior = _ => Task.FromResult(OperationResult.Ok(longIpv6));
        await fixture.Model.ConnectFromUserAsync();
        await RefreshIpAsync(fixture.Model);
        await Check.UntilAsync(() => fixture.Model.ExternalIpCountryCode == "NL", "The country lookup should normalize a lowercase ISO code.");
        var window = PopupChecks.CreateIsolatedWindow(PopupChecks.LoadWindowSource(), fixture);
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var flag = window.FindName("ExternalIpFlag") as Image;
            Check.True(flag is not null && flag.IsVisible && flag.Source is BitmapSource,
                "The actual external-IP row must render an Image control with country artwork, not an emoji character.");
            Check.True(ReferenceEquals(flag!.Source, CountryFlags.GetImage("NL")) && flag.ActualWidth >= 20 && flag.ActualHeight >= 14,
                "The IP row must display the measured country's cached image at a readable size.");
            Check.Equal(fixture.Model.ExternalIpCountryName, flag.ToolTip as string,
                "The actual country flag must expose its readable country name.");
            var address = (TextBlock)window.FindName("ExternalIpAddress");
            var body = (FrameworkElement)window.FindName("MainBody");
            var bounds = address.TransformToAncestor(body).TransformBounds(new Rect(address.RenderSize));
            Check.True(address.ActualWidth <= 240 && bounds.Left >= 0 && bounds.Right <= body.ActualWidth,
                "A long IPv6 address and its flag must remain within the fixed connection panel.");
            Check.Equal(longIpv6, address.ToolTip as string, "The full IPv6 address must remain available in its tooltip.");
            await fixture.Model.DisconnectFromUserAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check.True(!flag.IsVisible && flag.Source is null,
                "Disconnecting must remove both the rendered flag and its old bitmap source.");
        }
        finally { window.Close(); }
    }

    private static async Task CountryServiceBoundaryAsync()
    {
        using var handler = new FakeCountryHandler();
        using var client = new HttpClient(handler);
        handler.Reply = (_, _) => Task.FromResult(JsonResponse("{\"ip\":\"198.51.100.60\",\"country\":\"nl\"}"));
        Check.Equal("NL", await IpCountryLookup.LookupAsync("198.51.100.60", CancellationToken.None, client),
            "The country service should accept a valid response for the requested IPv4 exit address.");
        Check.True(handler.Requests.Last().Scheme == "https" && handler.Requests.Last().Host == "api.country.is"
            && handler.Requests.Last().AbsolutePath == "/198.51.100.60",
            "Country lookup must send the measured IP in the HTTPS path rather than geolocating the calling connection.");

        handler.Reply = (_, _) => Task.FromResult(JsonResponse("{\"ip\":\"2001:db8::60\",\"country\":\"DE\"}"));
        Check.Equal("DE", await IpCountryLookup.LookupAsync("2001:db8::60", CancellationToken.None, client),
            "An IPv6 exit address should also resolve its own country.");
        Check.Equal("/2001:db8::60", Uri.UnescapeDataString(handler.Requests.Last().AbsolutePath),
            "IPv6 must remain an explicit path value after URI escaping.");

        foreach (var invalid in new[]
        {
            "{\"ip\":\"198.51.100.99\",\"country\":\"NL\"}",
            "{\"ip\":\"198.51.100.60\",\"country\":\"ZZ\"}",
            "{\"ip\":\"198.51.100.60\",\"country\":24}",
            "{\"country\":\"NL\"}", "[]", "<html>unavailable</html>"
        })
        {
            handler.Reply = (_, _) => Task.FromResult(JsonResponse(invalid));
            Check.True(await IpCountryLookup.LookupAsync("198.51.100.60", CancellationToken.None, client) is null,
                "A malformed response or a country for a different address must never become a flag: " + invalid);
        }
        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
        {
            handler.Reply = (_, _) => Task.FromResult(new HttpResponseMessage(status));
            Check.True(await IpCountryLookup.LookupAsync("198.51.100.60", CancellationToken.None, client) is null,
                "An HTTP country-service failure must remain optional: " + status);
        }

        var calls = handler.Requests.Count;
        Check.True(await IpCountryLookup.LookupAsync("not an IP", CancellationToken.None, client) is null && calls == handler.Requests.Count,
            "An invalid address must not cause an HTTP request.");
        using var cancellation = new CancellationTokenSource();
        handler.Reply = async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return JsonResponse("{}");
        };
        var pending = IpCountryLookup.LookupAsync("198.51.100.60", cancellation.Token, client);
        await Check.UntilAsync(() => handler.Requests.Count > calls, "The cancellable country lookup did not begin.");
        cancellation.Cancel();
        Check.True(await pending is null, "Cancellation must end the optional country lookup without propagating an error.");
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class FakeCountryHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Reply { get; set; }
            = (_, _) => Task.FromResult(JsonResponse("{}"));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Reply(request, cancellationToken);
        }
    }

    private static TaskCompletionSource<string?> PendingCountry() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Task RefreshIpAsync(MainViewModel model)
        => (Task)typeof(MainViewModel).GetMethod("RefreshIpAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(model, [false])!;
}
