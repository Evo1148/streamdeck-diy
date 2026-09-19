using System.Buffers.Binary;
using StreamDeckDIY.Core.Audio;
using StreamDeckDIY.Core.Devices;
using StreamDeckDIY.Core.HostActions;
using StreamDeckDIY.Core.Profiles;
using StreamDeckDIY.Protocol.Messages;
using StreamDeckDIY.Protocol.Models;
using StreamDeckDIY.Protocol.ProtocolV1;
using StreamDeckDIY.Transport.Hid;
using StreamDeckDIY.App.ViewModels;
using StreamDeckDIY.App.Services;

var codec = new ProtocolV1Codec();
var control = new ControlId(ControlType.Button, 11);
var shortcut = new DeviceAction(
    ActionType.KeyboardShortcut,
    0x04,
    ActionModifiers.Control | ActionModifiers.Shift);

var actionBytes = ActionCodec.Serialize(shortcut);
Assert(actionBytes.Length == 8, "Action size");
Assert(actionBytes[0] == 2 && actionBytes[1] == 0x04, "Action fields");
Assert(actionBytes[2] == 0x03, "Modifier bitmask");
Assert(ActionCodec.Deserialize(actionBytes) == shortcut, "Action round trip");

var setPacket = codec.CreateSetBinding(0x1234, control, shortcut);
Assert(setPacket.Length == 64, "SET_BINDING packet size");
Assert(setPacket[3] == (byte)MessageType.SetBinding, "SET_BINDING type");
Assert(setPacket[4] == 0x34 && setPacket[5] == 0x12, "SET_BINDING sequence");
Assert(setPacket[6] == 10 && setPacket[7] == 0, "SET_BINDING payload size");
Assert(setPacket[8] == 0 && setPacket[9] == 11, "SET_BINDING control");

var getPacket = codec.CreateGetBinding(7, control);
Assert(getPacket[3] == (byte)MessageType.GetBinding, "GET_BINDING type");
Assert(getPacket[6] == 2 && getPacket[8] == 0 && getPacket[9] == 11,
    "GET_BINDING payload");

Assert(codec.CreateBeginConfigUpdate(8)[3] ==
       (byte)MessageType.BeginConfigUpdate,
    "BEGIN_CONFIG_UPDATE type");
Assert(codec.CreateCommitConfigUpdate(9)[3] ==
       (byte)MessageType.CommitConfigUpdate,
    "COMMIT_CONFIG_UPDATE type");
Assert(codec.CreateCancelConfigUpdate(10)[3] ==
       (byte)MessageType.CancelConfigUpdate,
    "CANCEL_CONFIG_UPDATE type");
var bootloaderPacket = codec.CreateEnterBootloader(11);
Assert(bootloaderPacket[3] == (byte)MessageType.EnterBootloader &&
       bootloaderPacket[6] == 0 && bootloaderPacket[7] == 0,
    "ENTER_BOOTLOADER type and empty payload");

var bindingResponse = codec.Serialize(new ProtocolPacket(
    MessageType.BindingInfo,
    7,
    [0, 11, .. actionBytes]));
var binding = codec.ParseBindingInfo(bindingResponse, 7);
Assert(binding.Control == control && binding.Action == shortcut,
    "BINDING_INFO parse");

AssertThrows(() => codec.CreateGetBinding(8, new ControlId(ControlType.Button, 12)),
    "Button index validation");
AssertThrows(() => ActionCodec.Serialize(shortcut with
{
    Modifiers = (ActionModifiers)0x10,
}), "Modifier validation");

var hostPayload = new byte[4];
BinaryPrimitives.WriteUInt32LittleEndian(hostPayload, 0xF1234567u);
var hostPacket = codec.Serialize(new ProtocolPacket(
    MessageType.HostActionTriggered, 0x4321, hostPayload));
var hostEvent = codec.ParseHostActionTriggered(hostPacket);
Assert(hostEvent.Sequence == 0x4321 && hostEvent.ActionId == 0xF1234567u,
    "HOST_ACTION_TRIGGERED parse");
AssertThrows(() => codec.ParseHostActionTriggered(codec.Serialize(
    new ProtocolPacket(MessageType.HostActionTriggered, 1, [1, 2, 3]))),
    "HOST_ACTION_TRIGGERED invalid payload");

await TestResponseRoutingAsync(codec, shortcut, hostPacket);
await TestDeviceConnectionLifecycleAsync(codec);
await TestBootloaderCommandAsync(codec);
await DeviceFirmwareUpdateTests.RunAsync(codec);
await TestHostActionRegistryAndExecutorAsync();
await TestMacrosAsync();
await AudioOutputTests.RunAsync();
await DisplayTests.RunAsync();
await ProfileTests.RunAsync();
await AutomationTests.RunAsync();
AppShellTests.Run();
TestBindingPresentation();
await TestBindingIconStoreAsync();
await DashboardTests.RunAsync();
await SystemStatsTests.RunAsync();
await DisplayLinkTests.RunAsync();
await ArtworkRgb565ConverterTests.RunAsync();
CompanionRasterPolicyTests.Run();
await CompanionPackTests.RunAsync();

Console.WriteLine("StreamDeckDIY.Protocol tests passed.");

static void TestBindingPresentation()
{
    Assert(BindingPresentation.Describe(
        new DeviceAction(ActionType.None), (uint _) => (string?)null) == "Sin asignar",
        "Unassigned binding has a friendly summary");
    Assert(BindingPresentation.Describe(
        new DeviceAction(ActionType.KeyboardShortcut, 0x06,
            ActionModifiers.Control | ActionModifiers.Shift),
        (uint _) => (string?)null) ==
        "Ctrl + Shift + C", "Keyboard shortcut summary is friendly");
    Assert(BindingPresentation.Describe(
        new DeviceAction(ActionType.ConsumerControl,
            ConsumerControl: ConsumerControlAction.VolumeDown),
        (uint _) => (string?)null) ==
        "Volumen −", "Consumer control summary is friendly");
    Assert(BindingPresentation.Describe(
        new DeviceAction(ActionType.HostAction, HostActionId: 7),
        id => id == 7 ? "Abrir Visual Studio" : null) ==
        "Abrir Visual Studio", "Host action summary resolves its product name");
    var audio = new HostActionDefinition(
        8, "SalidaAudioIntercambiar", HostActionKind.ToggleAudioOutput);
    Assert(BindingPresentation.Describe(
        new DeviceAction(ActionType.HostAction, HostActionId: 8),
        id => id == 8 ? audio : null) == "Alternar salida de audio",
        "Audio HostAction uses a friendly product summary");
    Assert(BindingPresentation.IconGlyph(
        new DeviceAction(ActionType.HostAction, HostActionId: 8),
        id => id == 8 ? audio : null) == "\uE767",
        "Audio HostAction uses the media icon");

    var controls = ProfileControls.All;
    foreach (var selected in controls)
    {
        Assert(controls.Count(item => BindingPresentation.IsSelected(item, selected)) == 1,
            $"Only one control is selected for {selected.Type}");
    }
    var encoderOrder = new[]
    {
        ControlType.EncoderClockwise,
        ControlType.EncoderCounterClockwise,
        ControlType.EncoderPress,
    }.OrderBy(BindingPresentation.EncoderOrder).ToArray();
    Assert(encoderOrder.SequenceEqual(new[]
    {
        ControlType.EncoderCounterClockwise,
        ControlType.EncoderPress,
        ControlType.EncoderClockwise,
    }), "Encoder presentation order is left, press, right");
}

static async Task TestBindingIconStoreAsync()
{
    var directory = Path.Combine(
        Path.GetTempPath(), $"StreamDeckDIY.IconTests.{Guid.NewGuid():N}");
    var path = Path.Combine(directory, "binding-icons.json");
    var control = new ControlId(ControlType.Button, 2);
    try
    {
        var empty = new BindingIconStore(path);
        await empty.InitializeAsync();
        Assert(empty.Get(1, control) is null,
            "Existing configurations default to automatic icons");

        await empty.SetAsync(1, control, "music");
        var reloaded = new BindingIconStore(path);
        await reloaded.InitializeAsync();
        Assert(reloaded.Get(1, control) == "music",
            "Manual binding icon persists and reloads");
        Assert(reloaded.Get(2, control) is null,
            "Binding icons are isolated by profile");

        await reloaded.SetAsync(1, control, null);
        var automatic = new BindingIconStore(path);
        await automatic.InitializeAsync();
        Assert(automatic.Get(1, control) is null,
            "Automatic mode removes the manual override");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static async Task TestHostActionRegistryAndExecutorAsync()
{
    var directory = Path.Combine(
        Path.GetTempPath(), $"StreamDeckDIY.Tests.{Guid.NewGuid():N}");
    var filePath = Path.Combine(directory, "host-actions.json");
    try
    {
        Assert(!HostActionValidator.Validate(
                "", HostActionKind.LaunchApplication, "notepad.exe").IsValid,
            "Empty HostAction name is invalid");
        Assert(!HostActionValidator.Validate(
                "   ", HostActionKind.LaunchApplication, "notepad.exe").IsValid,
            "Whitespace HostAction name is invalid");
        Assert(HostActionValidator.Validate(
                "HTTP", HostActionKind.OpenUrl, "http://example.com/").IsValid,
            "Absolute HTTP URL is valid");
        Assert(HostActionValidator.Validate(
                "HTTPS", HostActionKind.OpenUrl, "https://example.com/").IsValid,
            "Absolute HTTPS URL is valid");
        Assert(!HostActionValidator.Validate(
                "URL", HostActionKind.OpenUrl, "example.com").IsValid,
            "URL without scheme is invalid");
        Assert(!HostActionValidator.Validate(
                "URL", HostActionKind.OpenUrl, "ftp://example.com/").IsValid,
            "Non-HTTP URL scheme is invalid");
        Assert(!HostActionValidator.Validate(
                "Missing", HostActionKind.LaunchApplication,
                Path.Combine(directory, "missing.exe")).IsValid,
            "Missing absolute executable is invalid");
        Assert(HostActionValidator.Validate(
                "Notepad", HostActionKind.LaunchApplication, "notepad.exe").IsValid,
            "Relative executable name is valid");
        Assert(HostActionNameSuggester.ApplyWhenEmpty(
                   string.Empty, @"C:\Apps\Spotify.exe") == "Spotify" &&
               HostActionNameSuggester.ApplyWhenEmpty(
                   "Mi Spotify", @"C:\Apps\Spotify.exe") == "Mi Spotify",
            "Executable selection suggests a name only when empty");

        var registry = new HostActionRegistry(filePath);
        await registry.InitializeAsync();

        var application = await registry.CreateAsync(
            "Bloc de notas", HostActionKind.LaunchApplication, "notepad.exe");
        var url = await registry.CreateAsync(
            "OpenAI", HostActionKind.OpenUrl, "https://chatgpt.com/");
        Assert(application.Id == 1 && url.Id == 2,
            "Registry assigns unique IDs starting at one");
        Assert(registry.GetById(application.Id) == application,
            "Registry finds action by ID");

        var updated = await registry.UpdateAsync(application with { Name = "Notas" });
        Assert(updated.Id == application.Id && updated.Name == "Notas",
            "Registry update preserves ID");
        await AssertThrowsAsync<ArgumentException>(() => registry.UpdateAsync(updated with
        {
            Kind = HostActionKind.OpenUrl,
            Target = "chatgpt.com",
        }), "Invalid edit is rejected");
        Assert(registry.GetById(updated.Id) == updated,
            "Invalid edit preserves previous definition");

        var reloaded = new HostActionRegistry(filePath);
        await reloaded.InitializeAsync();
        Assert(reloaded.GetAll().Count == 2 &&
               reloaded.GetById(application.Id)?.Name == "Notas",
            "Registry persists and reloads versioned JSON");
        Assert((await File.ReadAllTextAsync(filePath)).Contains("\"version\": 3"),
            "Registry JSON contains version");

        var concurrent = Enumerable.Range(0, 8).Select(index =>
            reloaded.CreateAsync($"Concurrent {index}",
                HostActionKind.LaunchApplication, $"tool-{index}.exe")).ToArray();
        await Task.WhenAll(concurrent);
        var concurrentReload = new HostActionRegistry(filePath);
        await concurrentReload.InitializeAsync();
        Assert(concurrentReload.GetAll().Count == 10 &&
               concurrentReload.GetAll().Select(action => action.Id).Distinct().Count() == 10,
            "Concurrent HostAction saves serialize and survive a real JSON reload");

        Assert(await reloaded.DeleteAsync(application.Id), "Registry deletes action");
        var next = await reloaded.CreateAsync(
            "Otra", HostActionKind.LaunchApplication, "other.exe");
        Assert(next.Id == 11, "Registry does not reuse deleted or concurrently assigned IDs");

        var boundAction = new DeviceAction(ActionType.HostAction, HostActionId: url.Id);
        Assert(reloaded.GetById(boundAction.HostActionId)?.Name == "OpenAI",
            "Firmware HostAction ID resolves through Windows registry");

        var launcher = new TestHostActionLauncher();
        var executor = CreateExecutor(reloaded, launcher, new ImmediateDelay());
        var unknown = await executor.ExecuteAsync(999);
        Assert(unknown.Status == HostActionExecutionStatus.NotConfigured,
            "Unknown HostAction is safe");

        var missing = new HostActionDefinition(
            40, "Missing", HostActionKind.LaunchApplication,
            Path.Combine(directory, "missing.exe"));
        var missingRegistry = new StaticHostActionRegistry(missing);
        var missingExecutor = CreateExecutor(
            missingRegistry, launcher, new ImmediateDelay());
        var missingResult = await missingExecutor.ExecuteAsync(missing.Id);
        Assert(missingResult.Status == HostActionExecutionStatus.TargetNotFound &&
               launcher.ApplicationLaunchCount == 0,
            "Missing application returns a clear error without launching");

        var invalidUrl = new HostActionDefinition(
            41, "Invalid URL", HostActionKind.OpenUrl, "file:///unsafe");
        var invalidRegistry = new StaticHostActionRegistry(invalidUrl);
        var invalidUrlExecutor = CreateExecutor(
            invalidRegistry, launcher, new ImmediateDelay());
        var invalidUrlResult = await invalidUrlExecutor.ExecuteAsync(invalidUrl.Id);
        Assert(invalidUrlResult.Status == HostActionExecutionStatus.InvalidDefinition &&
               launcher.UrlLaunchCount == 0,
            "Invalid URL scheme is rejected without launching");

        var applicationResult = await executor.ExecuteAsync(next.Id);
        var urlResult = await executor.ExecuteAsync(url.Id);
        Assert(applicationResult.Status == HostActionExecutionStatus.Executed &&
               launcher.LastApplication == "other.exe",
            "Executor resolves LaunchApplication definition");
        Assert(urlResult.Status == HostActionExecutionStatus.Executed &&
               launcher.LastUrl?.Scheme == "https",
            "Executor resolves OpenUrl definition");

        var corruptPath = Path.Combine(directory, "corrupt.json");
        await File.WriteAllTextAsync(corruptPath, "{not-json");
        var corruptRegistry = new HostActionRegistry(corruptPath);
        await corruptRegistry.InitializeAsync();
        Assert(corruptRegistry.GetAll().Count == 0,
            "Corrupt registry starts with a valid empty collection");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestMacrosAsync()
{
    var directory = Path.Combine(
        Path.GetTempPath(), $"StreamDeckDIY.MacroTests.{Guid.NewGuid():N}");
    var filePath = Path.Combine(directory, "host-actions.json");
    try
    {
        var registry = new HostActionRegistry(filePath);
        await registry.InitializeAsync();
        var first = await registry.CreateAsync(
            "Primera", HostActionKind.LaunchApplication, "first.exe");
        var second = await registry.CreateAsync(
            "Segunda", HostActionKind.OpenUrl, "https://example.com/");
        var third = await registry.CreateAsync(
            "Tercera", HostActionKind.LaunchApplication, "third.exe");

        Assert(!HostActionValidator.Validate(
                "Vacía", HostActionKind.Macro, null, [], registry.GetById).IsValid,
            "Empty macro is invalid");
        Assert(!HostActionValidator.Validate(
                "Delay cero", HostActionKind.Macro, null,
                [MacroStep.Delay(0)], registry.GetById).IsValid,
            "Macro delay zero is invalid");
        Assert(!HostActionValidator.Validate(
                "Delay largo", HostActionKind.Macro, null,
                [MacroStep.Delay(60001)], registry.GetById).IsValid,
            "Macro delay over 60000 is invalid");
        Assert(!HostActionValidator.Validate(
                "Falta", HostActionKind.Macro, null,
                [MacroStep.Execute(999)], registry.GetById).IsValid,
            "Macro missing action is invalid");

        var steps = new[]
        {
            MacroStep.Execute(first.Id),
            MacroStep.Delay(500),
            MacroStep.Execute(second.Id),
        };
        var macro = await registry.CreateAsync(
            "Flujo", HostActionKind.Macro, steps: steps);
        Assert(macro.Kind == HostActionKind.Macro && macro.Steps!.SequenceEqual(steps),
            "Registry creates typed macro with ordered steps");
        Assert(!HostActionValidator.Validate(
                "Anidada", HostActionKind.Macro, null,
                [MacroStep.Execute(macro.Id)], registry.GetById).IsValid,
            "Macro cannot reference another macro");

        var editedSteps = new[]
        {
            MacroStep.Execute(first.Id),
            MacroStep.Delay(250),
            MacroStep.Execute(second.Id),
        };
        var edited = await registry.UpdateAsync(macro with
        {
            Name = "Flujo editado",
            Steps = editedSteps,
        });
        Assert(edited.Id == macro.Id && edited.Steps!.SequenceEqual(editedSteps),
            "Macro edit preserves ID and step order");

        var reloaded = new HostActionRegistry(filePath);
        await reloaded.InitializeAsync();
        Assert(reloaded.GetById(macro.Id)?.Steps!.SequenceEqual(editedSteps) == true,
            "Macro serialization preserves step order");

        var executionEvents = new List<string>();
        var launcher = new TestHostActionLauncher(executionEvents);
        var executor = CreateExecutor(
            reloaded, launcher, new RecordingDelay(executionEvents));
        var success = await executor.ExecuteAsync(macro.Id);
        Assert(success.Status == HostActionExecutionStatus.Executed &&
               executionEvents.SequenceEqual(new[]
               {
                   "app:first.exe", "delay:250", "url:https://example.com/",
               }),
            "Macro executes actions and delay in order without real waiting");

        var failingMacro = await reloaded.CreateAsync(
            "Falla", HostActionKind.Macro, steps:
            [
                MacroStep.Execute(first.Id),
                MacroStep.Execute(second.Id),
                MacroStep.Execute(third.Id),
            ]);
        executionEvents.Clear();
        launcher.FailTarget = "https://example.com/";
        var failed = await executor.ExecuteAsync(failingMacro.Id);
        Assert(failed.Status == HostActionExecutionStatus.Failed &&
               failed.Message.Contains("paso 2") &&
               !executionEvents.Contains("app:third.exe"),
            "Macro stops at failing step and does not run later steps");
        launcher.FailTarget = null;

        var gateDelay = new GateDelay();
        var concurrencyExecutor = CreateExecutor(reloaded, launcher, gateDelay);
        var waitingMacro = await reloaded.CreateAsync(
            "Espera", HostActionKind.Macro, steps: [MacroStep.Delay(10)]);
        var firstRun = concurrencyExecutor.ExecuteAsync(waitingMacro.Id);
        await gateDelay.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var duplicateRun = await concurrencyExecutor.ExecuteAsync(waitingMacro.Id);
        Assert(duplicateRun.Status == HostActionExecutionStatus.AlreadyRunning,
            "Macro cannot run twice concurrently");
        gateDelay.Release.TrySetResult();
        Assert((await firstRun).Status == HostActionExecutionStatus.Executed,
            "First macro run completes after asynchronous delay");
        Assert((await concurrencyExecutor.ExecuteAsync(waitingMacro.Id)).Status ==
               HostActionExecutionStatus.Executed,
            "Macro can run again after completion");

        Assert(await reloaded.DeleteAsync(first.Id), "Referenced action can be deleted");
        var unresolved = await executor.ExecuteAsync(macro.Id);
        Assert(unresolved.Status == HostActionExecutionStatus.Failed &&
               unresolved.Message.Contains("paso 1"),
            "Unresolved macro action stops safely with step feedback");

        var v1Path = Path.Combine(directory, "host-actions-v1.json");
        await File.WriteAllTextAsync(v1Path,
            "{\"version\":1,\"nextId\":2,\"actions\":[" +
            "{\"id\":1,\"name\":\"Anterior\",\"kind\":0," +
            "\"target\":\"notepad.exe\"}]}");
        var v1Registry = new HostActionRegistry(v1Path);
        await v1Registry.InitializeAsync();
        Assert(v1Registry.GetById(1)?.Name == "Anterior",
            "Version 1 registry loads without losing actions");
        await v1Registry.CreateAsync(
            "Nueva", HostActionKind.OpenUrl, "https://openai.com/");
        Assert((await File.ReadAllTextAsync(v1Path)).Contains("\"version\": 3"),
            "Version 1 registry saves subsequently as version 3");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static HostActionExecutor CreateExecutor(
    IHostActionRegistry registry,
    IHostActionLauncher launcher,
    IAsyncDelay delay)
{
    var definitionExecutor = new HostActionDefinitionExecutor(
        launcher, new NoopAudioOutputService());
    return new HostActionExecutor(
        registry, definitionExecutor,
        new MacroExecutor(registry, definitionExecutor, delay));
}

static async Task TestResponseRoutingAsync(
    ProtocolV1Codec codec,
    DeviceAction action,
    byte[] hostPacket)
{
    await using (var service = CreateService(codec, out var transport))
    {
        var hostEventCount = 0;
        service.HostActionTriggered += (_, _) => hostEventCount++;
        transport.OnWrite = request =>
        {
            var sequence = codec.Deserialize(request.Span).Sequence;
            transport.Emit(codec.Serialize(new ProtocolPacket(MessageType.Ack, sequence, [])));
        };

        await service.SetBindingAsync(new ControlId(ControlType.Button, 0), action);
        Assert(hostEventCount == 0, "Pending request plus ACK routes only ACK");
    }

    await using (var service = CreateService(codec, out var transport))
    {
        uint? propagatedActionId = null;
        service.HostActionTriggered += (_, args) => propagatedActionId = args.ActionId;
        transport.OnWrite = request =>
        {
            var sequence = codec.Deserialize(request.Span).Sequence;
            transport.Emit(hostPacket);
            transport.Emit(codec.Serialize(new ProtocolPacket(MessageType.Ack, sequence, [])));
        };

        await service.ExecuteActionTestAsync(action);
        Assert(propagatedActionId == 0xF1234567u,
            "HostAction propagates while ACK still completes pending request");
    }

    await using (var service = CreateService(codec, out var transport))
    {
        uint? propagatedActionId = null;
        service.HostActionTriggered += (_, args) => propagatedActionId = args.ActionId;
        transport.Emit(hostPacket);
        Assert(propagatedActionId == 0xF1234567u,
            "HostAction propagates without pending request");
    }

    await using (var service = CreateService(codec, out var transport))
    {
        var hostEventCount = 0;
        service.HostActionTriggered += (_, _) => hostEventCount++;
        transport.Emit(codec.Serialize(new ProtocolPacket(
            MessageType.DeviceInfo, 12, new byte[8])));
        transport.Emit(codec.Serialize(new ProtocolPacket(
            MessageType.BindingInfo, 13, new byte[10])));
        Assert(hostEventCount == 0,
            "DeviceInfo and BindingInfo are not consumed as HostAction events");
    }

    await using (var service = CreateService(codec, out var transport))
    {
        transport.Emit(new byte[ProtocolV1Codec.PacketSize]);
        transport.Emit(codec.Serialize(new ProtocolPacket(
            MessageType.GetDeviceInfo, 99, [])));
        Assert(service.IsConnected && !transport.CloseCalled,
            "Unexpected reports do not cause false disconnection");

        transport.OnWrite = request =>
        {
            var sequence = codec.Deserialize(request.Span).Sequence;
            transport.Emit(codec.Serialize(new ProtocolPacket(MessageType.Ack, sequence, [])));
        };
        await service.ExecuteActionTestAsync(action);
    }

    await TestHostActionOrderAsync(codec, eventBeforeAck: false);
    await TestHostActionOrderAsync(codec, eventBeforeAck: true);

    await using (var service = CreateService(codec, out var transport))
    {
        var failingExecutor = new ThrowingHostActionExecutor();
        var laterSubscriberCount = 0;
        service.HostActionTriggered += (_, args) =>
            failingExecutor.ExecuteAsync(args.ActionId).GetAwaiter().GetResult();
        service.HostActionTriggered += (_, _) => laterSubscriberCount++;

        transport.Emit(CreateHostActionPacket(codec, 1, 77));
        Assert(laterSubscriberCount == 1 && service.IsConnected,
            "Throwing HostAction subscriber does not stop report delivery");

        transport.OnWrite = request => EmitAck(codec, transport, request);
        await service.ExecuteActionTestAsync(new DeviceAction(
            ActionType.ConsumerControl,
            ConsumerControl: ConsumerControlAction.VolumeUp));
        Assert(service.IsConnected,
            "Normal request works after throwing HostAction subscriber");
    }
}

static async Task TestDeviceConnectionLifecycleAsync(ProtocolV1Codec codec)
{
    await using var service = CreateService(codec, out var transport);
    var states = new List<bool>();
    service.ConnectionChanged += (_, args) => states.Add(args.IsConnected);
    Assert(await service.ConnectAsync() && states.SequenceEqual([true]),
        "Device connection is published once");

    transport.OnWrite = _ => transport.Fail(new IOException("device removed"));
    await AssertThrowsAsync<IOException>(
        () => service.GetDeviceInfoAsync(),
        "Pending request completes on physical disconnect");
    Assert(!service.IsConnected && states.SequenceEqual([true, false]),
        "Transport loss propagates a disconnected device state");

    transport.OnWrite = request =>
    {
        var sequence = codec.Deserialize(request.Span).Sequence;
        transport.Emit(codec.Serialize(new ProtocolPacket(
            MessageType.DeviceInfo, sequence,
            [1, 0, 1, 0, 0, 12, 7, 0])));
    };
    Assert(await service.ReconnectDisplayLinkAsync() &&
           service.IsConnected && states.SequenceEqual([true, false, true]) &&
           transport.OpenCount == 2,
        "Reconnect validates GET_DEVICE_INFO before publishing Connected");
}

static async Task TestBootloaderCommandAsync(ProtocolV1Codec codec)
{
    await using (var service = CreateService(codec, out var transport))
    {
        Assert(await service.ConnectAsync(),
            "Bootloader test starts from a published connected state");
        DeviceConnectionChangedEventArgs? disconnected = null;
        service.ConnectionChanged += (_, args) =>
        {
            if (!args.IsConnected) disconnected = args;
        };
        transport.OnWrite = request =>
        {
            var packet = codec.Deserialize(request.Span);
            Assert(packet.MessageType == MessageType.EnterBootloader,
                "DeviceService sends ENTER_BOOTLOADER");
            transport.Emit(codec.Serialize(new ProtocolPacket(
                MessageType.Ack, packet.Sequence, [])));
            transport.Fail(new IOException("device entered ROM bootloader"));
        };
        await service.EnterBootloaderAsync();
        Assert(disconnected?.DisconnectReason ==
                   DeviceDisconnectReason.BootloaderRequested &&
               disconnected.Error is null,
            "Post-ACK bootloader disconnect is expected");
    }

    await using (var service = CreateService(codec, out var transport))
    {
        transport.OnWrite = request =>
        {
            var packet = codec.Deserialize(request.Span);
            transport.Emit(codec.Serialize(new ProtocolPacket(
                MessageType.Nack, packet.Sequence,
                [(byte)NackReason.InvalidState,
                 (byte)MessageType.EnterBootloader])));
        };
        await AssertThrowsAsync<ProtocolException>(
            () => service.EnterBootloaderAsync(),
            "ENTER_BOOTLOADER NACK is surfaced");
        Assert(service.IsConnected,
            "NACK does not mark the transport disconnected");
    }
}

static async Task TestHostActionOrderAsync(
    ProtocolV1Codec codec,
    bool eventBeforeAck)
{
    await using var service = CreateService(codec, out var transport);
    var hostEventCount = 0;
    uint? actionId = null;
    service.HostActionTriggered += (_, args) =>
    {
        hostEventCount++;
        actionId = args.ActionId;
    };
    transport.OnWrite = request =>
    {
        var requestPacket = codec.Deserialize(request.Span);
        var hostEvent = CreateHostActionPacket(codec, 1, 0x7000);
        var ack = codec.Serialize(new ProtocolPacket(
            MessageType.Ack, requestPacket.Sequence, []));
        if (eventBeforeAck)
        {
            transport.Emit(hostEvent);
            transport.Emit(ack);
        }
        else
        {
            transport.Emit(ack);
            transport.Emit(hostEvent);
        }
    };

    await service.ExecuteActionTestAsync(new DeviceAction(
        ActionType.HostAction, HostActionId: 1));
    Assert(hostEventCount == 1 && actionId == 1 && service.IsConnected,
        $"HostAction and ACK route correctly when eventBeforeAck={eventBeforeAck}");

    transport.OnWrite = request => EmitAck(codec, transport, request);
    await service.ExecuteActionTestAsync(new DeviceAction(
        ActionType.ConsumerControl,
        ConsumerControl: ConsumerControlAction.VolumeUp));
    Assert(service.IsConnected,
        $"ConsumerControl works after HostAction when eventBeforeAck={eventBeforeAck}");
}

static byte[] CreateHostActionPacket(
    ProtocolV1Codec codec,
    uint actionId,
    ushort sequence)
{
    var payload = new byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(payload, actionId);
    return codec.Serialize(new ProtocolPacket(
        MessageType.HostActionTriggered, sequence, payload));
}

static void EmitAck(
    ProtocolV1Codec codec,
    TestTransport transport,
    ReadOnlyMemory<byte> request)
{
    var sequence = codec.Deserialize(request.Span).Sequence;
    transport.Emit(codec.Serialize(new ProtocolPacket(MessageType.Ack, sequence, [])));
}

static DeviceService CreateService(ProtocolV1Codec codec, out TestTransport transport)
{
    transport = new TestTransport();
    return new DeviceService(transport, codec);
}

static void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"Test failed: {name}");
}

static void AssertThrows(Action action, string name)
{
    try
    {
        action();
    }
    catch (ProtocolException)
    {
        return;
    }
    throw new InvalidOperationException($"Test failed: {name}");
}

static async Task AssertThrowsAsync<TException>(Func<Task> action, string name)
    where TException : Exception
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

sealed class TestTransport : IHidTransport
{
    public event Action<ReadOnlyMemory<byte>>? ReportReceived;
    public event Action<Exception>? ConnectionLost;
    public Action<ReadOnlyMemory<byte>>? OnWrite { get; set; }
    public bool CloseCalled { get; private set; }
    public int OpenCount { get; private set; }
    public bool IsOpen => !CloseCalled;
    public Task<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        OpenCount++;
        CloseCalled = false;
        return Task.FromResult(true);
    }
    public Task WriteAsync(
        ReadOnlyMemory<byte> packet,
        CancellationToken cancellationToken = default)
    {
        OnWrite?.Invoke(packet);
        return Task.CompletedTask;
    }
    public void Close() => CloseCalled = true;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Emit(byte[] report) => ReportReceived?.Invoke(report);
    public void Fail(Exception exception)
    {
        CloseCalled = true;
        ConnectionLost?.Invoke(exception);
    }
}

sealed class ThrowingHostActionExecutor : IHostActionExecutor
{
    public Task<HostActionExecutionResult> ExecuteAsync(
        uint actionId,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated HostAction failure.");
}

sealed class TestHostActionLauncher(List<string>? events = null) : IHostActionLauncher
{
    public int ApplicationLaunchCount { get; private set; }
    public int UrlLaunchCount { get; private set; }
    public string? LastApplication { get; private set; }
    public Uri? LastUrl { get; private set; }
    public string? FailTarget { get; set; }

    public Task LaunchApplicationAsync(
        string target,
        CancellationToken cancellationToken)
    {
        if (target == FailTarget) throw new InvalidOperationException("Simulated launch failure.");
        ApplicationLaunchCount++;
        LastApplication = target;
        events?.Add($"app:{target}");
        return Task.CompletedTask;
    }

    public Task OpenUrlAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.AbsoluteUri == FailTarget)
            throw new InvalidOperationException("Simulated launch failure.");
        UrlLaunchCount++;
        LastUrl = uri;
        events?.Add($"url:{uri.AbsoluteUri}");
        return Task.CompletedTask;
    }
}

sealed class StaticHostActionRegistry(HostActionDefinition definition)
    : IHostActionRegistry
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
    public IReadOnlyList<HostActionDefinition> GetAll() => [definition];
    public HostActionDefinition? GetById(uint id) =>
        id == definition.Id ? definition : null;
    public Task<HostActionDefinition> CreateAsync(
        string name, HostActionKind kind, string? target = null,
        IReadOnlyList<MacroStep>? steps = null,
        AudioOutputConfiguration? audioOutput = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task<HostActionDefinition> UpdateAsync(
        HostActionDefinition value,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task<bool> DeleteAsync(
        uint id,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

sealed class NoopAudioOutputService : IAudioOutputService
{
    public Task<IReadOnlyList<AudioOutputDevice>> GetActiveOutputsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AudioOutputDevice>>([]);
    public Task<AudioOutputDevice?> GetDefaultOutputAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<AudioOutputDevice?>(null);
    public Task SetDefaultOutputAsync(
        string deviceId,
        CancellationToken cancellationToken = default) =>
        throw new AudioOutputUnavailableException(
            "El dispositivo de audio configurado ya no está disponible.");
}

sealed class ImmediateDelay : IAsyncDelay
{
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

sealed class RecordingDelay(List<string> events) : IAsyncDelay
{
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        events.Add($"delay:{milliseconds}");
        return Task.CompletedTask;
    }
}

sealed class GateDelay : IAsyncDelay
{
    private int invocationCount;
    public TaskCompletionSource Entered { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref invocationCount) != 1) return;
        Entered.TrySetResult();
        await Release.Task.WaitAsync(cancellationToken);
    }
}

