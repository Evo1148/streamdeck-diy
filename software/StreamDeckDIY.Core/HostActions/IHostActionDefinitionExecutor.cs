namespace StreamDeckDIY.Core.HostActions;

public interface IHostActionDefinitionExecutor
{
    Task<HostActionExecutionResult> ExecuteAsync(
        HostActionDefinition definition,
        CancellationToken cancellationToken = default);
}
