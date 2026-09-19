namespace StreamDeckDIY.Core.Audio;

public sealed record AudioOutputDevice(
    string Id,
    string DisplayName,
    bool IsAvailable = true)
{
    public override string ToString() => DisplayName;
}
