using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using SelectiveVpn.Abstractions;
using SelectiveVpn.Models;

namespace SelectiveVpn.Engine;

public sealed class MihomoEngine : IMihomoController
{
    private readonly string _engineDirectory;
    private readonly string _runtimeDirectory;
    private readonly HttpMessageHandler? _downloadHandler;
    private readonly ConcurrentQueue<string> _logTail = new();
    private readonly List<string> _secrets = [];
    private readonly VpnTrafficCounter _traffic = new();

    private Process? _process;
    private int _apiPort;
    private int _ipCheckPort;
    private string? _apiSecret;
    private string? _configPath;

    public MihomoEngine(string? rootDirectory = null, HttpMessageHandler? downloadHandler = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SelectiveVpn");
        _engineDirectory = Path.Combine(root, "engine");
        _runtimeDirectory = Path.Combine(root, "runtime");
        _downloadHandler = downloadHandler;
    }

    public event EventHandler<string>? EngineMessage;

    public event EventHandler? EngineStopped;

    public string EngineId => "mihomo-" + MihomoRelease.Version + "-amneziawg-3.1";

    public bool IsEngineAvailable => MihomoRelease.TryGetAsset(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture, out _);

    public bool IsRunning => _process is { HasExited: false };

    public async Task<OperationResult> StartAsync(
        MihomoStartRequest request,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            return OperationResult.Fail("Уже подключено.");

        lock (_traffic)
            _traffic.Reset();

        foreach (var target in request.Targets)
        {
            if (string.IsNullOrWhiteSpace(target.Path)
                || target.Path.Contains(',')
                || target.Path.Contains('\'')
                || target.Path.Contains('\n')
                || target.Path.Contains('\r'))
            {
                return OperationResult.Fail("Путь содержит недопустимый символ. Туннель не запускался.");
            }
        }

        Process? process = null;
        var port = 0;
        string? secret = null;
        string? configPath = null;
        try
        {
            Publish("Читаю конфигурацию AmneziaWG локально.");
            var read = AmneziaWgConfigReader.ReadFile(request.ConfigFilePath);
            if (!read.Succeeded || read.Profile is null)
                return OperationResult.Fail(read.Message);

            var binary = await EnsureBinaryAsync(request.CustomEnginePath, cancellationToken).ConfigureAwait(false);
            if (!binary.Succeeded)
                return OperationResult.Fail(binary.Error);

            _secrets.Clear();
            _secrets.AddRange(read.Profile.SecretValues());
            port = ReserveLoopbackPort();
            var ipCheckPort = ReserveLoopbackPort();
            secret = RandomNumberGenerator.GetHexString(32);
            _secrets.Add(secret);

            Directory.CreateDirectory(_runtimeDirectory);
            configPath = Path.Combine(_runtimeDirectory, "config.yaml");
            var yaml = MihomoProfileWriter.Build(
                read.Profile,
                AddressListResolver.Expand(request.Targets),
                port,
                secret,
                ipCheckPort,
                request.ExceptListed);
            await File.WriteAllTextAsync(configPath, yaml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
                .ConfigureAwait(false);

            Publish("Запускаю Mihomo.");
            process = CreateProcess(binary.Path, configPath);
            if (!process.Start())
            {
                ForgetSecrets(configPath);
                return OperationResult.Fail("Не удалось запустить процесс Mihomo.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var health = await WaitForApiAsync(process, port, secret, cancellationToken).ConfigureAwait(false);
            if (!health.Succeeded)
            {
                await StopProcessAsync(process, port, secret).ConfigureAwait(false);
                ForgetSecrets(configPath);
                return health;
            }

            _process = process;
            _apiPort = port;
            _ipCheckPort = ipCheckPort;
            _apiSecret = secret;
            _configPath = configPath;
            var ignored = read.Profile.IgnoredParameters.Count == 0
                ? ""
                : " Локальные параметры проигнорированы: " + string.Join(", ", read.Profile.IgnoredParameters) + ".";

            var scope = request.Targets.Count == 0
                ? "Весь трафик идёт через VPN."
                : request.ExceptListed
                    ? $"Исключений из VPN: {request.Targets.Count}. Остальной трафик идёт через VPN."
                    : $"Целей через VPN: {request.Targets.Count}. Остальной трафик идёт напрямую.";
            return OperationResult.Ok(
                $"Подключено. {read.Profile.Describe()}. {scope}{ignored}");
        }
        catch (OperationCanceledException)
        {
            if (process is not null)
                await StopProcessAsync(process, port, secret).ConfigureAwait(false);

            ForgetSecrets(configPath);
            return OperationResult.Fail("Подключение отменено. Туннель не запускался.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or UnauthorizedAccessException)
        {
            if (process is not null)
                await StopProcessAsync(process, port, secret).ConfigureAwait(false);

            ForgetSecrets(configPath);
            return OperationResult.Fail("Не удалось подготовить Mihomo. Туннель не запускался.");
        }
    }

    public async Task<OperationResult> QueryExitIpAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _ipCheckPort <= 0)
            return OperationResult.Fail("Маршрутизация не запущена. Внешний IP не запрашивался.");

        try
        {
            using var handler = new HttpClientHandler
            {
                Proxy = new WebProxy(new Uri($"http://127.0.0.1:{_ipCheckPort.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
                UseProxy = true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
            var text = (await client.GetStringAsync("https://api.ipify.org", cancellationToken).ConfigureAwait(false)).Trim();
            if (!IPAddress.TryParse(text, out _))
                return OperationResult.Fail("Сервис проверки IP вернул неожиданный ответ. Адрес не сохранён.");

            return OperationResult.Ok(text);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or OperationCanceledException)
        {
            return OperationResult.Fail("Не удалось получить внешний IP через VPN.");
        }
    }

    public async Task<VpnTraffic> QueryVpnTrafficAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _apiPort <= 0 || string.IsNullOrWhiteSpace(_apiSecret))
            return CurrentTraffic();

        try
        {
            using var handler = new SocketsHttpHandler { UseProxy = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_apiPort}/connections");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiSecret);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return CurrentTraffic();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            lock (_traffic)
                _traffic.Apply(json);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or JsonException)
        {
        }

        return CurrentTraffic();
    }

    private VpnTraffic CurrentTraffic()
    {
        lock (_traffic)
            return new VpnTraffic(_traffic.IncomingBytes, _traffic.OutgoingBytes);
    }

    public async Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        var process = _process;
        if (process is null || process.HasExited)
        {
            _process = null;
            return OperationResult.Fail("Соединение не запущено.");
        }

        try
        {
            await StopProcessAsync(process, _apiPort, _apiSecret).ConfigureAwait(false);
            _process = null;
            ForgetSecrets(_configPath);
            return OperationResult.Ok("Отключено. Процесс Mihomo остановлен, TUN выключен.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return OperationResult.Fail("Не удалось остановить Mihomo.");
        }
        finally
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private Process CreateProcess(string exePath, string configPath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = _runtimeDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };
        process.StartInfo.ArgumentList.Add("-d");
        process.StartInfo.ArgumentList.Add(_runtimeDirectory);
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add(configPath);
        process.OutputDataReceived += (_, args) => Remember(args.Data);
        process.ErrorDataReceived += (_, args) => Remember(args.Data);
        process.Exited += (_, _) =>
        {
            if (ReferenceEquals(_process, process))
                EngineStopped?.Invoke(this, EventArgs.Empty);
        };
        return process;
    }

    private readonly record struct PreparedBinary(bool Succeeded, string Path, string Error);

    private async Task<PreparedBinary> EnsureBinaryAsync(string? customEnginePath, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(customEnginePath))
        {
            if (!File.Exists(customEnginePath)
                || !string.Equals(Path.GetExtension(customEnginePath), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return new PreparedBinary(false, "", "Выбранное ядро не найдено. Туннель не запускался.");
            }

            Publish($"Используется выбранное ядро «{Path.GetFileName(customEnginePath)}».");
            return new PreparedBinary(true, Path.GetFullPath(customEnginePath), "");
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "mihomo.exe");
        if (File.Exists(bundled))
        {
            Publish("Используется Mihomo из комплекта приложения.");
            return new PreparedBinary(true, Path.GetFullPath(bundled), "");
        }

        if (!MihomoRelease.TryGetAsset(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture, out var asset))
            return new PreparedBinary(false, "", "Нужна 64-разрядная Windows. Туннель не запускался.");

        Directory.CreateDirectory(_engineDirectory);
        var exePath = Path.Combine(_engineDirectory, "mihomo.exe");
        var versionPath = Path.Combine(_engineDirectory, "version.txt");
        if (File.Exists(exePath)
            && File.Exists(versionPath)
            && string.Equals(File.ReadAllText(versionPath).Trim(), MihomoRelease.Version, StringComparison.Ordinal))
        {
            return new PreparedBinary(true, exePath, "");
        }

        Publish($"Скачиваю Mihomo {MihomoRelease.Version}.");
        var zipPath = Path.Combine(_engineDirectory, asset.FileName);
        using var client = new HttpClient(_downloadHandler ?? new SocketsHttpHandler(), disposeHandler: _downloadHandler is null)
        {
            Timeout = TimeSpan.FromMinutes(3)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SelectiveVpn");
        using var response = await client.GetAsync(MihomoRelease.DownloadUrl(asset), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return new PreparedBinary(false, "", "Не удалось скачать Mihomo. Туннель не запускался.");

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var destination = File.Create(zipPath))
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);

        var actual = await Sha256OfFileAsync(zipPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(zipPath);
            return new PreparedBinary(false, "", "Контрольная сумма Mihomo не совпала. Архив удалён, туннель не запускался.");
        }

        Publish("Контрольная сумма Mihomo совпала.");
        ExtractEngine(zipPath, exePath);
        await File.WriteAllTextAsync(versionPath, MihomoRelease.Version, cancellationToken).ConfigureAwait(false);
        File.Delete(zipPath);
        return new PreparedBinary(true, exePath, "");
    }

    private static void ExtractEngine(string zipPath, string exePath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entries = new List<ZipArchiveEntry>();
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var normalized = entry.FullName.Replace('\\', '/');
            if (normalized.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(entry.FullName))
                throw new InvalidDataException("Архив Mihomo содержит небезопасный путь.");

            entries.Add(entry);
        }

        var executable = entries.FirstOrDefault(entry =>
                entry.Name.Contains("mihomo", StringComparison.OrdinalIgnoreCase)
                && entry.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            ?? entries.FirstOrDefault(entry => entry.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        if (executable is null)
            throw new InvalidDataException("В архиве Mihomo нет исполняемого файла.");

        executable.ExtractToFile(exePath, overwrite: true);
        var directory = Path.GetDirectoryName(exePath)!;
        foreach (var library in entries.Where(entry => entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            library.ExtractToFile(Path.Combine(directory, library.Name), overwrite: true);
    }

    private async Task<OperationResult> WaitForApiAsync(
        Process process,
        int port,
        string secret,
        CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler { UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
                return OperationResult.Fail("Mihomo завершился при запуске. " + Tail());

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/version");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return OperationResult.Ok("api");
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            await Task.Delay(400, cancellationToken).ConfigureAwait(false);
        }

        return OperationResult.Fail("Mihomo не ответил на локальный запрос. " + Tail());
    }

    private async Task StopProcessAsync(Process process, int port, string? secret)
    {
        if (!process.HasExited && port > 0 && !string.IsNullOrWhiteSpace(secret))
        {
            try
            {
                using var handler = new SocketsHttpHandler { UseProxy = false };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
                using var request = new HttpRequestMessage(HttpMethod.Patch, $"http://127.0.0.1:{port}/configs");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                request.Content = new StringContent("{\"tun\":{\"enable\":false}}", Encoding.UTF8, "application/json");
                using var response = await client.SendAsync(request).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    await Task.Delay(700).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
            }
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
    }

    private void ForgetSecrets(string? configPath)
    {
        _apiPort = 0;
        _ipCheckPort = 0;
        _apiSecret = null;
        _configPath = null;
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            return;

        try
        {
            File.WriteAllText(configPath, "removed\n");
            File.Delete(configPath);
        }
        catch (IOException)
        {
        }
    }

    private void Remember(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        var redacted = SecretRedactor.Redact(line.Trim(), _secrets);
        if (redacted.Length > 300)
            redacted = redacted[..300];

        _logTail.Enqueue(redacted);
        while (_logTail.Count > 30 && _logTail.TryDequeue(out _))
        {
        }

        if (EngineLogFilter.ShouldShow(redacted))
            Publish(redacted);
    }

    private string Tail()
    {
        var lines = _logTail.ToArray();
        if (lines.Length == 0)
            return "Журнал процесса пуст.";

        return string.Join(" | ", lines.TakeLast(5));
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<string> Sha256OfFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private void Publish(string message) => EngineMessage?.Invoke(this, message);
}
