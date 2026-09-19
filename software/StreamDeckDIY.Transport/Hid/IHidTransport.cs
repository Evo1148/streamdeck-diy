namespace StreamDeckDIY.Transport.Hid;

public interface IHidTransport : IAsyncDisposable
{
    event Action<ReadOnlyMemory<byte>>? ReportReceived;
    event Action<Exception>? ConnectionLost;

    bool IsOpen { get; }

    Task<bool> OpenAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(
        ReadOnlyMemory<byte> packet,
        CancellationToken cancellationToken = default);
    void Close();
}
