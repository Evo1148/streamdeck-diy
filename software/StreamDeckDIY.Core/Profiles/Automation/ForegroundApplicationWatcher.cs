namespace StreamDeckDIY.Core.Profiles.Automation;

public sealed class ForegroundApplicationWatcher : IForegroundApplicationWatcher
{
    private readonly IForegroundApplicationProvider provider;
    private readonly TimeSpan interval;
    private readonly object sync = new();
    private CancellationTokenSource? loopCancellation;
    private Task? loopTask;

    public ForegroundApplicationWatcher(
        IForegroundApplicationProvider provider, TimeSpan? interval = null)
    {
        this.provider = provider;
        this.interval = interval ?? TimeSpan.FromMilliseconds(500);
        if (this.interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));
    }

    public event EventHandler<ForegroundApplicationChangedEventArgs>? ApplicationChanged;
    public bool IsRunning
    {
        get
        {
            lock (sync) return loopTask is { IsCompleted: false };
        }
    }

    public Task<ForegroundApplication?> GetCurrentAsync(
        CancellationToken cancellationToken = default) =>
        provider.GetForegroundApplicationAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            if (loopTask is { IsCompleted: false }) return Task.CompletedTask;
            loopCancellation?.Dispose();
            loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            loopTask = RunAsync(loopCancellation.Token);
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? task;
        lock (sync)
        {
            loopCancellation?.Cancel();
            task = loopTask;
        }
        if (task is not null)
        {
            try { await task; }
            catch (OperationCanceledException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        lock (sync)
        {
            loopCancellation?.Dispose();
            loopCancellation = null;
            loopTask = null;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        string? previous = null;
        using var timer = new PeriodicTimer(interval);
        while (!cancellationToken.IsCancellationRequested)
        {
            ForegroundApplication? current = null;
            try
            {
                current = await provider.GetForegroundApplicationAsync(
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // A transient process/window race is retried on the next poll.
            }

            var normalized = current is null
                ? null
                : ProcessNameNormalizer.Normalize(current.ProcessName);
            if (current is not null && normalized != previous)
            {
                previous = normalized;
                ApplicationChanged?.Invoke(
                    this, new ForegroundApplicationChangedEventArgs(current));
            }
            else if (current is null)
            {
                previous = null;
            }

            if (!await timer.WaitForNextTickAsync(cancellationToken)) break;
        }
    }
}
