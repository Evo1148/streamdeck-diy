namespace StreamDeckDIY.Core.Profiles.Automation;

public static class ProcessNameNormalizer
{
    public static string Normalize(string processName)
    {
        var value = processName?.Trim() ?? string.Empty;
        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            value = value[..^4];
        return value.Trim().ToLowerInvariant();
    }

    public static string Canonicalize(string processName)
    {
        var normalized = Normalize(processName);
        if (string.IsNullOrEmpty(normalized) ||
            normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            normalized.Contains(Path.DirectorySeparatorChar) ||
            normalized.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException(
                "Introduce únicamente un nombre de proceso válido.", nameof(processName));
        }
        return normalized + ".exe";
    }
}
