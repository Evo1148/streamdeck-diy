namespace StreamDeckDIY.Core.Profiles.Automation;

public interface IForegroundApplicationProvider
{
    Task<ForegroundApplication?> GetForegroundApplicationAsync(
        CancellationToken cancellationToken = default);
}
