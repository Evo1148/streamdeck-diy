using System.Diagnostics;

namespace StreamDeckDIY.Core.HostActions;

public sealed class WindowsHostActionLauncher : IHostActionLauncher
{
    public Task LaunchApplicationAsync(
        string target,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Process.Start(new ProcessStartInfo(target)
            {
                UseShellExecute = false,
            });
        }, cancellationToken);

    public Task OpenUrlAsync(Uri uri, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }, cancellationToken);
}
