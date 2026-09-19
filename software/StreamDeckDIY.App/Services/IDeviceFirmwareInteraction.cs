namespace StreamDeckDIY.App.Services;

public interface IDeviceFirmwareInteraction
{
    Task<bool> ConfirmEnterBootloaderAsync();
    Task ShowBootloaderReadyAsync();
}
