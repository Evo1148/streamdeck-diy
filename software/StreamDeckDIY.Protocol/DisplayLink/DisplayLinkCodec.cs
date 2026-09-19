using System.Buffers.Binary;
using StreamDeckDIY.Protocol.Messages;
using StreamDeckDIY.Protocol.Models;
using StreamDeckDIY.Protocol.ProtocolV1;
namespace StreamDeckDIY.Protocol.DisplayLink;

public sealed class DisplayLinkCodec(ProtocolV1Codec protocol)
{
    public const byte Major=1,Minor=0;public const int EnvelopeSize=8,MaxBodySize=48;
    public byte[] CreateCommand(ushort sequence,DisplayLinkOpcode opcode,uint generation,
        ReadOnlySpan<byte> body=default,DisplayLinkFlags flags=DisplayLinkFlags.AckRequired)
    {
        if(body.Length>MaxBodySize)throw new ArgumentOutOfRangeException(nameof(body));
        var payload=new byte[EnvelopeSize+body.Length];payload[0]=Major;payload[1]=Minor;payload[2]=(byte)opcode;payload[3]=(byte)flags;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4),generation);body.CopyTo(payload.AsSpan(EnvelopeSize));
        return protocol.Serialize(new ProtocolPacket(MessageType.DisplayLinkCommand,sequence,payload));
    }
    public DisplayLinkResponse ParseResponse(ReadOnlySpan<byte> report,ushort sequence,DisplayLinkOpcode opcode)
    {
        var packet=protocol.Deserialize(report);if(packet.Sequence!=sequence)throw new ProtocolException("DisplayLink response sequence mismatch.");
        if(packet.MessageType==MessageType.Nack)throw new ProtocolException("Firmware does not support this DisplayLink request.");
        if(packet.MessageType!=MessageType.DisplayLinkResponse)throw new ProtocolException($"Expected DisplayLinkResponse, received {packet.MessageType}.");
        var result=ParsePayload(packet.Payload);if(result.Envelope.Opcode!=opcode)throw new ProtocolException("DisplayLink opcode mismatch.");
        if((result.Envelope.Flags&DisplayLinkFlags.ResponseError)!=0){var error=result.Body.Length==0?DisplayLinkError.InvalidState:(DisplayLinkError)result.Body[0];throw new DisplayLinkException(error,$"DisplayLink rejected {opcode}: {error}.");}
        return result;
    }
    public DisplayLinkResponse ParsePayload(ReadOnlySpan<byte> payload)
    {
        if(payload.Length<EnvelopeSize||payload.Length>ProtocolV1Codec.MaxPayloadSize)throw new ProtocolException("Invalid DisplayLink payload length.");
        var envelope=new DisplayLinkEnvelope(payload[0],payload[1],(DisplayLinkOpcode)payload[2],(DisplayLinkFlags)payload[3],BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4,4)));
        if(envelope.Major!=Major)throw new DisplayLinkException(DisplayLinkError.UnsupportedVersion,$"Unsupported DisplayLink {envelope.Major}.{envelope.Minor}.");
        return new(envelope,payload[EnvelopeSize..].ToArray());
    }
    public DisplayLinkInfo ParseInfo(DisplayLinkResponse response)
    {
        var b=response.Body;if(b.Length!=42)throw new ProtocolException("GET_INFO body must contain 42 bytes.");
        return new(response.Envelope.Major,response.Envelope.Minor,U16(b,0),U16(b,2),(DisplayPixelFormat)b[4],(DisplayBackendType)b[5],(b[6]&1)!=0,(b[6]&2)!=0,U32(b,8),U32(b,12),U16(b,16),U16(b,18),U16(b,20),U16(b,22),U16(b,24),b[26],U16(b,28),U32(b,30),U32(b,34),U32(b,38));
    }
    public DisplayLinkStatus ParseStatus(DisplayLinkResponse response)
    {
        var b=response.Body;if(b.Length!=28)throw new ProtocolException("GET_STATUS body must contain 28 bytes.");
        return new(U32(b,0),U32(b,4),(DisplayMode)b[8],b[9]!=0,U16(b,10),U16(b,12),U16(b,14),U32(b,16),b[20],(DisplayLinkError)b[21],b[22],U32(b,24));
    }
    public DisplayTouchEvent ParseTouchEvent(ReadOnlySpan<byte> report)
    {
        var packet=protocol.Deserialize(report);if(packet.MessageType!=MessageType.DisplayLinkEvent)throw new ProtocolException("Expected DisplayLinkEvent.");var p=ParsePayload(packet.Payload);if(p.Envelope.Opcode!=DisplayLinkOpcode.TouchEvent||p.Body.Length!=16)throw new ProtocolException("Invalid touch event.");var b=p.Body;return new(p.Envelope.Generation,U32(b,0),U16(b,4),b[6],I16(b,8),I16(b,10),U32(b,12));
    }
    public static void WriteU16(Span<byte>b,int o,ushort v)=>BinaryPrimitives.WriteUInt16LittleEndian(b[o..],v);
    public static void WriteI16(Span<byte>b,int o,short v)=>BinaryPrimitives.WriteInt16LittleEndian(b[o..],v);
    public static void WriteU32(Span<byte>b,int o,uint v)=>BinaryPrimitives.WriteUInt32LittleEndian(b[o..],v);
    private static ushort U16(byte[]b,int o)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));private static short I16(byte[]b,int o)=>BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(o));private static uint U32(byte[]b,int o)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
}
