using System.Buffers.Binary;
using StreamDeckDIY.Protocol.Models;

namespace StreamDeckDIY.Protocol.ProtocolV1;

public static class ActionCodec
{
    public const int SerializedSize = 8;
    private const ActionModifiers ValidModifiers =
        ActionModifiers.Control | ActionModifiers.Shift |
        ActionModifiers.Alt | ActionModifiers.Gui;

    public static byte[] Serialize(DeviceAction action)
    {
        Validate(action);
        var data = new byte[SerializedSize];
        data[0] = (byte)action.Type;
        data[1] = action.KeyCode;
        data[2] = (byte)action.Modifiers;
        data[3] = (byte)action.ConsumerControl;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), action.HostActionId);
        return data;
    }

    public static DeviceAction Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length != SerializedSize)
        {
            throw new ProtocolException($"Action must contain {SerializedSize} bytes.");
        }

        var action = new DeviceAction(
            (ActionType)data[0],
            data[1],
            (ActionModifiers)data[2],
            (ConsumerControlAction)data[3],
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4)));
        Validate(action);
        return action;
    }

    public static void Validate(DeviceAction action)
    {
        var valid = action.Type switch
        {
            ActionType.None =>
                action.KeyCode == 0 && action.Modifiers == ActionModifiers.None &&
                action.ConsumerControl == ConsumerControlAction.VolumeUp &&
                action.HostActionId == 0,
            ActionType.Keyboard =>
                action.KeyCode != 0 && action.Modifiers == ActionModifiers.None &&
                action.ConsumerControl == ConsumerControlAction.VolumeUp &&
                action.HostActionId == 0,
            ActionType.KeyboardShortcut =>
                action.KeyCode != 0 && action.Modifiers != ActionModifiers.None &&
                (action.Modifiers & ~ValidModifiers) == 0 &&
                action.ConsumerControl == ConsumerControlAction.VolumeUp &&
                action.HostActionId == 0,
            ActionType.ConsumerControl =>
                action.KeyCode == 0 && action.Modifiers == ActionModifiers.None &&
                action.ConsumerControl <= ConsumerControlAction.PlayPause &&
                action.HostActionId == 0,
            ActionType.HostAction =>
                action.KeyCode == 0 && action.Modifiers == ActionModifiers.None &&
                action.ConsumerControl == ConsumerControlAction.VolumeUp,
            _ => false,
        };

        if (!valid)
        {
            throw new ProtocolException("Action contains an invalid field combination.");
        }
    }
}
