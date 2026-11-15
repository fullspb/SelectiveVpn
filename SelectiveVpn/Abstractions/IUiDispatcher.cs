namespace SelectiveVpn.Abstractions;

/// <summary>
/// UI-thread marshaling used by MainViewModel. WPF and Avalonia each provide an implementation.
/// </summary>
public interface IUiDispatcher
{
    bool CheckAccess();

    void Post(Action action);

    IUiTimer CreateTimer(TimeSpan interval, Action tick);
}

/// <summary>Lightweight timer abstraction so ViewModels do not depend on DispatcherTimer.</summary>
public interface IUiTimer : IDisposable
{
    void Start();

    void Stop();
}
