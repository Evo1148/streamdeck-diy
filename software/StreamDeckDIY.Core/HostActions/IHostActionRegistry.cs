namespace StreamDeckDIY.Core.HostActions;

using StreamDeckDIY.Core.Audio;

public interface IHostActionRegistry
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<HostActionDefinition> GetAll();
    HostActionDefinition? GetById(uint id);
    Task<HostActionDefinition> CreateAsync(
        string name,
        HostActionKind kind,
        string? target = null,
        IReadOnlyList<MacroStep>? steps = null,
        AudioOutputConfiguration? audioOutput = null,
        CancellationToken cancellationToken = default);
    Task<HostActionDefinition> UpdateAsync(
        HostActionDefinition definition,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(uint id, CancellationToken cancellationToken = default);
}
