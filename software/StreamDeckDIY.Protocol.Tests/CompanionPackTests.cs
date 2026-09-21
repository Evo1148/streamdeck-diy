using System.IO.Compression;
using System.Text.Json;
using StreamDeckDIY.Core.Dashboard;
using StreamDeckDIY.Core.Display;
using StreamDeckDIY.Core.DisplayLink;
using StreamDeckDIY.App.Services;

internal static class CompanionPackTests
{
    public static async Task RunAsync()
    {
        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", ".."));
        var source = Path.Combine(solutionRoot, "StreamDeckDIY.App", "Assets", "Companions");
        var directory = Path.Combine(Path.GetTempPath(), $"companion-tests-{Guid.NewGuid():N}");
        var official = Path.Combine(directory, "official");
        var custom = Path.Combine(directory, "custom");
        Directory.CreateDirectory(official);
        try
        {
            CopyDirectory(Path.Combine(source, CompanionPackCatalog.OfficialHikariId),
                Path.Combine(official, CompanionPackCatalog.OfficialHikariId));
            CopyDirectory(Path.Combine(source, CompanionPackCatalog.OfficialNeonId),
                Path.Combine(official, CompanionPackCatalog.OfficialNeonId));
            var catalog = new CompanionPackCatalog(custom, official);
            await catalog.InitializeAsync();
            Assert(catalog.Packs.Count == 2 && catalog.Packs.All(pack => pack.IsOfficial),
                "Valid schema v1 official manifests load");

            var hikari = await catalog.ResolveAssetAsync(null, VisualIdentity.Hikari, CompanionMood.Happy);
            var neon = await catalog.ResolveAssetAsync("missing-pack", VisualIdentity.Neon, CompanionMood.Alert);
            Assert(hikari is { Pack.Id: CompanionPackCatalog.OfficialHikariId, ResolvedMood: CompanionMood.Happy } &&
                   neon is { Pack.Id: CompanionPackCatalog.OfficialNeonId, ResolvedMood: CompanionMood.Alert },
                "Missing pack IDs fall back to the identity-specific official pack");

            var basicZip = Path.Combine(directory, "basic.zip");
            CreatePackZip(basicZip, source, "custom-basic", neutralOnly: true);
            var imported = await catalog.ImportAsync(basicZip);
            var busy = await catalog.ResolveAssetAsync(imported.Id, VisualIdentity.Neon, CompanionMood.Busy);
            Assert(!imported.IsOfficial && busy is { ResolvedMood: CompanionMood.Neutral } &&
                   Directory.Exists(Path.Combine(custom, "custom-basic")),
                "ZIP import copies into controlled storage and missing moods fall back to Neutral");

            var badSchema = Path.Combine(directory, "bad-schema.zip");
            CreatePackZip(badSchema, source, "bad-schema", schema: 2);
            await AssertRejectedAsync(() => catalog.ImportAsync(badSchema),
                "Unsupported schemaVersion is rejected");
            var missing = Path.Combine(directory, "missing.zip");
            CreatePackZip(missing, source, "missing-state", omitFile: true);
            await AssertRejectedAsync(() => catalog.ImportAsync(missing),
                "Manifest mapping to a missing sprite is rejected");
            var invalid = Path.Combine(directory, "invalid-png.zip");
            CreatePackZip(invalid, source, "invalid-png", invalidPng: true);
            await AssertRejectedAsync(() => catalog.ImportAsync(invalid), "Invalid PNG is rejected");
            var traversal = Path.Combine(directory, "traversal.zip");
            using (var archive = ZipFile.Open(traversal, ZipArchiveMode.Create))
                using (var writer = new StreamWriter(archive.CreateEntry("../outside.txt").Open())) writer.Write("x");
            await AssertRejectedAsync(() => catalog.ImportAsync(traversal), "ZIP traversal is rejected");
            var oversized = Path.Combine(directory, "oversized.zip");
            using (var archive = ZipFile.Open(oversized, ZipArchiveMode.Create))
                await archive.CreateEntry("huge.png").Open().WriteAsync(new byte[CompanionPackFormat.MaxAssetBytes + 1]);
            await AssertRejectedAsync(() => catalog.ImportAsync(oversized), "Oversized asset is rejected");

            var nekoZip = Path.Combine(directory, "Neko-01.zip");
            var vexZip = Path.Combine(directory, "Vex.zip");
            CreatePackZip(nekoZip, source, "neon.neko.01", name: "Neko 01");
            CreatePackZip(vexZip, source, "neon.vex.01", name: "Vex 01");
            var neko = await catalog.ImportAsync(nekoZip);
            var vex = await catalog.ImportAsync(vexZip);
            Assert(neko.Id == "neon.neko.01" && neko.Name == "Neko 01" &&
                   vex.Id == "neon.vex.01" && vex.Name == "Vex 01" &&
                   catalog.Packs.Contains(neko) && catalog.Packs.Contains(vex),
                "Neko-01.zip and Vex.zip import with size:64 source-image manifests");
            var processor = new CompanionAssetProcessor();
            CompanionResolvedAsset? nekoBusy = null;
            foreach (var mood in Enum.GetValues<CompanionMood>())
            {
                var nekoMood = await catalog.ResolveAssetAsync(neko.Id, VisualIdentity.Neon, mood);
                var vexMood = await catalog.ResolveAssetAsync(vex.Id, VisualIdentity.Neon, mood);
                Assert(nekoMood?.ResolvedMood == mood && vexMood?.ResolvedMood == mood,
                    $"Neko and Vex resolve their real {mood} sprites");
                foreach (var identity in new[] { VisualIdentity.Hikari, VisualIdentity.Neon })
                {
                    var background = DashboardVisualTokenResolver.CompanionBackground(
                        identity, DashboardVisualOptions.Default);
                    foreach (var sourceAsset in new[] { nekoMood!, vexMood! })
                    {
                        var preparedAsset = await processor.PrepareAsync(sourceAsset, identity, background);
                        var preparedAgain = await processor.PrepareAsync(sourceAsset, identity, background);
                        var rgb565 = await ArtworkRgb565Converter.ConvertAsync(
                            preparedAsset.PngBytes, preparedAsset.Width, preparedAsset.Height,
                            background, CancellationToken.None);
                        Assert(preparedAsset.Width == CompanionPackFormat.RenderWidth &&
                               preparedAsset.Height == CompanionPackFormat.RenderHeight &&
                               preparedAsset.ContentKey == preparedAgain.ContentKey &&
                               rgb565?.Length == 96 * 96 * 2,
                            $"{sourceAsset.Pack.Name} {mood} prepares deterministically at 96x96 for {identity}");
                        Assert((ushort)(rgb565![0] | rgb565[1] << 8) == background,
                            $"{sourceAsset.Pack.Name} {mood} transparent corner uses the {identity} background");
                        if (sourceAsset.Pack.Id == neko.Id &&
                            mood == CompanionMood.Busy &&
                            identity == VisualIdentity.Neon)
                            nekoBusy = preparedAsset;
                    }
                }
            }
            var busyState = RuntimeState(nekoBusy!) with
            {
                Configuration = RuntimeState(nekoBusy!).Configuration with
                {
                    VisualIdentity = VisualIdentity.Neon,
                    FunctionalPreset = FunctionalPreset.Companion,
                    PresetId = DashboardPresets.Companion.Id,
                },
                NowPlaying = new(true, true, "Tema", "Artista", "Album", .25, "Player",
                    Artwork: nekoBusy!.PngBytes,
                    ArtworkKey: ArtworkContentKey.Create(nekoBusy.PngBytes)),
                SystemStats = new SystemStatsState
                {
                    CpuUsagePercent = 12,
                    GpuUsagePercent = 23,
                    RamUsedBytes = 34UL,
                    RamTotalBytes = 100UL,
                },
            };
            var stableGraph = DashboardDisplayGraphCompiler.Compile(busyState, 0);
            var stableImages = stableGraph.Nodes.Where(node => node.Kind == DisplayNodeKind.Image)
                .Select(node => node.AssetId).Order().ToArray();
            var stableAssets = stableGraph.Assets!.OrderBy(asset => asset.Id).ToArray();
            Assert(stableImages.Length == 2 && stableAssets.Length == 2 &&
                   stableImages.SequenceEqual(stableAssets.Select(asset => asset.Id)) &&
                   stableAssets.Select(asset => asset.Id).Distinct().Count() == 2,
                "Neon Companion with real Neko Busy and artwork references two distinct resident assets");
            for (var sample = 1; sample <= 60; sample++)
            {
                var updated = busyState with
                {
                    ClockText = $"12:{sample % 60:00}",
                    NowPlaying = busyState.NowPlaying with { Progress = sample / 60d },
                    SystemStats = busyState.SystemStats with
                    {
                        CpuUsagePercent = 10 + sample % 20,
                        GpuUsagePercent = 20 + sample % 10,
                        RamUsedBytes = (ulong)(30 + sample % 5),
                        RamTotalBytes = 100UL,
                    },
                };
                var graph = DashboardDisplayGraphCompiler.Compile(updated, 0);
                Assert(graph.Nodes.Where(node => node.Kind == DisplayNodeKind.Image)
                           .Select(node => node.AssetId).Order().SequenceEqual(stableImages) &&
                       graph.Assets!.OrderBy(asset => asset.Id)
                           .SequenceEqual(stableAssets),
                    $"Neko Busy plus artwork keeps stable asset IDs and hashes at idle sample {sample}");
            }

            var hikariBackground = DashboardVisualTokenResolver.CompanionBackground(
                VisualIdentity.Hikari, DashboardVisualOptions.Default);
            var hikariPrepared = await processor.PrepareAsync(hikari!, VisualIdentity.Hikari, hikariBackground);
            var neonBackground = DashboardVisualTokenResolver.CompanionBackground(
                VisualIdentity.Neon, DashboardVisualOptions.Default);
            var neonPrepared = await processor.PrepareAsync(
                hikari!, VisualIdentity.Neon, neonBackground);
            Assert(hikariPrepared.ContentKey != neonPrepared.ContentKey,
                "COMPANION-CACHE-01: theme switch invalidates the processed asset identity");
            var state = RuntimeState(hikariPrepared);
            var first = DashboardDisplayGraphCompiler.Compile(state, 10);
            var second = DashboardDisplayGraphCompiler.Compile(state, 10);
            Assert(first == second || JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second),
                "Companion DisplayGraph compilation is deterministic");
            Assert(first.Nodes.Single(node => node.Kind == DisplayNodeKind.Image &&
                       node.AssetId == CompanionPackFormat.ActiveAssetId) is not null &&
                   first.Assets?.Single().Id == CompanionPackFormat.ActiveAssetId,
                "Preview and TFT share the same Image node and active asset ID");

            await catalog.DeleteAsync(imported.Id);
            Assert(catalog.Packs.All(pack => pack.Id != imported.Id) &&
                   !Directory.Exists(Path.Combine(custom, imported.Id)),
                "Custom packs uninstall cleanly while official packs remain protected");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static DashboardRuntimeState RuntimeState(CompanionResolvedAsset asset)
    {
        var config = DashboardConfiguration.CreateDefault(1) with
        { FunctionalPreset = FunctionalPreset.Companion, PresetId = DashboardPresets.Companion.Id };
        return new(config, "General", "12:00", new(false, false, "", "", "", null, ""),
            SystemStatsState.Unavailable, MascotVisualEngine.Frame(MascotState.Idle, 0, true),
            [], [], CompanionState.Default, CompanionAsset: asset);
    }

    private static void CreatePackZip(string path, string source, string id, int schema = 1,
        bool neutralOnly = false, bool omitFile = false, bool invalidPng = false,
        string name = "Test pack")
    {
        var sourceRoot = Path.Combine(source, CompanionPackCatalog.OfficialHikariId);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var states = neutralOnly
            ? new Dictionary<string, string> { ["neutral"] = "neutral.png" }
            : Enum.GetNames<CompanionMood>().ToDictionary(name => name.ToLowerInvariant(), name => name.ToLowerInvariant() + ".png");
        var manifest = new CompanionPackManifest(schema, id, name, "Tests", "1.0.0", 64, 64, states);
        using (var writer = new StreamWriter(archive.CreateEntry("companion.json").Open()))
            writer.Write(JsonSerializer.Serialize(manifest));
        foreach (var file in states.Values)
        {
            if (omitFile && file == "neutral.png") continue;
            using var target = archive.CreateEntry(file).Open();
            if (invalidPng && file == "neutral.png") target.Write([1, 2, 3]);
            else using (var input = File.OpenRead(Path.Combine(sourceRoot, file))) input.CopyTo(target);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
    }
    private static async Task AssertRejectedAsync(Func<Task> action, string name)
    {
        try { await action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException($"Test failed: {name}");
    }
    private static void Assert(bool condition, string name)
    { if (!condition) throw new InvalidOperationException($"Test failed: {name}"); }
}

