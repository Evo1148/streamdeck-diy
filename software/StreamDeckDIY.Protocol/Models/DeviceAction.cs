namespace StreamDeckDIY.Protocol.Models;

public readonly record struct DeviceAction(
    ActionType Type,
    byte KeyCode = 0,
    ActionModifiers Modifiers = ActionModifiers.None,
    ConsumerControlAction ConsumerControl = ConsumerControlAction.VolumeUp,
    uint HostActionId = 0);
