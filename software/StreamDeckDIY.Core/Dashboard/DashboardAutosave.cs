namespace StreamDeckDIY.Core.Dashboard;

public enum DashboardSaveState { Saved, Dirty, Saving, Error }

public sealed class DashboardSaveStateChangedEventArgs(DashboardSaveState state) : EventArgs
{
    public DashboardSaveState State { get; } = state;
}

public sealed class DashboardAutosave : IAsyncDisposable
{
    private readonly Func<DashboardConfiguration, CancellationToken, Task> save;
    private readonly TimeSpan delay;
    private readonly object sync = new();
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private CancellationTokenSource? debounce;
    private DashboardConfiguration? pending;
    private long revision;
    private bool disposed;

    public DashboardAutosave(
        Func<DashboardConfiguration, CancellationToken, Task> save,
        TimeSpan? delay = null)
    {
        this.save = save;
        this.delay = delay ?? TimeSpan.FromMilliseconds(650);
    }

    public event EventHandler<DashboardSaveStateChangedEventArgs>? StateChanged;
    public DashboardSaveState State { get; private set; } = DashboardSaveState.Saved;
    public bool IsDirty
    {
        get { lock (sync) return pending is not null; }
    }

    public void Queue(DashboardConfiguration configuration)
    {
        CancellationTokenSource current;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            pending = Snapshot(configuration);
            revision++;
            debounce?.Cancel();
            debounce?.Dispose();
            debounce = current = new CancellationTokenSource();
            SetState(DashboardSaveState.Dirty);
        }
        _ = RunDebounceAsync(current);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? scheduled;
        lock (sync)
        {
            scheduled = debounce;
            debounce = null;
        }
        scheduled?.Cancel();
        scheduled?.Dispose();

        await SavePendingAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SavePendingAsync(CancellationToken cancellationToken)
    {
        await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DashboardConfiguration? snapshot;
            long savedRevision;
            lock (sync)
            {
                snapshot = pending;
                savedRevision = revision;
                if (snapshot is not null) SetState(DashboardSaveState.Saving);
            }
            if (snapshot is null) return;

            try
            {
                await save(snapshot, cancellationToken).ConfigureAwait(false);
                lock (sync)
                {
                    if (revision == savedRevision)
                    {
                        pending = null;
                        SetState(DashboardSaveState.Saved);
                    }
                    else
                    {
                        SetState(DashboardSaveState.Dirty);
                    }
                }
            }
            catch
            {
                lock (sync)
                    if (revision == savedRevision) SetState(DashboardSaveState.Error);
                throw;
            }
        }
        finally { saveGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? scheduled;
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            scheduled = debounce;
            debounce = null;
        }
        scheduled?.Cancel();
        scheduled?.Dispose();
        await saveGate.WaitAsync().ConfigureAwait(false);
        saveGate.Release();
        saveGate.Dispose();
    }

    private async Task RunDebounceAsync(CancellationTokenSource scheduled)
    {
        var token = scheduled.Token;
        try
        {
            await Task.Delay(delay, token).ConfigureAwait(false);
            lock (sync)
            {
                if (!ReferenceEquals(debounce, scheduled)) return;
                debounce = null;
            }
            await SavePendingAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Dashboard autosave failed for dashboard-config.json: {exception.Message}");
        }
        finally { scheduled.Dispose(); }
    }

    private static DashboardConfiguration Snapshot(DashboardConfiguration value) =>
        value with
        {
            Widgets = value.Widgets.Select(widget => widget with
            {
                Options = widget.Options is null ? null : widget.Options with { },
            }).ToArray(),
        };

    private void SetState(DashboardSaveState value)
    {
        if (State == value) return;
        State = value;
        StateChanged?.Invoke(this, new DashboardSaveStateChangedEventArgs(value));
    }
}
