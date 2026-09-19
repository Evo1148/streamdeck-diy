namespace StreamDeckDIY.Core.Audio;

public interface IAudioOutputService
{
    Task<IReadOnlyList<AudioOutputDevice>> GetActiveOutputsAsync(
        CancellationToken cancellationToken = default);
    Task<AudioOutputDevice?> GetDefaultOutputAsync(
        CancellationToken cancellationToken = default);
    Task SetDefaultOutputAsync(
        string deviceId, CancellationToken cancellationToken = default);
}

public sealed class AudioOutputUnavailableException(string message) : Exception(message);
