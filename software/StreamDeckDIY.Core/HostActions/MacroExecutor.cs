namespace StreamDeckDIY.Core.HostActions;

public sealed class MacroExecutor(
    IHostActionRegistry registry,
    IHostActionDefinitionExecutor definitionExecutor,
    IAsyncDelay delay) : IMacroExecutor
{
    private readonly object sync = new();
    private readonly HashSet<uint> running = [];

    public async Task<HostActionExecutionResult> ExecuteAsync(
        HostActionDefinition macro,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            if (!running.Add(macro.Id))
                return new(HostActionExecutionStatus.AlreadyRunning,
                    $"La macro '{macro.Name}' ya está ejecutándose.");
        }

        try
        {
            if (macro.Kind != HostActionKind.Macro || macro.Steps is not { Count: > 0 })
                return new(HostActionExecutionStatus.InvalidDefinition,
                    "La macro debe contener al menos un paso.");

            for (var index = 0; index < macro.Steps!.Count; index++)
            {
                var step = macro.Steps[index];
                if (step.Kind == MacroStepKind.Delay)
                {
                    if (step.DelayMilliseconds is < 1 or > 60000)
                        return StepFailure(
                            macro, index,
                            "el tiempo de espera debe estar entre 1 y 60000 ms.");
                    await delay.DelayAsync(step.DelayMilliseconds, cancellationToken);
                    continue;
                }

                if (step.Kind != MacroStepKind.ExecuteHostAction)
                    return StepFailure(macro, index, "tipo de paso no compatible.");

                var action = registry.GetById(step.ActionId);
                if (action is null)
                    return StepFailure(macro, index, "la acción seleccionada ya no existe.");
                if (action.Kind == HostActionKind.Macro)
                    return StepFailure(macro, index, "una macro no puede contener otra macro.");

                var result = await definitionExecutor.ExecuteAsync(
                    action, cancellationToken);
                if (result.Status != HostActionExecutionStatus.Executed)
                    return StepFailure(macro, index, result.Message);
            }

            return new(HostActionExecutionStatus.Executed,
                $"Macro '{macro.Name}' ejecutada.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(HostActionExecutionStatus.Failed,
                $"Macro '{macro.Name}' detenida: {exception.Message}");
        }
        finally
        {
            lock (sync) running.Remove(macro.Id);
        }
    }

    private static HostActionExecutionResult StepFailure(
        HostActionDefinition macro,
        int zeroBasedIndex,
        string reason) =>
        new(HostActionExecutionStatus.Failed,
            $"Macro '{macro.Name}' detenida en el paso {zeroBasedIndex + 1}: {reason}");
}
