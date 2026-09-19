using System.Text.Json;
using StreamDeckDIY.Core.Audio;
using StreamDeckDIY.Core.Persistence;

namespace StreamDeckDIY.Core.HostActions;

public sealed class HostActionRegistry(string filePath) : IHostActionRegistry
{
    private const int CurrentVersion = 3;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object sync = new();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly List<HostActionDefinition> actions = [];
    private uint nextId = 1;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            RegistryDocument? document = null;
            try
            {
                if (File.Exists(filePath))
                {
                    await using var stream = File.OpenRead(filePath);
                    document = await JsonSerializer.DeserializeAsync<RegistryDocument>(
                        stream, JsonOptions, cancellationToken);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                document = null;
            }

            lock (sync)
            {
                actions.Clear();
                if (document is { Version: 1 or 2 or CurrentVersion } &&
                    TryValidate(document.Actions, out var validActions))
                {
                    actions.AddRange(validActions);
                    nextId = document.NextId is 0
                        ? NextAfterHighest(validActions) : document.NextId;
                    if (actions.Any(action => action.Id == nextId))
                        nextId = NextFreeId(nextId);
                }
                else nextId = 1;
            }
        }
        finally { operationGate.Release(); }
    }

    public IReadOnlyList<HostActionDefinition> GetAll()
    {
        lock (sync) return actions.OrderBy(action => action.Id).ToArray();
    }

    public HostActionDefinition? GetById(uint id)
    {
        lock (sync) return actions.FirstOrDefault(action => action.Id == id);
    }

    public async Task<HostActionDefinition> CreateAsync(
        string name,
        HostActionKind kind,
        string? target = null,
        IReadOnlyList<MacroStep>? steps = null,
        AudioOutputConfiguration? audioOutput = null,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        List<HostActionDefinition>? previous = null;
        uint previousNextId = 0;
        try
        {
            HostActionDefinition definition;
            RegistryDocument document;
            lock (sync)
            {
                previous = actions.ToList();
                previousNextId = nextId;
                var id = NextFreeId(nextId);
                definition = Normalize(new(id, name, kind, target, steps, audioOutput));
                ValidateDefinition(definition);
                nextId = id == uint.MaxValue ? 1 : id + 1;
                actions.Add(definition);
                document = Snapshot();
            }
            await SaveAsync(document, cancellationToken);
            return definition;
        }
        catch
        {
            if (previous is not null) Restore(previous, previousNextId);
            throw;
        }
        finally { operationGate.Release(); }
    }

    public async Task<HostActionDefinition> UpdateAsync(
        HostActionDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Id == 0) throw new ArgumentOutOfRangeException(nameof(definition));
        var normalized = Normalize(definition);
        ValidateDefinition(normalized);

        await operationGate.WaitAsync(cancellationToken);
        List<HostActionDefinition>? previous = null;
        uint previousNextId = 0;
        try
        {
            RegistryDocument document;
            lock (sync)
            {
                previous = actions.ToList();
                previousNextId = nextId;
                var index = actions.FindIndex(action => action.Id == definition.Id);
                if (index < 0)
                    throw new KeyNotFoundException($"Host Action {definition.Id} no existe.");
                actions[index] = normalized;
                document = Snapshot();
            }
            await SaveAsync(document, cancellationToken);
            return normalized;
        }
        catch
        {
            if (previous is not null) Restore(previous, previousNextId);
            throw;
        }
        finally { operationGate.Release(); }
    }

    public async Task<bool> DeleteAsync(
        uint id,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        List<HostActionDefinition>? previous = null;
        uint previousNextId = 0;
        try
        {
            RegistryDocument? document = null;
            lock (sync)
            {
                var index = actions.FindIndex(action => action.Id == id);
                if (index >= 0)
                {
                    previous = actions.ToList();
                    previousNextId = nextId;
                    actions.RemoveAt(index);
                    document = Snapshot();
                }
            }
            if (document is null) return false;
            await SaveAsync(document, cancellationToken);
            return true;
        }
        catch
        {
            if (previous is not null) Restore(previous, previousNextId);
            throw;
        }
        finally { operationGate.Release(); }
    }

    private Task SaveAsync(
        RegistryDocument document,
        CancellationToken cancellationToken) =>
        AtomicFile.WriteAllTextAsync(
            filePath, JsonSerializer.Serialize(document, JsonOptions), cancellationToken);

    private void Restore(IReadOnlyCollection<HostActionDefinition> previous, uint previousNextId)
    {
        lock (sync)
        {
            actions.Clear();
            actions.AddRange(previous);
            nextId = previousNextId;
        }
    }

    private RegistryDocument Snapshot() => new()
    {
        Version = CurrentVersion,
        NextId = nextId,
        Actions = actions.OrderBy(action => action.Id).ToList(),
    };

    private uint NextFreeId(uint candidate)
    {
        if (candidate == 0) candidate = 1;
        var start = candidate;
        do
        {
            if (actions.All(action => action.Id != candidate)) return candidate;
            candidate = candidate == uint.MaxValue ? 1 : candidate + 1;
        } while (candidate != start);
        throw new InvalidOperationException("No quedan IDs disponibles para Host Actions.");
    }

    private static uint NextAfterHighest(IReadOnlyCollection<HostActionDefinition> definitions)
    {
        if (definitions.Count == 0) return 1;
        var highest = definitions.Max(action => action.Id);
        return highest == uint.MaxValue ? 1 : highest + 1;
    }

    private static bool TryValidate(
        IEnumerable<HostActionDefinition>? definitions,
        out IReadOnlyList<HostActionDefinition> valid)
    {
        var list = definitions?.ToList() ?? [];
        var ids = new HashSet<uint>();
        var isValid = list.All(action =>
            action is not null && action.Id != 0 && ids.Add(action.Id) &&
            !string.IsNullOrWhiteSpace(action.Name) &&
            Enum.IsDefined(action.Kind) &&
            (action.Kind is HostActionKind.Macro or
                HostActionKind.SetAudioOutput or HostActionKind.ToggleAudioOutput ||
             !string.IsNullOrWhiteSpace(action.Target)) &&
            (action.Steps?.All(step => Enum.IsDefined(step.Kind)) ?? true) &&
            (action.Kind is not (HostActionKind.SetAudioOutput or
                 HostActionKind.ToggleAudioOutput) ||
             HostActionValidator.Validate(
                 action.Name, action.Kind, null,
                 audioOutput: action.AudioOutput).IsValid));
        valid = isValid ? list : [];
        return isValid;
    }

    private void ValidateDefinition(HostActionDefinition definition)
    {
        var result = HostActionValidator.Validate(
            definition.Name, definition.Kind, definition.Target,
            definition.Steps, GetById, definition.AudioOutput);
        if (!result.IsValid) throw new ArgumentException(result.Message);
    }

    private static HostActionDefinition Normalize(HostActionDefinition definition) =>
        definition with
        {
            Name = definition.Name.Trim(),
            Target = definition.Kind is HostActionKind.Macro or
                HostActionKind.SetAudioOutput or HostActionKind.ToggleAudioOutput
                ? null
                : definition.Target?.Trim(),
            Steps = definition.Kind == HostActionKind.Macro
                ? definition.Steps?.ToArray()
                : null,
            AudioOutput = definition.Kind is HostActionKind.SetAudioOutput or
                HostActionKind.ToggleAudioOutput &&
                definition.AudioOutput is { } audioOutput
                ? audioOutput with
                {
                    PrimaryDeviceId = audioOutput.PrimaryDeviceId.Trim(),
                    SecondaryDeviceId = audioOutput.SecondaryDeviceId?.Trim(),
                }
                : null,
        };

    private sealed class RegistryDocument
    {
        public int Version { get; set; } = CurrentVersion;
        public uint NextId { get; set; } = 1;
        public List<HostActionDefinition> Actions { get; set; } = [];
    }
}
