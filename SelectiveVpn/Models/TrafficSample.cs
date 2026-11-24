using System.Diagnostics;

namespace SelectiveVpn.Models;

/// <summary>
/// Speed of the VPN channel measured at one moment, in bytes per second.
/// <paramref name="Seconds"/> is a time stamp on the <see cref="Now"/> clock.
/// </summary>
public sealed record TrafficSample(double Seconds, double DownPerSecond, double UpPerSecond)
{
    /// <summary>Monotonic clock shared by the sampler and the graph, in seconds.</summary>
    public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}
