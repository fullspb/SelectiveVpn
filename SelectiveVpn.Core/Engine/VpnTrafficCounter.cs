using System.Text.Json;

namespace SelectiveVpn.Engine;

/// <summary>
/// Суммирует байты соединений, которые прошли через прокси awg.
/// Прямые соединения не учитываются. Закрытые соединения остаются в итоге.
/// </summary>
public sealed class VpnTrafficCounter
{
    private readonly Dictionary<string, (long Up, long Down)> _seen = new(StringComparer.Ordinal);
    private long _incoming;
    private long _outgoing;

    public long IncomingBytes => _incoming;

    public long OutgoingBytes => _outgoing;

    public void Reset()
    {
        _seen.Clear();
        _incoming = 0;
        _outgoing = 0;
    }

    public void Apply(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("connections", out var connections)
            || connections.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var live = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in connections.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idElement))
                continue;

            var id = idElement.GetString();
            if (string.IsNullOrEmpty(id) || !GoesThroughVpn(item))
                continue;

            var upload = ReadBytes(item, "upload");
            var download = ReadBytes(item, "download");
            live.Add(id);
            if (_seen.TryGetValue(id, out var previous))
            {
                if (upload > previous.Up)
                    _outgoing += upload - previous.Up;
                if (download > previous.Down)
                    _incoming += download - previous.Down;
            }
            else
            {
                _outgoing += upload;
                _incoming += download;
            }

            _seen[id] = (upload, download);
        }

        if (_seen.Count == live.Count)
            return;

        foreach (var id in _seen.Keys.Where(id => !live.Contains(id)).ToArray())
            _seen.Remove(id);
    }

    private static bool GoesThroughVpn(JsonElement item)
    {
        if (!item.TryGetProperty("chains", out var chains) || chains.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var chain in chains.EnumerateArray())
        {
            if (string.Equals(chain.GetString(), "awg", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static long ReadBytes(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
            return 0;

        return value.TryGetInt64(out var bytes) && bytes > 0 ? bytes : 0;
    }
}
