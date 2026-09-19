namespace StreamDeckDIY.Protocol.Models;

public enum ActionType : byte
{
    None,
    Keyboard,
    KeyboardShortcut,
    ConsumerControl,
    HostAction,
}
