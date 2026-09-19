namespace StreamDeckDIY.Core.Profiles.Automation;

public interface IAutoProfileService : IAsyncDisposable
{
    event EventHandler<AutoProfileFeedbackEventArgs>? FeedbackAvailable;
    event EventHandler? ProfileActivated;
    bool AutoSwitchEnabled { get; }
    IReadOnlyList<ProfileActivationRule> Rules { get; }
    string? LoadWarning { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task SetEnabledAsync(bool enabled,
                         CancellationToken cancellationToken = default);
    Task<ProfileActivationRule> CreateRuleAsync(
        string processName, uint profileId, bool enabled,
        CancellationToken cancellationToken = default);
    Task UpdateRuleAsync(ProfileActivationRule rule,
                         CancellationToken cancellationToken = default);
    Task DeleteRuleAsync(uint id, CancellationToken cancellationToken = default);
    Task EvaluateCurrentAsync(CancellationToken cancellationToken = default);
    Task ObserveAsync(ForegroundApplication application);
}

public sealed class AutoProfileFeedbackEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}
