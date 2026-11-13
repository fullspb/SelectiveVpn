using SelectiveVpn.Models;
using SelectiveVpn.Services;
using SelectiveVpn.ViewModels;

namespace SelectiveVpn.UiTests;

internal static class ConnectionDurationChecks
{
    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
    [
        ("connection duration begins only after a successful connection", StartsAfterSuccessAsync),
        ("connection duration uses continuous monotonic hours", MonotonicContinuousHoursAsync),
        ("editing profiles modes and themes does not reset connection duration", EditingDoesNotResetAsync),
        ("reconnecting and disconnecting reset connection duration", ReconnectResetsAsync),
        ("failed stop preserves the original connection duration", FailedStopKeepsDurationAsync),
        ("engine exit and recovery wait reset duration until a fresh connection", RecoveryStartsFreshAsync),
        ("failed and cancelled starts never accumulate connection duration", FailedAndCancelledStartsAsync),
        ("duration notifications continue while traffic and IP queries are blocked", TimerIgnoresBlockedQueriesAsync)
    ];

    private static async Task StartsAfterSuccessAsync()
    {
        var time = new ManualConnectionTimeProvider();
        await using var fixture = new TestFixture(timeProvider: time);
        var ready = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Engine.StartBehavior = (_, token) => ready.Task.WaitAsync(token);
        var connecting = fixture.Model.ConnectFromUserAsync();
        try
        {
            await Check.UntilAsync(() => fixture.Model.IsBusy, "The synthetic connection did not enter its pending state.");
            time.Advance(TimeSpan.FromHours(2));
            ExpectDuration(fixture.Model, "00:00:00", "Time spent preparing a connection must not count as connected time.");
            ready.SetResult(OperationResult.Ok("Synthetic connection ready"));
            await connecting;
            ExpectDuration(fixture.Model, "00:00:00", "Connected time must begin at the actual successful start.");
            time.Advance(TimeSpan.FromSeconds(61));
            ExpectDuration(fixture.Model, "00:01:01", "An established connection must accumulate elapsed time.");
        }
        finally
        {
            ready.TrySetResult(OperationResult.Fail("Synthetic cleanup"));
            await fixture.Model.DisconnectFromUserAsync();
            await connecting;
        }
    }

    private static async Task MonotonicContinuousHoursAsync()
    {
        var time = new ManualConnectionTimeProvider();
        await using var fixture = new TestFixture(timeProvider: time);
        await fixture.Model.ConnectFromUserAsync();
        time.Advance(TimeSpan.FromHours(27) + TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(5));
        ExpectDuration(fixture.Model, "27:04:05", "Connection duration must not wrap to zero hours after a day.");
        time.JumpWallClock(TimeSpan.FromDays(-7));
        ExpectDuration(fixture.Model, "27:04:05", "Changing the wall clock must not change monotonic connected time.");
        time.Advance(TimeSpan.FromSeconds(1));
        ExpectDuration(fixture.Model, "27:04:06", "A wall-clock adjustment must not interrupt elapsed-time accumulation.");
    }

    private static async Task EditingDoesNotResetAsync()
    {
        var time = new ManualConnectionTimeProvider();
        await using var fixture = new TestFixture(timeProvider: time);
        await fixture.Model.ConnectFromUserAsync();
        time.Advance(TimeSpan.FromSeconds(67));
        fixture.Model.SelectedProfile = fixture.Model.Profiles.Single(profile => profile.ConfigPath == fixture.BetaPath);
        fixture.Model.RoutingModeIndex = 1;
        fixture.Model.SelectedThemeId = AppTheme.Depth;
        ExpectDuration(fixture.Model, "00:01:07", "Editing another profile or selecting a theme must not reset the running connection's clock.");
        fixture.Model.SelectedProfile = fixture.Model.Profiles.Single(profile => profile.ConfigPath == fixture.AlphaPath);
        fixture.Model.RoutingModeIndex = 1;
        Check.True(fixture.Model.HasPendingChanges, "Changing the running profile's mode should remain a pending routing edit.");
        time.Advance(TimeSpan.FromSeconds(20));
        ExpectDuration(fixture.Model, "00:01:27", "Pending routing edits must not reset or restart the connection clock.");
        Check.Equal(1, fixture.Engine.Starts.Count, "Editing state must not silently create a new connection.");
    }

    private static async Task ReconnectResetsAsync()
    {
        var time = new ManualConnectionTimeProvider();
        await using var fixture = new TestFixture(timeProvider: time);
        await fixture.Model.ConnectFromUserAsync();
        time.Advance(TimeSpan.FromSeconds(67));
        fixture.Model.AddFolderCommand.Execute(null);
        await fixture.Model.ReconnectFromUserAsync();
        Check.Equal(2, fixture.Engine.Starts.Count, "The actual reconnect action must establish a new connection.");
        ExpectDuration(fixture.Model, "00:00:00", "A successful reconnect must begin a new connected-time interval.");
        time.Advance(TimeSpan.FromSeconds(9));
        ExpectDuration(fixture.Model, "00:00:09", "The new connection must measure from its own successful start.");
        await fixture.Model.DisconnectFromUserAsync();
        ExpectDuration(fixture.Model, "00:00:00", "A successful disconnect must clear connected time.");
        time.Advance(TimeSpan.FromHours(3));
        ExpectDuration(fixture.Model, "00:00:00", "Disconnected time must never accumulate into connection duration.");
    }

    private static async Task FailedStopKeepsDurationAsync()
    {
        var time = new ManualConnectionTimeProvider();
        await using var fixture = new TestFixture(timeProvider: time);
        await fixture.Model.ConnectFromUserAsync();
        time.Advance(TimeSpan.FromSeconds(80));
        fixture.Engine.StopBehavior = _ => Task.FromResult(OperationResult.Fail("Synthetic stop refused"));
        await fixture.Model.DisconnectFromUserAsync();
        Check.True(fixture.Model.IsConnected && fixture.Engine.IsRunning, "The refused stop must leave the real connection state intact.");
        ExpectDuration(fixture.Model, "00:01:20", "A refused stop must preserve the original connection timestamp.");
        time.Advance(TimeSpan.FromSeconds(15));
        fixture.Engine.StopBehavior = _ => throw new InvalidOperationException("Synthetic stop exception");
        await fixture.Model.DisconnectFromUserAsync();
        ExpectDuration(fixture.Model, "00:01:35", "A throwing stop must also preserve elapsed time while the connection remains running.");
        time.Advance(TimeSpan.FromSeconds(5));
        ExpectDuration(fixture.Model, "00:01:40", "Elapsed time must continue after a failed stop.");
        fixture.Engine.StopBehavior = null;
        await fixture.Model.DisconnectFromUserAsync();
        ExpectDuration(fixture.Model, "00:00:00", "The clock should clear once stopping actually succeeds.");
    }

    private static async Task RecoveryStartsFreshAsync()
    {
        var time = new ManualConnectionTimeProvider();
        await using var fixture = new TestFixture(timeProvider: time);
        fixture.Settings.AutoReconnect = true;
        var delayEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var permitRetry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DelayChoice = async (_, token) =>
        {
            delayEntered.TrySetResult();
            await permitRetry.Task.WaitAsync(token);
        };
        await fixture.Model.ConnectFromUserAsync();
        time.Advance(TimeSpan.FromSeconds(90));
        fixture.Engine.Crash();
        try
        {
            await delayEntered.Task;
            Check.True(fixture.Model.IsRecovering && !fixture.Model.IsConnected, "The crashed connection must wait for its recovery attempt.");
            time.Advance(TimeSpan.FromMinutes(5));
            ExpectDuration(fixture.Model, "00:00:00", "Neither the previous connection nor recovery waiting time should appear as connected time.");
            permitRetry.SetResult();
            await Check.UntilAsync(() => fixture.Model.IsConnected && !fixture.Model.IsRecovering, "The synthetic recovery did not establish a new connection.");
            ExpectDuration(fixture.Model, "00:00:00", "A recovered connection must have a fresh successful-start timestamp.");
            time.Advance(TimeSpan.FromSeconds(12));
            ExpectDuration(fixture.Model, "00:00:12", "Recovery must restart normal duration measurement after success.");
            fixture.Settings.AutoReconnect = false;
            fixture.Engine.Crash();
            Check.True(!fixture.Model.IsConnected, "A crash with recovery disabled should settle as disconnected.");
            ExpectDuration(fixture.Model, "00:00:00", "An unrecovered engine exit must also clear connected time.");
        }
        finally
        {
            permitRetry.TrySetResult();
            await fixture.Model.DisconnectFromUserAsync();
        }
    }

    private static async Task FailedAndCancelledStartsAsync()
    {
        var time = new ManualConnectionTimeProvider();
        await using var fixture = new TestFixture(timeProvider: time);
        fixture.Engine.StartBehavior = (_, _) =>
        {
            time.Advance(TimeSpan.FromMinutes(3));
            return Task.FromResult(OperationResult.Fail("Synthetic connection failure"));
        };
        await fixture.Model.ConnectFromUserAsync();
        ExpectDuration(fixture.Model, "00:00:00", "A failed start must not create a connected-time interval.");
        fixture.Engine.StartBehavior = async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return OperationResult.Ok("Unreachable after synthetic cancellation");
        };
        var connecting = fixture.Model.ConnectFromUserAsync();
        try
        {
            await Check.UntilAsync(() => fixture.Model.IsBusy, "The synthetic pending start did not begin.");
            time.Advance(TimeSpan.FromHours(1));
            ExpectDuration(fixture.Model, "00:00:00", "A pending connection must continue to display zero elapsed connected time.");
            await fixture.Model.DisconnectFromUserAsync();
            await connecting;
            ExpectDuration(fixture.Model, "00:00:00", "Cancelling a pending connection must leave duration at zero.");
        }
        finally
        {
            await fixture.Model.DisconnectFromUserAsync();
            await connecting;
        }
    }

    private static async Task TimerIgnoresBlockedQueriesAsync()
    {
        var time = new ManualConnectionTimeProvider();
        await using var fixture = new TestFixture(timeProvider: time, enableTrafficPolling: true);
        fixture.Engine.TrafficBehavior = async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return default;
        };
        fixture.Engine.IpBehavior = async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return OperationResult.Fail("Unreachable after query cancellation");
        };
        await fixture.Model.ConnectFromUserAsync();
        var notified = false;
        fixture.Model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.ConnectionDuration) && fixture.Model.ConnectionDuration == "00:00:08")
                notified = true;
        };
        time.Advance(TimeSpan.FromSeconds(8));
        await Check.UntilAsync(() => notified && fixture.Engine.TrafficQueries > 0 && fixture.Engine.IpQueries > 0,
            "The duration must notify its binding while independent traffic and external-IP queries remain blocked.", milliseconds: 4000);
        Check.True(fixture.Model.IsConnected, "Background queries must not interrupt a successful connection.");
        ExpectDuration(fixture.Model, "00:00:08", "The duration binding should expose the fake elapsed time after its real dispatcher tick.");
    }

    private static void ExpectDuration(MainViewModel model, string expected, string message)
        => Check.Equal(expected, model.ConnectionDuration, message);
}

internal sealed class ManualConnectionTimeProvider : TimeProvider
{
    private long _timestamp;
    private DateTimeOffset _utcNow = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        _timestamp += elapsed.Ticks;
        _utcNow += elapsed;
    }

    public void JumpWallClock(TimeSpan adjustment) => _utcNow += adjustment;
}
