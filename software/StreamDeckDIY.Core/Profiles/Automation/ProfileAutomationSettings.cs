namespace StreamDeckDIY.Core.Profiles.Automation;

public sealed record ProfileAutomationSettings(
    bool AutoSwitchEnabled,
    uint NextId,
    ProfileActivationRule[] Rules);
