using System.Windows.Input;

namespace SelectiveVpn.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _onException;
    private bool _isRunning;

    public RelayCommand(Action execute, Func<bool>? canExecute = null, Action<Exception>? onException = null)
        : this(
            () =>
            {
                execute();
                return Task.CompletedTask;
            },
            canExecute,
            onException)
    {
    }

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onException = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _onException = onException;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        _isRunning = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            if (_onException is null)
                throw;

            _onException(ex);
        }
        finally
        {
            _isRunning = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}

public sealed class ParameterRelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _onException;

    public ParameterRelayCommand(Action<object?> execute, Func<bool>? canExecute = null, Action<Exception>? onException = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _onException = onException;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        try
        {
            _execute(parameter);
        }
        catch (Exception ex)
        {
            if (_onException is null)
                throw;

            _onException(ex);
        }
    }
}
