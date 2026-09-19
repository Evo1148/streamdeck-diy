using StreamDeckDIY.Core.Audio;
using StreamDeckDIY.Core.HostActions;
using StreamDeckDIY.App.Services;

internal static class AudioOutputTests
{
    public static async Task RunAsync()
    {
        await TestCoreAudioIdentityAsync();

        var deviceA = new AudioOutputDevice("endpoint-a", "Cascos");
        var deviceB = new AudioOutputDevice("endpoint-b", "Altavoces");
        var deviceC = new AudioOutputDevice("endpoint-c", "Monitor");
        var service = new FakeAudioOutputService([deviceA, deviceB, deviceC]);
        var enumerated = await service.GetActiveOutputsAsync();
        Assert(enumerated.SequenceEqual([deviceA, deviceB, deviceC]),
            "Audio enumeration returns typed endpoint IDs and names");

        var launcher = new AudioTestLauncher();
        var executor = new HostActionDefinitionExecutor(launcher, service);
        var set = new HostActionDefinition(
            10, "Cascos", HostActionKind.SetAudioOutput,
            AudioOutput: new AudioOutputConfiguration(deviceA.Id, null, deviceA.DisplayName));
        Assert((await executor.ExecuteAsync(set)).Status ==
               HostActionExecutionStatus.Executed &&
               service.LastSetDeviceId == deviceA.Id,
            "SetAudioOutput uses the configured DeviceId");

        service.UnavailableIds.Add("missing");
        var missing = set with
        {
            AudioOutput = new AudioOutputConfiguration("missing", null, "Antiguo"),
        };
        Assert((await executor.ExecuteAsync(missing)).Status ==
               HostActionExecutionStatus.TargetNotFound,
            "Unavailable endpoint returns a safe result");

        var toggle = new HostActionDefinition(
            11, "Cascos / Altavoces", HostActionKind.ToggleAudioOutput,
            AudioOutput: new AudioOutputConfiguration(
                deviceA.Id, deviceB.Id, deviceA.DisplayName, deviceB.DisplayName));
        service.DefaultDevice = deviceA;
        await executor.ExecuteAsync(toggle);
        Assert(service.LastSetDeviceId == deviceB.Id, "Toggle A selects B");
        service.DefaultDevice = deviceB;
        await executor.ExecuteAsync(toggle);
        Assert(service.LastSetDeviceId == deviceA.Id, "Toggle B selects A");
        service.DefaultDevice = deviceC;
        await executor.ExecuteAsync(toggle);
        Assert(service.LastSetDeviceId == deviceA.Id, "Toggle other selects A");

        Assert(!HostActionValidator.Validate(
            "Inválida", HostActionKind.ToggleAudioOutput, null,
            audioOutput: new AudioOutputConfiguration("same", "same")).IsValid,
            "Toggle rejects identical endpoints");

        service.Failure = new InvalidOperationException("fallo simulado");
        Assert((await executor.ExecuteAsync(set)).Status == HostActionExecutionStatus.Failed,
            "Audio service failure is contained");
        service.Failure = null;
        var app = new HostActionDefinition(
            12, "Notas", HostActionKind.LaunchApplication, "notepad.exe");
        Assert((await executor.ExecuteAsync(app)).Status ==
               HostActionExecutionStatus.Executed && launcher.ApplicationLaunchCount == 1,
            "Other HostActions still execute after an audio failure");

        await TestPersistenceAsync(set, toggle);
    }

    private static async Task TestCoreAudioIdentityAsync()
    {
        const string endpointId =
            "{0.0.0.00000000}.{11111111-2222-3333-4444-555555555555}";
        var endpoint = new AudioOutputDevice(endpointId, "Cascos");
        var nativeApi = new FakeCoreAudioEndpointApi([endpoint], endpointId);
        var service = new WindowsAudioOutputService(nativeApi);

        var enumerated = await service.GetActiveOutputsAsync();
        Assert(enumerated.Single().Id == endpointId,
            "Enumeration preserves the IMMDevice endpoint ID");
        Assert((await service.GetDefaultOutputAsync())?.Id == endpointId,
            "Default output uses an ID comparable with enumeration");
        await service.SetDefaultOutputAsync(endpointId);
        Assert(nativeApi.LastSetDeviceId == endpointId,
            "SetDefaultEndpoint receives the enumerated endpoint ID unchanged");

        await AssertThrowsAsync<AudioOutputUnavailableException>(
            () => service.SetDefaultOutputAsync(
                @"\\?\SWD#MMDEVAPI#{legacy-device-interface}"),
            "Legacy DeviceInformation ID is rejected safely");
    }

    private static async Task TestPersistenceAsync(
        HostActionDefinition set,
        HostActionDefinition toggle)
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"StreamDeckDIY.AudioTests.{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(directory, "host-actions.json");
            var registry = new HostActionRegistry(path);
            await registry.InitializeAsync();
            var savedSet = await registry.CreateAsync(
                set.Name, set.Kind, audioOutput: set.AudioOutput);
            var savedToggle = await registry.CreateAsync(
                toggle.Name, toggle.Kind, audioOutput: toggle.AudioOutput);

            var reloaded = new HostActionRegistry(path);
            await reloaded.InitializeAsync();
            Assert(reloaded.GetById(savedSet.Id)?.AudioOutput == set.AudioOutput,
                "SetAudioOutput persists and reloads");
            Assert(reloaded.GetById(savedToggle.Id)?.AudioOutput == toggle.AudioOutput,
                "ToggleAudioOutput persists and reloads");
            Assert(savedSet.Id == 1 && savedToggle.Id == 2,
                "Existing ID allocation remains stable");
            Assert((await File.ReadAllTextAsync(path)).Contains("\"version\": 3"),
                "Audio registry writes schema version 3");

            foreach (var version in new[] { 1, 2 })
            {
                var legacyPath = Path.Combine(directory, $"host-actions-v{version}.json");
                await File.WriteAllTextAsync(legacyPath,
                    $"{{\"version\":{version},\"nextId\":8,\"actions\":[" +
                    "{\"id\":7,\"name\":\"Anterior\",\"kind\":1," +
                    "\"target\":\"https://example.com/\"}]}");
                var legacy = new HostActionRegistry(legacyPath);
                await legacy.InitializeAsync();
                Assert(legacy.GetById(7)?.Name == "Anterior",
                    $"Schema v{version} loads with its existing ID");
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Test failed: {name}");
    }

    private static async Task AssertThrowsAsync<TException>(
        Func<Task> action, string name) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Test failed: {name}");
    }

    private sealed class FakeCoreAudioEndpointApi(
        IReadOnlyList<AudioOutputDevice> devices,
        string? defaultId) : ICoreAudioEndpointApi
    {
        public string? LastSetDeviceId { get; private set; }
        public IReadOnlyList<AudioOutputDevice> GetActiveOutputs() => devices;
        public string? GetDefaultOutputId() => defaultId;
        public void SetDefaultOutput(string deviceId) => LastSetDeviceId = deviceId;
    }

    private sealed class FakeAudioOutputService(
        IReadOnlyList<AudioOutputDevice> devices) : IAudioOutputService
    {
        public HashSet<string> UnavailableIds { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public AudioOutputDevice? DefaultDevice { get; set; }
        public string? LastSetDeviceId { get; private set; }
        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<AudioOutputDevice>> GetActiveOutputsAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(devices);

        public Task<AudioOutputDevice?> GetDefaultOutputAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(DefaultDevice);

        public Task SetDefaultOutputAsync(
            string deviceId,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;
            if (UnavailableIds.Contains(deviceId))
                throw new AudioOutputUnavailableException(
                    "El dispositivo de audio configurado ya no está disponible.");
            LastSetDeviceId = deviceId;
            return Task.CompletedTask;
        }
    }

    private sealed class AudioTestLauncher : IHostActionLauncher
    {
        public int ApplicationLaunchCount { get; private set; }

        public Task LaunchApplicationAsync(
            string target, CancellationToken cancellationToken)
        {
            ApplicationLaunchCount++;
            return Task.CompletedTask;
        }

        public Task OpenUrlAsync(Uri uri, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
