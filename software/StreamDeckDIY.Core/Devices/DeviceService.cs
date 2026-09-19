using System.IO;
using StreamDeckDIY.Protocol.Messages;
using StreamDeckDIY.Protocol.ProtocolV1;
using StreamDeckDIY.Protocol.Models;
using StreamDeckDIY.Transport.Hid;
using StreamDeckDIY.Protocol.DisplayLink;

namespace StreamDeckDIY.Core.Devices;

public sealed class DeviceService : IDeviceService, IDisplayLinkChannel
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(2);
    private readonly IHidTransport transport;
    private readonly ProtocolV1Codec protocol;
    private readonly DisplayLinkCodec displayLink;
    private readonly SemaphoreSlim exchangeLock = new(1, 1);
    private readonly object pendingSync = new();
    private PendingRequest? pendingRequest;
    private ushort nextSequence = 1;
    private int publishedConnectionState;
    private int bootloaderRequestState;

    public DeviceService(IHidTransport transport, ProtocolV1Codec protocol)
    {
        this.transport = transport;
        this.protocol = protocol;
        displayLink = new DisplayLinkCodec(protocol);
        transport.ReportReceived += OnReportReceived;
        transport.ConnectionLost += OnConnectionLost;
    }

    public event EventHandler<HostActionTriggeredEventArgs>? HostActionTriggered;
    public event EventHandler<DisplayTouchEvent>? DisplayTouchReceived;
    public event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged;

    public bool IsConnected => transport.IsOpen;

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var connected = await transport.OpenAsync(cancellationToken);
            if (connected) Interlocked.Exchange(ref bootloaderRequestState, 0);
            PublishConnectionState(connected);
            return connected;
        }
        catch
        {
            PublishConnectionState(false);
            throw;
        }
    }

    public async Task<bool> ReconnectDisplayLinkAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsConnected) return true;
        if (!await transport.OpenAsync(cancellationToken)) return false;
        Interlocked.Exchange(ref bootloaderRequestState, 0);
        try
        {
            // Validate Protocol v1 before DisplayLink starts its own handshake.
            await GetDeviceInfoAsync(cancellationToken);
            PublishConnectionState(true);
            return true;
        }
        catch
        {
            transport.Close();
            PublishConnectionState(false);
            throw;
        }
    }

    public void Disconnect()
    {
        CompletePendingRequest(new IOException("HID device was closed."));
        transport.Close();
        PublishConnectionState(false);
    }

    public async Task<StreamDeckDeviceInfo> GetDeviceInfoAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        var sequence = NextSequence();
        var response = await ExchangeAsync(
            protocol.CreateGetDeviceInfo(sequence), sequence, cancellationToken);
        var info = protocol.ParseDeviceInfo(response, sequence);
        return new StreamDeckDeviceInfo(
            info.ProtocolVersion,
            new Version(info.FirmwareMajor, info.FirmwareMinor, info.FirmwarePatch),
            info.ButtonCount,
            info.EncoderCount,
            (StreamDeckCapabilities)info.Capabilities);
    }

    public async Task<BindingInfo> GetBindingAsync(
        ControlId control,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        var sequence = NextSequence();
        var response = await ExchangeAsync(
            protocol.CreateGetBinding(sequence, control), sequence, cancellationToken);
        var binding = protocol.ParseBindingInfo(response, sequence);
        if (binding.Control != control)
        {
            throw new ProtocolException(
                "BINDING_INFO does not match the requested control.");
        }
        return binding;
    }

    public async Task SetBindingAsync(
        ControlId control,
        DeviceAction action,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        var sequence = NextSequence();
        var response = await ExchangeAsync(
            protocol.CreateSetBinding(sequence, control, action), sequence, cancellationToken);
        protocol.ParseAck(response, sequence);
    }

    public async Task ExecuteActionTestAsync(
        DeviceAction action,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        var sequence = NextSequence();
        System.Diagnostics.Debug.WriteLine(
            $"ACTION TEST TX trace={sequence} type={action.Type}");
        var response = await ExchangeAsync(
            protocol.CreateExecuteActionTest(sequence, action), sequence, cancellationToken);
        protocol.ParseAck(response, sequence);
        System.Diagnostics.Debug.WriteLine(
            $"ACTION TEST ACK trace={sequence} type={action.Type}");
    }

    public Task BeginConfigUpdateAsync(CancellationToken cancellationToken = default) =>
        SendEmptyCommandAsync(protocol.CreateBeginConfigUpdate, cancellationToken);

    public Task CommitConfigUpdateAsync(CancellationToken cancellationToken = default) =>
        SendEmptyCommandAsync(protocol.CreateCommitConfigUpdate, cancellationToken);

    public Task CancelConfigUpdateAsync(CancellationToken cancellationToken = default) =>
        SendEmptyCommandAsync(protocol.CreateCancelConfigUpdate, cancellationToken);

    public async Task EnterBootloaderAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        if (Interlocked.CompareExchange(ref bootloaderRequestState, 1, 0) != 0)
            throw new InvalidOperationException(
                "Bootloader entry has already been requested.");
        try
        {
            var sequence = NextSequence();
            var response = await ExchangeAsync(
                protocol.CreateEnterBootloader(sequence), sequence,
                cancellationToken, expectsBootloaderDisconnect: true);
            protocol.ParseAck(response, sequence);
        }
        catch
        {
            Interlocked.Exchange(ref bootloaderRequestState, 0);
            throw;
        }
    }

    public async Task<DisplayLinkResponse> ExchangeDisplayLinkAsync(DisplayLinkOpcode opcode,
        uint generation,ReadOnlyMemory<byte> body=default,
        DisplayLinkFlags flags=DisplayLinkFlags.AckRequired,CancellationToken cancellationToken=default)
    {
        EnsureConnected();
        var sequence=NextSequence();
        var trace=opcode!=DisplayLinkOpcode.AssetChunk;
        var started=System.Diagnostics.Stopwatch.GetTimestamp();
        if(trace)System.Diagnostics.Debug.WriteLine(
            $"DL TX opcode={opcode} generation={generation}");
        try
        {
            var response=await ExchangeAsync(
                displayLink.CreateCommand(sequence,opcode,generation,body.Span,flags),
                sequence,cancellationToken);
            var parsed=displayLink.ParseResponse(response,sequence,opcode);
            if(trace)System.Diagnostics.Debug.WriteLine(
                $"DL RX/ACK opcode={opcode} generation={generation} latency_us={(long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMicroseconds}");
            return parsed;
        }
        catch(TimeoutException)
        {
            if(trace)System.Diagnostics.Debug.WriteLine(
                $"DL TIMEOUT opcode={opcode} generation={generation} elapsed_us={(long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMicroseconds}");
            throw;
        }
    }

    public async Task SendDisplayLinkOneWayAsync(DisplayLinkOpcode opcode,uint generation,
        ReadOnlyMemory<byte> body,CancellationToken cancellationToken=default)
    {
        EnsureConnected();await exchangeLock.WaitAsync(cancellationToken);
        try{var sequence=NextSequence();await transport.WriteAsync(displayLink.CreateCommand(
            sequence,opcode,generation,body.Span,DisplayLinkFlags.None),cancellationToken);}
        finally{exchangeLock.Release();}
    }

    public async ValueTask DisposeAsync()
    {
        transport.ReportReceived -= OnReportReceived;
        transport.ConnectionLost -= OnConnectionLost;
        CompletePendingRequest(new IOException("Device service was disposed."));
        await transport.DisposeAsync();
        await exchangeLock.WaitAsync();
        exchangeLock.Release();
        exchangeLock.Dispose();
    }

    private void OnReportReceived(ReadOnlyMemory<byte> report)
    {
        ProtocolPacket packet;
        try
        {
            packet = protocol.Deserialize(report.Span);
        }
        catch (ProtocolException)
        {
            // Malformed reports do not imply a USB disconnection.
            return;
        }

        if (packet.MessageType == MessageType.HostActionTriggered)
        {
            try
            {
                var hostAction = protocol.ParseHostActionTriggered(report.Span);
                PublishHostAction(hostAction.ActionId);
            }
            catch (ProtocolException)
            {
                // Ignore malformed asynchronous events without affecting requests.
            }
            return;
        }

        if (packet.MessageType == MessageType.DisplayLinkEvent)
        {
            try
            {
                var touch=displayLink.ParseTouchEvent(report.Span);
                var handlers=DisplayTouchReceived;
                if(handlers is not null)foreach(EventHandler<DisplayTouchEvent> handler in handlers.GetInvocationList())
                    try{handler(this,touch);}catch(Exception){ }
            }
            catch(Exception exception) when(exception is ProtocolException or DisplayLinkException)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"TOUCH RX DROP trace=unknown reason=invalid_packet detail={exception.Message}");
            }
            return;
        }

        if (!ProtocolV1Codec.IsResponse(packet.MessageType)) return;

        TaskCompletionSource<byte[]>? completion = null;
        lock (pendingSync)
        {
            if (pendingRequest is not null && pendingRequest.Sequence == packet.Sequence)
            {
                if (pendingRequest.ExpectsBootloaderDisconnect &&
                    packet.MessageType == MessageType.Ack)
                    Interlocked.Exchange(ref bootloaderRequestState, 2);
                completion = pendingRequest.Completion;
                pendingRequest = null;
            }
        }
        completion?.TrySetResult(report.ToArray());
    }

    private async Task SendEmptyCommandAsync(
        Func<ushort, byte[]> createRequest,
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        var sequence = NextSequence();
        var response = await ExchangeAsync(
            createRequest(sequence), sequence, cancellationToken);
        protocol.ParseAck(response, sequence);
    }

    private void PublishHostAction(uint actionId)
    {
        var handlers = HostActionTriggered;
        if (handlers is null) return;

        var eventArgs = new HostActionTriggeredEventArgs(actionId);
        foreach (EventHandler<HostActionTriggeredEventArgs> handler in
                 handlers.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch (Exception)
            {
                // Host-side execution failures must not escape into the HID reader.
            }
        }
    }

    private async Task<byte[]> ExchangeAsync(
        ReadOnlyMemory<byte> request,
        ushort sequence,
        CancellationToken cancellationToken,
        bool expectsBootloaderDisconnect = false)
    {
        await exchangeLock.WaitAsync(cancellationToken);
        var completion = new TaskCompletionSource<byte[]>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            lock (pendingSync)
            {
                pendingRequest = new PendingRequest(
                    sequence, completion, expectsBootloaderDisconnect);
            }

            await transport.WriteAsync(request, cancellationToken);
            return await completion.Task.WaitAsync(ResponseTimeout, cancellationToken);
        }
        finally
        {
            lock (pendingSync)
            {
                if (ReferenceEquals(pendingRequest?.Completion, completion))
                {
                    pendingRequest = null;
                }
            }
            exchangeLock.Release();
        }
    }

    private void OnConnectionLost(Exception exception)
    {
        var expectedBootloader =
            Interlocked.Exchange(ref bootloaderRequestState, 0) == 2;
        CompletePendingRequest(exception);
        PublishConnectionState(
            false,
            expectedBootloader ? null : exception,
            expectedBootloader
                ? DeviceDisconnectReason.BootloaderRequested
                : DeviceDisconnectReason.Unexpected);
    }

    private void PublishConnectionState(
        bool connected,
        Exception? error = null,
        DeviceDisconnectReason disconnectReason = DeviceDisconnectReason.None)
    {
        var value = connected ? 1 : 0;
        if (Interlocked.Exchange(ref publishedConnectionState, value) == value) return;
        var handlers = ConnectionChanged;
        if (handlers is null) return;
        var args = new DeviceConnectionChangedEventArgs(
            connected, error, disconnectReason);
        foreach (EventHandler<DeviceConnectionChangedEventArgs> handler in
                 handlers.GetInvocationList())
            try { handler(this, args); }
            catch (Exception) { }
    }

    private void CompletePendingRequest(Exception exception)
    {
        TaskCompletionSource<byte[]>? completion;
        lock (pendingSync)
        {
            completion = pendingRequest?.Completion;
            pendingRequest = null;
        }
        completion?.TrySetException(exception);
    }

    private sealed record PendingRequest(
        ushort Sequence,
        TaskCompletionSource<byte[]> Completion,
        bool ExpectsBootloaderDisconnect);

    private ushort NextSequence()
    {
        lock (pendingSync)
        {
            var sequence = nextSequence++;
            if (nextSequence == 0) nextSequence = 1;
            return sequence;
        }
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("StreamDeck DIY is not connected.");
        }
    }
}
