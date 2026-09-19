using System.Text.Json.Serialization;
using StreamDeckDIY.Core.Audio;

namespace StreamDeckDIY.Core.HostActions;

public enum HostActionKind
{
    LaunchApplication = 0,
    OpenUrl = 1,
    Macro = 2,
    SetAudioOutput = 3,
    ToggleAudioOutput = 4,
}

public sealed record HostActionDefinition(
    uint Id,
    string Name,
    HostActionKind Kind,
    string? Target = null,
    IReadOnlyList<MacroStep>? Steps = null,
    AudioOutputConfiguration? AudioOutput = null)
{
    [JsonIgnore]
    public string KindDisplayName => Kind switch
    {
        HostActionKind.LaunchApplication => "Aplicación",
        HostActionKind.OpenUrl => "URL",
        HostActionKind.Macro => "Macro",
        HostActionKind.SetAudioOutput => "Salida de audio",
        HostActionKind.ToggleAudioOutput => "Alternar salida de audio",
        _ => "Desconocido",
    };

    [JsonIgnore]
    public string Summary => Kind switch
    {
        HostActionKind.Macro => $"{Steps?.Count ?? 0} pasos",
        HostActionKind.SetAudioOutput =>
            AudioOutput?.PrimaryDeviceName ?? "Dispositivo de audio",
        HostActionKind.ToggleAudioOutput =>
            $"{AudioOutput?.PrimaryDeviceName ?? "A"} ↔ " +
            $"{AudioOutput?.SecondaryDeviceName ?? "B"}",
        _ => Target ?? string.Empty,
    };

    public override string ToString() => Name;
}
