namespace StreamDeckDIY.Core.HostActions;

public interface IAsyncDelay
{
    Task DelayAsync(int milliseconds, CancellationToken cancellationToken);
}

public sealed class SystemAsyncDelay : IAsyncDelay
{
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) =>
        Task.Delay(milliseconds, cancellationToken);
}
