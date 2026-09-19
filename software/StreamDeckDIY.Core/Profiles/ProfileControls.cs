using StreamDeckDIY.Protocol.Models;

namespace StreamDeckDIY.Core.Profiles;

public static class ProfileControls
{
    public const int UserButtonCount = 9;
    public const int PhysicalButtonCount = 12;

    public static IReadOnlyList<ControlId> UserButtons { get; } =
        Enumerable.Range(0, UserButtonCount).Select(
            index => new ControlId(ControlType.Button, checked((byte)index))).ToArray();

    public static IReadOnlyList<ControlId> Encoder { get; } =
    [
        new(ControlType.EncoderClockwise, 0),
        new(ControlType.EncoderCounterClockwise, 0),
        new(ControlType.EncoderPress, 0),
    ];

    public static IReadOnlyList<ControlId> All { get; } =
    [
        .. Enumerable.Range(0, PhysicalButtonCount).Select(
            index => new ControlId(ControlType.Button, checked((byte)index))),
        .. Encoder,
    ];

    public static bool IsComplete(IReadOnlyCollection<BindingInfo> bindings) =>
        bindings.Count == All.Count &&
        All.All(control => bindings.Count(binding => binding.Control == control) == 1);

    public static bool IsPageComplete(IReadOnlyCollection<BindingInfo> bindings) =>
        bindings.Count == UserButtonCount &&
        UserButtons.All(control => bindings.Count(binding => binding.Control == control) == 1);

    public static BindingInfo[] RuntimeBindings(StreamDeckProfile profile)
    {
        var none = new DeviceAction(ActionType.None);
        return
        [
            .. profile.ActivePage.Bindings.OrderBy(binding => binding.Control.Index),
            .. Enumerable.Range(UserButtonCount, PhysicalButtonCount - UserButtonCount)
                .Select(index => new BindingInfo(
                    new ControlId(ControlType.Button, checked((byte)index)), none)),
            .. profile.Bindings.Where(binding => binding.Control.Type != ControlType.Button),
        ];
    }
}
