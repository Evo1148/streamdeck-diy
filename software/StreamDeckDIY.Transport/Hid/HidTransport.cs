using System.IO;
using HidSharp;

namespace StreamDeckDIY.Transport.Hid;

public sealed class HidTransport : IHidTransport
{
    private const int RawReportSize = StreamDeckHidIdentity.ReportSize + 1;
    private const uint VendorControlUsage =
        ((uint)StreamDeckHidIdentity.UsagePage << 16) | StreamDeckHidIdentity.UsageId;

    private readonly SemaphoreSlim writeLock = new(1, 1);
    private HidStream? stream;
    private CancellationTokenSource? readCancellation;
    private Task? readTask;
    private bool disposed;

    public event Action<ReadOnlyMemory<byte>>? ReportReceived;
    public event Action<Exception>? ConnectionLost;

    public bool IsOpen => Volatile.Read(ref stream) is not null;

    public async Task<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Close();

        var openedStream = await Task.Run(
            () => OpenVendorControl(cancellationToken), cancellationToken);
        if (openedStream is null) return false;
        if (cancellationToken.IsCancellationRequested)
        {
            openedStream.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }

        // The receive loop is permanent; request timeouts are managed by DeviceService.
        openedStream.ReadTimeout = Timeout.Infinite;
        var cancellation = new CancellationTokenSource();
        stream = openedStream;
        readCancellation = cancellation;
        readTask = ReadLoopAsync(openedStream, cancellation.Token);
        return true;
    }

    public async Task WriteAsync(
        ReadOnlyMemory<byte> packet,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (packet.Length != StreamDeckHidIdentity.ReportSize)
        {
            throw new ArgumentException(
                $"HID control reports must contain {StreamDeckHidIdentity.ReportSize} bytes.",
                nameof(packet));
        }

        await writeLock.WaitAsync(cancellationToken);
        try
        {
            var activeStream = stream ??
                throw new InvalidOperationException("HID device is not open.");
            try
            {
                var outputReport = new byte[RawReportSize];
                packet.CopyTo(outputReport.AsMemory(1));
                await activeStream.WriteAsync(outputReport, cancellationToken);
            }
            catch (Exception exception) when (
                exception is IOException or ObjectDisposedException or
                    UnauthorizedAccessException)
            {
                SignalConnectionLost(activeStream, exception);
                throw;
            }
        }
        finally
        {
            writeLock.Release();
        }
    }

    public void Close()
    {
        var cancellation = Interlocked.Exchange(ref readCancellation, null);
        var activeStream = Interlocked.Exchange(ref stream, null);
        cancellation?.Cancel();
        activeStream?.Dispose();
        cancellation?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        var activeReadTask = readTask;
        Close();
        if (activeReadTask is not null)
        {
            try
            {
                await activeReadTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        await writeLock.WaitAsync();
        writeLock.Release();
        writeLock.Dispose();
    }

    private async Task ReadLoopAsync(HidStream activeStream, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var rawReport = new byte[RawReportSize];
                await activeStream.ReadExactlyAsync(rawReport, cancellationToken);
                if (rawReport[0] != 0)
                {
                    throw new IOException(
                        $"Unexpected HID Report ID {rawReport[0]}; expected 0.");
                }

                var packet = rawReport.AsSpan(1, StreamDeckHidIdentity.ReportSize).ToArray();
                ReportReceived?.Invoke(packet);
            }
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException or
                ObjectDisposedException or UnauthorizedAccessException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                SignalConnectionLost(activeStream, exception);
            }
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(
                    ref stream, null, activeStream), activeStream))
            {
                activeStream.Dispose();
            }
        }
    }

    private void SignalConnectionLost(HidStream activeStream, Exception exception)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(
                ref stream, null, activeStream), activeStream)) return;

        activeStream.Dispose();
        var handlers = ConnectionLost;
        if (handlers is null) return;
        foreach (Action<Exception> handler in handlers.GetInvocationList())
            try { handler(exception); }
            catch (Exception) { }
    }

    private static HidStream? OpenVendorControl(CancellationToken cancellationToken)
    {
        foreach (var candidate in DeviceList.Local.GetHidDevices(
                     StreamDeckHidIdentity.VendorId,
                     StreamDeckHidIdentity.ProductId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsVendorControl(candidate)) continue;
            if (candidate.TryOpen(out HidStream? openedStream)) return openedStream;
        }
        return null;
    }

    private static bool IsVendorControl(HidDevice candidate)
    {
        try
        {
            var descriptor = candidate.GetReportDescriptor();
            var hasVendorUsage = descriptor.DeviceItems.Any(
                item => item.Usages.ContainsValue(VendorControlUsage));
            return hasVendorUsage && !descriptor.ReportsUseID &&
                   candidate.GetMaxInputReportLength() == RawReportSize &&
                   candidate.GetMaxOutputReportLength() == RawReportSize;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }
}
