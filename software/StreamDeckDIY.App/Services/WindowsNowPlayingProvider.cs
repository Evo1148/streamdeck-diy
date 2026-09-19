using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Control;
using Windows.Storage.Streams;
using StreamDeckDIY.Core.Dashboard;

namespace StreamDeckDIY.App.Services;

public sealed class WindowsNowPlayingProvider : INowPlayingProvider
{
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly ArtworkRefreshState artwork = new();
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private MediaSnapshot? cachedMedia;
    private bool disposed;

    public event EventHandler<NowPlayingState>? StateChanged;
    public NowPlayingState Current { get; private set; } = Unavailable();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (manager is not null) return;
        manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync()
            .AsTask(cancellationToken);
        manager.CurrentSessionChanged += OnCurrentSessionChanged;
        manager.SessionsChanged += OnSessionsChanged;
        await AttachCurrentAsync(cancellationToken);
    }

    public async Task<bool> ControlAsync(
        DashboardMediaCommand command, CancellationToken cancellationToken = default)
    {
        var active = session;
        if (active is null) return false;
        return command switch
        {
            DashboardMediaCommand.PlayPause => await active.TryTogglePlayPauseAsync().AsTask(cancellationToken),
            DashboardMediaCommand.Next => await active.TrySkipNextAsync().AsTask(cancellationToken),
            DashboardMediaCommand.Previous => await active.TrySkipPreviousAsync().AsTask(cancellationToken),
            _ => false,
        };
    }

    public ValueTask DisposeAsync()
    {
        if (disposed) return ValueTask.CompletedTask;
        disposed = true;
        if (manager is not null)
        {
            manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            manager.SessionsChanged -= OnSessionsChanged;
        }
        DetachSession();
        manager = null;
        return ValueTask.CompletedTask;
    }

    private async void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args) => await RefreshAfterEventAsync();
    private async void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args) => await RefreshAfterEventAsync();
    private async void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => await RefreshAfterEventAsync();
    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) => RefreshPlayback();
    private void OnTimelinePropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args) => RefreshPlayback();

    private async Task RefreshAfterEventAsync()
    {
        try { await AttachCurrentAsync(CancellationToken.None); }
        catch (Exception) when (disposed) { }
        catch (Exception) { Publish(Unavailable()); }
    }

    private async Task AttachCurrentAsync(CancellationToken cancellationToken)
    {
        await refreshGate.WaitAsync(cancellationToken);
        try
        {
        var current = manager?.GetCurrentSession();
        if (!ReferenceEquals(current, session))
        {
            DetachSession();
            session = current;
            cachedMedia = null;
            if (session is not null)
            {
                session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
            }
        }
        if (session is null) { Publish(Unavailable()); return; }

        var media = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
        var timeline = session.GetTimelineProperties();
        var snapshot = new MediaSnapshot(
            string.IsNullOrWhiteSpace(media.Title) ? "Sin título" : media.Title,
            media.Artist ?? string.Empty, media.AlbumTitle ?? string.Empty,
            session.SourceAppUserModelId ?? string.Empty, timeline.EndTime);
        var request = artwork.Begin(snapshot.Identity);
        try
        {
            artwork.TryApply(request, snapshot.Identity,
                await ReadArtworkAsync(media.Thumbnail, cancellationToken), true);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            artwork.TryApply(request, snapshot.Identity, null, false);
        }
        if (!artwork.IsCurrent(request, snapshot.Identity)) return;
        cachedMedia = snapshot;
        PublishPlayback(session, snapshot);
        }
        finally { refreshGate.Release(); }
    }

    private void RefreshPlayback()
    {
        try
        {
            if (session is not null && cachedMedia is not null)
                PublishPlayback(session, cachedMedia);
        }
        catch (Exception) when (disposed) { }
        catch (Exception) { Publish(Unavailable()); }
    }

    private void PublishPlayback(
        GlobalSystemMediaTransportControlsSession active, MediaSnapshot media)
    {
        var playback = active.GetPlaybackInfo();
        var timeline = active.GetTimelineProperties();
        double? progress = timeline.EndTime > TimeSpan.Zero
            ? Math.Clamp(timeline.Position.TotalMilliseconds / timeline.EndTime.TotalMilliseconds, 0, 1)
            : null;
        Publish(new NowPlayingState(true,
            playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            media.Title, media.Artist, media.Album,
            progress, media.Source, artwork.Current.Bytes,
            timeline.Position, timeline.EndTime, timeline.LastUpdatedTime,
            artwork.Current.Key));
    }

    private void DetachSession()
    {
        if (session is null) return;
        session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        session = null;
    }

    private void Publish(NowPlayingState state)
    {
        Current = state;
        StateChanged?.Invoke(this, state);
    }

    private static async Task<byte[]?> ReadArtworkAsync(
        IRandomAccessStreamReference? reference, CancellationToken cancellationToken)
    {
        if (reference is null) return null;
        using var stream = await reference.OpenReadAsync().AsTask(cancellationToken);
        if (stream.Size == 0 || stream.Size > 8 * 1024 * 1024) return null;
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size).AsTask(cancellationToken);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static NowPlayingState Unavailable() =>
        new(false, false, "Sin reproducción", "", "", null, "");

    private sealed record MediaSnapshot(
        string Title, string Artist, string Album, string Source, TimeSpan Duration)
    {
        public string Identity => string.Join("\u001F", Source, Title, Artist, Album,
            Duration.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
