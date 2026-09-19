namespace StreamDeckDIY.Core.HostActions;

public interface IHostActionLauncher
{
    Task LaunchApplicationAsync(string target, CancellationToken cancellationToken);
    Task OpenUrlAsync(Uri uri, CancellationToken cancellationToken);
}
