namespace StreamDeckDIY.Core.Profiles.Automation;

public interface IForegroundApplicationWatcher : IAsyncDisposable
{
    event EventHandler<ForegroundApplicationChangedEventArgs>? ApplicationChanged;
    bool IsRunning { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task<ForegroundApplication?> GetCurrentAsync(
        CancellationToken cancellationToken = default);
}

public sealed class ForegroundApplicationChangedEventArgs(
    ForegroundApplication application) : EventArgs
{
    public ForegroundApplication Application { get; } = application;
}
