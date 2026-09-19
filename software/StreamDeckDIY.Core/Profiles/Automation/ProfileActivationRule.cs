namespace StreamDeckDIY.Core.Profiles.Automation;

public sealed record ProfileActivationRule(
    uint Id,
    string ProcessName,
    uint ProfileId,
    bool Enabled);
