using Avalonia.Threading;
using SelectiveVpn.Desktop.Abstractions;

namespace SelectiveVpn.Desktop.Services;

internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    public IUiTimer CreateTimer(TimeSpan interval, Action tick)
        => new AvaloniaUiTimer(interval, tick);

    private sealed class AvaloniaUiTimer : IUiTimer
    {
        private readonly DispatcherTimer _timer;
        private readonly Action _tick;
        private bool _disposed;

        public AvaloniaUiTimer(TimeSpan interval, Action tick)
        {
            _tick = tick;
            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += OnTick;
        }

        private void OnTick(object? sender, EventArgs e) => _tick();

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
        }
    }
}
