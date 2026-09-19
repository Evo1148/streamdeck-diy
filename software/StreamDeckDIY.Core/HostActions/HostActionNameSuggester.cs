namespace StreamDeckDIY.Core.HostActions;

public static class HostActionNameSuggester
{
    public static string FromExecutable(string executablePath) =>
        Path.GetFileNameWithoutExtension(executablePath).Trim();

    public static string ApplyWhenEmpty(string? currentName, string executablePath) =>
        string.IsNullOrWhiteSpace(currentName)
            ? FromExecutable(executablePath)
            : currentName;
}
