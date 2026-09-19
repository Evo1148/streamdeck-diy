using StreamDeckDIY.Protocol.Messages;

namespace StreamDeckDIY.Protocol.Models;

public sealed record ProtocolPacket(
    MessageType MessageType,
    ushort Sequence,
    byte[] Payload);
