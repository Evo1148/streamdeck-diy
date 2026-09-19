using System.Text.Json;
using StreamDeckDIY.Core.Persistence;
using StreamDeckDIY.Protocol.Models;

namespace StreamDeckDIY.App.Services;

public sealed class BindingIconStore
{
    private const int CurrentVersion = 1;
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private Dictionary<string, string> icons = new(StringComparer.Ordinal);

    public BindingIconStore(string path) => this.path = path;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path)) return;
            var document = JsonSerializer.Deserialize<BindingIconDocument>(
                await File.ReadAllTextAsync(path, cancellationToken));
            if (document?.Version == CurrentVersion && document.Icons is not null)
                icons = new Dictionary<string, string>(document.Icons, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            icons.Clear();
        }
        finally
        {
            gate.Release();
        }
    }

    public string? Get(uint profileId, ControlId control) =>
        icons.TryGetValue(Key(profileId, control), out var iconKey) ? iconKey : null;

    public async Task SetAsync(
        uint profileId,
        ControlId control,
        string? iconKey,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var key = Key(profileId, control);
            var next = new Dictionary<string, string>(icons, StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(iconKey)) next.Remove(key);
            else next[key] = iconKey;
            await AtomicFile.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(new BindingIconDocument(CurrentVersion, next),
                    new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            icons = next;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string Key(uint profileId, ControlId control) =>
        $"{profileId}:{(byte)control.Type}:{control.Index}";

    private sealed record BindingIconDocument(
        int Version,
        Dictionary<string, string> Icons);
}
