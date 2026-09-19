namespace StreamDeckDIY.Core.Audio;

public sealed record AudioOutputConfiguration(
    string PrimaryDeviceId,
    string? SecondaryDeviceId = null,
    string? PrimaryDeviceName = null,
    string? SecondaryDeviceName = null);
