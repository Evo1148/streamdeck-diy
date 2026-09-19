using System.Text.Json;
using StreamDeckDIY.Core.Persistence;

namespace StreamDeckDIY.Core.Dashboard;

public sealed class DashboardConfigurationStore
{
    private const int CurrentVersion = 5;
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private Dictionary<uint, DashboardConfiguration> configurations = [];

    public DashboardConfigurationStore(string path) => this.path = path;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var migrate = false;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path)) return;
            var document = JsonSerializer.Deserialize<Document>(
                await File.ReadAllTextAsync(path, cancellationToken));
            migrate = document?.Version is 1 or 2 or 3 or 4;
            configurations = document?.Version is 1 or 2 or 3 or 4 or CurrentVersion &&
                document.Configurations is not null
                ? document.Configurations
                    .Where(item => item.ProfileId != 0)
                    .GroupBy(item => item.ProfileId)
                    .ToDictionary(group => group.Key,
                        group => Normalize(group.Last(), group.Key, document.Version))
                : [];
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            configurations.Clear();
        }
        finally { gate.Release(); }
        if (migrate && configurations.Count > 0)
            await SaveAsync(configurations.Values.First(), cancellationToken);
    }

    public DashboardConfiguration Get(uint profileId) =>
        configurations.TryGetValue(profileId, out var configuration)
            ? Normalize(configuration, profileId)
            : DashboardConfiguration.CreateDefault(profileId);

    public async Task SaveAsync(DashboardConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (configuration.ProfileId == 0)
                throw new InvalidDataException("El Dashboard requiere un ProfileId válido.");
            var next = new Dictionary<uint, DashboardConfiguration>(configurations)
            {
                [configuration.ProfileId] = Normalize(configuration, configuration.ProfileId),
            };
            var json = JsonSerializer.Serialize(
                new Document(CurrentVersion, next.Values.ToArray()),
                new JsonSerializerOptions { WriteIndented = true });
            await AtomicFile.WriteAllTextAsync(path, json, cancellationToken);
            configurations = next;
        }
        finally { gate.Release(); }
    }

    private static DashboardConfiguration Normalize(DashboardConfiguration value, uint profileId,
        int sourceVersion = CurrentVersion)
    {
        var functionalPreset = sourceVersion < 4
            ? DashboardPresets.FunctionalFrom(value.PresetId)
            : VisualSystemCatalog.Normalize(value.FunctionalPreset);
        var preset = DashboardPresets.For(functionalPreset);
        var sourceWidgets = value.Widgets ?? [];
        var widgets = Enum.GetValues<DashboardWidgetKind>().Select(kind =>
        {
            var existing = sourceWidgets.FirstOrDefault(widget => widget.Kind == kind);
            var options = existing?.Options ?? new();
            if (sourceVersion < 3)
                options = options with { ShowTemperatures = true, ShowNetwork = false, ShowDisk = false };
            if (kind == DashboardWidgetKind.SystemStats &&
                !options.ShowCpu && !options.ShowGpu && !options.ShowRam)
                options = options with { ShowCpu = true };
            return existing is null
                ? new DashboardWidgetInstance(kind, preset.PrimaryWidgets.Contains(kind), new())
                : existing with { Options = options };
        }).ToArray();
        return value with
        {
            ProfileId = profileId,
            PresetId = preset.Id,
            VisualIdentity = VisualSystemCatalog.Normalize(value.VisualIdentity),
            FunctionalPreset = functionalPreset,
            VisualOptions = value.VisualOptions ?? DashboardVisualOptions.Default,
            CompanionPackId = sourceVersion < 5 || string.IsNullOrWhiteSpace(value.CompanionPackId)
                ? null : value.CompanionPackId.Trim(),
            Widgets = widgets,
        };
    }

    private sealed record Document(int Version, DashboardConfiguration[]? Configurations);
}
