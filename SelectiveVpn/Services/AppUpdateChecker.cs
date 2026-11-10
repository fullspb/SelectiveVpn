using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SelectiveVpn.Services;

public sealed record UpdateInfo(string Version, string DownloadUrl, string? ReleaseNotes);

internal static class AppUpdateChecker
{
    private const string LatestUrl = "https://api.github.com/repos/fullspb/SelectiveVpn/releases/latest";
    private const string SetupPrefix = "SelectiveVpn-Setup-";

    public static string UpdatesDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SelectiveVpn",
        "updates");

    public static async Task<UpdateInfo?> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var handler = new SocketsHttpHandler { UseProxy = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SelectiveVpn/" + AppVersion.Current);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await client.GetAsync(LatestUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagNode))
                return null;

            var remoteVersion = NormalizeVersion(tagNode.GetString());
            if (remoteVersion is null || !IsNewer(remoteVersion, AppVersion.Current))
                return null;

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            string? downloadUrl = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameNode) ? nameNode.GetString() : null;
                var url = asset.TryGetProperty("browser_download_url", out var urlNode) ? urlNode.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                    continue;
                if (!name.StartsWith(SetupPrefix, StringComparison.OrdinalIgnoreCase)
                    || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                downloadUrl = url;
                break;
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
                return null;

            var notes = root.TryGetProperty("body", out var bodyNode) ? bodyNode.GetString() : null;
            return new UpdateInfo(remoteVersion, downloadUrl, notes);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return null;
        }
    }

    public static string LocalSetupPath(string version)
        => Path.Combine(UpdatesDirectory, $"{SetupPrefix}{NormalizeVersion(version) ?? version}.exe");

    public static async Task DownloadAsync(
        string downloadUrl,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var tempPath = destinationPath + ".partial";
        try
        {
            using var handler = new SocketsHttpHandler { UseProxy = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SelectiveVpn/" + AppVersion.Current);

            using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);

            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                readTotal += read;
                if (total > 0 && progress is not null)
                    progress.Report(Math.Clamp(100.0 * readTotal / total, 0, 100));
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Close();

            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
            File.Move(tempPath, destinationPath);
            progress?.Report(100);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (IOException)
            {
            }

            throw;
        }
    }

    public static bool IsNewer(string remoteVersion, string localVersion)
    {
        var remote = NormalizeVersion(remoteVersion);
        var local = NormalizeVersion(localVersion);
        if (remote is null || local is null)
            return false;
        if (!Version.TryParse(remote, out var remoteParsed) || !Version.TryParse(local, out var localParsed))
            return false;
        return remoteParsed > localParsed;
    }

    private static string? NormalizeVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var value = raw.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];
        var plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus > 0)
            value = value[..plus];
        return value;
    }
}
