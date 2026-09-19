namespace StreamDeckDIY.Protocol.Models;

[Flags]
public enum DeviceCapabilities : ushort
{
    None = 0,
    KeyboardHid = 1 << 0,
    ConsumerHid = 1 << 1,
    VendorControl = 1 << 2,
    Display = 1 << 3,
    Touch = 1 << 4,
}
