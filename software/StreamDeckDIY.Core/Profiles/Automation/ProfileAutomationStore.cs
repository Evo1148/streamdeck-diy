using System.Text.Json;
using StreamDeckDIY.Core.Persistence;

namespace StreamDeckDIY.Core.Profiles.Automation;

public sealed class ProfileAutomationStore : IProfileAutomationStore
{
    private const int CurrentVersion = 1;
    private readonly string filePath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private ProfileAutomationSettings settings = EmptySettings();

    public ProfileAutomationStore(string filePath) => this.filePath = filePath;

    public ProfileAutomationSettings Settings => Clone(settings);
    public string? LoadWarning { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(filePath)) return;
            try
            {
                await using var stream = File.OpenRead(filePath);
                var document = await JsonSerializer.DeserializeAsync<Document>(
                    stream, options, cancellationToken);
                if (document is null || document.Version != CurrentVersion)
                    throw new InvalidDataException("Versión no compatible.");
                var loaded = new ProfileAutomationSettings(
                    document.AutoSwitchEnabled, document.NextId, document.Rules);
                Validate(loaded);
                settings = Clone(loaded);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                settings = EmptySettings();
                LoadWarning = $"No se pudo cargar profile-automation.json: {exception.Message}";
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(
        ProfileAutomationSettings value,
        CancellationToken cancellationToken = default)
    {
        Validate(value);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var document = new Document
            {
                AutoSwitchEnabled = value.AutoSwitchEnabled,
                NextId = value.NextId,
                Rules = value.Rules,
            };
            await AtomicFile.WriteAllTextAsync(
                filePath, JsonSerializer.Serialize(document, options), cancellationToken);
            settings = Clone(value);
            LoadWarning = null;
        }
        finally
        {
            gate.Release();
        }
    }

    private static void Validate(ProfileAutomationSettings value)
    {
        if (value.NextId == 0 || value.Rules.Any(rule => rule.Id == 0) ||
            value.Rules.Select(rule => rule.Id).Distinct().Count() != value.Rules.Length ||
            value.Rules.Any(rule => rule.Id >= value.NextId))
            throw new InvalidDataException("La colección de reglas contiene IDs inválidos.");
        foreach (var rule in value.Rules)
            _ = ProcessNameNormalizer.Canonicalize(rule.ProcessName);
    }

    private static ProfileAutomationSettings Clone(ProfileAutomationSettings value) =>
        value with { Rules = value.Rules.ToArray() };

    private static ProfileAutomationSettings EmptySettings() => new(false, 1, []);

    public sealed class Document
    {
        public int Version { get; set; } = CurrentVersion;
        public bool AutoSwitchEnabled { get; set; }
        public uint NextId { get; set; } = 1;
        public ProfileActivationRule[] Rules { get; set; } = [];
    }
}
