using System.Diagnostics;
using System.Runtime.InteropServices;
using StreamDeckDIY.Core.Profiles.Automation;

namespace StreamDeckDIY.App.Services;

public sealed class WindowsForegroundApplicationProvider
    : IForegroundApplicationProvider
{
    public Task<ForegroundApplication?> GetForegroundApplicationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = GetForegroundWindow();
        if (window == nint.Zero) return Task.FromResult<ForegroundApplication?>(null);
        _ = GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return Task.FromResult<ForegroundApplication?>(null);
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return Task.FromResult<ForegroundApplication?>(
                new ForegroundApplication(processId, process.ProcessName));
        }
        catch (ArgumentException)
        {
            return Task.FromResult<ForegroundApplication?>(null);
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult<ForegroundApplication?>(null);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint window, out uint processId);
}
