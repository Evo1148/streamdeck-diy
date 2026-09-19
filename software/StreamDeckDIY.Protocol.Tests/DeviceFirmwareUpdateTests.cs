using StreamDeckDIY.App.Services;
using StreamDeckDIY.App.ViewModels;
using StreamDeckDIY.Core.Devices;
using StreamDeckDIY.Protocol.Messages;
using StreamDeckDIY.Protocol.Models;
using StreamDeckDIY.Protocol.ProtocolV1;

internal static class DeviceFirmwareUpdateTests
{
    public static async Task RunAsync(ProtocolV1Codec codec)
    {
        await CancelledConfirmationDoesNotSendAsync(codec);
        await DoubleRequestSendsOnceAsync(codec);
        await NackIsPresentedAsync(codec);
    }

    private static async Task CancelledConfirmationDoesNotSendAsync(
        ProtocolV1Codec codec)
    {
        var transport = new TestTransport();
        await using var service = new DeviceService(transport, codec);
        var interaction = new FirmwareInteraction { Confirm = false };
        var viewModel = new DeviceFirmwareUpdateViewModel(service, interaction);
        var writes = 0;
        transport.OnWrite = _ => writes++;
        await viewModel.EnterBootloaderAsync();
        Assert(writes == 0 && interaction.Shown == 0,
            "Cancelled bootloader confirmation sends nothing");
    }

    private static async Task DoubleRequestSendsOnceAsync(ProtocolV1Codec codec)
    {
        var transport = new TestTransport();
        await using var service = new DeviceService(transport, codec);
        var confirmation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var interaction = new FirmwareInteraction
        {
            Confirmation = confirmation.Task,
        };
        var viewModel = new DeviceFirmwareUpdateViewModel(service, interaction);
        var writes = 0;
        transport.OnWrite = request =>
        {
            writes++;
            var sequence = codec.Deserialize(request.Span).Sequence;
            transport.Emit(codec.Serialize(new ProtocolPacket(
                MessageType.Ack, sequence, [])));
        };
        var first = viewModel.EnterBootloaderAsync();
        var second = viewModel.EnterBootloaderAsync();
        confirmation.SetResult(true);
        await Task.WhenAll(first, second);
        Assert(writes == 1 && interaction.Shown == 1,
            "Double bootloader request sends once");
    }

    private static async Task NackIsPresentedAsync(ProtocolV1Codec codec)
    {
        var transport = new TestTransport();
        await using var service = new DeviceService(transport, codec);
        var interaction = new FirmwareInteraction { Confirm = true };
        var viewModel = new DeviceFirmwareUpdateViewModel(service, interaction);
        transport.OnWrite = request =>
        {
            var sequence = codec.Deserialize(request.Span).Sequence;
            transport.Emit(codec.Serialize(new ProtocolPacket(
                MessageType.Nack, sequence,
                [(byte)NackReason.InvalidState,
                 (byte)MessageType.EnterBootloader])));
        };
        await viewModel.EnterBootloaderAsync();
        Assert(viewModel.StatusText.StartsWith(
                   "No se pudo entrar en modo actualización:",
                   StringComparison.Ordinal) && interaction.Shown == 0,
            "Bootloader NACK is shown without success dialog");
    }

    private sealed class FirmwareInteraction : IDeviceFirmwareInteraction
    {
        public bool Confirm { get; init; }
        public Task<bool>? Confirmation { get; init; }
        public int Shown { get; private set; }

        public Task<bool> ConfirmEnterBootloaderAsync() =>
            Confirmation ?? Task.FromResult(Confirm);

        public Task ShowBootloaderReadyAsync()
        {
            Shown++;
            return Task.CompletedTask;
        }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Test failed: {name}");
    }
}
