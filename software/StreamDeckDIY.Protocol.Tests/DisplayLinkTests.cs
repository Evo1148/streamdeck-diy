using StreamDeckDIY.Core.Dashboard;
using StreamDeckDIY.Core.Devices;
using StreamDeckDIY.Core.Display;
using StreamDeckDIY.Core.DisplayLink;
using StreamDeckDIY.Protocol.DisplayLink;
using StreamDeckDIY.Protocol.Messages;
using StreamDeckDIY.Protocol.Models;
using StreamDeckDIY.Protocol.ProtocolV1;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;

internal static class DisplayLinkTests
{
    public static async Task RunAsync()
    {
        TestGoldenVectors();
        TestGraphCompilers();
        TestVisualParityContract();
        TestIconCatalogContract();
        TestVisualSystemArchitecture();
        TestLocalControlParameters();
        await TestInitialLifecycleAndStartupCoalescingAsync();
        await TestUpdatesCoalesceDuringFullSyncAsync();
        await TestProfileSwitchOwnsNewGenerationAsync();
        await TestBootSessionRecoveryAsync();
        await TestPhysicalReconnectLifecycleAsync();
        await TestDisconnectBoundariesAsync();
        await TestUnsupportedAndHandshakeFailureAsync();
        await TestCapabilitiesRemainVisibleAfterFaultAsync();
        await TestStableProgressPatchesAsync();
        await TestAssetTransferAndTouchRoutingAsync();
        await TestAssetCommitOrderingAndStabilityAsync();
        await TestAssetNotReadyIsExpectedStateAsync();
        await TestIdleReadyDoesNotRetryOrThrowAsync();
        await TestBootloaderDisconnectDefersReconnectAsync();
    }

    private static async Task TestAssetNotReadyIsExpectedStateAsync()
    {
        var channel = new FakeChannel { IsConnected = false };
        await using var sync = new DisplaySyncService(channel);
        var firstChance = 0;
        EventHandler<FirstChanceExceptionEventArgs> handler = (_, args) =>
        {
            if (args.Exception is InvalidOperationException exception &&
                exception.Message.Contains("DisplayLink", StringComparison.Ordinal))
                Interlocked.Increment(ref firstChance);
        };
        AppDomain.CurrentDomain.FirstChanceException += handler;
        try
        {
            var transferred = await sync.TransferAssetAsync(
                1, 1, 1, 1, new byte[] { 0, 0 });
            Assert(!transferred && firstChance == 0 && channel.Commands.Count == 0,
                "Asset request before Ready returns not-started without first-chance exception or traffic");
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= handler;
        }
    }

    private static async Task TestIdleReadyDoesNotRetryOrThrowAsync()
    {
        var channel = new FakeChannel();
        await using var sync = new DisplaySyncService(
            channel, TimeSpan.FromMilliseconds(5));
        await sync.SynchronizeAsync(Graph(10), "1:system");
        var getInfoCount = channel.Commands.Count(
            opcode => opcode == DisplayLinkOpcode.GetInfo);
        var firstChance = 0;
        EventHandler<FirstChanceExceptionEventArgs> handler = (_, args) =>
        {
            if (args.Exception is InvalidOperationException)
                Interlocked.Increment(ref firstChance);
        };
        AppDomain.CurrentDomain.FirstChanceException += handler;
        try
        {
            await Task.Delay(120);
            Assert(sync.Lifecycle == DisplayLinkLifecycleState.Ready &&
                   channel.Commands.Count(opcode =>
                       opcode == DisplayLinkOpcode.GetInfo) == getInfoCount &&
                   firstChance == 0,
                "Idle Ready lifecycle stays stable without reconnect or InvalidOperationException storm");
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= handler;
        }
    }

    private static async Task TestBootloaderDisconnectDefersReconnectAsync()
    {
        var channel = new FakeChannel();
        await using var sync = new DisplaySyncService(
            channel, TimeSpan.FromMilliseconds(10));
        await sync.SynchronizeAsync(Graph(11), "1:system");
        channel.DisconnectForBootloader();
        await Task.Delay(100);
        Assert(sync.Lifecycle == DisplayLinkLifecycleState.Disconnected &&
               sync.State.LastError == "Modo actualización solicitado" &&
               channel.ReconnectCount == 0,
            "Requested bootloader disconnect stops work and does not reconnect immediately");
    }

    private static void TestIconCatalogContract()
    {
        var solutionRoot=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..","..","..",".."));
        var header=Path.Combine(solutionRoot,"..","firmware",
            "src","display","display_icon_catalog.hpp");
        Assert(File.Exists(header),"Firmware icon catalog source is available to the contract test");
        var firmware=Regex.Matches(File.ReadAllText(header),
                @"^\s*(\w+)\s*=\s*(\d+),",RegexOptions.Multiline)
            .Select(match=>(Name:match.Groups[1].Value,
                Id:ushort.Parse(match.Groups[2].Value)))
            .ToDictionary(item=>item.Name,item=>item.Id,StringComparer.Ordinal);
        Assert(DashboardIconCatalog.Contracts.Count==24 &&
               DashboardIconCatalog.Contracts.All(icon=>
                   firmware.TryGetValue(icon.Name,out var id)&&id==icon.Id) &&
               firmware.Count==DashboardIconCatalog.Contracts.Count,
            "Windows and firmware icon names and numeric IDs match exactly");
        Assert(DisplayAssetIds.IsArtwork(ArtworkContentKey.AssetId("artwork-a")) &&
               !DisplayAssetIds.IsArtwork(DisplayAssetIds.ActiveCompanion) &&
               DisplayAssetIds.IsCompanion(CompanionPackFormat.ActiveAssetId),
            "Artwork and Companion use disjoint stable asset namespaces");

        var navigation=DashboardDisplayGraphCompiler.Compile(
            Runtime(DashboardPresets.ControlInfo.Id,20),0);
        var iconIds=navigation.Nodes.Where(node=>node.Kind==DisplayNodeKind.Icon)
            .Select(node=>node.IconId).ToHashSet();
        Assert(iconIds.Contains(DashboardIconCatalog.Left)&&
               iconIds.Contains(DashboardIconCatalog.Grid)&&
               iconIds.Contains(DashboardIconCatalog.Right)&&
               navigation.Nodes.All(node=>node.Kind!=DisplayNodeKind.Text||
                   node.Text is not ("‹" or "⌂" or "›")),
            "System navigation uses supported semantic Icon nodes instead of Unicode text glyphs");
    }

    private static void TestGoldenVectors()
    {
        var protocol = new ProtocolV1Codec();
        var codec = new DisplayLinkCodec(protocol);
        var getInfo = codec.CreateCommand(
            0x1234, DisplayLinkOpcode.GetInfo, 0);
        Assert(getInfo[0] == 0x53 && getInfo[1] == 0x44 &&
               getInfo[2] == 1 && getInfo[3] == 0x20 &&
               getInfo[4] == 0x34 && getInfo[5] == 0x12 &&
               getInfo[6] == 8 && getInfo[8] == 1 &&
               getInfo[9] == 0 && getInfo[10] == 1 &&
               getInfo[11] == 1, "GetInfo golden vector");

        var node = new DisplayNode(
            2, 1, DisplayNodeKind.Rectangle,
            new DisplayBounds(10, 20, 30, 40), 5,
            FillColor: 0x1234, CornerRadius: 7);
        var encoded = DisplayLinkCommandEncoder.DefineNode(node);
        Assert(encoded.Length == 34 && encoded[0] == 2 &&
               encoded[2] == 1 && encoded[4] == 1 &&
               encoded[6] == 5 && encoded[8] == 10 &&
               encoded[10] == 20 && encoded[12] == 30 &&
               encoded[14] == 40 && encoded[16] == 0x34 &&
               encoded[17] == 0x12 && encoded[21] == 7,
            "DefineNode shared golden vector");

        var text = new string('á', 48);
        var fragments =
            DisplayLinkCommandEncoder.StringFragments(8, text).ToArray();
        Assert(fragments.Length >= 3 &&
               fragments.All(fragment => fragment.Length <= 48) &&
               fragments.Select(fragment => fragment.AsSpan(6).ToArray())
                   .SelectMany(value => value)
                   .SequenceEqual(System.Text.Encoding.UTF8.GetBytes(text)),
            "UTF-8 fragments preserve code points");
    }

    private static void TestGraphCompilers()
    {
        var runtime = Runtime(DashboardPresets.ControlInfo.Id, 17);
        foreach (var preset in DashboardPresets.All)
        {
            var configuration = (runtime.Configuration with
            {
                PresetId = preset.Id,
                FunctionalPreset = DashboardPresets.FunctionalFrom(preset.Id),
                Widgets = preset.DefaultWidgets(),
            }).WithWidget(DashboardWidgetKind.Mascot, false)
              .WithWidget(DashboardWidgetKind.NowPlaying, false);
            var graph = DashboardDisplayGraphCompiler.Compile(
                runtime with { Configuration = configuration }, 0);
            Assert(graph.Nodes.Count <= 128 &&
                   graph.Nodes.Select(node => node.Id).Distinct().Count() ==
                   graph.Nodes.Count,
                $"Dashboard compiler {preset.Name}");
            var layout = DashboardLayoutEngine.Build(configuration);
            Assert(layout.All(slot => graph.Nodes.Any(node =>
                       node.Id == (ushort)(1000 + (int)slot.Kind * 100))),
                $"DisplayGraph {preset.Name} includes the same structural slots as preview layout");
            var matrixRoot = (ushort)(1000 + (int)DashboardWidgetKind.ButtonMatrix * 100);
            var buttonNodes = graph.Nodes.Where(node => node.ParentId == matrixRoot &&
                    node.Kind == DisplayNodeKind.Icon && node.Id >= matrixRoot + 2)
                .OrderBy(node => node.Id).ToArray();
            var buttonTouches = graph.TouchRegions.Where(region =>
                    region.Id is >= 100 and < 109).OrderBy(region => region.Id).ToArray();
            var navigationTouches = graph.TouchRegions.Where(region =>
                    region.Id is >= 109 and <= 111).OrderBy(region => region.Id).ToArray();
            var matrixSlot = layout.Single(slot =>
                slot.Kind == DashboardWidgetKind.ButtonMatrix).Bounds;
            Assert(buttonNodes.Length == 9 && buttonTouches.Length == 9 &&
                   navigationTouches.Select(region => region.ActionParameter)
                       .SequenceEqual(new uint[] { 9, 10, 11 }),
                $"DisplayGraph {preset.Name} contains 9 user controls and system navigation");
            for (var index = 0; index < 9; index++)
            {
                var expectedColumn = index % ButtonMatrixLayout.Columns;
                var expectedRow = index / ButtonMatrixLayout.Columns;
                var expected = ButtonMatrixLayout.Cell(matrixSlot, index);
                var touch = buttonTouches[index];
                var visual = buttonNodes[index];
                Assert(touch.ActionParameter == (uint)index &&
                       touch.Bounds.X == (short)expected.X &&
                       touch.Bounds.Y == (short)expected.Y &&
                       touch.Bounds.Width == (short)(expected.X + expected.Width - touch.Bounds.X) &&
                       touch.Bounds.Height == (short)(expected.Y + expected.Height - touch.Bounds.Y) &&
                        visual.Bounds.X >= touch.Bounds.X - (short)matrixSlot.X &&
                        visual.Bounds.Y >= touch.Bounds.Y - (short)matrixSlot.Y &&
                        visual.Bounds.X + visual.Bounds.Width <=
                            touch.Bounds.X - (short)matrixSlot.X + touch.Bounds.Width &&
                        visual.Bounds.Y + visual.Bounds.Height <=
                            touch.Bounds.Y - (short)matrixSlot.Y + touch.Bounds.Height &&
                       (touch.Bounds.X > buttonTouches[0].Bounds.X) == (expectedColumn > 0) &&
                       (touch.Bounds.Y > buttonTouches[0].Bounds.Y) == (expectedRow > 0),
                    $"DisplayGraph {preset.Name} button {index} uses 3x3 row-major geometry");
            }
            Assert(graph.Nodes.Count(node => node.ParentId == matrixRoot &&
                       node.Kind == DisplayNodeKind.Rectangle && node.Id >= matrixRoot + 2) == 0,
                $"Hikari {preset.Name} separates hit areas from visual card weight");
        }

        var scene = new DisplayScene("advanced",
        [
            new DisplayTextElement("title", 1, 2, 100, 20, "Hola", 14),
            new DisplayRectangleElement("box", 4, 5, 80, 40, "#FF0000"),
            new DisplayImageElement("art", 9, 10, 30, 30, "asset"),
        ]);
        var advanced = DisplaySceneGraphAdapter.Compile(scene, 0);
        Assert(advanced.Mode == DisplayMode.Scene &&
               advanced.Nodes.Any(node => node.Kind == DisplayNodeKind.Text) &&
               advanced.Nodes.Any(node => node.Kind == DisplayNodeKind.Image),
            "Advanced DisplayScene converges on DisplayGraph");

        var snake = SnakeOverlayDemo.Create(0);
        Assert(snake.Nodes.Count(node => node.Kind == DisplayNodeKind.Circle) == 6 &&
               snake.Animations.Single().TargetNodeId == 500 &&
               snake.TouchRegions.Single().Mode == DisplayTouchMode.PassThrough,
            "Snake uses generic nodes, animation and pass-through touch");
    }

    private static void TestVisualParityContract()
    {
        Assert(DisplayGraphVisualContract.Width == 480 &&
               DisplayGraphVisualContract.Height == 320,
            "Visual parity canvas is exactly 480 x 320");
        Assert(DisplayGraphVisualContract.FontScale(7) == 1 &&
               DisplayGraphVisualContract.FontScale(8) == 2 &&
               DisplayGraphVisualContract.FontPixelHeight(14) == 14 &&
               DisplayGraphVisualContract.GlyphAdvance(14) == 12,
            "Shared bitmap text metrics match firmware tiers");
        var fitted = DisplayGraphVisualContract.FitSingleLine(
            "Cambiar salida de audio predeterminada", 84, 7);
        Assert(fitted == "CAMBIAR SAL..." &&
               DisplayGraphVisualContract.FitSingleLine("áéíóú", 80, 7) == "AEIOU",
            "Long and accented text follows deterministic firmware normalization and ellipsis");
        foreach (var color in new[] { "#FF0000", "#00FF00", "#0000FF", "#A974FF" })
        {
            var encoded = Rgb565.FromHex(color);
            var decoded = Rgb565.ToHex(encoded);
            Assert(decoded.Length == 7 && decoded[0] == '#',
                $"RGB565 color {color} has a deterministic preview conversion");
        }

        Assert(DisplayParityGoldenScenes.All.Count == 9 &&
               DisplayParityGoldenScenes.All.Take(7).Select(scene => scene.Id).SequenceEqual(
                   Enumerable.Range(1, 7).Select(index => $"PARITY-{index:00}")) &&
               DisplayParityGoldenScenes.All[7].Id == "ASSET-PARITY-01" &&
               DisplayParityGoldenScenes.All[8].Id == "COMPANION-HERO-01",
            "Seven primitive scenes plus asset and Companion hero parity scenes are available");
        var kinds = DisplayParityGoldenScenes.All.SelectMany(scene => scene.Graph.Nodes)
            .Select(node => node.Kind).ToHashSet();
        Assert(new[] { DisplayNodeKind.Rectangle, DisplayNodeKind.Circle,
                   DisplayNodeKind.Text, DisplayNodeKind.Icon, DisplayNodeKind.Image,
                   DisplayNodeKind.Progress, DisplayNodeKind.Line }
               .All(kinds.Contains),
            "Golden scenes cover every drawable DisplayGraph primitive");
        Assert(DisplayParityGoldenScenes.All.All(scene =>
                   scene.Graph.Nodes.Count <= 128 &&
                   scene.Graph.Nodes.Select(node => node.Id).Distinct().Count() ==
                   scene.Graph.Nodes.Count),
            "Golden scenes fit firmware limits and keep stable unique NodeIds");
        var assetParity=DisplayParityGoldenScenes.All.Single(scene=>scene.Id=="ASSET-PARITY-01").Graph;
        Assert(assetParity.Nodes.Count(node=>node.Kind==DisplayNodeKind.Icon)>=12 &&
               assetParity.Assets?.Select(asset=>asset.Id).Order().SequenceEqual(
                   new ushort[]{0x0101,DisplayAssetIds.ActiveCompanion}.Order())==true &&
               assetParity.Nodes.Where(node=>node.Kind==DisplayNodeKind.Image)
                   .Select(node=>node.AssetId).Order().SequenceEqual(
                       assetParity.Assets!.Select(asset=>asset.Id).Order()),
            "ASSET-PARITY-01 covers nine content icons, navigation, artwork and Companion");

        var companionHero=DisplayParityGoldenScenes.All.Single(
            scene=>scene.Id=="COMPANION-HERO-01").Graph;
        var companionImage=companionHero.Nodes.Single(node=>
            node.Kind==DisplayNodeKind.Image&&
            node.AssetId==DisplayAssetIds.ActiveCompanion);
        Assert(companionImage.Bounds.Width>=150&&companionImage.Bounds.Height>=150&&
               companionHero.Nodes.Count(node=>node.Kind==DisplayNodeKind.Icon)>=12&&
               companionHero.Assets?.Single(asset=>
                   asset.Id==DisplayAssetIds.ActiveCompanion) is
                   {Width:96,Height:96,ByteLength:18432}&&
               companionHero.Assets.Any(asset=>asset.Id==0x0101),
            "COMPANION-HERO-01 covers a large hero, artwork, 3x3, stats and navigation");
        var runtime = Runtime(DashboardPresets.Media.Id, 37) with
        {
            Buttons = Enumerable.Range(0, 9).Select(index => new DashboardControlState(
                new ControlId(ControlType.Button, (byte)index),
                index == 0 ? "Cambiar Audio" : "Sin asignar",
                index == 0 ? "\uE767" : "\uE80A",
                new DeviceAction(index == 0 ? ActionType.HostAction : ActionType.None))).ToArray(),
        };
        foreach (var preset in DashboardPresets.All)
        {
            var graph = DashboardDisplayGraphCompiler.Compile(runtime with
            {
                Configuration = runtime.Configuration with { PresetId = preset.Id },
            }, 7);
            Assert(graph.Nodes.Any(node => node.Kind == DisplayNodeKind.Icon &&
                                          node.IconId == DashboardIconCatalog.Volume) &&
                   graph.Nodes.Where(node => node.Kind == DisplayNodeKind.Icon)
                       .All(node => node.IconId is >= 1 and <= 24),
                $"{preset.Name} emits firmware-supported semantic icons");
            Assert(graph.TouchRegions.Where(region => region.Id is >= 100 and < 109)
                       .Select(region => region.Bounds).Distinct().Count() == 9 &&
                   graph.TouchRegions.Count(region => region.Id is >= 109 and <= 111) == 3,
                $"{preset.Name} preview and device share nine user and three navigation hit regions");
        }
    }

    private static void TestVisualSystemArchitecture()
    {
        Assert(VisualSystemGoldenScenes.All.Select(scene => scene.Id).SequenceEqual(new[]
               { "HIKARI-CONTROL", "NEON-CONTROL", "HIKARI-COMPANION", "NEON-COMPANION" }),
            "Visual System exposes all four identity and functional reference combinations");
        var hikari = VisualSystemGoldenScenes.All[0].Graph;
        var neonControl = VisualSystemGoldenScenes.All[1].Graph;
        var hikariCompanion = VisualSystemGoldenScenes.All[2].Graph;
        var neon = VisualSystemGoldenScenes.All[3].Graph;
        var graphs = VisualSystemGoldenScenes.All.Select(scene => scene.Graph).ToArray();
        Assert(graphs.All(graph => graph.Nodes.Count <= 128 &&
                   graph.Nodes.All(node => node.Bounds.Width >= 0 && node.Bounds.Height >= 0) &&
                   graph.Nodes.Select(node => node.Id).Distinct().Count() == graph.Nodes.Count &&
                   graph.Nodes.Where(node => node.Kind == DisplayNodeKind.Icon)
                       .All(node => node.IconId is >= 1 and <= 24)),
            "All visual combinations fit the firmware pool and supported assets");
        var matrixRoot = (ushort)(1000 + (int)DashboardWidgetKind.ButtonMatrix * 100);
        Assert(hikari.Nodes.Count(node => node.ParentId == matrixRoot &&
                   node.Kind == DisplayNodeKind.Rectangle && node.Id >= matrixRoot + 2) == 0 &&
               hikari.Nodes.Count(node => node.ParentId == matrixRoot &&
                   node.Kind == DisplayNodeKind.Line) == 4 &&
               neonControl.Nodes.Count(node => node.ParentId == matrixRoot &&
                   node.Kind == DisplayNodeKind.Rectangle && node.Id >= matrixRoot + 2) == 9,
            "Hikari uses one lightweight action surface while Neon retains denser expressive tiles");
        var hikariMatrix = hikari.Nodes.Single(node => node.Id == matrixRoot).Bounds;
        var neonMatrix = neonControl.Nodes.Single(node => node.Id == matrixRoot).Bounds;
        var hikariCompanionMatrix = hikariCompanion.Nodes.Single(node => node.Id == matrixRoot).Bounds;
        var neonCompanionMatrix = neon.Nodes.Single(node => node.Id == matrixRoot).Bounds;
        Assert(hikariMatrix.X > 150 && neonMatrix.X < 30 &&
               hikariCompanionMatrix.X > 300 && neonCompanionMatrix.X < 30,
            "Identity changes composition geometry independently from the functional preset");
        var hikariEncoderRoot = (ushort)(1000 + (int)DashboardWidgetKind.Encoder * 100);
        Assert(graphs.All(graph => !graph.Nodes.Any(node => node.Id == hikariEncoderRoot) &&
                   !graph.TouchRegions.Any(region => region.Id is >= 120 and <= 122)) &&
               hikariMatrix.Width * hikariMatrix.Height >
                   DashboardCompositionResolver.Compose(DashboardConfiguration.CreateDefault(1))
                       .Where(slot => slot.Kind is DashboardWidgetKind.NowPlaying or
                           DashboardWidgetKind.SystemStats)
                       .Sum(slot => slot.Bounds.Width * slot.Bounds.Height),
            "Every preset removes encoder UI while Control keeps the 3x3 dominant");
        Assert(graphs.All(graph => graph.TouchRegions.Where(region => region.Id is >= 100 and <= 111)
                   .Select(region => region.ActionParameter).Order()
                   .SequenceEqual(Enumerable.Range(0, 12).Select(value => (uint)value))),
            "Every composition preserves button 0-8 and Previous Home Next 9-11 semantics");
        Assert(hikari.Nodes.Concat(neon.Nodes).Where(node => node.Kind == DisplayNodeKind.Text)
                   .All(node => !(node.Text ?? string.Empty).Contains("...", StringComparison.Ordinal)) &&
               new[] { DashboardIconCatalog.Volume, DashboardIconCatalog.Message,
                       DashboardIconCatalog.Folder, DashboardIconCatalog.Music,
                       DashboardIconCatalog.Tools }
                   .All(icon => hikari.Nodes.Any(node => node.Kind == DisplayNodeKind.Icon &&
                                                        node.IconId == icon)),
            "Golden compositions use deliberate short labels and semantic iconography");

        var source = Runtime(DashboardPresets.ControlInfo.Id, 42);
        var originalButtons = source.Buttons.ToArray();
        var hikariConfig = source.Configuration with
        {
            VisualIdentity = VisualIdentity.Hikari,
            FunctionalPreset = FunctionalPreset.Control,
            PresetId = DashboardPresets.ControlInfo.Id,
        };
        var neonConfig = source.Configuration with
        {
            VisualIdentity = VisualIdentity.Neon,
            FunctionalPreset = FunctionalPreset.Companion,
            PresetId = DashboardPresets.Companion.Id,
            Widgets = DashboardPresets.Companion.DefaultWidgets(),
        };
        var first = DashboardDisplayGraphCompiler.Compile(source with
        {
            Configuration = hikariConfig,
        }, 5);
        var second = DashboardDisplayGraphCompiler.Compile(source with
        {
            Configuration = neonConfig,
            Companion = null,
        }, 5);
        var repeated = DashboardDisplayGraphCompiler.Compile(source with
        {
            Configuration = neonConfig,
            Companion = null,
        }, 5);
        Assert(second.Nodes.SequenceEqual(repeated.Nodes) &&
               second.TouchRegions.SequenceEqual(repeated.TouchRegions),
            "Visual compiler is deterministic and Companion has a safe fallback");
        Assert(source.Buttons.SequenceEqual(originalButtons) &&
               first.TouchRegions.Where(region => region.Id is >= 100 and <= 111)
                   .Select(region => region.ActionParameter).Order().SequenceEqual(
                       second.TouchRegions.Where(region => region.Id is >= 100 and <= 111)
                           .Select(region => region.ActionParameter).Order()),
            "Changing identity or preset preserves button and system navigation IDs");
        Assert(hikari.Nodes.All(node => node.Bounds.X >= 0 && node.Bounds.Y >= 0) &&
               neon.Nodes.All(node => node.Bounds.X >= 0 && node.Bounds.Y >= 0) &&
               DisplayGraphVisualContract.Width == 480 && DisplayGraphVisualContract.Height == 320,
            "Golden visual combinations share the common 480 x 320 DisplayGraph path");
    }

    private static void TestLocalControlParameters()
    {
        for (byte index = 0; index < 9; index++)
        {
            var expected = new ControlId(ControlType.Button, index);
            Assert(DisplayLocalControlParameter.Encode(expected) == index &&
                   DisplayLocalControlParameter.TryDecode(index, out var decoded) &&
                   decoded == expected,
                $"Touch parameter {index} maps to button {index}");
        }
        Assert(!DisplayLocalControlParameter.TryDecode(9, out _) &&
               !DisplayLocalControlParameter.TryDecode(10, out _) &&
               !DisplayLocalControlParameter.TryDecode(11, out _),
            "System navigation parameters are never decoded as user bindings");
        var encoders = new[]
        {
            new ControlId(ControlType.EncoderCounterClockwise, 0),
            new ControlId(ControlType.EncoderPress, 0),
            new ControlId(ControlType.EncoderClockwise, 0),
        };
        for (var index = 0; index < encoders.Length; index++)
        {
            var parameter = (uint)(12 + index);
            Assert(DisplayLocalControlParameter.Encode(encoders[index]) == parameter &&
                   DisplayLocalControlParameter.TryDecode(parameter, out var decoded) &&
                   decoded == encoders[index],
                $"Touch parameter {parameter} maps to encoder control");
        }
        Assert(!DisplayLocalControlParameter.TryDecode(15, out _),
            "Unknown touch parameter is rejected");
    }

    private static async Task TestInitialLifecycleAndStartupCoalescingAsync()
    {
        var channel = new FakeChannel { HoldGetInfo = true };
        await using var sync = new DisplaySyncService(channel);
        sync.QueueGraph(Graph(17), "1:system");
        sync.QueueGraph(Graph(19), "1:system");
        sync.QueueGraph(Graph(22), "1:system");
        await channel.GetInfoEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert(channel.Commands.SequenceEqual([DisplayLinkOpcode.GetInfo]) &&
               !channel.Commands.Contains(DisplayLinkOpcode.BeginUpdate),
            "Updates before GET_INFO are coalesced and never patched");
        channel.ReleaseGetInfo.TrySetResult();
        await sync.FlushAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert(sync.Lifecycle == DisplayLinkLifecycleState.Ready &&
               sync.State.Protocol == "1.0" &&
               sync.ActiveGeneration != 0 &&
               channel.Commands.Count(opcode => opcode == DisplayLinkOpcode.BeginSync) == 1 &&
               !channel.Commands.Contains(DisplayLinkOpcode.BeginUpdate),
            "Initial handshake performs one full sync before Ready");
    }

    private static async Task TestUpdatesCoalesceDuringFullSyncAsync()
    {
        var channel = new FakeChannel { HoldCommit = true };
        await using var sync = new DisplaySyncService(channel);
        sync.QueueGraph(Graph(10), "1:system");
        await channel.CommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        sync.QueueGraph(Graph(20), "1:system");
        sync.QueueGraph(Graph(30), "1:system");
        sync.QueueGraph(Graph(40), "1:system");

        Assert(sync.Lifecycle == DisplayLinkLifecycleState.Synchronizing &&
               !channel.Commands.Contains(DisplayLinkOpcode.BeginUpdate),
            "Stats during full sync cannot start BeginUpdate");
        channel.ReleaseCommit.TrySetResult();
        await sync.FlushAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert(channel.Commands.Count(
                   opcode => opcode == DisplayLinkOpcode.BeginUpdate) == 1 &&
               sync.Lifecycle == DisplayLinkLifecycleState.Ready,
            "Only the latest startup update is patched after CommitSync");
    }

    private static async Task TestProfileSwitchOwnsNewGenerationAsync()
    {
        var channel = new FakeChannel();
        await using var sync = new DisplaySyncService(channel);
        await sync.SynchronizeAsync(Graph(10), "1:system");
        var first = sync.ActiveGeneration;
        await sync.SynchronizeAsync(Graph(20), "2:system");

        Assert(sync.ActiveGeneration == first + 1 &&
               channel.Commands.Count(
                   opcode => opcode == DisplayLinkOpcode.BeginSync) == 2 &&
               channel.BeginSyncGenerations.SequenceEqual([first, first + 1]),
            "Profile switch blocks patches and advances service-owned generation");
    }

    private static async Task TestBootSessionRecoveryAsync()
    {
        var channel = new FakeChannel();
        await using var sync = new DisplaySyncService(channel);
        await sync.SynchronizeAsync(Graph(10), "1:system");
        var originalGeneration = sync.ActiveGeneration;
        channel.BootSessionId++;
        channel.ActiveGeneration = 0;
        channel.RejectNextBeginUpdate = true;

        await sync.SynchronizeAsync(Graph(11), "1:system");
        Assert(sync.Lifecycle == DisplayLinkLifecycleState.Ready &&
               sync.ActiveGeneration > originalGeneration &&
               channel.Commands.Count(
                   opcode => opcode == DisplayLinkOpcode.GetInfo) == 2 &&
               channel.Commands.Count(
                   opcode => opcode == DisplayLinkOpcode.BeginSync) == 2,
            "InvalidGeneration triggers one controlled handshake and full resync");
    }

    private static async Task TestPhysicalReconnectLifecycleAsync()
    {
        var channel = new FakeChannel();
        await using var sync = new DisplaySyncService(channel);
        await sync.SynchronizeAsync(Graph(10), "1:system");
        var originalGeneration = sync.ActiveGeneration;
        channel.IsConnected = false;
        channel.ActiveGeneration = 0;
        channel.BootSessionId++;

        Assert(await sync.ReconnectAsync() &&
               sync.Lifecycle == DisplayLinkLifecycleState.Ready &&
               sync.ActiveGeneration > originalGeneration &&
               channel.Commands.Count(
                   opcode => opcode == DisplayLinkOpcode.GetInfo) == 2,
            "Physical reconnect handshakes and full-syncs before Ready");
    }

    private static async Task TestDisconnectBoundariesAsync()
    {
        var channel = new FakeChannel { ReconnectAvailable = false };
        await using var sync = new DisplaySyncService(
            channel, TimeSpan.FromMilliseconds(20));
        await sync.SynchronizeAsync(Graph(10), "1:system");
        var commandsBeforeDisconnect = channel.Commands.Count;
        channel.Disconnect(new IOException("device removed"));
        sync.QueueGraph(Graph(25), "1:system");
        await sync.FlushAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var oneWayAfterDisconnect = channel.OneWay.Count;
        await Task.Delay(70);
        Assert(sync.Lifecycle == DisplayLinkLifecycleState.Disconnected &&
               channel.Commands.Count == commandsBeforeDisconnect &&
               channel.OneWay.Count == oneWayAfterDisconnect,
            "Disconnect while Ready stops heartbeat, patches and device requests");

        channel.ActiveGeneration = 0;
        channel.BootSessionId++;
        channel.ReconnectAvailable = true;
        Assert(await sync.ReconnectAsync() &&
               sync.Lifecycle == DisplayLinkLifecycleState.Ready &&
               channel.ConnectionSubscriberCount == 1,
            "Reconnect performs one handshake/full sync without duplicate subscriptions");

        foreach (var failingOpcode in new[]
                 {
                     DisplayLinkOpcode.DefineNode,
                     DisplayLinkOpcode.BeginUpdate,
                     DisplayLinkOpcode.AssetBegin,
                     DisplayLinkOpcode.AssetChunk,
                 })
        {
            var failing = new FakeChannel
            {
                DisconnectAtOpcode = failingOpcode,
                ReconnectAvailable = false,
            };
            await using var service = new DisplaySyncService(failing);
            if (failingOpcode == DisplayLinkOpcode.BeginUpdate)
            {
                await service.SynchronizeAsync(Graph(10), "1:system");
                failing.DisconnectAtOpcode = DisplayLinkOpcode.BeginUpdate;
                await service.SynchronizeAsync(Graph(20), "1:system");
            }
            else if (failingOpcode is DisplayLinkOpcode.AssetBegin or
                     DisplayLinkOpcode.AssetChunk)
            {
                await service.SynchronizeAsync(Graph(10), "1:system");
                if (failingOpcode == DisplayLinkOpcode.AssetBegin)
                    failing.DisconnectAtOpcode = failingOpcode;
                else
                    failing.DisconnectAtOneWayOpcode = failingOpcode;
                try
                {
                    await service.TransferAssetAsync(
                        1, 1, 1, 1, new byte[] { 1, 2 });
                }
                catch (IOException) { }
            }
            else
            {
                await service.SynchronizeAsync(Graph(10), "1:system");
            }
            Assert(service.Lifecycle == DisplayLinkLifecycleState.Disconnected,
                $"Disconnect during {failingOpcode} remains Disconnected");
        }
    }

    private static async Task TestUnsupportedAndHandshakeFailureAsync()
    {
        await using (var unsupported =
                     new DisplaySyncService(new UnsupportedChannel()))
        {
            unsupported.QueueGraph(Graph(1), "1:system");
            await unsupported.FlushAsync().WaitAsync(TimeSpan.FromSeconds(1));
            Assert(unsupported.Lifecycle ==
                       DisplayLinkLifecycleState.Unsupported,
                "Unsupported firmware is identified");
        }

        var timeoutChannel = new FakeChannel { FailGetInfo = true };
        await using var timeout = new DisplaySyncService(timeoutChannel);
        timeout.QueueGraph(Graph(1), "1:system");
        await timeout.FlushAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert(timeout.Lifecycle == DisplayLinkLifecycleState.Faulted &&
               !timeoutChannel.Commands.Contains(DisplayLinkOpcode.BeginSync) &&
               !timeoutChannel.Commands.Contains(DisplayLinkOpcode.BeginUpdate),
            "GET_INFO failure cannot start sync or patches");
    }

    private static async Task TestCapabilitiesRemainVisibleAfterFaultAsync()
    {
        var channel = new FakeChannel();
        await using var sync = new DisplaySyncService(channel);
        await sync.SynchronizeAsync(Graph(10), "1:system");
        channel.FailNextBeginUpdate = true;
        await sync.SynchronizeAsync(Graph(11), "1:system");

        Assert(sync.Lifecycle == DisplayLinkLifecycleState.Faulted &&
               sync.State.Available && sync.State.Protocol == "1.0" &&
               sync.State.Backend == "Null" &&
               sync.State.Resolution == "480×320",
            "A sync fault does not erase decoded GET_INFO capabilities");
    }

    private static async Task TestAssetTransferAndTouchRoutingAsync()
    {
        var channel = new FakeChannel();
        await using var sync = new DisplaySyncService(channel);
        await sync.SynchronizeAsync(Graph(10), "1:system");
        await sync.TransferAssetAsync(
            1, 2, 2, 2, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        Assert(channel.Commands.Contains(DisplayLinkOpcode.AssetBegin) &&
               channel.OneWay.Contains(DisplayLinkOpcode.AssetChunk) &&
               channel.Commands.Contains(DisplayLinkOpcode.AssetCommit),
            "Asset transfer uses begin, one-way chunks and commit");
        var beginCount = channel.Commands.Count(command => command == DisplayLinkOpcode.AssetBegin);
        await sync.TransferAssetAsync(
            2, 2, 2, 2, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        Assert(channel.Commands.Count(command => command == DisplayLinkOpcode.AssetBegin) == beginCount,
            "identical asset content is transferred only once per connection session");
        await sync.TransferAssetAsync(2, 2, 2, 2, new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 });
        Assert(channel.Commands.Count(command => command == DisplayLinkOpcode.AssetBegin) == beginCount + 1,
            "same Companion asset ID with new mood content replaces the resident sprite");
        await sync.ReleaseAssetAsync(2);
        Assert(channel.Commands.Contains(DisplayLinkOpcode.AssetRelease),
            "Companion asset unload uses the existing AssetRelease lifecycle");
        await sync.TransferAssetAsync(2, 2, 2, 2, new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 });
        Assert(channel.Commands.Count(command => command == DisplayLinkOpcode.AssetBegin) == beginCount + 2,
            "released content may be transferred again exactly once");
        beginCount += 2;
        channel.Disconnect(new IOException("test reconnect"));
        await sync.ReconnectAsync();
        await sync.TransferAssetAsync(
            3, 2, 2, 2, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        Assert(channel.Commands.Count(command => command == DisplayLinkOpcode.AssetBegin) == beginCount + 1,
            "reconnect clears the session cache and permits exactly one asset retransmission");

        var received = 0;
        sync.TouchReceived += (_, _) => received++;
        channel.EmitTouch(new(
            sync.ActiveGeneration, 1, 100, 1, 10, 20, 30));
        channel.EmitTouch(new(
            sync.ActiveGeneration, 1, 100, 1, 10, 20, 30));
        channel.EmitTouch(new(
            sync.ActiveGeneration - 1, 2, 100, 1, 10, 20, 30));
        channel.EmitTouch(new(
            sync.ActiveGeneration, 2, 999, 1, 10, 20, 30));
        var current = new DisplayTouchEvent(
            sync.ActiveGeneration, 3, 100, 1, 10, 20, 30);
        Assert(received == 1 &&
               sync.TryResolveTouchRegion(current, out var region) &&
               region.ActionKind == DisplayTouchActionKind.LocalControl &&
               DisplayLocalControlParameter.TryDecode(
                   region.ActionParameter, out var control) &&
               control == new ControlId(ControlType.Button, 0),
            "Active touch region resolves to its local control");
        Assert(!sync.TryResolveTouchRegion(
                   current with { Generation = sync.ActiveGeneration - 1 }, out _),
            "Stale touch generation cannot resolve an active region");
    }

    private static async Task TestAssetCommitOrderingAndStabilityAsync()
    {
        var channel=new FakeChannel();
        await using var sync=new DisplaySyncService(channel);
        var artworkA=(ushort)0x0101;
        var companion=DisplayAssetIds.ActiveCompanion;
        var graph=AssetGraph(artworkA,companion);
        var stableUploads=new[]
        {
            new DisplayAssetUpload(artworkA,artworkA,2,2,new byte[]{1,2,3,4,5,6,7,8}),
            new DisplayAssetUpload(companion,companion,2,2,new byte[]{9,10,11,12,13,14,15,16}),
        };
        for(var second=0;second<60;second++)
            Assert(await sync.CommitGraphWithAssetsAsync(
                graph,"asset-parity",stableUploads,[]),
                $"60-second logical stability sample {second} commits");

        Assert(channel.Commands.Count(op=>op==DisplayLinkOpcode.AssetBegin)==2 &&
               channel.Commands.Count(op=>op==DisplayLinkOpcode.AssetCommit)==2,
            "Sixty idle clock/stats/media refreshes perform zero redundant asset reloads");
        var firstCommit=channel.Traffic.IndexOf(DisplayLinkOpcode.AssetCommit);
        var firstGraph=channel.Traffic.IndexOf(DisplayLinkOpcode.BeginSync);
        Assert(firstCommit>=0&&firstGraph>firstCommit,
            "Assets become READY before the first graph references them");

        for(var mood=1;mood<5;mood++)
        {
            var pixels=Enumerable.Range(0,8).Select(value=>(byte)(value+20+mood)).ToArray();
            Assert(await sync.CommitGraphWithAssetsAsync(graph,"asset-parity",
                [new(companion,companion,2,2,pixels)],[]),
                $"Companion mood {mood} replaces the stable slot");
        }
        Assert(channel.Commands.Count(op=>op==DisplayLinkOpcode.AssetBegin)==6,
            "Five Companion moods produce exactly five uploads in one stable asset slot");

        ushort previous=artworkA;
        foreach(var next in new ushort[]{0x0102,0x0103})
        {
            var nextGraph=AssetGraph(next,companion);
            var mark=channel.Traffic.Count;
            Assert(await sync.CommitGraphWithAssetsAsync(nextGraph,"asset-parity",
                [new(next,next,2,2,new byte[]{31,32,33,34,35,36,37,(byte)next})],
                [previous]),"Artwork replacement commits");
            var traffic=channel.Traffic.Skip(mark).ToArray();
            var uploadCommit=Array.IndexOf(traffic,DisplayLinkOpcode.AssetCommit);
            var graphCommit=Array.FindIndex(traffic,op=>
                op is DisplayLinkOpcode.CommitUpdate or DisplayLinkOpcode.CommitSync);
            var release=Array.IndexOf(traffic,DisplayLinkOpcode.AssetRelease);
            Assert(uploadCommit>=0&&graphCommit>uploadCommit&&release>graphCommit,
                "Artwork transition orders READY, graph commit, then old-asset eviction");
            previous=next;
            graph=nextGraph;
        }
        Assert(channel.Commands.Count(op=>op==DisplayLinkOpcode.AssetRelease)==2,
            "Artwork A to B to C evicts exactly the two obsolete content IDs");

        var beginsBeforeReconnect=channel.Commands.Count(op=>op==DisplayLinkOpcode.AssetBegin);
        channel.IsConnected=false;
        channel.ActiveGeneration=0;
        channel.BootSessionId++;
        Assert(await sync.ReconnectAsync()&&
               sync.Lifecycle==DisplayLinkLifecycleState.Synchronizing,
            "Asset graph reconnect waits for resource reconstruction");
        channel.IsConnected=true;
        Assert(await sync.CommitGraphWithAssetsAsync(graph,"asset-parity",
            [
                new(previous,previous,2,2,new byte[]{31,32,33,34,35,36,37,(byte)previous}),
                new(companion,companion,2,2,new byte[]{24,25,26,27,28,29,30,31}),
            ],[])&&sync.Lifecycle==DisplayLinkLifecycleState.Ready &&
            channel.Commands.Count(op=>op==DisplayLinkOpcode.AssetBegin)==beginsBeforeReconnect+2,
            "Reconnect uploads both resident resources once and reconstructs the final scene");
    }

    private static async Task TestStableProgressPatchesAsync()
    {
        var first = Graph(20);
        var second = Graph(30);
        Assert(first.Nodes.Select(node => node.Id).SequenceEqual(
                   second.Nodes.Select(node => node.Id)) &&
               DisplayGraphDiffer.Diff(first, second).All(change =>
                   change.Kind is not DisplayGraphChangeKind.Define),
            "Dynamic progress keeps stable NodeIds and patches existing nodes");

        var channel = new FakeChannel();
        await using var sync = new DisplaySyncService(channel);
        await sync.SynchronizeAsync(first, "1:system");
        await sync.SynchronizeAsync(second, "1:system");
        Assert(channel.PatchBodies.Any(body => body.Skip(2).Chunk(4)
                   .Any(tlv => tlv.Length > 0 && tlv[0] == 13)),
            "A valid progress change is encoded as PATCH_NODE ProgressValue");

        channel.PatchBodies.Clear();
        var unavailable = second with
        {
            Nodes = second.Nodes.Select(node => node.Kind == DisplayNodeKind.Progress
                ? node with { ProgressValue = 0, ProgressAvailable = false }
                : node).ToArray(),
        };
        await sync.SynchronizeAsync(unavailable, "1:system");
        Assert(!channel.PatchBodies.Any(body => body.Skip(2).Chunk(4)
                   .Any(tlv => tlv.Length > 0 && tlv[0] == 13)),
            "Unavailable dynamic values do not patch progress to zero");

        var mediaA = Runtime(DashboardPresets.Media.Id, 20) with
        {
            NowPlaying = new(true, true, "A", "Artist", "Album", .1, "Player",
                ArtworkKey: "00000004-12345678"),
        };
        var mediaB = mediaA with
        {
            NowPlaying = mediaA.NowPlaying with { Title = "B", Progress = .2 },
        };
        var mediaC = mediaB with
        {
            NowPlaying = mediaB.NowPlaying with { ArtworkKey = "00000004-87654321" },
        };
        var graphA = DashboardDisplayGraphCompiler.Compile(mediaA, 0);
        var graphB = DashboardDisplayGraphCompiler.Compile(mediaB, 0);
        var graphC = DashboardDisplayGraphCompiler.Compile(mediaC, 0);
        var imageA = graphA.Nodes.Single(node => node.Kind == DisplayNodeKind.Image);
        var imageB = graphB.Nodes.Single(node => node.Kind == DisplayNodeKind.Image);
        var imageC = graphC.Nodes.Single(node => node.Kind == DisplayNodeKind.Image);
        Assert(imageA.Id == imageB.Id && imageA.AssetId == imageB.AssetId &&
               !DisplayGraphDiffer.Diff(graphA, graphB).Any(change =>
                   change.Node?.Kind == DisplayNodeKind.Image),
            "metadata and progress patches preserve the image node and asset identity");
        Assert(imageC.Id == imageA.Id && imageC.AssetId != imageA.AssetId &&
               DisplayGraphDiffer.Diff(graphB, graphC).Count(change =>
                   change.Node?.Kind == DisplayNodeKind.Image) == 1,
            "new artwork patches the stable image node exactly once");
    }

    private static DisplayGraph AssetGraph(ushort artworkId,ushort companionId)=>new(
        0,DisplayMode.Dashboard,
        [
            new(1,0,DisplayNodeKind.Rectangle,new(0,0,480,320),FillColor:0),
            new(2,0,DisplayNodeKind.Image,new(10,10,64,64),AssetId:artworkId),
            new(3,0,DisplayNodeKind.Image,new(84,10,64,64),AssetId:companionId),
        ],[],[],
        [
            new(artworkId,2,2,0,8),
            new(companionId,2,2,0,8),
        ]);

    private static DisplayGraph Graph(int cpu)
    {
        var runtime = Runtime(DashboardPresets.System.Id, cpu);
        return DashboardDisplayGraphCompiler.Compile(runtime, 0);
    }

    private static DashboardRuntimeState Runtime(string preset, int cpu) => new(
        DashboardConfiguration.CreateDefault(1) with { PresetId = preset },
        "General", "14:38",
        new(true, true, "Tema", "Artista", "Álbum", .5, "Player"),
        new SystemStatsState
        {
            CpuUsagePercent = cpu,
            GpuUsagePercent = 13,
            RamUsedBytes = 8UL << 30,
            RamTotalBytes = 16UL << 30,
        },
        MascotVisualEngine.Frame(MascotState.Idle, 0, false), [], []);

    private static void Assert(bool value, string name)
    {
        if (!value) throw new InvalidOperationException($"Test failed: {name}");
    }

    private sealed class FakeChannel : IDisplayLinkChannel
    {
        private uint stagingGeneration;
        private EventHandler<DeviceConnectionChangedEventArgs>? connectionChanged;

        public event EventHandler<DisplayTouchEvent>? DisplayTouchReceived;
        public event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged
        {
            add { connectionChanged += value; ConnectionSubscriberCount++; }
            remove { connectionChanged -= value; ConnectionSubscriberCount--; }
        }
        public bool IsConnected { get; set; } = true;
        public bool ReconnectAvailable { get; set; } = true;
        public int ConnectionSubscriberCount { get; private set; }
        public int ReconnectCount { get; private set; }
        public List<DisplayLinkOpcode> Commands { get; } = [];
        public List<DisplayLinkOpcode> OneWay { get; } = [];
        public List<DisplayLinkOpcode> Traffic { get; } = [];
        public List<uint> BeginSyncGenerations { get; } = [];
        public List<byte[]> PatchBodies { get; } = [];
        public uint ActiveGeneration { get; set; }
        public uint BootSessionId { get; set; } = 99;
        public bool HoldGetInfo { get; init; }
        public bool HoldCommit { get; init; }
        public bool FailGetInfo { get; init; }
        public bool RejectNextBeginUpdate { get; set; }
        public bool FailNextBeginUpdate { get; set; }
        public DisplayLinkOpcode? DisconnectAtOpcode { get; set; }
        public DisplayLinkOpcode? DisconnectAtOneWayOpcode { get; set; }
        public TaskCompletionSource GetInfoEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseGetInfo { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CommitEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCommit { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> ReconnectDisplayLinkAsync(
            CancellationToken cancellationToken = default)
        {
            ReconnectCount++;
            IsConnected = ReconnectAvailable;
            if (IsConnected)
                connectionChanged?.Invoke(this, new(true));
            return Task.FromResult(IsConnected);
        }

        public async Task<DisplayLinkResponse> ExchangeDisplayLinkAsync(
            DisplayLinkOpcode opcode,
            uint generation,
            ReadOnlyMemory<byte> body = default,
            DisplayLinkFlags flags = DisplayLinkFlags.AckRequired,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(opcode);
            Traffic.Add(opcode);
            if (opcode == DisplayLinkOpcode.PatchNode)
                PatchBodies.Add(body.ToArray());
            if (DisconnectAtOpcode == opcode)
            {
                DisconnectAtOpcode = null;
                Disconnect(new IOException($"removed during {opcode}"));
                throw new IOException($"removed during {opcode}");
            }
            if (opcode == DisplayLinkOpcode.GetInfo)
            {
                GetInfoEntered.TrySetResult();
                if (FailGetInfo) throw new TimeoutException("GET_INFO timeout");
                if (HoldGetInfo)
                    await ReleaseGetInfo.Task.WaitAsync(cancellationToken);
            }
            if (opcode == DisplayLinkOpcode.BeginSync)
            {
                stagingGeneration = generation;
                BeginSyncGenerations.Add(generation);
            }
            if (opcode == DisplayLinkOpcode.CommitSync)
            {
                CommitEntered.TrySetResult();
                if (HoldCommit)
                    await ReleaseCommit.Task.WaitAsync(cancellationToken);
                ActiveGeneration = stagingGeneration;
            }
            if (opcode == DisplayLinkOpcode.BeginUpdate)
            {
                if (FailNextBeginUpdate)
                {
                    FailNextBeginUpdate = false;
                    throw new ProtocolException("simulated transport fault");
                }
                if (RejectNextBeginUpdate || generation != ActiveGeneration)
                {
                    RejectNextBeginUpdate = false;
                    throw new DisplayLinkException(
                        DisplayLinkError.InvalidGeneration,
                        "DisplayLink rejected BeginUpdate: InvalidGeneration.");
                }
            }

            var result = opcode switch
            {
                DisplayLinkOpcode.GetInfo => Info(),
                DisplayLinkOpcode.GetStatus => Status(),
                _ => [],
            };
            return new(new(1, 0, opcode, 0, generation), result);
        }

        public Task SendDisplayLinkOneWayAsync(
            DisplayLinkOpcode opcode,
            uint generation,
            ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken = default)
        {
            OneWay.Add(opcode);
            Traffic.Add(opcode);
            if (DisconnectAtOneWayOpcode == opcode)
            {
                DisconnectAtOneWayOpcode = null;
                var exception = new IOException($"removed during {opcode}");
                Disconnect(exception);
                return Task.FromException(exception);
            }
            return Task.CompletedTask;
        }

        public void EmitTouch(DisplayTouchEvent touch) =>
            DisplayTouchReceived?.Invoke(this, touch);

        public void Disconnect(Exception exception)
        {
            IsConnected = false;
            connectionChanged?.Invoke(this, new(false, exception));
        }

        public void DisconnectForBootloader()
        {
            IsConnected = false;
            connectionChanged?.Invoke(this, new(
                false, null, DeviceDisconnectReason.BootloaderRequested));
        }

        private byte[] Info()
        {
            var body = new byte[42];
            DisplayLinkCodec.WriteU16(body, 0, 480);
            DisplayLinkCodec.WriteU16(body, 2, 320);
            body[4] = 1;
            body[5] = 0;
            DisplayLinkCodec.WriteU32(body, 8, 0xFF);
            DisplayLinkCodec.WriteU32(body, 12, 0x3F);
            DisplayLinkCodec.WriteU16(body, 16, 1);
            DisplayLinkCodec.WriteU16(body, 18, 1);
            DisplayLinkCodec.WriteU16(body, 20, 128);
            DisplayLinkCodec.WriteU16(body, 22, 32);
            DisplayLinkCodec.WriteU16(body, 24, 32);
            body[26] = 8;
            DisplayLinkCodec.WriteU16(body, 28, 4096);
            DisplayLinkCodec.WriteU32(body, 30, 65536);
            DisplayLinkCodec.WriteU32(body, 34, 32768);
            DisplayLinkCodec.WriteU32(body, 38, BootSessionId);
            return body;
        }

        private byte[] Status()
        {
            var body = new byte[28];
            DisplayLinkCodec.WriteU32(body, 0, ActiveGeneration);
            body[8] = 1;
            body[9] = 1;
            DisplayLinkCodec.WriteU16(body, 10, ActiveGeneration == 0 ? (ushort)0 : (ushort)1);
            return body;
        }
    }

    private sealed class UnsupportedChannel : IDisplayLinkChannel
    {
        public event EventHandler<DisplayTouchEvent>? DisplayTouchReceived
        {
            add { }
            remove { }
        }
        public bool IsConnected => true;
        public Task<bool> ReconnectDisplayLinkAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task<DisplayLinkResponse> ExchangeDisplayLinkAsync(
            DisplayLinkOpcode opcode,
            uint generation,
            ReadOnlyMemory<byte> body = default,
            DisplayLinkFlags flags = DisplayLinkFlags.AckRequired,
            CancellationToken cancellationToken = default) =>
            Task.FromException<DisplayLinkResponse>(
                new ProtocolException(
                    "Firmware does not support this DisplayLink request."));
        public Task SendDisplayLinkOneWayAsync(
            DisplayLinkOpcode opcode,
            uint generation,
            ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}



