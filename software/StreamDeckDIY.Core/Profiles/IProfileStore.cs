using StreamDeckDIY.Protocol.Models;

namespace StreamDeckDIY.Core.Profiles;

public interface IProfileStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<StreamDeckProfile> GetAll();
    StreamDeckProfile? GetById(uint id);
    uint? ActiveProfileId { get; }
    Task<StreamDeckProfile> CreateInitialAsync(
        string name, IReadOnlyCollection<BindingInfo> bindings,
        CancellationToken cancellationToken = default);
    Task<StreamDeckProfile> CreateCopyAsync(
        string name, uint sourceId,
        CancellationToken cancellationToken = default);
    Task RenameAsync(uint id, string name,
                     CancellationToken cancellationToken = default);
    Task DeleteAsync(uint id, CancellationToken cancellationToken = default);
    Task SetActiveAsync(uint id, CancellationToken cancellationToken = default);
    Task ReplaceBindingsAsync(
        uint id, IReadOnlyCollection<BindingInfo> bindings,
        CancellationToken cancellationToken = default);
    Task UpdateBindingAsync(
        uint id, BindingInfo binding,
        CancellationToken cancellationToken = default);
    Task SetActivePageAsync(uint profileId, uint pageId,
                            CancellationToken cancellationToken = default);
    Task<ProfilePage> AddPageAsync(uint profileId, string name,
                                   CancellationToken cancellationToken = default);
}
