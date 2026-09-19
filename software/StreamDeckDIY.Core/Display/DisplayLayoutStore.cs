using System.Text.Json;
using StreamDeckDIY.Core.Persistence;

namespace StreamDeckDIY.Core.Display;

public sealed record DisplayLayoutDocument(
    int Version,
    DisplayConfiguration Configuration,
    IReadOnlyList<DisplayProfilePages> Profiles);
public sealed record DisplayLayoutLoadResult(
    DisplayWorkspace Workspace,
    bool Migrated = false,
    string? Warning = null);

public interface IDisplayLayoutStore
{
    Task<DisplayLayoutLoadResult> LoadAsync(
        DisplayWorkspace fallback,
        uint activeProfileId,
        CancellationToken cancellationToken = default);
    Task SaveAsync(
        DisplayWorkspace workspace,
        CancellationToken cancellationToken = default);
}

public sealed class DisplayLayoutStore(string filePath) : IDisplayLayoutStore
{
    public const int CurrentVersion = 3;
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<DisplayLayoutLoadResult> LoadAsync(
        DisplayWorkspace fallback,
        uint activeProfileId,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) return new(fallback);
        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            using var parsed = JsonDocument.Parse(json);
            var version = parsed.RootElement.GetProperty("Version").GetInt32();
            if (version == 1)
            {
                var legacy = JsonSerializer.Deserialize<LegacyLayoutDocument>(json, JsonOptions)
                    ?? throw new InvalidDataException("El diseño v1.4 está vacío.");
                if (!IsValid(legacy.Configuration, legacy.Scene))
                    throw new InvalidDataException("El diseño v1.4 no es válido.");
                var profiles = fallback.Profiles.ToArray();
                var index = Array.FindIndex(profiles, p => p.ProfileId == activeProfileId);
                if (index < 0) index = 0;
                var profile = profiles[index];
                var migratedScene = DisplayLayoutMigrator.ScaleScene(
                    legacy.Scene, legacy.Configuration, fallback.Configuration);
                var page = profile.Pages.OrderBy(p => p.Order).First() with
                    { Scene = migratedScene };
                profiles[index] = profile with
                {
                    DefaultPageId = page.Id,
                    Pages = profile.Pages.Select(p => p.Id == page.Id ? page : p).ToArray(),
                };
                var migrated = new DisplayWorkspace(fallback.Configuration, profiles);
                await SaveAsync(migrated, cancellationToken);
                return new(migrated, true,
                    "El diseño v1.4 se migró a páginas y a 480 × 320.");
            }
            if (version == 2)
            {
                var legacy = JsonSerializer.Deserialize<DisplayLayoutDocument>(json, JsonOptions)
                    ?? throw new InvalidDataException("El diseño v2 está vacío.");
                if (!IsValid(legacy.Configuration, legacy.Profiles))
                    throw new InvalidDataException("El diseño v2 no es válido.");
                var migrated = DisplayLayoutMigrator.ScaleWorkspace(
                    new DisplayWorkspace(legacy.Configuration, legacy.Profiles),
                    fallback.Configuration);
                await SaveAsync(migrated, cancellationToken);
                return new(migrated, true,
                    "El diseño se migró de 320 × 240 a 480 × 320.");
            }
            if (version != CurrentVersion)
                throw new InvalidDataException($"Versión de diseño no compatible: {version}.");
            var document = JsonSerializer.Deserialize<DisplayLayoutDocument>(json, JsonOptions)
                ?? throw new InvalidDataException("El diseño guardado está vacío.");
            if (!IsValid(document.Configuration, document.Profiles))
                throw new InvalidDataException("El diseño guardado no es válido.");
            if (document.Configuration != fallback.Configuration)
                throw new InvalidDataException("La resolución guardada no coincide con la pantalla actual.");
            return new(new(document.Configuration, document.Profiles));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            JsonException or InvalidDataException or KeyNotFoundException)
        {
            return new(fallback, Warning:
                $"No se pudo cargar display-layout.json: {exception.Message}");
        }
    }

    public async Task SaveAsync(
        DisplayWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        await saveGate.WaitAsync(cancellationToken);
        try
        {
            var document = new DisplayLayoutDocument(
                CurrentVersion, workspace.Configuration, workspace.Profiles);
            await AtomicFile.WriteAllTextAsync(
                filePath, JsonSerializer.Serialize(document, JsonOptions), cancellationToken);
        }
        finally { saveGate.Release(); }
    }

    private static bool IsValid(
        DisplayConfiguration configuration,
        IReadOnlyList<DisplayProfilePages> profiles) =>
        ValidConfiguration(configuration) && profiles.Count > 0 &&
        profiles.Select(p => p.ProfileId).Distinct().Count() == profiles.Count &&
        profiles.All(profile =>
            !string.IsNullOrWhiteSpace(profile.ProfileName) &&
            profile.Pages.Count > 0 &&
            profile.Pages.Any(page => page.Id == profile.DefaultPageId) &&
            profile.Pages.Select(page => page.Id).Distinct().Count() == profile.Pages.Count &&
            profile.Pages.All(page =>
                !string.IsNullOrWhiteSpace(page.Id) &&
                !string.IsNullOrWhiteSpace(page.Name) &&
                IsValid(configuration, page.Scene)));

    private static bool IsValid(
        DisplayConfiguration configuration,
        DisplayScene scene)
    {
        if (!ValidConfiguration(configuration) || string.IsNullOrWhiteSpace(scene.Id) ||
            !DisplayColor.TryNormalize(scene.Background, out _)) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in scene.Elements)
        {
            if (string.IsNullOrWhiteSpace(element.Id) || !ids.Add(element.Id) ||
                !double.IsFinite(element.X) || !double.IsFinite(element.Y) ||
                !double.IsFinite(element.Width) || element.Width <= 0 ||
                !double.IsFinite(element.Height) || element.Height <= 0 ||
                !double.IsFinite(element.Opacity) || element.Opacity is < 0 or > 1)
                return false;
            if (element is DisplayTextElement text &&
                (!double.IsFinite(text.FontSize) || text.FontSize <= 0 ||
                 !DisplayColor.TryNormalize(text.Color, out _))) return false;
            if (element is DisplayRectangleElement rectangle &&
                (!double.IsFinite(rectangle.CornerRadius) || rectangle.CornerRadius < 0 ||
                 !DisplayColor.TryNormalize(rectangle.Fill, out _))) return false;
        }
        return true;
    }

    private static bool ValidConfiguration(DisplayConfiguration configuration) =>
        double.IsFinite(configuration.Width) && configuration.Width > 0 &&
        double.IsFinite(configuration.Height) && configuration.Height > 0;

    private sealed record LegacyLayoutDocument(
        int Version,
        DisplayConfiguration Configuration,
        DisplayScene Scene);
}

public static class DisplayLayoutMigrator
{
    public static DisplayWorkspace ScaleWorkspace(
        DisplayWorkspace source, DisplayConfiguration target) => new(
        target,
        source.Profiles.Select(profile => profile with
        {
            Pages = profile.Pages.Select(page => page with
            {
                Scene = ScaleScene(page.Scene, source.Configuration, target),
            }).ToArray(),
        }).ToArray());

    public static DisplayScene ScaleScene(
        DisplayScene scene,
        DisplayConfiguration source,
        DisplayConfiguration target)
    {
        var scaleX = target.Width / source.Width;
        var scaleY = target.Height / source.Height;
        return scene with
        {
            Elements = scene.Elements.Select(element => Scale(element, scaleX, scaleY)).ToArray(),
        };
    }

    private static DisplayElement Scale(DisplayElement element, double x, double y) =>
        element switch
        {
            DisplayTextElement text => text with
            {
                X = text.X * x, Y = text.Y * y,
                Width = text.Width * x, Height = text.Height * y,
            },
            DisplayRectangleElement rectangle => rectangle with
            {
                X = rectangle.X * x, Y = rectangle.Y * y,
                Width = rectangle.Width * x, Height = rectangle.Height * y,
            },
            DisplayImageElement image => image with
            {
                X = image.X * x, Y = image.Y * y,
                Width = image.Width * x, Height = image.Height * y,
            },
            _ => element,
        };
}

public interface IDisplayAutosaveDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
public sealed class DisplayAutosaveDelay : IDisplayAutosaveDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

public sealed class DisplayLayoutAutosave : IAsyncDisposable
{
    private readonly IDisplayLayoutStore store;
    private readonly IDisplayAutosaveDelay delay;
    private readonly TimeSpan debounce;
    private readonly object gate = new();
    private CancellationTokenSource? pending;
    private Task pendingTask = Task.CompletedTask;

    public DisplayLayoutAutosave(
        IDisplayLayoutStore store,
        TimeSpan? debounce = null,
        IDisplayAutosaveDelay? delay = null)
    {
        this.store = store;
        this.debounce = debounce ?? TimeSpan.FromMilliseconds(500);
        this.delay = delay ?? new DisplayAutosaveDelay();
    }

    public event EventHandler<Exception>? SaveFailed;

    public void Schedule(DisplayWorkspace workspace)
    {
        lock (gate)
        {
            pending?.Cancel();
            pending?.Dispose();
            pending = new();
            pendingTask = SaveAfterDelayAsync(workspace, pending.Token);
        }
    }

    public async Task FlushAsync(
        DisplayWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        var previous = CancelPending();
        try { await previous.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        await store.SaveAsync(workspace, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Task task;
        lock (gate)
        {
            pending?.Cancel();
            task = pendingTask;
        }
        try { await task; } catch (OperationCanceledException) { }
        _ = CancelPending();
    }

    private async Task SaveAfterDelayAsync(
        DisplayWorkspace workspace,
        CancellationToken cancellationToken)
    {
        try
        {
            await delay.DelayAsync(debounce, cancellationToken);
            await store.SaveAsync(workspace, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { SaveFailed?.Invoke(this, exception); }
    }

    private Task CancelPending()
    {
        lock (gate)
        {
            pending?.Cancel();
            pending?.Dispose();
            pending = null;
            var previous = pendingTask;
            pendingTask = Task.CompletedTask;
            return previous;
        }
    }
}
