namespace SelectiveVpn.Desktop.Abstractions;

/// <summary>UI-thread marshaling for the Avalonia MVP (mirrors SelectiveVpn.Abstractions.IUiDispatcher).</summary>
public interface IUiDispatcher
{
    bool CheckAccess();

    void Post(Action action);

    IUiTimer CreateTimer(TimeSpan interval, Action tick);
}

public interface IUiTimer : IDisposable
{
    void Start();

    void Stop();
}
