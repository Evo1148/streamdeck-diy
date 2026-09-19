namespace StreamDeckDIY.Core.Devices;

public sealed record StreamDeckDeviceInfo(
    byte ProtocolVersion,
    Version FirmwareVersion,
    byte ButtonCount,
    byte EncoderCount,
    StreamDeckCapabilities Capabilities);
