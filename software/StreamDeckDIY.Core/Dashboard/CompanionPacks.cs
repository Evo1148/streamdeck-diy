using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StreamDeckDIY.Core.Dashboard;

public static class CompanionPackFormat
{
    public const int SchemaVersion = 1;
    public const ushort SpriteWidth = 64;
    public const ushort SpriteHeight = 64;
    public const ushort RenderWidth = CompanionRasterPolicy.RenderSize;
    public const ushort RenderHeight = CompanionRasterPolicy.RenderSize;
    public const ushort ActiveAssetId = DisplayAssetIds.ActiveCompanion;
    public const long MaxArchiveBytes = 16 * 1024 * 1024;
    public const long MaxAssetBytes = 3 * 1024 * 1024;
    public const long MaxExpandedBytes = 16 * 1024 * 1024;
    public const int MaxSourceDimension = 2048;
    public const int MaxEntries = 16;
}

public sealed record CompanionPackManifest(
    int SchemaVersion,
    string Id,
    string Name,
    string? Author,
    string Version,
    int SpriteWidth,
    int SpriteHeight,
    Dictionary<string, string> States,
    Dictionary<string, string>? Metadata = null,
    int? Size = null,
    string? Origin = null);

public sealed record CompanionPack(
    string Id,
    string Name,
    string? Author,
    string Version,
    ushort SpriteWidth,
    ushort SpriteHeight,
    IReadOnlyDictionary<CompanionMood, string> AssetPaths,
    IReadOnlyDictionary<string, string> Metadata,
    bool IsOfficial,
    string RootPath)
{
    public string DisplayName => IsOfficial ? Name : $"{Name} · {Author ?? "Personalizado"}";
    public override string ToString() => DisplayName;
}

public sealed record CompanionResolvedAsset(
    CompanionPack Pack,
    CompanionMood RequestedMood,
    CompanionMood ResolvedMood,
    ushort AssetId,
    string ContentKey,
    byte[] PngBytes,
    ushort Width,
    ushort Height,
    uint SourceCrc32);

public sealed class CompanionPackCatalog
{
    public const string OfficialHikariId = "official-hikari";
    public const string OfficialNeonId = "official-neon";
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
    private readonly string customRoot;
    private readonly string officialRoot;
    private readonly List<CompanionPack> packs = [];

    public CompanionPackCatalog(string customRoot, string officialRoot)
    {
        this.customRoot = Path.GetFullPath(customRoot);
        this.officialRoot = Path.GetFullPath(officialRoot);
    }

    public IReadOnlyList<CompanionPack> Packs => packs;
    public string StoragePath => customRoot;

    public async Task InitializeAsync(CancellationToken token = default)
    {
        Directory.CreateDirectory(customRoot);
        packs.Clear();
        await LoadRootAsync(officialRoot, true, token);
        await LoadRootAsync(customRoot, false, token);
    }

    public CompanionPack ResolvePack(string? id, VisualIdentity identity)
    {
        var requested = !string.IsNullOrWhiteSpace(id)
            ? packs.FirstOrDefault(pack => string.Equals(pack.Id, id, StringComparison.OrdinalIgnoreCase))
            : null;
        return requested ?? DefaultPack(identity) ??
            packs.FirstOrDefault(pack => pack.IsOfficial) ??
            throw new InvalidOperationException("No hay ningún Companion Pack válido instalado.");
    }

    public async Task<CompanionResolvedAsset?> ResolveAssetAsync(
        string? packId, VisualIdentity identity, CompanionMood mood,
        CancellationToken token = default)
    {
        var selected = TryResolvePack(packId, identity);
        var fallback = DefaultPack(identity);
        foreach (var candidate in Candidates(selected, fallback, mood))
        {
            token.ThrowIfCancellationRequested();
            if (!candidate.Pack.AssetPaths.TryGetValue(candidate.Mood, out var path)) continue;
            try
            {
                var bytes = await File.ReadAllBytesAsync(path, token);
                ValidatePng(bytes);
                var crc = Crc32(bytes);
                return new(candidate.Pack, mood, candidate.Mood,
                    CompanionPackFormat.ActiveAssetId,
                    $"{candidate.Pack.Id}:{candidate.Mood}:{crc:X8}:{bytes.Length}",
                    bytes, CompanionPackFormat.SpriteWidth, CompanionPackFormat.SpriteHeight, crc);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // Continue through the deterministic fallback chain.
            }
        }
        return null;
    }

    public async Task<CompanionPack> ImportAsync(string archivePath, CancellationToken token = default)
    {
        var archiveInfo = new FileInfo(archivePath);
        if (!archiveInfo.Exists) throw new FileNotFoundException("No se encontró el Companion Pack.", archivePath);
        if (archiveInfo.Length > CompanionPackFormat.MaxArchiveBytes)
            throw new InvalidDataException("El Companion Pack supera el tamaño máximo permitido.");

        var staging = Path.Combine(customRoot, $".import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count is 0 or > CompanionPackFormat.MaxEntries)
                throw new InvalidDataException("El ZIP contiene un número de entradas no permitido.");
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(entry.Name)) continue;
                if (entry.Length > CompanionPackFormat.MaxAssetBytes)
                    throw new InvalidDataException("Un archivo del pack supera el límite permitido.");
                expanded = checked(expanded + entry.Length);
                if (expanded > CompanionPackFormat.MaxExpandedBytes)
                    throw new InvalidDataException("El contenido expandido del pack supera el límite permitido.");
                var destination = SafeDestination(staging, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var source = entry.Open();
                await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(target, token);
            }

            var manifestPaths = Directory.GetFiles(staging, "companion.json", SearchOption.AllDirectories);
            if (manifestPaths.Length != 1)
                throw new InvalidDataException("El ZIP debe contener exactamente un companion.json.");
            var root = Path.GetDirectoryName(manifestPaths[0])!;
            var pack = await LoadPackAsync(root, false, token);
            var unexpected = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => !IsAllowedPackFile(path, root)).ToArray();
            if (unexpected.Length > 0)
                throw new InvalidDataException("El pack contiene archivos no permitidos.");
            var destinationRoot = Path.Combine(customRoot, pack.Id);
            if (Directory.Exists(destinationRoot) || packs.Any(item =>
                    string.Equals(item.Id, pack.Id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Ya existe un Companion Pack con id '{pack.Id}'.");
            Directory.Move(root, destinationRoot);
            var installed = await LoadPackAsync(destinationRoot, false, token);
            packs.Add(installed);
            SortPacks();
            return installed;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    public Task DeleteAsync(string id, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var pack = packs.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (pack is null) return Task.CompletedTask;
        if (pack.IsOfficial) throw new InvalidOperationException("Las mascotas oficiales no se pueden eliminar.");
        var root = Path.GetFullPath(pack.RootPath);
        if (!IsInside(root, customRoot)) throw new InvalidOperationException("Ruta de pack no válida.");
        Directory.Delete(root, true);
        packs.Remove(pack);
        return Task.CompletedTask;
    }

    private async Task LoadRootAsync(string root, bool official, CancellationToken token)
    {
        if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.GetDirectories(root).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var pack = await LoadPackAsync(directory, official, token);
                if (packs.All(item => !string.Equals(item.Id, pack.Id, StringComparison.OrdinalIgnoreCase)))
                    packs.Add(pack);
            }
            catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // An invalid installed pack is ignored; official/default fallback remains available.
            }
        }
        SortPacks();
    }

    private static async Task<CompanionPack> LoadPackAsync(string root, bool official, CancellationToken token)
    {
        var manifestPath = Path.Combine(root, "companion.json");
        if (!File.Exists(manifestPath)) throw new InvalidDataException("Falta companion.json.");
        var manifest = JsonSerializer.Deserialize<CompanionPackManifest>(
            await File.ReadAllTextAsync(manifestPath, token), JsonOptions)
            ?? throw new InvalidDataException("Manifest vacío.");
        ValidateManifest(manifest);
        var states = new Dictionary<CompanionMood, string>();
        foreach (var mapping in manifest.States)
        {
            if (!Enum.TryParse<CompanionMood>(mapping.Key, true, out var mood) || !Enum.IsDefined(mood))
                throw new InvalidDataException($"Estado Companion desconocido: {mapping.Key}.");
            if (Path.IsPathRooted(mapping.Value) || mapping.Value.Contains('/') || mapping.Value.Contains('\\') ||
                !string.Equals(Path.GetExtension(mapping.Value), ".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Las rutas de sprites deben ser nombres PNG locales.");
            var path = SafeDestination(root, mapping.Value);
            if (!File.Exists(path)) throw new InvalidDataException($"Falta el sprite '{mapping.Value}'.");
            var bytes = await File.ReadAllBytesAsync(path, token);
            if (bytes.LongLength > CompanionPackFormat.MaxAssetBytes)
                throw new InvalidDataException("Un sprite supera el límite permitido.");
            ValidatePng(bytes);
            states[mood] = path;
        }
        if (!states.ContainsKey(CompanionMood.Neutral))
            throw new InvalidDataException("El estado Neutral es obligatorio.");
        var targetWidth = manifest.Size ?? manifest.SpriteWidth;
        var targetHeight = manifest.Size ?? manifest.SpriteHeight;
        return new(manifest.Id, manifest.Name.Trim(), string.IsNullOrWhiteSpace(manifest.Author) ? null : manifest.Author.Trim(),
            manifest.Version.Trim(), (ushort)targetWidth, (ushort)targetHeight,
            states, manifest.Metadata ?? new Dictionary<string, string>(), official, Path.GetFullPath(root));
    }

    private static void ValidateManifest(CompanionPackManifest manifest)
    {
        if (manifest.SchemaVersion != CompanionPackFormat.SchemaVersion)
            throw new InvalidDataException($"schemaVersion {manifest.SchemaVersion} no está soportado.");
        if (!IdPattern.IsMatch(manifest.Id ?? string.Empty))
            throw new InvalidDataException("El id del pack no es válido.");
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 80 ||
            string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Length > 32)
            throw new InvalidDataException("Nombre o versión no válidos.");
        var targetWidth = manifest.Size ?? manifest.SpriteWidth;
        var targetHeight = manifest.Size ?? manifest.SpriteHeight;
        if (targetWidth != CompanionPackFormat.SpriteWidth ||
            targetHeight != CompanionPackFormat.SpriteHeight)
            throw new InvalidDataException("Companion V1 requiere un tamaño de destino de 64×64 (size o spriteWidth/spriteHeight).");
        if (manifest.States is null || manifest.States.Count is 0 or > 5)
            throw new InvalidDataException("El mapa de estados no es válido.");
        if (manifest.Metadata is { Count: > 16 } || manifest.Metadata?.Any(pair => pair.Key.Length > 64 || pair.Value.Length > 256) == true)
            throw new InvalidDataException("La metadata supera los límites permitidos.");
    }

    internal static void ValidatePng(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 45 || !bytes[..8].SequenceEqual(signature))
            throw new InvalidDataException("El sprite no es un PNG válido.");
        var offset = 8;
        var sawHeader = false; var sawData = false; var sawEnd = false;
        while (offset + 12 <= bytes.Length)
        {
            var length = ReadBigEndian(bytes, offset); offset += 4;
            if (length > int.MaxValue || offset + 8L + length > bytes.Length)
                throw new InvalidDataException("PNG truncado.");
            var type = System.Text.Encoding.ASCII.GetString(bytes.Slice(offset, 4)); offset += 4;
            var data = bytes.Slice(offset, (int)length); offset += (int)length + 4;
            if (type == "IHDR")
            {
                if (sawHeader || data.Length != 13) throw new InvalidDataException("IHDR no válido.");
                var width = ReadBigEndian(data, 0); var height = ReadBigEndian(data, 4);
                if (width is < 16 or > CompanionPackFormat.MaxSourceDimension ||
                    height is < 16 or > CompanionPackFormat.MaxSourceDimension)
                    throw new InvalidDataException("Las dimensiones de la imagen fuente no están permitidas.");
                if (data[8] is not (8 or 16) || data[9] is not (2 or 3 or 4 or 6))
                    throw new InvalidDataException("Formato de píxel PNG no soportado.");
                sawHeader = true;
            }
            else if (type == "IDAT") sawData = true;
            else if (type == "IEND") { sawEnd = true; break; }
        }
        if (!sawHeader || !sawData || !sawEnd) throw new InvalidDataException("PNG incompleto.");
    }

    private CompanionPack? TryResolvePack(string? id, VisualIdentity identity) =>
        !string.IsNullOrWhiteSpace(id)
            ? packs.FirstOrDefault(pack => string.Equals(pack.Id, id, StringComparison.OrdinalIgnoreCase)) ?? DefaultPack(identity)
            : DefaultPack(identity);
    private CompanionPack? DefaultPack(VisualIdentity identity)
    {
        var id = VisualSystemCatalog.Normalize(identity) == VisualIdentity.Neon ? OfficialNeonId : OfficialHikariId;
        return packs.FirstOrDefault(pack => string.Equals(pack.Id, id, StringComparison.OrdinalIgnoreCase));
    }
    private static IEnumerable<(CompanionPack Pack, CompanionMood Mood)> Candidates(
        CompanionPack? selected, CompanionPack? fallback, CompanionMood mood)
    {
        if (selected is not null) { yield return (selected, mood); if (mood != CompanionMood.Neutral) yield return (selected, CompanionMood.Neutral); }
        if (fallback is not null && !ReferenceEquals(fallback, selected))
        { yield return (fallback, mood); if (mood != CompanionMood.Neutral) yield return (fallback, CompanionMood.Neutral); }
    }
    private static bool IsAllowedPackFile(string path, string root)
    {
        var full = Path.GetFullPath(path);
        if (!IsInside(full, root)) return false;
        if (full == Path.Combine(Path.GetFullPath(root), "companion.json")) return true;
        if (!string.Equals(Path.GetExtension(full), ".png", StringComparison.OrdinalIgnoreCase)) return false;
        var bytes = File.ReadAllBytes(full);
        if (bytes.LongLength > CompanionPackFormat.MaxAssetBytes) return false;
        try { ValidatePng(bytes); return true; }
        catch (InvalidDataException) { return false; }
    }
    private static string SafeDestination(string root, string relative)
    {
        var destination = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInside(destination, root)) throw new InvalidDataException("El ZIP contiene una ruta no segura.");
        return destination;
    }
    private static bool IsInside(string path, string root)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
    private void SortPacks() => packs.Sort((left, right) =>
        left.IsOfficial == right.IsOfficial ? string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase) : left.IsOfficial ? -1 : 1);
    private static uint ReadBigEndian(ReadOnlySpan<byte> bytes, int offset) =>
        ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1)); }
        return ~crc;
    }
}
