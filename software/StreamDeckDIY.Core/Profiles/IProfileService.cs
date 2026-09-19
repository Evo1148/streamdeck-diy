using StreamDeckDIY.Protocol.Models;

namespace StreamDeckDIY.Core.Profiles;

public sealed record ProfileBindingSaveResult(
    bool DeviceSynchronized,
    bool DeviceSyncPending,
    string? DeviceSyncError = null);

public interface IProfileService
{
    IReadOnlyList<StreamDeckProfile> Profiles { get; }
    StreamDeckProfile ActiveProfile { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SynchronizeWithDeviceAsync(CancellationToken cancellationToken = default);
    Task<StreamDeckProfile> CreateAsync(
        string name, CancellationToken cancellationToken = default);
    Task RenameAsync(uint id, string name,
                     CancellationToken cancellationToken = default);
    Task DeleteAsync(uint id, CancellationToken cancellationToken = default);
    Task SwitchProfileAsync(uint targetId,
                            CancellationToken cancellationToken = default);
    Task<ProfileBindingSaveResult> SaveBindingAsync(
        uint profileId, ControlId control, DeviceAction action,
        CancellationToken cancellationToken = default);
    Task<ProfilePage> AddPageAsync(uint profileId, string name,
                                   CancellationToken cancellationToken = default);
    Task NavigateAsync(uint profileId, SystemNavigation navigation,
                       CancellationToken cancellationToken = default);
    Task SelectPageAsync(uint profileId, uint pageId,
                         CancellationToken cancellationToken = default);
    bool IsHome { get; }
}
