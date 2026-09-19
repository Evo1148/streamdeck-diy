namespace StreamDeckDIY.Protocol.Models;

public readonly record struct HostActionTriggered(ushort Sequence, uint ActionId);
