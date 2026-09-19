using StreamDeckDIY.Core.Audio;

namespace StreamDeckDIY.Core.HostActions;

public sealed class HostActionDefinitionExecutor(
    IHostActionLauncher launcher,
    IAudioOutputService audioOutputService)
    : IHostActionDefinitionExecutor
{
    public async Task<HostActionExecutionResult> ExecuteAsync(
        HostActionDefinition definition,
        CancellationToken cancellationToken = default)
    {
        if (definition.Kind == HostActionKind.LaunchApplication &&
            definition.Target is not null &&
            Path.IsPathFullyQualified(definition.Target) &&
            !File.Exists(definition.Target))
        {
            return new(HostActionExecutionStatus.TargetNotFound,
                $"No existe la aplicación: {definition.Target}");
        }
        var validation = HostActionValidator.Validate(
            definition.Name, definition.Kind, definition.Target,
            audioOutput: definition.AudioOutput);
        if (!validation.IsValid)
            return new(HostActionExecutionStatus.InvalidDefinition, validation.Message);

        try
        {
            switch (definition.Kind)
            {
            case HostActionKind.LaunchApplication:
                await launcher.LaunchApplicationAsync(
                    definition.Target!, cancellationToken);
                break;
            case HostActionKind.OpenUrl:
                await launcher.OpenUrlAsync(
                    new Uri(definition.Target!, UriKind.Absolute), cancellationToken);
                break;
            case HostActionKind.SetAudioOutput:
                await audioOutputService.SetDefaultOutputAsync(
                    definition.AudioOutput!.PrimaryDeviceId, cancellationToken);
                break;
            case HostActionKind.ToggleAudioOutput:
                var current = await audioOutputService.GetDefaultOutputAsync(
                    cancellationToken);
                var audio = definition.AudioOutput!;
                var targetId = string.Equals(
                    current?.Id, audio.PrimaryDeviceId,
                    StringComparison.OrdinalIgnoreCase)
                    ? audio.SecondaryDeviceId!
                    : audio.PrimaryDeviceId;
                await audioOutputService.SetDefaultOutputAsync(
                    targetId, cancellationToken);
                break;
            default:
                return new(HostActionExecutionStatus.InvalidDefinition,
                    "El tipo de Host Action no es ejecutable directamente.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AudioOutputUnavailableException exception)
        {
            return new(HostActionExecutionStatus.TargetNotFound, exception.Message);
        }
        catch (Exception exception)
        {
            return new(HostActionExecutionStatus.Failed,
                $"No se pudo ejecutar '{definition.Name}': {exception.Message}");
        }

        return new(HostActionExecutionStatus.Executed,
            $"Host Action '{definition.Name}' ejecutada.");
    }
}
