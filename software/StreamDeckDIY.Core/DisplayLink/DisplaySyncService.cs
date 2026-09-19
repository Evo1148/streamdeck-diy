using StreamDeckDIY.Core.Devices;
using StreamDeckDIY.Protocol.DisplayLink;
using StreamDeckDIY.Protocol.ProtocolV1;

namespace StreamDeckDIY.Core.DisplayLink;

public enum DisplayLinkLifecycleState
{
    Disconnected,
    Handshaking,
    Synchronizing,
    Ready,
    Unsupported,
    Faulted,
}

public sealed record DisplayAssetUpload(
    ushort TransferId,
    ushort AssetId,
    ushort Width,
    ushort Height,
    ReadOnlyMemory<byte> Rgb565);

public sealed record DisplayLinkDiagnosticState(
    DisplayLinkLifecycleState Lifecycle,
    bool Available,
    string Protocol,
    string Backend,
    string Resolution,
    uint Generation,
    ushort Nodes,
    uint AssetBytes,
    bool HostOnline,
    string LastError)
{
    public static DisplayLinkDiagnosticState Disconnected { get; } = new(
        DisplayLinkLifecycleState.Disconnected, false, "--", "--", "--",
        0, 0, 0, false, "DisplayLink desconectado");
}

public sealed class DisplaySyncService : IAsyncDisposable
{
    private readonly IDisplayLinkChannel channel;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly object queueSync = new();
    private readonly object assetSync = new();
    private readonly object reconnectSync = new();
    private readonly TimeSpan heartbeatInterval;
    private DisplayGraph? activeGraph;
    private DisplayGraph? desiredGraph;
    private DisplayGraph? queuedGraph;
    private string? activeIdentity;
    private string desiredIdentity = string.Empty;
    private string queuedIdentity = string.Empty;
    private bool pumpRunning;
    private Task pump = Task.CompletedTask;
    private Task heartbeat = Task.CompletedTask;
    private Task reconnect = Task.CompletedTask;
    private DisplayLinkStatus? lastStatus;
    private CancellationTokenSource? assetTransfer;
    private readonly Dictionary<ushort, AssetFingerprint> transferredAssets = [];
    private uint generation;
    private uint bootSession;
    private uint lastTouchEventId;
    private uint heartbeatTicks;
    private bool initialized;
    private volatile bool bootloaderTransition;

    public DisplaySyncService(
        IDisplayLinkChannel channel, TimeSpan? heartbeatInterval = null)
    {
        this.channel = channel;
        this.heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(2);
        channel.DisplayTouchReceived += OnTouch;
        channel.ConnectionChanged += OnConnectionChanged;
    }

    public event EventHandler<DisplayLinkDiagnosticState>? StateChanged;
    public event EventHandler<DisplayTouchEvent>? TouchReceived;

    public DisplayLinkInfo? Info { get; private set; }
    public DisplayLinkLifecycleState Lifecycle { get; private set; } =
        DisplayLinkLifecycleState.Disconnected;
    public DisplayLinkDiagnosticState State { get; private set; } =
        DisplayLinkDiagnosticState.Disconnected;
    public uint ActiveGeneration => activeGraph?.Generation ?? 0;

    public bool TryResolveTouchRegion(
        DisplayTouchEvent touch, out DisplayTouchRegion region)
    {
        var graph = activeGraph;
        if (Lifecycle == DisplayLinkLifecycleState.Ready && graph is not null &&
            touch.Generation == graph.Generation)
        {
            var match = graph.TouchRegions.FirstOrDefault(candidate =>
                candidate.Id == touch.RegionId);
            if (match is not null)
            {
                region = match;
                return true;
            }
        }
        region = null!;
        return false;
    }

    public async Task<bool> InitializeAsync(CancellationToken token = default)
    {
        initialized = true;
        await operations.WaitAsync(token);
        try
        {
            if (Lifecycle == DisplayLinkLifecycleState.Ready ||
                Lifecycle == DisplayLinkLifecycleState.Synchronizing)
                return Info is not null;
            if (Lifecycle == DisplayLinkLifecycleState.Unsupported)
                return false;
            return await HandshakeCoreAsync(token);
        }
        finally
        {
            operations.Release();
        }
    }

    public void QueueGraph(DisplayGraph graph, string sceneIdentity = "")
    {
        lock (queueSync)
        {
            desiredGraph = graph;
            desiredIdentity = sceneIdentity;
            if (!channel.IsConnected)
            {
                queuedGraph = null;
                queuedIdentity = string.Empty;
                if (initialized) EnsureReconnectLoop();
                return;
            }
            queuedGraph = graph;
            queuedIdentity = sceneIdentity;
            if (pumpRunning ||
                Lifecycle is DisplayLinkLifecycleState.Unsupported or
                    DisplayLinkLifecycleState.Faulted)
                return;
            pumpRunning = true;
            pump = Task.Run(PumpAsync);
        }
    }

    public async Task FlushAsync(CancellationToken token = default)
    {
        while (true)
        {
            Task current;
            lock (queueSync) current = pump;
            await current.WaitAsync(token);
            lock (queueSync)
            {
                if (!pumpRunning) return;
            }
        }
    }

    public async Task SynchronizeAsync(
        DisplayGraph graph,
        string sceneIdentity = "",
        CancellationToken token = default)
    {
        await operations.WaitAsync(token);
        try
        {
            desiredGraph = graph;
            desiredIdentity = sceneIdentity;
            if (!await EnsureHandshakeCoreAsync(token)) return;
            await SynchronizeCoreAsync(graph, sceneIdentity, token);
        }
        catch (DisplayLinkException exception)
            when (exception.Error == DisplayLinkError.InvalidGeneration)
        {
            await RecoverGenerationCoreAsync(graph, sceneIdentity, exception, token);
        }
        catch (Exception exception) when (
            exception is ProtocolException or DisplayLinkException or
                InvalidOperationException or IOException or TimeoutException)
        {
            HandleOperationFailure(exception);
        }
        finally
        {
            operations.Release();
        }
    }

    public async Task<bool> TransferAssetAsync(
        ushort transferId,
        ushort assetId,
        ushort width,
        ushort height,
        ReadOnlyMemory<byte> rgb565,
        CancellationToken token = default)
    {
        var transferLifetime =
            CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);

        var transferToken = transferLifetime.Token;

        CancellationTokenSource? previousTransfer;

        lock (assetSync)
        {
            previousTransfer = assetTransfer;
            assetTransfer = transferLifetime;
        }

        previousTransfer?.Cancel();

        var entered = false;

        try
        {
            await operations.WaitAsync(transferToken);
            entered = true;
            if (Lifecycle is not (DisplayLinkLifecycleState.Synchronizing or
                    DisplayLinkLifecycleState.Ready) || Info is null)
                return false;
            if (rgb565.Length > Info.MaxSingleAssetBytes)
                throw new ArgumentOutOfRangeException(nameof(rgb565));

            var fingerprint = new AssetFingerprint(width, height,
                (uint)rgb565.Length, Crc32(rgb565.Span));
            AssetFingerprint? previous = null;
            lock (assetSync)
            {
                if (transferredAssets.TryGetValue(assetId, out var existing))
                {
                    if (existing == fingerprint) return true;
                    previous = existing;
                }
            }
            System.Diagnostics.Debug.WriteLine(
                $"ASSET UPLOAD id=0x{assetId:X4} bytes={rgb565.Length} crc={fingerprint.Crc32:X8}");

            var begin = new byte[19];
            DisplayLinkCodec.WriteU16(begin, 0, transferId);
            DisplayLinkCodec.WriteU16(begin, 2, assetId);
            begin[4] = 1;
            DisplayLinkCodec.WriteU16(begin, 5, width);
            DisplayLinkCodec.WriteU16(begin, 7, height);
            DisplayLinkCodec.WriteU32(begin, 9, (uint)rgb565.Length);
            DisplayLinkCodec.WriteU32(begin, 13, fingerprint.Crc32);
            await channel.ExchangeDisplayLinkAsync(
                DisplayLinkOpcode.AssetBegin, ActiveGeneration, begin,
                cancellationToken: transferLifetime.Token);

            for (var offset = 0; offset < rgb565.Length; offset += 42)
            {
                var count = Math.Min(42, rgb565.Length - offset);
                var chunk = new byte[6 + count];
                DisplayLinkCodec.WriteU16(chunk, 0, transferId);
                DisplayLinkCodec.WriteU32(chunk, 2, (uint)offset);
                rgb565.Span.Slice(offset, count).CopyTo(chunk.AsSpan(6));
                await channel.SendDisplayLinkOneWayAsync(
                    DisplayLinkOpcode.AssetChunk, ActiveGeneration, chunk,
                    transferLifetime.Token);
                await Task.Yield();
            }
            await channel.ExchangeDisplayLinkAsync(
                DisplayLinkOpcode.AssetCommit, ActiveGeneration,
                DisplayLinkCommandEncoder.Id(transferId),
                cancellationToken: transferLifetime.Token);
            lock (assetSync) transferredAssets[assetId] = fingerprint;
            System.Diagnostics.Debug.WriteLine(previous.HasValue
                ? $"ASSET REPLACE id=0x{assetId:X4} crc={fingerprint.Crc32:X8}"
                : $"ASSET READY id=0x{assetId:X4} crc={fingerprint.Crc32:X8}");
            return true;
        }
        finally
        {
            if (entered)
                operations.Release();

            lock (assetSync)
            {
                if (ReferenceEquals(assetTransfer, transferLifetime))
                    assetTransfer = null;
            }

            transferLifetime.Dispose();
        }
    }

    public async Task<bool> ReleaseAssetAsync(ushort assetId, CancellationToken token = default)
    {
        await operations.WaitAsync(token);
        try
        {
            if (Lifecycle is not (DisplayLinkLifecycleState.Synchronizing or
                    DisplayLinkLifecycleState.Ready) || Info is null) return false;
            await channel.ExchangeDisplayLinkAsync(DisplayLinkOpcode.AssetRelease,
                ActiveGeneration, DisplayLinkCommandEncoder.Id(assetId), cancellationToken: token);
            lock (assetSync) transferredAssets.Remove(assetId);
            System.Diagnostics.Debug.WriteLine($"ASSET EVICT id=0x{assetId:X4}");
            return true;
        }
        finally { operations.Release(); }
    }

    public async Task<bool> CommitGraphWithAssetsAsync(
        DisplayGraph graph,
        string sceneIdentity,
        IReadOnlyList<DisplayAssetUpload> uploads,
        IReadOnlyList<ushort> releaseAfterCommit,
        CancellationToken token = default)
    {
        if (Lifecycle == DisplayLinkLifecycleState.Disconnected &&
            !await InitializeAsync(token)) return false;
        foreach (var upload in uploads)
            if (!await TransferAssetAsync(upload.TransferId, upload.AssetId,
                    upload.Width, upload.Height, upload.Rgb565, token))
                return false;

        QueueGraph(graph, sceneIdentity);
        await FlushAsync(token);
        if (Lifecycle != DisplayLinkLifecycleState.Ready) return false;

        var referenced = graph.Nodes.Where(node => node.Kind == DisplayNodeKind.Image)
            .Select(node => node.AssetId).ToHashSet();
        foreach (var assetId in releaseAfterCommit.Distinct())
            if (!referenced.Contains(assetId) &&
                !await ReleaseAssetAsync(assetId, token))
                return false;
        return true;
    }

    public async Task RefreshStatusAsync(CancellationToken token = default)
    {
        await operations.WaitAsync(token);
        try
        {
            if (Info is null || Lifecycle == DisplayLinkLifecycleState.Unsupported)
                return;
            lastStatus = await GetStatusCoreAsync(ActiveGeneration, token);
            PublishState();
        }
        finally
        {
            operations.Release();
        }
    }

    public async Task<bool> ReconnectAsync(CancellationToken token = default)
    {
        if (channel.IsConnected && Lifecycle == DisplayLinkLifecycleState.Ready)
            return true;
        bool connected;
        try
        {
            connected = await channel.ReconnectDisplayLinkAsync(token);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or TimeoutException)
        {
            SetDisconnected(exception.Message);
            return false;
        }
        if (!connected)
        {
            SetDisconnected("StreamDeck DIY todavía no está disponible");
            return false;
        }
        await operations.WaitAsync(token);
        try
        {
            Info = null;
            activeGraph = null;
            activeIdentity = null;
            if (!await HandshakeCoreAsync(token)) return false;
            if (desiredGraph is not null &&
                (desiredGraph.Assets is null || desiredGraph.Assets.Count == 0))
                await SynchronizeCoreAsync(desiredGraph, desiredIdentity, token);
            return Lifecycle is DisplayLinkLifecycleState.Synchronizing or
                DisplayLinkLifecycleState.Ready;
        }
        finally
        {
            operations.Release();
        }
    }

    private async Task<bool> EnsureHandshakeCoreAsync(CancellationToken token)
    {
        if (!channel.IsConnected)
        {
            SetDisconnected("StreamDeck DIY desconectado");
            EnsureReconnectLoop();
            return false;
        }
        if (Lifecycle == DisplayLinkLifecycleState.Unsupported) return false;
        if (Info is not null &&
            Lifecycle is DisplayLinkLifecycleState.Synchronizing or
                DisplayLinkLifecycleState.Ready)
            return true;
        return await HandshakeCoreAsync(token);
    }

    private async Task<bool> HandshakeCoreAsync(CancellationToken token)
    {
        SetLifecycle(DisplayLinkLifecycleState.Handshaking);
        try
        {
            var response = await channel.ExchangeDisplayLinkAsync(
                DisplayLinkOpcode.GetInfo, 0, cancellationToken: token);
            var info = new DisplayLinkCodec(new ProtocolV1Codec()).ParseInfo(response);
            if (info.Major != DisplayLinkCodec.Major)
            {
                Info = null;
                SetLifecycle(DisplayLinkLifecycleState.Unsupported,
                    $"DisplayLink {info.Major}.{info.Minor} no es compatible.");
                return false;
            }

            var changedBoot = bootSession != 0 && bootSession != info.BootSessionId;
            bootSession = info.BootSessionId;
            Info = info;
            lastStatus = await GetStatusCoreAsync(0, token);
            generation = Math.Max(generation, lastStatus.ActiveGeneration);
            if (changedBoot)
            {
                lock (assetSync) transferredAssets.Clear();
                activeGraph = null;
                activeIdentity = null;
                lastTouchEventId = 0;
            }
            SetLifecycle(DisplayLinkLifecycleState.Synchronizing);
            return true;
        }
        catch (ProtocolException exception)
            when (exception.Message.Contains(
                "does not support", StringComparison.OrdinalIgnoreCase))
        {
            Info = null;
            activeGraph = null;
            SetLifecycle(DisplayLinkLifecycleState.Unsupported,
                "DisplayLink no disponible con este firmware");
            return false;
        }
        catch (Exception exception) when (
            exception is ProtocolException or DisplayLinkException or
                InvalidOperationException or IOException or TimeoutException)
        {
            HandleOperationFailure(exception);
            return false;
        }
    }

    private async Task SynchronizeCoreAsync(
        DisplayGraph requested,
        string sceneIdentity,
        CancellationToken token)
    {
        requested = PreserveUnavailableProgress(activeGraph, requested);
        var changes = activeGraph is null
            ? Array.Empty<DisplayGraphChange>()
            : DisplayGraphDiffer.Diff(activeGraph, requested).ToArray();
        var requiresFull = Lifecycle != DisplayLinkLifecycleState.Ready ||
            activeGraph is null ||
            !string.Equals(activeIdentity, sceneIdentity, StringComparison.Ordinal) ||
            DisplayGraphDiffer.RequiresFullSync(activeGraph, requested) ||
            changes.Any(change =>
                change.Kind is not (DisplayGraphChangeKind.Patch or
                    DisplayGraphChangeKind.SetText or DisplayGraphChangeKind.Delete)) ||
            changes.Any(change => change.Kind == DisplayGraphChangeKind.Patch &&
                DisplayLinkCommandEncoder.PatchNode(
                    change.Node!, change.PreviousNode!).Length >
                DisplayLinkCodec.MaxBodySize);

        if (requiresFull)
        {
            SetLifecycle(DisplayLinkLifecycleState.Synchronizing);
            var staged = requested with { Generation = NextGeneration() };
            await FullSyncCoreAsync(staged, token);
            var status = await GetStatusCoreAsync(staged.Generation, token);
            if (status.ActiveGeneration != staged.Generation)
                throw new DisplayLinkException(
                    DisplayLinkError.InvalidGeneration,
                    $"COMMIT_SYNC confirmó generation {status.ActiveGeneration}; " +
                    $"se esperaba {staged.Generation}.");
            activeGraph = staged;
            activeIdentity = sceneIdentity;
            lastStatus = status;
            SetLifecycle(DisplayLinkLifecycleState.Ready);
            await SendClockSyncAsync(token);
            StartHeartbeat();
            return;
        }

        var current = requested with { Generation = activeGraph!.Generation };
        await PatchCoreAsync(current, changes, token);
        activeGraph = current;
        lastStatus = await GetStatusCoreAsync(current.Generation, token);
        if (lastStatus.ActiveGeneration != current.Generation)
            throw new DisplayLinkException(
                DisplayLinkError.InvalidGeneration,
                "El firmware ya no tiene la generación activa esperada.");
        PublishState();
    }

    private async Task RecoverGenerationCoreAsync(
        DisplayGraph graph,
        string sceneIdentity,
        DisplayLinkException cause,
        CancellationToken token)
    {
        activeGraph = null;
        activeIdentity = null;
        Info = null;
        SetLifecycle(DisplayLinkLifecycleState.Synchronizing, cause.Message);
        if (!await HandshakeCoreAsync(token)) return;
        try
        {
            await SynchronizeCoreAsync(graph, sceneIdentity, token);
        }
        catch (Exception exception) when (
            exception is ProtocolException or DisplayLinkException or
                InvalidOperationException or IOException or TimeoutException)
        {
            SetLifecycle(DisplayLinkLifecycleState.Faulted, exception.Message);
        }
    }

    private async Task FullSyncCoreAsync(DisplayGraph graph, CancellationToken token)
    {
        try
        {
            await Exchange(DisplayLinkOpcode.BeginSync, graph.Generation,
                DisplayLinkCommandEncoder.BeginSync(graph.Mode), token);
            foreach (var node in graph.Nodes)
            {
                await Exchange(DisplayLinkOpcode.DefineNode, graph.Generation,
                    DisplayLinkCommandEncoder.DefineNode(node), token);
                if (node.Kind != DisplayNodeKind.Text) continue;
                foreach (var fragment in DisplayLinkCommandEncoder.StringFragments(
                             node.Id, node.Text ?? string.Empty))
                    await Exchange(DisplayLinkOpcode.SetStringFragment,
                        graph.Generation, fragment, token);
            }
            foreach (var region in graph.TouchRegions)
                await Exchange(DisplayLinkOpcode.DefineTouchRegion, graph.Generation,
                    DisplayLinkCommandEncoder.DefineTouch(region), token);
            foreach (var animation in graph.Animations)
            {
                await Exchange(DisplayLinkOpcode.DefineAnimation, graph.Generation,
                    DisplayLinkCommandEncoder.DefineAnimation(animation), token);
                foreach (var keyframe in animation.Keyframes ?? [])
                    await Exchange(DisplayLinkOpcode.DefineKeyframe, graph.Generation,
                        DisplayLinkCommandEncoder.DefineKeyframe(
                            animation.Id, keyframe), token);
            }
            await Exchange(DisplayLinkOpcode.CommitSync, graph.Generation,
                ReadOnlyMemory<byte>.Empty, token);
            foreach (var animation in graph.Animations)
                await Exchange(DisplayLinkOpcode.PlayAnimation, graph.Generation,
                    DisplayLinkCommandEncoder.Id(animation.Id), token);
        }
        catch
        {
            try
            {
                await Exchange(DisplayLinkOpcode.CancelSync, graph.Generation,
                    ReadOnlyMemory<byte>.Empty, token);
            }
            catch { }
            throw;
        }
    }

    private async Task PatchCoreAsync(
        DisplayGraph current,
        IReadOnlyList<DisplayGraphChange> changes,
        CancellationToken token)
    {
        if (Lifecycle != DisplayLinkLifecycleState.Ready)
            throw new InvalidOperationException(
                "Dynamic DisplayLink updates require Ready state.");
        if (changes.Count == 0) return;
        await Exchange(DisplayLinkOpcode.BeginUpdate, current.Generation,
            ReadOnlyMemory<byte>.Empty, token);
        foreach (var change in changes)
        {
            if (change.Kind == DisplayGraphChangeKind.Patch)
                await Exchange(DisplayLinkOpcode.PatchNode, current.Generation,
                    DisplayLinkCommandEncoder.PatchNode(
                        change.Node!, change.PreviousNode!), token);
            else if (change.Kind == DisplayGraphChangeKind.SetText)
                foreach (var fragment in DisplayLinkCommandEncoder.StringFragments(
                             change.Id, change.Text ?? string.Empty))
                    await Exchange(DisplayLinkOpcode.SetStringFragment,
                        current.Generation, fragment, token);
            else if (change.Kind == DisplayGraphChangeKind.Delete)
                await Exchange(DisplayLinkOpcode.DeleteNode, current.Generation,
                    DisplayLinkCommandEncoder.Id(change.Id), token);
        }
        await Exchange(DisplayLinkOpcode.CommitUpdate, current.Generation,
            ReadOnlyMemory<byte>.Empty, token);
    }

    private Task Exchange(
        DisplayLinkOpcode opcode,
        uint activeGeneration,
        ReadOnlyMemory<byte> body,
        CancellationToken token) =>
        channel.ExchangeDisplayLinkAsync(
            opcode, activeGeneration, body, cancellationToken: token);

    private async Task<DisplayLinkStatus> GetStatusCoreAsync(
        uint expectedGeneration,
        CancellationToken token)
    {
        var response = await channel.ExchangeDisplayLinkAsync(
            DisplayLinkOpcode.GetStatus, expectedGeneration,
            cancellationToken: token);
        return new DisplayLinkCodec(new ProtocolV1Codec()).ParseStatus(response);
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            DisplayGraph? next;
            string identity;
            lock (queueSync)
            {
                next = queuedGraph;
                identity = queuedIdentity;
                queuedGraph = null;
                if (next is null)
                {
                    pumpRunning = false;
                    return;
                }
            }
            await SynchronizeAsync(next, identity, lifetime.Token);
            if (Lifecycle is DisplayLinkLifecycleState.Unsupported or
                DisplayLinkLifecycleState.Faulted)
            {
                lock (queueSync) pumpRunning = false;
                return;
            }
        }
    }

    private void StartHeartbeat()
    {
        if (heartbeat.IsCompleted)
            heartbeat = HeartbeatAsync(lifetime.Token);
    }

    private async Task HeartbeatAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(heartbeatInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                if (!channel.IsConnected)
                {
                    if (!bootloaderTransition)
                    {
                        SetDisconnected("Conexión DisplayLink perdida");
                        EnsureReconnectLoop();
                    }
                    continue;
                }
                try
                {
                    await channel.SendDisplayLinkOneWayAsync(
                        DisplayLinkOpcode.Heartbeat, ActiveGeneration,
                        ReadOnlyMemory<byte>.Empty, token);
                    if (++heartbeatTicks % 30 == 0)
                        await SendClockSyncAsync(token);
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidOperationException)
                {
                    SetDisconnected(exception.Message);
                    EnsureReconnectLoop();
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void EnsureReconnectLoop(TimeSpan? initialDelay = null)
    {
        lock (reconnectSync)
        {
            if (!reconnect.IsCompleted || lifetime.IsCancellationRequested) return;
            reconnect = ReconnectLoopAsync(initialDelay ?? TimeSpan.Zero,
                                           lifetime.Token);
        }
    }

    private async Task ReconnectLoopAsync(
        TimeSpan initialDelay, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            if (initialDelay > TimeSpan.Zero)
                await Task.Delay(initialDelay, token);
            bootloaderTransition = false;
            while (!token.IsCancellationRequested)
            {
                if (channel.IsConnected &&
                    Lifecycle == DisplayLinkLifecycleState.Ready) return;
                if (await ReconnectAsync(token)) return;
                await timer.WaitForNextTickAsync(token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private Task SendClockSyncAsync(CancellationToken token)
    {
        var now = DateTimeOffset.Now;
        var epoch = unchecked((ulong)now.ToUnixTimeSeconds());
        var body = new byte[11];
        DisplayLinkCodec.WriteU32(body, 0, (uint)epoch);
        DisplayLinkCodec.WriteU32(body, 4, (uint)(epoch >> 32));
        DisplayLinkCodec.WriteI16(body, 8,
            (short)Math.Clamp(now.Offset.TotalMinutes, short.MinValue, short.MaxValue));
        body[10] = 1;
        return channel.SendDisplayLinkOneWayAsync(
            DisplayLinkOpcode.ClockSync, ActiveGeneration, body, token);
    }

    private uint NextGeneration()
    {
        generation++;
        if (generation == 0) generation = 1;
        return generation;
    }

    private static DisplayGraph PreserveUnavailableProgress(
        DisplayGraph? previous, DisplayGraph requested)
    {
        if (previous is null) return requested;
        var oldNodes = previous.Nodes.ToDictionary(node => node.Id);
        return requested with
        {
            Nodes = requested.Nodes.Select(node =>
                node.Kind == DisplayNodeKind.Progress && !node.ProgressAvailable &&
                oldNodes.TryGetValue(node.Id, out var old) &&
                old.Kind == DisplayNodeKind.Progress
                    ? node with { ProgressValue = old.ProgressValue }
                    : node).ToArray(),
        };
    }

    private void SetLifecycle(
        DisplayLinkLifecycleState lifecycle, string error = "")
    {
        if (Lifecycle != lifecycle)
            System.Diagnostics.Debug.WriteLine(
                $"DISPLAYLINK STATE {Lifecycle} -> {lifecycle} reason={error}");
        Lifecycle = lifecycle;
        PublishState(error);
    }

    private void SetDisconnected(string error)
    {
        lock (assetSync)
        {
            assetTransfer?.Cancel();
            transferredAssets.Clear();
        }
        Info = null;
        activeGraph = null;
        activeIdentity = null;
        lastStatus = null;
        SetLifecycle(DisplayLinkLifecycleState.Disconnected, error);
    }

    private readonly record struct AssetFingerprint(
        ushort Width, ushort Height, uint ByteLength, uint Crc32);

    private void HandleOperationFailure(Exception exception)
    {
        if (!channel.IsConnected)
        {
            SetDisconnected(exception.Message);
            EnsureReconnectLoop();
            return;
        }
        SetLifecycle(DisplayLinkLifecycleState.Faulted, exception.Message);
    }

    private void PublishState(string error = "")
    {
        var info = Info;
        var status = lastStatus;
        State = new(
            Lifecycle,
            info is not null,
            info is null ? "--" : $"{info.Major}.{info.Minor}",
            info?.Backend.ToString() ?? "--",
            info is null ? "--" : $"{info.Width}×{info.Height}",
            status?.ActiveGeneration ?? ActiveGeneration,
            status?.NodeCount ?? (ushort)(activeGraph?.Nodes.Count ?? 0),
            status?.AssetBytesUsed ?? 0,
            status?.HostOnline ?? false,
            string.IsNullOrWhiteSpace(error) ? Lifecycle switch
            {
                DisplayLinkLifecycleState.Handshaking => "Iniciando...",
                DisplayLinkLifecycleState.Synchronizing => "Sincronizando...",
                DisplayLinkLifecycleState.Ready => string.Empty,
                DisplayLinkLifecycleState.Unsupported =>
                    "DisplayLink no disponible con este firmware",
                DisplayLinkLifecycleState.Disconnected => "DisplayLink desconectado",
                _ => "DisplayLink no disponible",
            } : error);
        StateChanged?.Invoke(this, State);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
        }
        return crc ^ 0xFFFFFFFF;
    }

    public async ValueTask DisposeAsync()
    {
        channel.DisplayTouchReceived -= OnTouch;
        channel.ConnectionChanged -= OnConnectionChanged;
        lifetime.Cancel();
        lock (assetSync) assetTransfer?.Cancel();
        try
        {
            await Task.WhenAll(heartbeat, pump, reconnect);
        }
        catch (OperationCanceledException) { }
        await operations.WaitAsync();
        operations.Release();
        operations.Dispose();
        lifetime.Dispose();
    }

    private void OnConnectionChanged(
        object? sender, DeviceConnectionChangedEventArgs args)
    {
        if (lifetime.IsCancellationRequested) return;
        if (args.IsConnected)
        {
            bootloaderTransition = false;
            return;
        }
        var bootloaderRequested =
            args.DisconnectReason == DeviceDisconnectReason.BootloaderRequested;
        bootloaderTransition = bootloaderRequested;
        SetDisconnected(bootloaderRequested
            ? "Modo actualización solicitado"
            : args.Error?.Message ?? "StreamDeck DIY desconectado");
        EnsureReconnectLoop(bootloaderRequested ? TimeSpan.FromSeconds(4) : null);
    }

    private void OnTouch(object? sender, DisplayTouchEvent touch)
    {
        if (Lifecycle != DisplayLinkLifecycleState.Ready)
        {
            System.Diagnostics.Debug.WriteLine(
                $"TOUCH RX DROP trace={touch.EventId} reason=lifecycle_{Lifecycle}");
            return;
        }
        var graph = activeGraph;
        if (graph is null)
        {
            System.Diagnostics.Debug.WriteLine(
                $"TOUCH RX DROP trace={touch.EventId} reason=no_active_scene");
            return;
        }
        if (touch.Generation != graph.Generation)
        {
            System.Diagnostics.Debug.WriteLine(
                $"TOUCH RX DROP trace={touch.EventId} reason=stale_generation event={touch.Generation} active={graph.Generation} boot={bootSession}");
            return;
        }
        if (touch.EventId <= lastTouchEventId)
        {
            System.Diagnostics.Debug.WriteLine(
                $"TOUCH RX DROP trace={touch.EventId} reason=duplicate_or_out_of_order last={lastTouchEventId}");
            return;
        }
        var region = graph.TouchRegions.FirstOrDefault(candidate =>
            candidate.Id == touch.RegionId);
        if (region is null)
        {
            System.Diagnostics.Debug.WriteLine(
                $"TOUCH RX DROP trace={touch.EventId} reason=unknown_region region={touch.RegionId}");
            return;
        }
        lastTouchEventId = touch.EventId;
        System.Diagnostics.Debug.WriteLine(
            $"TOUCH RX trace={touch.EventId} control={region.ActionParameter} region={touch.RegionId} generation={touch.Generation} boot={bootSession}");
        TouchReceived?.Invoke(this, touch);
    }
}
