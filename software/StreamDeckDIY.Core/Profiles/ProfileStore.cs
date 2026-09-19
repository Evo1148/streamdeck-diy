using System.Text.Json;
using StreamDeckDIY.Core.Persistence;
using StreamDeckDIY.Protocol.Models;

namespace StreamDeckDIY.Core.Profiles;

public sealed class ProfileStore : IProfileStore
{
    private const int CurrentVersion = 2;
    private readonly string filePath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private ProfileDocument document = new();
    private bool initialized;

    public ProfileStore(string filePath) =>
        this.filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

    public uint? ActiveProfileId => document.Profiles.Count == 0
        ? null
        : document.ActiveProfileId;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (initialized) return;
            if (File.Exists(filePath))
            {
                await using (var stream = File.OpenRead(filePath))
                {
                    document = await JsonSerializer.DeserializeAsync<ProfileDocument>(
                        stream, jsonOptions, cancellationToken) ??
                        throw new InvalidDataException("profiles.json está vacío.");
                }
                var migrated = false;
                if (document.Version == 1)
                {
                    document = MigrateV1(document);
                    migrated = true;
                }
                var repairedActiveProfile = EnsureActiveProfile(document);
                ValidateDocument(document);
                if (migrated || repairedActiveProfile)
                    await SaveAsync(document, cancellationToken);
            }
            initialized = true;
        }
        finally
        {
            gate.Release();
        }
    }

    public IReadOnlyList<StreamDeckProfile> GetAll() =>
        document.Profiles.Select(Clone).ToArray();

    public StreamDeckProfile? GetById(uint id)
    {
        var profile = document.Profiles.FirstOrDefault(value => value.Id == id);
        return profile is null ? null : Clone(profile);
    }

    public Task<StreamDeckProfile> CreateInitialAsync(
        string name, IReadOnlyCollection<BindingInfo> bindings,
        CancellationToken cancellationToken = default) =>
        MutateAsync(current =>
        {
            if (current.Profiles.Count != 0)
                throw new InvalidOperationException("La biblioteca ya contiene perfiles.");
            var profile = CreateProfile(1, name, bindings);
            current.Profiles.Add(profile);
            current.ActiveProfileId = profile.Id;
            current.NextId = 2;
            return profile;
        }, cancellationToken);

    public Task<StreamDeckProfile> CreateCopyAsync(
        string name, uint sourceId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(current =>
        {
            var source = Required(current, sourceId);
            var profile = Clone(source) with { Id = current.NextId++, Name = name };
            current.Profiles.Add(profile);
            return profile;
        }, cancellationToken);

    public Task RenameAsync(uint id, string name,
                            CancellationToken cancellationToken = default) =>
        MutateAsync<object?>(current =>
        {
            var index = IndexOf(current, id);
            current.Profiles[index] = current.Profiles[index] with { Name = name };
            return null;
        }, cancellationToken);

    public Task DeleteAsync(uint id, CancellationToken cancellationToken = default) =>
        MutateAsync<object?>(current =>
        {
            current.Profiles.RemoveAt(IndexOf(current, id));
            return null;
        }, cancellationToken);

    public Task SetActiveAsync(uint id, CancellationToken cancellationToken = default) =>
        MutateAsync<object?>(current =>
        {
            _ = Required(current, id);
            current.ActiveProfileId = id;
            return null;
        }, cancellationToken);

    public Task ReplaceBindingsAsync(
        uint id, IReadOnlyCollection<BindingInfo> bindings,
        CancellationToken cancellationToken = default) =>
        MutateAsync<object?>(current =>
        {
            var index = IndexOf(current, id);
            current.Profiles[index] = CreateProfile(
                id, current.Profiles[index].Name, bindings);
            return null;
        }, cancellationToken);

    public Task UpdateBindingAsync(
        uint id, BindingInfo binding,
        CancellationToken cancellationToken = default) =>
        MutateAsync<object?>(current =>
        {
            var index = IndexOf(current, id);
            var profile = current.Profiles[index];
            if (binding.Control.Type == ControlType.Button)
            {
                if (binding.Control.Index >= ProfileControls.UserButtonCount)
                    throw new InvalidOperationException(
                        "Los botones 10–12 son controles de navegación del sistema.");
                var pages = profile.Pages.Select(Clone).ToArray();
                var pageIndex = Array.FindIndex(
                    pages, page => page.Id == profile.ActivePageId);
                var pageBindings = CopyBindings(pages[pageIndex].Bindings);
                var bindingIndex = Array.FindIndex(
                    pageBindings, value => value.Control == binding.Control);
                pageBindings[bindingIndex] = binding;
                pages[pageIndex] = pages[pageIndex] with { Bindings = pageBindings };
                profile = profile with { Pages = pages };
            }
            else
            {
                var bindings = CopyBindings(profile.Bindings);
                var bindingIndex = Array.FindIndex(
                    bindings, value => value.Control == binding.Control);
                if (bindingIndex < 0)
                    throw new InvalidDataException("El control no pertenece al perfil.");
                bindings[bindingIndex] = binding;
                profile = profile with { Bindings = bindings };
            }
            current.Profiles[index] = RefreshRuntimeBindings(profile);
            return null;
        }, cancellationToken);

    public Task SetActivePageAsync(
        uint profileId, uint pageId,
        CancellationToken cancellationToken = default) =>
        MutateAsync<object?>(current =>
        {
            var index = IndexOf(current, profileId);
            var profile = current.Profiles[index];
            if (profile.Pages.All(page => page.Id != pageId))
                throw new KeyNotFoundException($"No existe la página {pageId}.");
            current.Profiles[index] = RefreshRuntimeBindings(
                profile with { ActivePageId = pageId });
            return null;
        }, cancellationToken);

    public Task<ProfilePage> AddPageAsync(
        uint profileId, string name,
        CancellationToken cancellationToken = default) =>
        MutateAsync(current =>
        {
            var index = IndexOf(current, profileId);
            var profile = current.Profiles[index];
            var page = new ProfilePage(
                profile.NextPageId, name,
                ProfileControls.UserButtons.Select(control =>
                    new BindingInfo(control, new DeviceAction(ActionType.None))).ToArray());
            current.Profiles[index] = profile with
            {
                Pages = [.. profile.Pages.Select(Clone), page],
                NextPageId = profile.NextPageId + 1,
            };
            return page;
        }, cancellationToken);

    private async Task<T> MutateAsync<T>(
        Func<ProfileDocument, T> mutation,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            var next = Clone(document);
            var result = mutation(next);
            ValidateDocument(next);
            await SaveAsync(next, cancellationToken);
            document = next;
            return result switch
            {
                StreamDeckProfile profile => (T)(object)Clone(profile),
                ProfilePage page => (T)(object)Clone(page),
                _ => result,
            };
        }
        finally
        {
            gate.Release();
        }
    }

    private Task SaveAsync(
        ProfileDocument value, CancellationToken cancellationToken) =>
        AtomicFile.WriteAllTextAsync(
            filePath, JsonSerializer.Serialize(value, jsonOptions), cancellationToken);

    private static StreamDeckProfile CreateProfile(
        uint id, string name, IEnumerable<BindingInfo> source)
    {
        var bindings = CopyBindings(source);
        if (!ProfileControls.IsComplete(bindings))
            throw new InvalidDataException(
                "La instantánea del dispositivo no contiene los 15 controles esperados.");
        var page = new ProfilePage(
            1, "Página 1",
            bindings.Where(binding =>
                    binding.Control.Type == ControlType.Button &&
                    binding.Control.Index < ProfileControls.UserButtonCount)
                .OrderBy(binding => binding.Control.Index).ToArray());
        var legacy = bindings.Where(binding =>
                binding.Control.Type == ControlType.Button &&
                binding.Control.Index >= ProfileControls.UserButtonCount)
            .OrderBy(binding => binding.Control.Index).ToArray();
        return RefreshRuntimeBindings(new StreamDeckProfile(id, name, bindings)
        {
            Pages = [page],
            ActivePageId = 1,
            DefaultPageId = 1,
            NextPageId = 2,
            LegacyBottomRowBindings = legacy,
        });
    }

    private static ProfileDocument MigrateV1(ProfileDocument old) => new()
    {
        Version = CurrentVersion,
        ActiveProfileId = old.ActiveProfileId,
        NextId = old.NextId,
        Profiles = old.Profiles.Select(profile =>
            CreateProfile(profile.Id, profile.Name, profile.Bindings)).ToList(),
    };

    private static StreamDeckProfile RefreshRuntimeBindings(
        StreamDeckProfile profile) =>
        profile with { Bindings = ProfileControls.RuntimeBindings(profile) };

    private static bool EnsureActiveProfile(ProfileDocument value)
    {
        if (value.Profiles.Count == 0 ||
            value.Profiles.Any(profile => profile.Id == value.ActiveProfileId))
            return false;
        value.ActiveProfileId = value.Profiles[0].Id;
        return true;
    }

    private static void ValidateDocument(ProfileDocument value)
    {
        if (value.Version != CurrentVersion)
            throw new InvalidDataException($"Versión de profiles.json no compatible: {value.Version}.");
        if (value.Profiles.Count == 0) return;
        if (value.Profiles.Select(profile => profile.Id).Distinct().Count() !=
            value.Profiles.Count)
            throw new InvalidDataException("profiles.json contiene IDs duplicados.");
        if (value.Profiles.All(profile => profile.Id != value.ActiveProfileId))
            throw new InvalidDataException("El perfil activo no existe.");
        foreach (var profile in value.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name) ||
                !ProfileControls.IsComplete(profile.Bindings) ||
                profile.Pages.Length == 0 ||
                profile.Pages.Any(page => !ProfileControls.IsPageComplete(page.Bindings)) ||
                profile.Pages.Select(page => page.Id).Distinct().Count() != profile.Pages.Length ||
                profile.Pages.All(page => page.Id != profile.ActivePageId) ||
                profile.Pages.All(page => page.Id != profile.DefaultPageId) ||
                profile.NextPageId == 0 ||
                profile.Pages.Any(page => page.Id >= profile.NextPageId))
                throw new InvalidDataException(
                    "profiles.json contiene un perfil o página inválidos.");
        }
        if (value.NextId == 0 || value.Profiles.Any(profile => profile.Id >= value.NextId))
            throw new InvalidDataException("profiles.json contiene un contador de IDs inválido.");
    }

    private static ProfileDocument Clone(ProfileDocument value) => new()
    {
        Version = value.Version,
        ActiveProfileId = value.ActiveProfileId,
        NextId = value.NextId,
        Profiles = value.Profiles.Select(Clone).ToList(),
    };

    private static StreamDeckProfile Clone(StreamDeckProfile value) =>
        value with
        {
            Bindings = CopyBindings(value.Bindings),
            Pages = value.Pages.Select(Clone).ToArray(),
            LegacyBottomRowBindings = CopyBindings(value.LegacyBottomRowBindings),
        };

    private static ProfilePage Clone(ProfilePage value) =>
        value with { Bindings = CopyBindings(value.Bindings) };

    private static BindingInfo[] CopyBindings(IEnumerable<BindingInfo> bindings) =>
        bindings.ToArray();

    private static StreamDeckProfile Required(ProfileDocument value, uint id) =>
        value.Profiles.FirstOrDefault(profile => profile.Id == id) ??
        throw new KeyNotFoundException($"No existe el perfil {id}.");

    private static int IndexOf(ProfileDocument value, uint id)
    {
        var index = value.Profiles.FindIndex(profile => profile.Id == id);
        return index >= 0 ? index : throw new KeyNotFoundException(
            $"No existe el perfil {id}.");
    }

    private void EnsureInitialized()
    {
        if (!initialized)
            throw new InvalidOperationException("ProfileStore no está inicializado.");
    }

    public sealed class ProfileDocument
    {
        public int Version { get; set; } = CurrentVersion;
        public uint ActiveProfileId { get; set; }
        public uint NextId { get; set; } = 1;
        public List<StreamDeckProfile> Profiles { get; set; } = [];
    }
}
