using StreamDeckDIY.Core.Devices;
using StreamDeckDIY.Protocol.Models;

namespace StreamDeckDIY.Core.Profiles;

public sealed class ProfileService : IProfileService
{
    private readonly IProfileStore store;
    private readonly IDeviceService device;
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private Func<CancellationToken, Task>? beforeProfileSwitch;
    private bool isHome;

    public ProfileService(IProfileStore store, IDeviceService device)
    {
        this.store = store;
        this.device = device;
    }

    public void SetBeforeProfileSwitch(Func<CancellationToken, Task> callback) =>
        beforeProfileSwitch = callback ?? throw new ArgumentNullException(nameof(callback));

    public IReadOnlyList<StreamDeckProfile> Profiles => store.GetAll();
    public StreamDeckProfile ActiveProfile =>
        store.ActiveProfileId is uint id && store.GetById(id) is { } profile
            ? profile
            : throw new InvalidOperationException("No hay un perfil activo.");
    public bool IsHome => isHome;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken);
        try { await store.InitializeAsync(cancellationToken); }
        finally { operationLock.Release(); }
    }

    public async Task SynchronizeWithDeviceAsync(
        CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            if (store.GetAll().Count == 0)
            {
                var runtimeBindings = await ReadRuntimeBindingsAsync(cancellationToken);
                await store.CreateInitialAsync(
                    "Predeterminado", runtimeBindings, cancellationToken);
                return;
            }

            await ApplyProfileToDeviceAsync(ActiveProfile, cancellationToken);
        }
        finally { operationLock.Release(); }
    }

    public async Task<StreamDeckProfile> CreateAsync(
        string name, CancellationToken cancellationToken = default)
    {
        name = ValidateName(name);
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            return await store.CreateCopyAsync(
                name, ActiveProfile.Id, cancellationToken);
        }
        finally { operationLock.Release(); }
    }

    public async Task RenameAsync(uint id, string name,
                                  CancellationToken cancellationToken = default)
    {
        name = ValidateName(name);
        await operationLock.WaitAsync(cancellationToken);
        try { await store.RenameAsync(id, name, cancellationToken); }
        finally { operationLock.Release(); }
    }

    public async Task DeleteAsync(
        uint id, CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            if (Profiles.Count == 1)
                throw new InvalidOperationException("No se puede eliminar el único perfil.");
            if (ActiveProfile.Id == id)
                throw new InvalidOperationException(
                    "Cambia a otro perfil antes de eliminar el perfil activo.");
            await store.DeleteAsync(id, cancellationToken);
        }
        finally { operationLock.Release(); }
    }

    public async Task SwitchProfileAsync(
        uint targetId, CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            var target = store.GetById(targetId) ??
                throw new KeyNotFoundException($"No existe el perfil {targetId}.");
            if (target.Id == ActiveProfile.Id) return;

            if (beforeProfileSwitch is not null)
                await beforeProfileSwitch(cancellationToken);
            await ApplyProfileToDeviceAsync(target, cancellationToken);
            await store.SetActiveAsync(target.Id, cancellationToken);
            isHome = false;
        }
        finally { operationLock.Release(); }
    }

    public async Task<ProfileBindingSaveResult> SaveBindingAsync(
        uint profileId, ControlId control, DeviceAction action,
        CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            _ = store.GetById(profileId) ??
                throw new KeyNotFoundException($"No existe el perfil {profileId}.");
            var activeProfileId = ActiveProfile.Id;

            await store.UpdateBindingAsync(
                profileId, new BindingInfo(control, action), cancellationToken);

            if (profileId != activeProfileId || isHome)
                return new(false, false);
            if (!device.IsConnected)
                return new(false, true);

            try
            {
                await device.SetBindingAsync(control, action, cancellationToken);
                return new(true, false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                return new(false, true, exception.Message);
            }
        }
        finally { operationLock.Release(); }
    }

    public async Task<ProfilePage> AddPageAsync(
        uint profileId, string name, CancellationToken cancellationToken = default)
    {
        name = ValidateName(name);
        await operationLock.WaitAsync(cancellationToken);
        try { return await store.AddPageAsync(profileId, name, cancellationToken); }
        finally { operationLock.Release(); }
    }

    public async Task SelectPageAsync(
        uint profileId, uint pageId, CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            var profile = store.GetById(profileId) ??
                throw new KeyNotFoundException($"No existe el perfil {profileId}.");
            if (profile.ActivePageId == pageId && !isHome) return;
            var target = profile with { ActivePageId = pageId };
            _ = target.ActivePage;
            target = target with { Bindings = ProfileControls.RuntimeBindings(target) };
            if (profileId == ActiveProfile.Id)
                await ApplyProfileToDeviceAsync(target, cancellationToken);
            await store.SetActivePageAsync(profileId, pageId, cancellationToken);
            if (profileId == ActiveProfile.Id) isHome = false;
        }
        finally { operationLock.Release(); }
    }

    public async Task NavigateAsync(
        uint profileId, SystemNavigation navigation,
        CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            var profile = store.GetById(profileId) ??
                throw new KeyNotFoundException($"No existe el perfil {profileId}.");
            if (profileId != ActiveProfile.Id)
                throw new InvalidOperationException(
                    "La navegación física solo puede cambiar el perfil activo.");
            if (navigation == SystemNavigation.Home)
            {
                if (isHome) return;
                var home = profile with
                {
                    Bindings = profile.Bindings.Select(binding =>
                        binding.Control.Type == ControlType.Button
                            ? binding with { Action = new DeviceAction(ActionType.None) }
                            : binding).ToArray(),
                };
                await ApplyProfileToDeviceAsync(home, cancellationToken);
                isHome = true;
                return;
            }

            var targetIndex = isHome
                ? Array.FindIndex(profile.Pages, page => page.Id == profile.DefaultPageId)
                : profile.ActivePageIndex +
                    (navigation == SystemNavigation.Previous ? -1 : 1);
            if (targetIndex < 0 || targetIndex >= profile.Pages.Length) return;
            var targetPageId = profile.Pages[targetIndex].Id;
            var target = profile with { ActivePageId = targetPageId };
            target = target with { Bindings = ProfileControls.RuntimeBindings(target) };
            await ApplyProfileToDeviceAsync(target, cancellationToken);
            await store.SetActivePageAsync(profileId, targetPageId, cancellationToken);
            isHome = false;
        }
        finally { operationLock.Release(); }
    }

    private async Task ApplyProfileToDeviceAsync(
        StreamDeckProfile profile, CancellationToken cancellationToken)
    {
        var began = false;
        try
        {
            await device.BeginConfigUpdateAsync(cancellationToken);
            began = true;
            foreach (var binding in profile.Bindings)
                await device.SetBindingAsync(
                    binding.Control, binding.Action, cancellationToken);
            await device.CommitConfigUpdateAsync(cancellationToken);
            began = false;
        }
        catch
        {
            if (began)
            {
                try { await device.CancelConfigUpdateAsync(CancellationToken.None); }
                catch { /* Preserve the original synchronization failure. */ }
            }
            throw;
        }
    }

    private async Task<BindingInfo[]> ReadRuntimeBindingsAsync(
        CancellationToken cancellationToken)
    {
        var result = new BindingInfo[ProfileControls.All.Count];
        for (var index = 0; index < ProfileControls.All.Count; index++)
            result[index] = await device.GetBindingAsync(
                ProfileControls.All[index], cancellationToken);
        return result;
    }

    private static string ValidateName(string name)
    {
        var value = name?.Trim();
        return string.IsNullOrEmpty(value)
            ? throw new ArgumentException("El nombre del perfil es obligatorio.", nameof(name))
            : value;
    }
}
