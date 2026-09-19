using System.Buffers.Binary;
using StreamDeckDIY.Protocol.Messages;
using StreamDeckDIY.Protocol.Models;

namespace StreamDeckDIY.Protocol.ProtocolV1;

public sealed class ProtocolV1Codec
{
    public const ushort PacketMagic = 0x4453;
    public const byte Version = 1;
    public const int PacketSize = 64;
    public const int HeaderSize = 8;
    public const int MaxPayloadSize = PacketSize - HeaderSize;

    private const int MagicOffset = 0;
    private const int VersionOffset = 2;
    private const int MessageTypeOffset = 3;
    private const int SequenceOffset = 4;
    private const int PayloadLengthOffset = 6;
    private const int DeviceInfoPayloadSize = 8;
    private const int ControlSize = 2;
    private const int BindingPayloadSize = ControlSize + ActionCodec.SerializedSize;
    private const int HostActionTriggeredPayloadSize = 4;

    public byte[] CreateGetDeviceInfo(ushort sequence) =>
        Serialize(new ProtocolPacket(MessageType.GetDeviceInfo, sequence, []));

    public byte[] CreateSetBinding(
        ushort sequence, ControlId control, DeviceAction action)
    {
        ValidateControl(control);
        var payload = new byte[BindingPayloadSize];
        WriteControl(control, payload);
        ActionCodec.Serialize(action).CopyTo(payload, ControlSize);
        return Serialize(new ProtocolPacket(MessageType.SetBinding, sequence, payload));
    }

    public byte[] CreateGetBinding(ushort sequence, ControlId control)
    {
        ValidateControl(control);
        var payload = new byte[ControlSize];
        WriteControl(control, payload);
        return Serialize(new ProtocolPacket(MessageType.GetBinding, sequence, payload));
    }

    public byte[] CreateExecuteActionTest(ushort sequence, DeviceAction action) =>
        Serialize(new ProtocolPacket(
            MessageType.ExecuteActionTest,
            sequence,
            ActionCodec.Serialize(action)));

    public byte[] CreateBeginConfigUpdate(ushort sequence) =>
        CreateEmptyRequest(MessageType.BeginConfigUpdate, sequence);

    public byte[] CreateCommitConfigUpdate(ushort sequence) =>
        CreateEmptyRequest(MessageType.CommitConfigUpdate, sequence);

    public byte[] CreateCancelConfigUpdate(ushort sequence) =>
        CreateEmptyRequest(MessageType.CancelConfigUpdate, sequence);

    public byte[] CreateEnterBootloader(ushort sequence) =>
        CreateEmptyRequest(MessageType.EnterBootloader, sequence);

    public byte[] Serialize(ProtocolPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(packet.Payload);

        if (packet.Payload.Length > MaxPayloadSize)
        {
            throw new ArgumentOutOfRangeException(nameof(packet),
                $"Payload cannot exceed {MaxPayloadSize} bytes.");
        }

        var data = new byte[PacketSize];
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(MagicOffset, 2), PacketMagic);
        data[VersionOffset] = Version;
        data[MessageTypeOffset] = (byte)packet.MessageType;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(SequenceOffset, 2), packet.Sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(PayloadLengthOffset, 2),
            checked((ushort)packet.Payload.Length));
        packet.Payload.CopyTo(data, HeaderSize);
        return data;
    }

    public ProtocolPacket Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length != PacketSize)
        {
            throw new ProtocolException($"Expected {PacketSize} bytes, received {data.Length}.");
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(MagicOffset, 2)) != PacketMagic)
        {
            throw new ProtocolException("The response has an invalid magic value.");
        }

        if (data[VersionOffset] != Version)
        {
            throw new ProtocolException($"Unsupported protocol version {data[VersionOffset]}.");
        }

        var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(
            data.Slice(PayloadLengthOffset, 2));
        if (payloadLength > MaxPayloadSize)
        {
            throw new ProtocolException($"Invalid payload length {payloadLength}.");
        }

        return new ProtocolPacket(
            (MessageType)data[MessageTypeOffset],
            BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(SequenceOffset, 2)),
            data.Slice(HeaderSize, payloadLength).ToArray());
    }

    public DeviceInfo ParseDeviceInfo(ReadOnlySpan<byte> response, ushort expectedSequence)
    {
        var packet = ParseExpected(response, expectedSequence, MessageType.DeviceInfo);
        if (packet.Payload.Length != DeviceInfoPayloadSize)
        {
            throw new ProtocolException(
                $"DEVICE_INFO payload must contain {DeviceInfoPayloadSize} bytes.");
        }

        var payload = packet.Payload.AsSpan();
        return new DeviceInfo(
            payload[0], payload[1], payload[2], payload[3], payload[4], payload[5],
            (DeviceCapabilities)BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2)));
    }

    public BindingInfo ParseBindingInfo(
        ReadOnlySpan<byte> response, ushort expectedSequence)
    {
        var packet = ParseExpected(response, expectedSequence, MessageType.BindingInfo);
        if (packet.Payload.Length != BindingPayloadSize)
        {
            throw new ProtocolException(
                $"BINDING_INFO payload must contain {BindingPayloadSize} bytes.");
        }

        var control = ReadControl(packet.Payload);
        return new BindingInfo(
            control,
            ActionCodec.Deserialize(packet.Payload.AsSpan(ControlSize, ActionCodec.SerializedSize)));
    }

    public void ParseAck(ReadOnlySpan<byte> response, ushort expectedSequence)
    {
        var packet = ParseExpected(response, expectedSequence, MessageType.Ack);
        if (packet.Payload.Length != 0)
        {
            throw new ProtocolException("ACK payload must be empty.");
        }
    }

    public HostActionTriggered ParseHostActionTriggered(ReadOnlySpan<byte> report)
    {
        var packet = Deserialize(report);
        if (packet.MessageType != MessageType.HostActionTriggered)
        {
            throw new ProtocolException(
                $"Expected {MessageType.HostActionTriggered}, received 0x{(byte)packet.MessageType:X2}.");
        }
        if (packet.Payload.Length != HostActionTriggeredPayloadSize)
        {
            throw new ProtocolException(
                $"HOST_ACTION_TRIGGERED payload must contain {HostActionTriggeredPayloadSize} bytes.");
        }
        return new HostActionTriggered(
            packet.Sequence,
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload));
    }

    public static bool IsResponse(MessageType messageType) =>
        messageType is MessageType.Ack or MessageType.Nack or
            MessageType.DeviceInfo or MessageType.BindingInfo or MessageType.DisplayLinkResponse;

    public static void ValidateControl(ControlId control)
    {
        var valid = control.Type switch
        {
            ControlType.Button => control.Index < 12,
            ControlType.EncoderClockwise or ControlType.EncoderCounterClockwise or
                ControlType.EncoderPress => control.Index < 1,
            _ => false,
        };
        if (!valid)
        {
            throw new ProtocolException(
                $"Invalid control {control.Type} index {control.Index}.");
        }
    }

    private ProtocolPacket ParseExpected(
        ReadOnlySpan<byte> response,
        ushort expectedSequence,
        MessageType expectedType)
    {
        var packet = Deserialize(response);
        if (packet.Sequence != expectedSequence)
        {
            throw new ProtocolException(
                $"Response sequence {packet.Sequence} does not match request {expectedSequence}.");
        }

        if (packet.MessageType == MessageType.Nack)
        {
            var reason = packet.Payload.Length > 0
                ? ((NackReason)packet.Payload[0]).ToString()
                : "Unknown";
            var rejectedType = packet.Payload.Length > 1 ? packet.Payload[1] : (byte)0;
            throw new ProtocolException(
                $"Device rejected message 0x{rejectedType:X2}: {reason}.");
        }

        if (packet.MessageType != expectedType)
        {
            throw new ProtocolException(
                $"Expected {expectedType}, received 0x{(byte)packet.MessageType:X2}.");
        }

        return packet;
    }

    private static void WriteControl(ControlId control, Span<byte> destination)
    {
        destination[0] = (byte)control.Type;
        destination[1] = control.Index;
    }

    private byte[] CreateEmptyRequest(MessageType type, ushort sequence) =>
        Serialize(new ProtocolPacket(type, sequence, []));

    private static ControlId ReadControl(ReadOnlySpan<byte> source)
    {
        var control = new ControlId((ControlType)source[0], source[1]);
        ValidateControl(control);
        return control;
    }
}
