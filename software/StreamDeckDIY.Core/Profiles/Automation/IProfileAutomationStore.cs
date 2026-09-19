namespace StreamDeckDIY.Core.Profiles.Automation;

public interface IProfileAutomationStore
{
    ProfileAutomationSettings Settings { get; }
    string? LoadWarning { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ProfileAutomationSettings settings,
                   CancellationToken cancellationToken = default);
}
