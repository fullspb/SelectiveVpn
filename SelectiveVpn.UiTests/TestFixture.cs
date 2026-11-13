using System.IO;
using System.Text.Json;
using System.Windows;
using SelectiveVpn.Abstractions;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;
using SelectiveVpn.Services;
using SelectiveVpn.Stubs;
using SelectiveVpn.ViewModels;

namespace SelectiveVpn.UiTests;

internal sealed class TestFixture : IAsyncDisposable
{
    private static readonly string TempParent = Path.Combine(Path.GetTempPath(), "SelectiveVpn.UiTests");
    private readonly List<Window> _dialogs = [];
    private readonly MainViewModelEnvironment _environment;

    public TestFixture(bool subscriptions = false, bool emptyRules = false, bool warnFullTunnel = false,
        Action<UserSettings>? configureSettings = null, TimeProvider? timeProvider = null, bool enableTrafficPolling = false)
    {
        Root = Path.Combine(TempParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Executable = Path.Combine(Root, "example.exe");
        File.WriteAllText(Executable, "Not an executable. Only a routing-path fixture.");
        Folder = Path.Combine(Root, "applications");
        Directory.CreateDirectory(Folder);
        AlphaPath = WriteProfile("Alpha", subscriptions);
        BetaPath = WriteProfile("Beta", subscriptions);
        var cache = Path.Combine(Root, "provider.yaml");
        File.WriteAllText(cache, "proxies:\n  - name: Node A\n    type: ss\n    server: 203.0.113.10\n    port: 443\n    cipher: aes-128-gcm\n    password: test\n  - name: Node B\n    type: ss\n    server: 203.0.113.11\n    port: 443\n    cipher: aes-128-gcm\n    password: test\n");
        Engine = new FakeMihomoController(cache);
        Files = new FakeFilePicker { Folder = Folder, Executable = Executable, Config = AlphaPath };
        Settings = new UserSettings
        {
            ConfigPath = AlphaPath,
            Theme = AppTheme.Dark,
            Language = AppLanguage.Russian,
            SkipFullTunnelWarning = !warnFullTunnel,
            Profiles =
            [
                new SavedProfile { ConfigPath = AlphaPath, Targets = emptyRules ? [] : [new SavedTarget { Path = Executable, Kind = "file" }] },
                new SavedProfile { ConfigPath = BetaPath, Targets = emptyRules ? [] : [new SavedTarget { Path = Executable, Kind = "file" }] }
            ]
        };
        configureSettings?.Invoke(Settings);

        _environment = new MainViewModelEnvironment
        {
            LoadSettings = () => (Settings, true),
            SaveSettings = settings =>
            {
                SaveCount++;
                SavedSettingsJson = JsonSerializer.Serialize(settings);
            },
            IsElevated = () => IsElevated,
            ShowDialog = dialog =>
            {
                _dialogs.Add(dialog);
                DialogCount++;
                return DialogChoice?.Invoke(dialog) ?? false;
            },
            ApplyStartup = enabled => { StartupRequests.Add(enabled); return (true, "Test startup change"); },
            Relaunch = _ => { ElevationRequests++; return (false, "Test elevation declined"); },
            SaveConnectSession = (path, rules, exceptListed) =>
            {
                SavedSessionPath = path;
                SavedSessionRules = rules.ToArray();
                SavedSessionExceptListed = exceptListed;
                SessionSaveCount++;
            },
            DelayAsync = (delay, token) => DelayChoice?.Invoke(delay, token) ?? Task.CompletedTask,
            LookupIpCountryAsync = (ip, token) =>
            {
                CountryLookupRequests.Add(ip);
                return CountryLookupChoice?.Invoke(ip, token) ?? Task.FromResult<string?>(null);
            },
            TimeProvider = timeProvider ?? TimeProvider.System,
            EnableTrafficPolling = enableTrafficPolling
        };
        Model = new MainViewModel(Engine, new StubAppRuleStore(), Files, _environment);
    }

    public string Root { get; }
    public string Executable { get; }
    public string Folder { get; }
    public string AlphaPath { get; }
    public string BetaPath { get; }
    public UserSettings Settings { get; private set; }
    public FakeMihomoController Engine { get; }
    public FakeFilePicker Files { get; }
    public MainViewModel Model { get; private set; }
    public int SaveCount { get; private set; }
    public int DialogCount { get; private set; }
    public int ElevationRequests { get; private set; }
    public int SessionSaveCount { get; private set; }
    public string? SavedSessionPath { get; private set; }
    public IReadOnlyList<AppRule> SavedSessionRules { get; private set; } = [];
    public bool SavedSessionExceptListed { get; private set; }
    public string? SavedSettingsJson { get; private set; }
    public List<bool> StartupRequests { get; } = [];
    public bool IsElevated { get; set; } = true;
    public Func<Window, bool?>? DialogChoice { get; set; }
    public Func<TimeSpan, CancellationToken, Task>? DelayChoice { get; set; }
    public Func<string, CancellationToken, Task<string?>>? CountryLookupChoice { get; set; }
    public List<string> CountryLookupRequests { get; } = [];

    public async Task ReopenFromSavedSettingsAsync()
    {
        Model.SaveSettings();
        await Model.PrepareForCloseAsync();
        Model.Dispose();
        Settings = JsonSerializer.Deserialize<UserSettings>(SavedSettingsJson!)
            ?? throw new InvalidOperationException("Synthetic settings could not be restored.");
        Model = new MainViewModel(Engine, new StubAppRuleStore(), Files, _environment);
    }

    private string WriteProfile(string name, bool subscription)
    {
        var path = Path.Combine(Root, name + (subscription ? SubscriptionProfile.FileExtension : ".conf"));
        if (subscription)
            SubscriptionProfile.SaveToPath(path, new SubscriptionProfile { Url = "https://example.invalid/not-fetched", Title = name, NodeCount = 2 });
        else
            File.WriteAllText(path, "[Interface]\nPrivateKey = AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\nAddress = 10.66.0.5/32\n[Peer]\nPublicKey = AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\nEndpoint = 203.0.113.10:51820\nAllowedIPs = 0.0.0.0/0\n");
        return path;
    }

    public async ValueTask DisposeAsync()
    {
        Engine.StopBehavior = null;
        await Model.PrepareForCloseAsync();
        Model.Dispose();
        foreach (var dialog in _dialogs)
            dialog.Close();
        var target = Path.GetFullPath(Root);
        var allowedParent = Path.GetFullPath(TempParent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(allowedParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Test cleanup target escaped its dedicated temporary directory.");
        Directory.Delete(target, recursive: true);
    }
}

internal sealed class FakeFilePicker : IFilePicker
{
    public string? Config { get; set; }
    public string? Executable { get; set; }
    public string? Folder { get; set; }
    public string? PickAmneziaConfig() => Config;
    public string? PickExecutable() => Executable;
    public string? PickFolder() => Folder;
    public string? PickEngine() => null;
}

internal sealed class FakeMihomoController(string cachePath) : IMihomoController
{
    public event EventHandler<string>? EngineMessage;
    public event EventHandler? EngineStopped;
    public string EngineId => "isolated-test-engine";
    public bool IsEngineAvailable => true;
    public bool IsRunning { get; private set; }
    public string? ProviderCachePath => cachePath;
    public List<MihomoStartRequest> Starts { get; } = [];
    public int Stops { get; private set; }
    public int IpQueries { get; private set; }
    public int TrafficQueries { get; private set; }
    public List<string> SelectedNodes { get; } = [];
    public string ActiveNode { get; private set; } = "Node A";
    public Func<MihomoStartRequest, CancellationToken, Task<OperationResult>>? StartBehavior { get; set; }
    public Func<CancellationToken, Task<OperationResult>>? StopBehavior { get; set; }
    public Func<string, CancellationToken, Task<OperationResult>>? SelectBehavior { get; set; }
    public Func<CancellationToken, Task<OperationResult>>? IpBehavior { get; set; }
    public Func<CancellationToken, Task<string?>>? NodeBehavior { get; set; }
    public Func<CancellationToken, Task<VpnTraffic>>? TrafficBehavior { get; set; }
    public IReadOnlyList<VpnNodeInfo> Nodes { get; } = [new("AUTO", "Auto", true), new("Node A", "Node A", false), new("Node B", "Node B", false)];

    public async Task<OperationResult> StartAsync(MihomoStartRequest request, CancellationToken cancellationToken = default)
    {
        Starts.Add(request);
        var result = StartBehavior is null ? OperationResult.Ok("Test engine started") : await StartBehavior(request, cancellationToken);
        if (result.Succeeded)
            IsRunning = true;
        return result;
    }

    public async Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        Stops++;
        if (StopBehavior is not null)
            return await StopBehavior(cancellationToken);
        var wasRunning = IsRunning;
        IsRunning = false;
        if (wasRunning)
            EngineStopped?.Invoke(this, EventArgs.Empty);
        return OperationResult.Ok("Test engine stopped");
    }

    public Task<OperationResult> QueryExitIpAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IpQueries++;
        return IpBehavior?.Invoke(cancellationToken) ?? Task.FromResult(OperationResult.Ok("203.0.113.24"));
    }

    public Task<VpnTraffic> QueryVpnTrafficAsync(CancellationToken cancellationToken = default)
    {
        TrafficQueries++;
        return TrafficBehavior?.Invoke(cancellationToken)
            ?? Task.FromResult(new VpnTraffic(315 * 1024 * 1024, 492 * 1024 * 1024));
    }

    public async Task<OperationResult> SelectVpnNodeAsync(string nodeName, CancellationToken cancellationToken = default)
    {
        SelectedNodes.Add(nodeName);
        var result = SelectBehavior is null ? OperationResult.Ok("Test node selected") : await SelectBehavior(nodeName, cancellationToken);
        if (result.Succeeded)
            ActiveNode = nodeName;
        return result;
    }

    public Task<string?> GetActiveVpnNodeAsync(CancellationToken cancellationToken = default)
        => NodeBehavior?.Invoke(cancellationToken) ?? Task.FromResult<string?>(ActiveNode);
    public Task<IReadOnlyList<VpnNodeInfo>> ListVpnNodesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Nodes);
    public Task<IReadOnlyDictionary<string, int>> MeasureVpnNodeDelaysAsync(IEnumerable<string> nodeNames, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<string, int>>(nodeNames.ToDictionary(name => name, _ => 20));

    public void Crash()
    {
        IsRunning = false;
        EngineStopped?.Invoke(this, EventArgs.Empty);
    }

    public void SendMessage(string message) => EngineMessage?.Invoke(this, message);
}
