using System.Windows.Input;
using StreamDeckDIY.App.Services;
using StreamDeckDIY.Core.Devices;
using StreamDeckDIY.Protocol.ProtocolV1;

namespace StreamDeckDIY.App.ViewModels;

public sealed class DeviceFirmwareUpdateViewModel : ObservableObject
{
    private readonly IDeviceService deviceService;
    private readonly IDeviceFirmwareInteraction interaction;
    private int requestRunning;
    private string statusText = "La copia del UF2 se realiza manualmente.";

    public DeviceFirmwareUpdateViewModel(
        IDeviceService deviceService,
        IDeviceFirmwareInteraction interaction)
    {
        this.deviceService = deviceService;
        this.interaction = interaction;
        EnterBootloaderCommand = new AsyncRelayCommand(
            _ => EnterBootloaderAsync());
    }

    public ICommand EnterBootloaderCommand { get; }
    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public async Task EnterBootloaderAsync()
    {
        if (Interlocked.Exchange(ref requestRunning, 1) != 0) return;
        try
        {
            if (!await interaction.ConfirmEnterBootloaderAsync()) return;
            StatusText = "Solicitando modo actualización...";
            await deviceService.EnterBootloaderAsync();
            StatusText = "Orden confirmada. Esperando RPI-RP2.";
            await interaction.ShowBootloaderReadyAsync();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or
                ProtocolException or TimeoutException)
        {
            StatusText = $"No se pudo entrar en modo actualización: {exception.Message}";
        }
        finally
        {
            Interlocked.Exchange(ref requestRunning, 0);
        }
    }
}
