using System.Windows;
using System.Windows.Threading;
using SelectiveVpn.Abstractions;

namespace SelectiveVpn.Services;

internal sealed class WpfUiDispatcher : IUiDispatcher
{
    private Dispatcher Dispatcher
        => Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    public bool CheckAccess() => Dispatcher.CheckAccess();

    public void Post(Action action)
    {
        var dispatcher = Dispatcher;
        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }

    public IUiTimer CreateTimer(TimeSpan interval, Action tick)
        => new WpfUiTimer(Dispatcher, interval, tick);

    private sealed class WpfUiTimer : IUiTimer
    {
        private readonly DispatcherTimer _timer;
        private readonly Action _tick;
        private bool _disposed;

        public WpfUiTimer(Dispatcher dispatcher, TimeSpan interval, Action tick)
        {
            _tick = tick;
            _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = interval
            };
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
