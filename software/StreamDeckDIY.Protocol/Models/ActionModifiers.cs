namespace StreamDeckDIY.Protocol.Models;

[Flags]
public enum ActionModifiers : byte
{
    None = 0,
    Control = 0x01,
    Shift = 0x02,
    Alt = 0x04,
    Gui = 0x08,
}
