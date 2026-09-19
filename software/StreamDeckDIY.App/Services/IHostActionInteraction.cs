using StreamDeckDIY.Core.HostActions;

namespace StreamDeckDIY.App.Services;

public interface IHostActionInteraction
{
    Task<string?> PickExecutableAsync();
    Task<bool> ConfirmDeleteAsync(
        HostActionDefinition definition,
        int macroUsageCount);
}
