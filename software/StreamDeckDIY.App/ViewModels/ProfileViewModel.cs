using System.Collections.ObjectModel;
using System.Windows.Input;
using StreamDeckDIY.App.Services;
using StreamDeckDIY.Core.Profiles;

namespace StreamDeckDIY.App.ViewModels;

public sealed class ProfileViewModel : ObservableObject
{
    private readonly IProfileService service;
    private readonly IProfileInteraction interaction;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private StreamDeckProfile? activeProfile;
    private StreamDeckProfile? selectedProfile;
    private ProfilePresentationItem? selectedProfileItem;
    private bool suppressActivation;
    private bool initialized;
    private bool isBusy = true;
    private string feedback = string.Empty;

    public ProfileViewModel(IProfileService service, IProfileInteraction interaction)
    {
        this.service = service;
        this.interaction = interaction;
        NewCommand = new AsyncRelayCommand(_ => CreateAsync());
        RenameCommand = new AsyncRelayCommand(_ => RenameAsync());
        DeleteCommand = new AsyncRelayCommand(_ => DeleteAsync());
    }

    public event EventHandler? ActiveProfileChanged;
    public event EventHandler? ProfilesChanged;
    public event EventHandler? SelectedProfileChanged;

    public ObservableCollection<StreamDeckProfile> Profiles { get; } = [];
    public ObservableCollection<ProfilePresentationItem> ProfileItems { get; } = [];
    public ICommand NewCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand DeleteCommand { get; }

    public StreamDeckProfile? ActiveProfile
    {
        get => activeProfile;
        set
        {
            if (!SetProperty(ref activeProfile, value) || suppressActivation || value is null)
                return;
            _ = ActivateProfileAsync(value);
        }
    }

    public StreamDeckProfile? SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (!SetProperty(ref selectedProfile, value)) return;
            if (!suppressActivation)
            {
                SelectedProfileItem = ProfileItems.FirstOrDefault(
                    item => item.Profile.Id == value?.Id);
                SelectedProfileChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public ProfilePresentationItem? SelectedProfileItem
    {
        get => selectedProfileItem;
        set
        {
            if (!SetProperty(ref selectedProfileItem, value) ||
                suppressActivation || value is null) return;
            SelectedProfile = value.Profile;
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value)) OnPropertyChanged(nameof(CanInteract));
        }
    }
    public bool CanInteract => !IsBusy;

    public string Feedback
    {
        get => feedback;
        private set => SetProperty(ref feedback, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        var entered = false;
        try
        {
            await initializationLock.WaitAsync(cancellationToken);
            entered = true;
            if (initialized) return;
            await service.InitializeAsync(cancellationToken);
            Refresh();
            initialized = true;
        }
        finally
        {
            if (entered) initializationLock.Release();
            IsBusy = false;
        }
    }

    public void Refresh(uint? selectedId = null)
    {
        suppressActivation = true;
        try
        {
            var available = service.Profiles;
            if (available.Count == 0)
            {
                Profiles.Clear();
                ProfileItems.Clear();
                ActiveProfile = null;
                SelectedProfile = null;
                SelectedProfileItem = null;
                return;
            }
            var active = service.ActiveProfile;
            var managementId = selectedId ?? SelectedProfile?.Id ?? active.Id;
            Profiles.Clear();
            foreach (var profile in available) Profiles.Add(profile);
            ActiveProfile = Profiles.First(
                profile => profile.Id == active.Id);
            SelectedProfile = Profiles.FirstOrDefault(
                profile => profile.Id == managementId) ?? ActiveProfile;
            ProfileItems.Clear();
            foreach (var profile in Profiles)
                ProfileItems.Add(new ProfilePresentationItem(
                    profile, profile.Id == active.Id));
            SelectedProfileItem = ProfileItems.FirstOrDefault(
                item => item.Profile.Id == SelectedProfile?.Id);
        }
        finally
        {
            suppressActivation = false;
        }
    }

    public async Task ActivateProfileAsync(StreamDeckProfile target)
    {
        if (IsBusy || target.Id == service.ActiveProfile.Id) return;
        var managementId = SelectedProfile?.Id;
        IsBusy = true;
        Feedback = "Aplicando perfil...";
        try
        {
            await service.SwitchProfileAsync(target.Id);
            Refresh(managementId);
            Feedback = $"Perfil '{target.Name}' activo";
            ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Refresh(managementId);
            Feedback = $"No se pudo aplicar el perfil: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CreateAsync()
    {
        if (IsBusy) return;
        var name = await interaction.RequestNameAsync("Nuevo perfil", "Nuevo perfil");
        if (name is null) return;
        IsBusy = true;
        try
        {
            var created = await service.CreateAsync(name);
            Refresh(created.Id);
            Feedback = $"Perfil '{created.Name}' creado.";
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Feedback = $"No se pudo crear el perfil: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RenameSelectedAsync()
    {
        if (IsBusy || SelectedProfile is null) return;
        var profile = SelectedProfile;
        var name = await interaction.RequestNameAsync("Renombrar perfil", profile.Name);
        if (name is null) return;
        IsBusy = true;
        try
        {
            await service.RenameAsync(profile.Id, name);
            Refresh(profile.Id);
            Feedback = "Perfil renombrado.";
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Feedback = $"No se pudo renombrar el perfil: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteSelectedAsync()
    {
        if (IsBusy || SelectedProfile is null) return;
        var profile = SelectedProfile;
        if (!await interaction.ConfirmDeleteAsync(profile.Name)) return;
        IsBusy = true;
        try
        {
            await service.DeleteAsync(profile.Id);
            Refresh();
            Feedback = "Perfil eliminado.";
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Feedback = $"No se pudo eliminar el perfil: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task RenameAsync() => RenameSelectedAsync();

    private Task DeleteAsync() => DeleteSelectedAsync();
}

public sealed record ProfilePresentationItem(StreamDeckProfile Profile, bool IsActive)
{
    public string Name => Profile.Name;
    public string ActiveText => IsActive ? "ACTIVO" : string.Empty;
    public string Summary => $"{Profile.Pages.Length} página(s) · 9 acciones por página";
}
