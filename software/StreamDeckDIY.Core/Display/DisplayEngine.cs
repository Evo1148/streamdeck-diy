namespace StreamDeckDIY.Core.Display;

public interface IDisplayAnimationClock
{
    DateTimeOffset UtcNow { get; }
    Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken);
}

public sealed class SystemDisplayAnimationClock : IDisplayAnimationClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) =>
        Task.Delay(duration, cancellationToken);
}

public sealed record DisplayAnimationOptions(
    bool ReducedMotion = false,
    TimeSpan? FrameInterval = null)
{
    public TimeSpan EffectiveFrameInterval =>
        FrameInterval ?? TimeSpan.FromMilliseconds(33);
}

public interface IDisplayEngine
{
    DisplayState State { get; }
    event EventHandler<DisplayState>? StateChanged;
    void ShowScene(DisplayScene scene);
    Task NavigateAsync(
        DisplayScene scene,
        DisplayPageTransition transition,
        CancellationToken cancellationToken = default);
    Task ShowOverlayAsync(
        DisplayScene scene,
        VisualEffectDefinition effect,
        CancellationToken cancellationToken = default);
    void DismissOverlay();
}

public sealed class DisplayEngine : IDisplayEngine, IDisposable
{
    private readonly object sync = new();
    private readonly IDisplayAnimationClock clock;
    private readonly DisplayAnimationOptions options;
    private CancellationTokenSource? overlayCancellation;
    private CancellationTokenSource? pageCancellation;
    private long overlayGeneration;
    private long pageGeneration;
    private DisplayState state;

    public DisplayEngine(
        DisplayScene initialScene,
        IDisplayAnimationClock? clock = null,
        DisplayAnimationOptions? options = null)
    {
        state = new DisplayState(initialScene);
        this.clock = clock ?? new SystemDisplayAnimationClock();
        this.options = options ?? new DisplayAnimationOptions();
    }

    public DisplayState State
    {
        get { lock (sync) return state; }
    }

    public event EventHandler<DisplayState>? StateChanged;

    public void ShowScene(DisplayScene scene)
    {
        lock (sync)
        {
            pageGeneration++;
            pageCancellation?.Cancel();
            pageCancellation?.Dispose();
            pageCancellation = null;
            state = state with { BaseScene = scene, PageTransition = null };
        }
        Publish();
    }

    public async Task NavigateAsync(
        DisplayScene scene,
        DisplayPageTransition transition,
        CancellationToken cancellationToken = default)
    {
        if (transition == DisplayPageTransition.None || options.ReducedMotion)
        {
            ShowScene(scene);
            return;
        }

        CancellationTokenSource localCancellation;
        long generation;
        var duration = TimeSpan.FromMilliseconds(240);
        lock (sync)
        {
            pageCancellation?.Cancel();
            pageCancellation?.Dispose();
            pageCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            localCancellation = pageCancellation;
            generation = ++pageGeneration;
            state = state with
            {
                PageTransition = new(transition, state.BaseScene, scene, 0),
            };
        }
        Publish();
        var started = clock.UtcNow;
        try
        {
            while (clock.UtcNow - started < duration)
            {
                await clock.DelayAsync(options.EffectiveFrameInterval, localCancellation.Token);
                var progress = Math.Clamp(
                    (clock.UtcNow - started).TotalMilliseconds / duration.TotalMilliseconds,
                    0, 1);
                lock (sync)
                {
                    if (generation != pageGeneration) return;
                    state = state with
                    {
                        PageTransition = state.PageTransition! with { Progress = progress },
                    };
                }
                Publish();
            }
        }
        catch (OperationCanceledException) when (localCancellation.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            lock (sync)
            {
                if (generation == pageGeneration)
                {
                    pageCancellation?.Dispose();
                    pageCancellation = null;
                    state = state with { BaseScene = scene, PageTransition = null };
                }
            }
            if (generation == pageGeneration) Publish();
        }
    }

    public async Task ShowOverlayAsync(
        DisplayScene scene,
        VisualEffectDefinition effect,
        CancellationToken cancellationToken = default)
    {
        CancellationTokenSource localCancellation;
        long generation;
        lock (sync)
        {
            overlayCancellation?.Cancel();
            overlayCancellation?.Dispose();
            overlayCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            localCancellation = overlayCancellation;
            generation = ++overlayGeneration;
            state = state with
            {
                OverlayScene = scene,
                Effect = effect,
                AnimationProgress = options.ReducedMotion ? 1 : 0,
            };
        }
        Publish();

        if (options.ReducedMotion)
        {
            DismissIfCurrent(generation);
            return;
        }

        var started = clock.UtcNow;
        try
        {
            while (true)
            {
                var elapsed = clock.UtcNow - started;
                if (elapsed >= effect.Duration) break;
                await clock.DelayAsync(
                    options.EffectiveFrameInterval, localCancellation.Token);
                var progress = Math.Clamp(
                    (clock.UtcNow - started).TotalMilliseconds /
                    effect.Duration.TotalMilliseconds, 0, 1);
                lock (sync)
                {
                    if (generation != overlayGeneration) return;
                    state = state with { AnimationProgress = progress };
                }
                Publish();
            }
        }
        catch (OperationCanceledException) when (localCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            DismissIfCurrent(generation);
        }
    }

    public void DismissOverlay()
    {
        lock (sync)
        {
            overlayGeneration++;
            overlayCancellation?.Cancel();
            overlayCancellation?.Dispose();
            overlayCancellation = null;
            state = state with
            {
                OverlayScene = null,
                Effect = null,
                AnimationProgress = 0,
            };
        }
        Publish();
    }

    public void Dispose()
    {
        lock (sync)
        {
            pageGeneration++;
            pageCancellation?.Cancel();
            pageCancellation?.Dispose();
            pageCancellation = null;
        }
        DismissOverlay();
    }

    private void DismissIfCurrent(long generation)
    {
        lock (sync)
        {
            if (generation != overlayGeneration) return;
            overlayGeneration++;
            overlayCancellation?.Dispose();
            overlayCancellation = null;
            state = state with
            {
                OverlayScene = null,
                Effect = null,
                AnimationProgress = 0,
            };
        }
        Publish();
    }

    private void Publish() => StateChanged?.Invoke(this, State);
}
