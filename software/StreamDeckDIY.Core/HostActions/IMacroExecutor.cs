namespace StreamDeckDIY.Core.HostActions;

public interface IMacroExecutor
{
    Task<HostActionExecutionResult> ExecuteAsync(
        HostActionDefinition macro,
        CancellationToken cancellationToken = default);
}
