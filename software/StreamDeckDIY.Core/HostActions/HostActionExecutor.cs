namespace StreamDeckDIY.Core.HostActions;

public sealed class HostActionExecutor(
    IHostActionRegistry registry,
    IHostActionDefinitionExecutor definitionExecutor,
    IMacroExecutor macroExecutor) : IHostActionExecutor
{
    public async Task<HostActionExecutionResult> ExecuteAsync(
        uint actionId,
        CancellationToken cancellationToken = default)
    {
        var definition = registry.GetById(actionId);
        if (definition is null)
        {
            return new(HostActionExecutionStatus.NotConfigured,
                $"Host Action {actionId} no está configurada.");
        }

        return definition.Kind == HostActionKind.Macro
            ? await macroExecutor.ExecuteAsync(definition, cancellationToken)
            : await definitionExecutor.ExecuteAsync(definition, cancellationToken);
    }
}
