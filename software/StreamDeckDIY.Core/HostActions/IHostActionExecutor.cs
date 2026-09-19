namespace StreamDeckDIY.Core.HostActions;

public enum HostActionExecutionStatus
{
    Executed,
    NotConfigured,
    InvalidDefinition,
    TargetNotFound,
    Failed,
    AlreadyRunning,
}

public readonly record struct HostActionExecutionResult(
    HostActionExecutionStatus Status,
    string Message);

public interface IHostActionExecutor
{
    Task<HostActionExecutionResult> ExecuteAsync(
        uint actionId,
        CancellationToken cancellationToken = default);
}
