using System.Windows.Input;

namespace StreamDeckDIY.App.ViewModels;

public sealed class AsyncRelayCommand(Func<object?, Task> execute) : ICommand
{
    private bool running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !running;

    public async void Execute(object? parameter)
    {
        if (running) return;
        running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await execute(parameter);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            running = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
