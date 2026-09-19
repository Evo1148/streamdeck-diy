namespace StreamDeckDIY.Protocol.Models;

public sealed record DeviceInfo(
    byte ProtocolVersion,
    byte FirmwareMajor,
    byte FirmwareMinor,
    byte FirmwarePatch,
    byte ButtonCount,
    byte EncoderCount,
    DeviceCapabilities Capabilities);
