using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace SelectiveVpn.Services;

internal static class IpCountryLookup
{
    private const int CacheLimit = 128;
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, string> Cache = new(StringComparer.Ordinal);
    private static readonly Queue<string> CacheOrder = new();
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        UseProxy = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    })
    {
        Timeout = TimeSpan.FromSeconds(4),
        MaxResponseContentBufferSize = 16 * 1024
    };

    public static Task<string?> LookupAsync(string ip, CancellationToken cancellationToken)
        => LookupCoreAsync(ip, cancellationToken, Client, useCache: true);

    internal static Task<string?> LookupAsync(string ip, CancellationToken cancellationToken, HttpClient client)
        => LookupCoreAsync(ip, cancellationToken, client, useCache: false);

    private static async Task<string?> LookupCoreAsync(string ip, CancellationToken cancellationToken,
        HttpClient client, bool useCache)
    {
        if (cancellationToken.IsCancellationRequested || !IPAddress.TryParse(ip, out var address))
            return null;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        var key = address.ToString();
        if (useCache)
        {
            lock (CacheGate)
                if (Cache.TryGetValue(key, out var cached))
                    return cached;
        }

        try
        {
            var uri = new Uri("https://api.country.is/" + Uri.EscapeDataString(key));
            using var response = await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("ip", out var ipNode) || ipNode.ValueKind != JsonValueKind.String
                || !IPAddress.TryParse(ipNode.GetString(), out var responseAddress)
                || !root.TryGetProperty("country", out var countryNode) || countryNode.ValueKind != JsonValueKind.String)
                return null;
            if (responseAddress.IsIPv4MappedToIPv6)
                responseAddress = responseAddress.MapToIPv4();
            if (!address.Equals(responseAddress))
                return null;
            var country = CountryFlags.Normalize(countryNode.GetString());
            if (country.Length == 0 || cancellationToken.IsCancellationRequested)
                return null;

            if (useCache)
            {
                lock (CacheGate)
                {
                    if (!Cache.ContainsKey(key))
                    {
                        while (Cache.Count >= CacheLimit && CacheOrder.TryDequeue(out var oldest))
                            Cache.Remove(oldest);
                        Cache[key] = country;
                        CacheOrder.Enqueue(key);
                    }
                }
            }
            return country;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            // Country information is optional; IP display and the VPN must work without the service.
            return null;
        }
    }
}
