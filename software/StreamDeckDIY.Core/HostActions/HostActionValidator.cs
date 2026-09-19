namespace StreamDeckDIY.Core.HostActions;

using StreamDeckDIY.Core.Audio;

public readonly record struct HostActionValidationResult(
    bool IsValid,
    string Message)
{
    public static HostActionValidationResult Valid() => new(true, string.Empty);
    public static HostActionValidationResult Invalid(string message) => new(false, message);
}

public static class HostActionValidator
{
    public static HostActionValidationResult Validate(
        string? name,
        HostActionKind kind,
        string? target,
        IReadOnlyList<MacroStep>? steps = null,
        Func<uint, HostActionDefinition?>? resolveAction = null,
        AudioOutputConfiguration? audioOutput = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return HostActionValidationResult.Invalid("Introduce un nombre.");
        if (!Enum.IsDefined(kind))
            return HostActionValidationResult.Invalid("El tipo de acción no es válido.");

        var trimmedTarget = target?.Trim() ?? string.Empty;
        return kind switch
        {
            HostActionKind.LaunchApplication => ValidateApplication(trimmedTarget),
            HostActionKind.OpenUrl => ValidateUrl(trimmedTarget),
            HostActionKind.Macro => ValidateMacro(steps, resolveAction),
            HostActionKind.SetAudioOutput => ValidateAudio(audioOutput, false),
            HostActionKind.ToggleAudioOutput => ValidateAudio(audioOutput, true),
            _ => HostActionValidationResult.Invalid("El tipo de acción no es válido."),
        };
    }

    private static HostActionValidationResult ValidateAudio(
        AudioOutputConfiguration? audio, bool requireSecondary)
    {
        if (string.IsNullOrWhiteSpace(audio?.PrimaryDeviceId))
            return HostActionValidationResult.Invalid(
                "Selecciona el dispositivo de audio principal.");
        if (!requireSecondary) return HostActionValidationResult.Valid();
        if (string.IsNullOrWhiteSpace(audio.SecondaryDeviceId))
            return HostActionValidationResult.Invalid(
                "Selecciona el segundo dispositivo de audio.");
        if (string.Equals(audio.PrimaryDeviceId, audio.SecondaryDeviceId,
                          StringComparison.OrdinalIgnoreCase))
            return HostActionValidationResult.Invalid(
                "Los dispositivos A y B deben ser distintos.");
        return HostActionValidationResult.Valid();
    }

    private static HostActionValidationResult ValidateMacro(
        IReadOnlyList<MacroStep>? steps,
        Func<uint, HostActionDefinition?>? resolveAction)
    {
        if (steps is null || steps.Count == 0)
            return HostActionValidationResult.Invalid(
                "La macro debe contener al menos un paso.");

        foreach (var step in steps)
        {
            switch (step.Kind)
            {
            case MacroStepKind.ExecuteHostAction:
                var action = resolveAction?.Invoke(step.ActionId);
                if (action is null)
                    return HostActionValidationResult.Invalid(
                        "La acción seleccionada ya no existe.");
                if (action.Kind is not (HostActionKind.LaunchApplication or
                    HostActionKind.OpenUrl))
                    return HostActionValidationResult.Invalid(
                        "Este tipo de acción no está disponible en macros.");
                break;

            case MacroStepKind.Delay:
                if (step.DelayMilliseconds is < 1 or > 60000)
                    return HostActionValidationResult.Invalid(
                        "El tiempo de espera debe estar entre 1 y 60000 ms.");
                break;

            default:
                return HostActionValidationResult.Invalid(
                    "La macro contiene un tipo de paso no compatible.");
            }
        }
        return HostActionValidationResult.Valid();
    }

    private static HostActionValidationResult ValidateApplication(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return HostActionValidationResult.Invalid("Introduce una aplicación.");
        if (!string.Equals(Path.GetExtension(target), ".exe",
                           StringComparison.OrdinalIgnoreCase))
            return HostActionValidationResult.Invalid(
                "Selecciona o introduce un archivo .exe.");
        if (Path.IsPathFullyQualified(target) && !File.Exists(target))
            return HostActionValidationResult.Invalid(
                "El ejecutable seleccionado no existe.");
        return HostActionValidationResult.Valid();
    }

    private static HostActionValidationResult ValidateUrl(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return HostActionValidationResult.Invalid(
                "Introduce una URL absoluta que comience por http:// o https://.");
        }
        return HostActionValidationResult.Valid();
    }
}
