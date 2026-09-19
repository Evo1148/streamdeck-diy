namespace StreamDeckDIY.Core.Dashboard;

public readonly record struct ProgressTransitionPlan(
    bool Available, double From, double To, TimeSpan Duration);

public sealed class ProgressValueTransition
{
    private double? from;
    private double target;
    private DateTimeOffset startedAt;
    private TimeSpan duration;

    public ProgressTransitionPlan Retarget(
        double? value, DateTimeOffset now, TimeSpan transitionDuration)
    {
        if (!value.HasValue || !double.IsFinite(value.Value))
            return new(false, Current(now) ?? 0, Current(now) ?? 0, TimeSpan.Zero);

        var next = Math.Clamp(value.Value, 0, 1);
        if (!from.HasValue)
        {
            from = target = next;
            startedAt = now;
            duration = TimeSpan.Zero;
            return new(true, next, next, TimeSpan.Zero);
        }

        var current = Current(now)!.Value;
        if (Math.Abs(next - target) < 0.0001)
        {
            var remaining = duration - (now - startedAt);
            return new(true, current, target,
                remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        }
        from = current;
        target = next;
        startedAt = now;
        duration = transitionDuration;
        return new(true, current, next, transitionDuration);
    }

    public double? Current(DateTimeOffset now)
    {
        if (!from.HasValue) return null;
        if (duration <= TimeSpan.Zero) return target;
        var progress = Math.Clamp(
            (now - startedAt).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
        return from.Value + (target - from.Value) * progress;
    }

    public void Reset() => from = null;
}

public readonly record struct NowPlayingProgressPlan(
    bool Available,
    double From,
    double SynchronizedValue,
    TimeSpan SynchronizationDuration,
    bool ContinuesPlaying,
    TimeSpan RemainingDuration);

public sealed class NowPlayingProgressTransition
{
    private const double SeekThreshold = 0.002;
    private static readonly TimeSpan SeekTransition = TimeSpan.FromMilliseconds(250);
    private string identity = string.Empty;
    private double from;
    private double synchronizedValue;
    private DateTimeOffset startedAt;
    private TimeSpan synchronizationDuration;
    private TimeSpan? mediaDuration;
    private bool playing;
    private bool available;

    public NowPlayingProgressPlan Update(NowPlayingState state, DateTimeOffset now)
    {
        var sample = NowPlayingTimeline.ProgressAt(state, now);
        if (!sample.HasValue)
        {
            available = false;
            return new(false, 0, 0, TimeSpan.Zero, false, TimeSpan.Zero);
        }

        var nextIdentity = NowPlayingTimeline.Identity(state);
        var current = available ? Current(now) ?? sample.Value : sample.Value;
        var newMedia = !available || !string.Equals(identity, nextIdentity,
            StringComparison.Ordinal);
        identity = nextIdentity;
        available = true;
        from = newMedia ? sample.Value : current;
        synchronizedValue = sample.Value;
        startedAt = now;
        synchronizationDuration = !newMedia &&
            Math.Abs(from - synchronizedValue) >= SeekThreshold
                ? SeekTransition
                : TimeSpan.Zero;
        mediaDuration = state.Duration;
        playing = state.IsPlaying;
        return Plan();
    }

    public double? Current(DateTimeOffset now)
    {
        if (!available) return null;
        var elapsed = now - startedAt;
        if (synchronizationDuration > TimeSpan.Zero && elapsed < synchronizationDuration)
        {
            var amount = Math.Clamp(
                elapsed.TotalMilliseconds / synchronizationDuration.TotalMilliseconds, 0, 1);
            return from + (synchronizedValue - from) * amount;
        }

        if (!playing || mediaDuration is not { } total || total <= TimeSpan.Zero)
            return synchronizedValue;
        var advancing = elapsed - synchronizationDuration;
        return Math.Clamp(synchronizedValue + advancing.TotalMilliseconds /
            total.TotalMilliseconds, 0, 1);
    }

    public void Reset()
    {
        identity = string.Empty;
        available = false;
    }

    private NowPlayingProgressPlan Plan()
    {
        var remaining = playing && mediaDuration is { } total && total > TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(Math.Max(0,
                (1 - synchronizedValue) * total.TotalMilliseconds))
            : TimeSpan.Zero;
        return new(true, from, synchronizedValue, synchronizationDuration,
            playing && remaining > TimeSpan.Zero, remaining);
    }
}

public static class NowPlayingTimeline
{
    public static double? ProgressAt(NowPlayingState state, DateTimeOffset now)
    {
        if (!state.IsAvailable) return null;
        if (state.Position is not { } position ||
            state.Duration is not { } duration || duration <= TimeSpan.Zero)
            return state.Progress is { } fallback && double.IsFinite(fallback)
                ? Math.Clamp(fallback, 0, 1)
                : null;
        var elapsed = state.IsPlaying && state.SampledAt is { } sampledAt
            ? now - sampledAt
            : TimeSpan.Zero;
        return Math.Clamp((position + elapsed).TotalMilliseconds /
            duration.TotalMilliseconds, 0, 1);
    }

    public static string Identity(NowPlayingState state) =>
        string.Join('\u001f', state.Source, state.Title, state.Artist, state.Album);
}
