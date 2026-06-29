using System.Windows.Input;

namespace AutoClicker.ViewModels;

public class RelayCommand : ICommand
{
  private readonly Action<object?>     _execute;
  private readonly Func<object?, bool>? _canExecute;

  public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
  {
    _execute    = execute;
    _canExecute = canExecute;
  }

  public event EventHandler? CanExecuteChanged;

  public bool CanExecute(object? p) => _canExecute?.Invoke(p) ?? true;
  public void Execute(object? p)    => _execute(p);

  public void RaiseCanExecuteChanged() =>
    CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class AsyncRelayCommand : ICommand
{
  private readonly Func<object?, Task>  _execute;
  private readonly Func<object?, bool>? _canExecute;
  private bool _isRunning;

  public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
  {
    _execute    = execute;
    _canExecute = canExecute;
  }

  public event EventHandler? CanExecuteChanged;

  public bool CanExecute(object? p) => !_isRunning && (_canExecute?.Invoke(p) ?? true);

  public async void Execute(object? p)
  {
    _isRunning = true;
    RaiseCanExecuteChanged();
    try   { await _execute(p); }
    finally
    {
      _isRunning = false;
      RaiseCanExecuteChanged();
    }
  }

  public void RaiseCanExecuteChanged() =>
    CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
