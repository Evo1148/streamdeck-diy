using System.Text.Json;
using StreamDeckDIY.Core.Persistence;
using StreamDeckDIY.Protocol.Models;

namespace StreamDeckDIY.App.Services;

public sealed class BindingLabelStore
{
    private const int CurrentVersion = 1;
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private Dictionary<string, string> labels = new(StringComparer.Ordinal);

    public BindingLabelStore(string path) => this.path = path;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path)) return;
            var document = JsonSerializer.Deserialize<Document>(
                await File.ReadAllTextAsync(path, cancellationToken));
            if (document?.Version == CurrentVersion && document.Labels is not null)
                labels = new(document.Labels, StringComparer.Ordinal);
        }
        catch (JsonException) { labels.Clear(); }
        finally { gate.Release(); }
    }

    public string? Get(uint profileId, ControlId control) =>
        labels.TryGetValue(Key(profileId, control), out var label) ? label : null;

    public async Task SetAsync(uint profileId, ControlId control, string? label,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var key = Key(profileId, control);
            var normalized = label?.Trim();
            var next = new Dictionary<string, string>(labels, StringComparer.Ordinal);
            if (string.IsNullOrEmpty(normalized)) next.Remove(key); else next[key] = normalized;
            await AtomicFile.WriteAllTextAsync(path,
                JsonSerializer.Serialize(new Document(CurrentVersion, next),
                    new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            labels = next;
        }
        finally { gate.Release(); }
    }

    private static string Key(uint profileId, ControlId control) =>
        $"{profileId}:{(byte)control.Type}:{control.Index}";
    private sealed record Document(int Version, Dictionary<string, string> Labels);
}
