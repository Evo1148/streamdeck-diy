namespace StreamDeckDIY.Core.Devices;

public sealed class HostActionTriggeredEventArgs(uint actionId) : EventArgs
{
    public uint ActionId { get; } = actionId;
}
