namespace StreamDeckDIY.Core.Devices;

using StreamDeckDIY.Protocol.Models;

public interface IDeviceService : IAsyncDisposable
{
    event EventHandler<HostActionTriggeredEventArgs>? HostActionTriggered;
    event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged
    {
        add { }
        remove { }
    }

    bool IsConnected { get; }

    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
    void Disconnect();
    Task<StreamDeckDeviceInfo> GetDeviceInfoAsync(
        CancellationToken cancellationToken = default);
    Task<BindingInfo> GetBindingAsync(
        ControlId control,
        CancellationToken cancellationToken = default);
    Task SetBindingAsync(
        ControlId control,
        DeviceAction action,
        CancellationToken cancellationToken = default);
    Task ExecuteActionTestAsync(
        DeviceAction action,
        CancellationToken cancellationToken = default);
    Task BeginConfigUpdateAsync(CancellationToken cancellationToken = default);
    Task CommitConfigUpdateAsync(CancellationToken cancellationToken = default);
    Task CancelConfigUpdateAsync(CancellationToken cancellationToken = default);
    Task EnterBootloaderAsync(CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException(
            "This device service does not support bootloader entry."));
}

public enum DeviceDisconnectReason
{
    None,
    Unexpected,
    BootloaderRequested,
}

public sealed class DeviceConnectionChangedEventArgs(
    bool isConnected,
    Exception? error = null,
    DeviceDisconnectReason disconnectReason = DeviceDisconnectReason.None)
    : EventArgs
{
    public bool IsConnected { get; } = isConnected;
    public Exception? Error { get; } = error;
    public DeviceDisconnectReason DisconnectReason { get; } = disconnectReason;
}
