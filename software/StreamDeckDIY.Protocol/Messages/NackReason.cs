namespace StreamDeckDIY.Protocol.Messages;

public enum NackReason : byte
{
    InvalidPacketSize = 1,
    InvalidMagic,
    UnsupportedVersion,
    UnknownMessageType,
    InvalidPayloadLength,
    UnsupportedMessage,
    InvalidControl,
    InvalidAction,
    ActionBusy,
    UnsupportedAction,
    StorageError,
    InvalidState,
}
