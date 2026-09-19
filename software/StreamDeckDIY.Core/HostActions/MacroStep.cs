namespace StreamDeckDIY.Core.HostActions;

public enum MacroStepKind
{
    ExecuteHostAction,
    Delay,
}

public sealed record MacroStep(
    MacroStepKind Kind,
    uint ActionId = 0,
    int DelayMilliseconds = 0)
{
    public static MacroStep Execute(uint actionId) =>
        new(MacroStepKind.ExecuteHostAction, ActionId: actionId);

    public static MacroStep Delay(int milliseconds) =>
        new(MacroStepKind.Delay, DelayMilliseconds: milliseconds);
}
