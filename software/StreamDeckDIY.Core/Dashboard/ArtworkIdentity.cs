namespace StreamDeckDIY.Core.Dashboard;

public sealed record ArtworkSnapshot(string? Key, byte[]? Bytes)
{
    public static ArtworkSnapshot Empty { get; } = new(null, null);
}

public sealed class ArtworkRefreshState
{
    private long generation;
    private string mediaIdentity = string.Empty;

    public ArtworkSnapshot Current { get; private set; } = ArtworkSnapshot.Empty;

    public long Begin(string identity)
    {
        mediaIdentity = identity;
        return ++generation;
    }

    public bool IsCurrent(long requestGeneration, string identity) =>
        requestGeneration == generation &&
        string.Equals(identity, mediaIdentity, StringComparison.Ordinal);

    public bool TryApply(long requestGeneration, string identity, byte[]? bytes, bool succeeded)
    {
        if (requestGeneration != generation ||
            !string.Equals(identity, mediaIdentity, StringComparison.Ordinal))
            return false;
        if (!succeeded) return true;
        if (bytes is not { Length: > 0 })
        {
            Current = ArtworkSnapshot.Empty;
            return true;
        }
        var key = ArtworkContentKey.Create(bytes);
        if (!string.Equals(Current.Key, key, StringComparison.Ordinal))
            Current = new(key, bytes);
        return true;
    }
}

public static class DisplayAssetIds
{
    // IDs 1..0x7FFF are content-addressed artwork. Companion uses one stable
    // replacement slot in the separate 0x8000..0xFFFF namespace.
    public const ushort ArtworkMin = 0x0001;
    public const ushort ArtworkMax = 0x7FFF;
    public const ushort ActiveCompanion = 0x8001;

    public static bool IsArtwork(ushort id) => id is >= ArtworkMin and <= ArtworkMax;
    public static bool IsCompanion(ushort id) => id == ActiveCompanion;
}

public static class ArtworkContentKey
{
    public static string Create(ReadOnlySpan<byte> bytes) => $"{bytes.Length:X8}-{Crc32(bytes):X8}";

    public static ushort AssetId(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;
        uint hash = 2166136261;
        foreach (var character in key) { hash ^= character; hash *= 16777619; }
        return (ushort)(DisplayAssetIds.ArtworkMin + hash % DisplayAssetIds.ArtworkMax);
    }

    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u);
        }
        return ~crc;
    }
}
