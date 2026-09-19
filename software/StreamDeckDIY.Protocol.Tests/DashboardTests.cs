using StreamDeckDIY.Core.Dashboard;
using StreamDeckDIY.Core.Profiles;
using StreamDeckDIY.Protocol.Models;
using StreamDeckDIY.App.Services;

internal static class DashboardTests
{
    public static async Task RunAsync()
    {
        TestDynamicProgress();
        TestArtworkIdentityAndRaces();
        TestButtonMatrixLayout();
        var directory = Path.Combine(
            Path.GetTempPath(), $"StreamDeckDIY.DashboardTests.{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "dashboard-config.json");
        try
        {
            var defaults = DashboardConfiguration.CreateDefault(7);
            Assert(defaults.PresetId == DashboardPresets.ControlInfo.Id &&
                   defaults.VisualIdentity == VisualIdentity.Hikari &&
                   defaults.FunctionalPreset == FunctionalPreset.Control &&
                   defaults.EffectiveVisualOptions == DashboardVisualOptions.Default,
                "Dashboard defaults to Hikari + Control with stable visual options");
            Assert(Enum.GetValues<VisualIdentity>().SequenceEqual(
                       new[] { VisualIdentity.Hikari, VisualIdentity.Neon,
                           VisualIdentity.Lumen, VisualIdentity.Studio }) &&
                   Enum.GetValues<FunctionalPreset>().SequenceEqual(
                       new[] { FunctionalPreset.Control, FunctionalPreset.Media,
                           FunctionalPreset.System, FunctionalPreset.Companion }) &&
                   VisualSystemCatalog.SelectableIdentities.SequenceEqual(
                       new[] { VisualIdentity.Hikari, VisualIdentity.Neon }) &&
                   VisualSystemCatalog.SelectablePresets.SequenceEqual(
                       new[] { FunctionalPreset.Control, FunctionalPreset.Companion }),
                "Visual architecture represents future identities and presets while Phase 1 exposes two");
            Assert(DashboardPresets.All.Select(p => p.Name).SequenceEqual(
                    new[] { "Control + Info", "Media", "System", "Companion" }) &&
                   DashboardPresets.All.SelectMany(preset => new[] { VisualIdentity.Hikari, VisualIdentity.Neon }
                       .SelectMany(identity => DashboardCompositionResolver.Compose(
                           Configuration(preset, identity)))).All(s =>
                        s.Bounds.X >= 0 && s.Bounds.Y >= 0 &&
                        s.Bounds.X + s.Bounds.Width <= 480 &&
                        s.Bounds.Y + s.Bounds.Height <= 320),
                "Every identity composition fits the 480 x 320 surface");
            var visualConfigurations = new[] { DashboardPresets.ControlInfo, DashboardPresets.Companion }
                .SelectMany(preset => new[] { VisualIdentity.Hikari, VisualIdentity.Neon }
                    .Select(identity => Configuration(preset, identity))).ToArray();
            var visualCompositions = visualConfigurations
                .Select(DashboardCompositionResolver.Compose).ToArray();
            Assert(visualConfigurations.All(configuration =>
                       DashboardCompositionResolver.Compose(configuration).SequenceEqual(
                           DashboardCompositionResolver.Compose(configuration))) &&
                   visualCompositions.All(composition => composition.SelectMany((slot, index) =>
                       composition.Skip(index + 1).Select(other => Overlaps(slot.Bounds, other.Bounds)))
                       .All(overlap => !overlap)),
                "Identity compositions are deterministic and non-overlapping");
            Assert(DashboardPresets.All.SelectMany(preset =>
                       new[] { VisualIdentity.Hikari, VisualIdentity.Neon }
                           .SelectMany(identity => DashboardCompositionResolver.Compose(
                               Configuration(preset, identity))))
                   .All(slot => slot.Kind != DashboardWidgetKind.Encoder),
                "No functional preset contains permanent encoder UI");
            Assert(Area(Slot(DashboardPresets.Media, DashboardWidgetKind.NowPlaying)) >
                       Area(Slot(DashboardPresets.Media, DashboardWidgetKind.ButtonMatrix)) &&
                   Slot(DashboardPresets.System, DashboardWidgetKind.SystemStats).Width >= 320 &&
                   Slot(DashboardPresets.Companion, DashboardWidgetKind.Mascot).Width >= 280,
                "Preset hierarchy prioritizes media, system metrics and companion mascot");
            Assert(DashboardPresets.All.All(preset => preset.Regions.All(region =>
                       (defaults with { PresetId = preset.Id }).IsEnabled(region.Widget))),
                "Every preset keeps all of its structural widgets visible");

            var store = new DashboardConfigurationStore(path);
            await store.InitializeAsync();
            var customized = defaults
                .WithWidget(DashboardWidgetKind.ButtonMatrix, false)
                .WithWidget(DashboardWidgetKind.Mascot, true)
                .WithOptions(DashboardWidgetKind.SystemStats,
                    options => options with { ShowNetwork = true, ShowDisk = true }) with
                { MascotState = MascotState.Happy };
            await store.SaveAsync(customized);
            await store.SaveAsync((DashboardConfiguration.CreateDefault(8)
                .WithWidget(DashboardWidgetKind.Clock, false)
                .WithWidget(DashboardWidgetKind.NowPlaying, false)) with
                {
                    PresetId = DashboardPresets.Companion.Id,
                    FunctionalPreset = FunctionalPreset.Companion,
                    VisualIdentity = VisualIdentity.Neon,
                    VisualOptions = new(VisualAccentVariant.Cool, ShowClock: true,
                        ShowSystemStats: true, ShowArtwork: false, ShowMascot: true,
                        VisualBackgroundVariant.HighContrast),
                });

            var reloaded = new DashboardConfigurationStore(path);
            await reloaded.InitializeAsync();
            var reloadedSeven = reloaded.Get(7);
            var reloadedEight = reloaded.Get(8);
            Assert(reloadedSeven.IsEnabled(DashboardWidgetKind.ButtonMatrix) &&
                   reloadedSeven.IsEnabled(DashboardWidgetKind.Mascot) &&
                   !reloadedSeven.Widgets.Single(widget =>
                       widget.Kind == DashboardWidgetKind.ButtonMatrix).Enabled &&
                   reloadedSeven.MascotState == MascotState.Happy &&
                   reloadedSeven.Options(DashboardWidgetKind.SystemStats).ShowNetwork &&
                   reloadedSeven.Options(DashboardWidgetKind.SystemStats).ShowDisk,
                "Legacy disabled flags are preserved but structural widgets render visible");
            Assert(reloadedEight.IsEnabled(DashboardWidgetKind.Clock) &&
                   reloadedEight.IsEnabled(DashboardWidgetKind.NowPlaying) &&
                   reloadedEight.PresetId == DashboardPresets.Companion.Id &&
                   reloadedEight.FunctionalPreset == FunctionalPreset.Companion &&
                   reloadedEight.VisualIdentity == VisualIdentity.Neon &&
                   !reloadedEight.EffectiveVisualOptions.ShowArtwork &&
                   !reloadedEight.Widgets.Single(widget =>
                       widget.Kind == DashboardWidgetKind.Clock).Enabled &&
                   !reloadedEight.Widgets.Single(widget =>
                       widget.Kind == DashboardWidgetKind.NowPlaying).Enabled &&
                   File.ReadAllText(path).Contains("\"Version\": 5"),
                "Appearance serializes per profile while preserving legacy widget flags");
            await store.SaveAsync(reloadedSeven with { CompanionPackId = "custom-cat" });
            await store.SaveAsync(reloadedEight with { CompanionPackId = "official-neon" });
            var packReloaded = new DashboardConfigurationStore(path);
            await packReloaded.InitializeAsync();
            Assert(packReloaded.Get(7).CompanionPackId == "custom-cat" &&
                   packReloaded.Get(8).CompanionPackId == "official-neon",
                "Companion pack selection persists independently per profile");
            Assert(reloaded.Get(99).ProfileId == 99 &&
                   reloaded.Get(99).PresetId == DashboardPresets.ControlInfo.Id &&
                   reloaded.Get(99).CompanionPackId is null,
                "Missing and legacy profiles get a safe identity-specific Companion default");

            var saveCount = 0;
            DashboardConfiguration? lastSaved = null;
            await using (var autosave = new DashboardAutosave(
                (value, _) =>
                {
                    saveCount++;
                    lastSaved = value;
                    return Task.CompletedTask;
                }, TimeSpan.FromMilliseconds(80)))
            {
                await Task.Delay(120);
                Assert(saveCount == 0 && autosave.State == DashboardSaveState.Saved,
                    "Initial hydration does not trigger an autosave");

                autosave.Queue(defaults with { Theme = DashboardTheme.Graphite });
                Assert(autosave.IsDirty && autosave.State == DashboardSaveState.Dirty,
                    "Dashboard modification marks autosave dirty");
                autosave.Queue(defaults with { Theme = DashboardTheme.Violet });
                autosave.Queue(defaults.WithWidget(DashboardWidgetKind.Mascot, true) with
                    { Theme = DashboardTheme.Violet });
                await Task.Delay(180);
                Assert(saveCount == 1 && lastSaved?.Theme == DashboardTheme.Violet &&
                       lastSaved.IsEnabled(DashboardWidgetKind.Mascot) && !autosave.IsDirty,
                    "Debounce collapses edits and persists the latest state");

                autosave.Queue(DashboardConfiguration.CreateDefault(7) with
                    { PresetId = DashboardPresets.Media.Id });
                await autosave.FlushAsync();
                Assert(saveCount == 2 && lastSaved?.ProfileId == 7 &&
                       lastSaved.PresetId == DashboardPresets.Media.Id,
                    "Profile change flush persists the profile being left");

                autosave.Queue(DashboardConfiguration.CreateDefault(8) with
                    { Theme = DashboardTheme.Graphite });
                await autosave.FlushAsync();
                Assert(saveCount == 3 && lastSaved?.ProfileId == 8,
                    "Explicit close flush persists pending dashboard state");
            }

            var validBeforeFailure = await File.ReadAllTextAsync(path);
            var failWrite = true;
            var failedValue = DashboardConfiguration.CreateDefault(9) with
                { Theme = DashboardTheme.Graphite };
            await using (var failingAutosave = new DashboardAutosave(
                async (value, token) =>
                {
                    if (failWrite) throw new IOException("Simulated persistence failure");
                    await store.SaveAsync(value, token);
                }, TimeSpan.FromSeconds(10)))
            {
                failingAutosave.Queue(failedValue);
                try { await failingAutosave.FlushAsync(); }
                catch (IOException) { }
                Assert(failingAutosave.State == DashboardSaveState.Error &&
                       failingAutosave.IsDirty &&
                       await File.ReadAllTextAsync(path) == validBeforeFailure,
                    "Failed autosave keeps dirty memory and preserves the valid file");
                failWrite = false;
                failingAutosave.Queue(failedValue);
                await failingAutosave.FlushAsync();
                Assert(failingAutosave.State == DashboardSaveState.Saved,
                    "A later edit retries autosave after an error");
            }

            var profileAOff = (DashboardConfiguration.CreateDefault(20) with
                { PresetId = DashboardPresets.Companion.Id,
                  FunctionalPreset = FunctionalPreset.Companion,
                  Widgets = DashboardPresets.Companion.DefaultWidgets() })
                .WithWidget(DashboardWidgetKind.Mascot, false);
            var profileBOff = (DashboardConfiguration.CreateDefault(21) with
                { PresetId = DashboardPresets.Companion.Id,
                  FunctionalPreset = FunctionalPreset.Companion,
                  Widgets = DashboardPresets.Companion.DefaultWidgets() })
                .WithWidget(DashboardWidgetKind.Mascot, false);
            await store.SaveAsync(profileAOff);
            await store.SaveAsync(profileBOff);
            await using (var profileAutosave = new DashboardAutosave(
                store.SaveAsync, TimeSpan.FromSeconds(10)))
            {
                profileAutosave.Queue(
                    profileAOff.WithWidget(DashboardWidgetKind.Mascot, true));
                Assert(profileAutosave.IsDirty,
                    "Mascot toggle marks the active profile dashboard dirty");
                await profileAutosave.FlushAsync();
                await profileAutosave.FlushAsync();
                profileAutosave.Queue(profileBOff);
                await profileAutosave.FlushAsync();
            }
            var profileReloaded = new DashboardConfigurationStore(path);
            await profileReloaded.InitializeAsync();
            Assert(profileReloaded.Get(20).IsEnabled(DashboardWidgetKind.Mascot) &&
                   profileReloaded.Get(21).IsEnabled(DashboardWidgetKind.Mascot) &&
                   profileReloaded.Get(20).Widgets.Single(widget =>
                       widget.Kind == DashboardWidgetKind.Mascot).Enabled &&
                   !profileReloaded.Get(21).Widgets.Single(widget =>
                       widget.Kind == DashboardWidgetKind.Mascot).Enabled,
                "Effective visibility remains on while persisted flags stay backward compatible");

            var raceStore = new DashboardConfigurationStore(path);
            await raceStore.InitializeAsync();
            var raceFirst = raceStore.SaveAsync(DashboardConfiguration.CreateDefault(30) with
                { Theme = DashboardTheme.Graphite });
            var raceLast = raceStore.SaveAsync(DashboardConfiguration.CreateDefault(30) with
                { Theme = DashboardTheme.Violet });
            await Task.WhenAll(raceFirst, raceLast);
            var raceReloaded = new DashboardConfigurationStore(path);
            await raceReloaded.InitializeAsync();
            Assert(raceReloaded.Get(30).Theme == DashboardTheme.Violet,
                "Serialized real-file dashboard writes preserve the last queued value");

            var autosaveReloaded = new DashboardConfigurationStore(path);
            await autosaveReloaded.InitializeAsync();
            Assert(autosaveReloaded.Get(9).Theme == DashboardTheme.Graphite &&
                   autosaveReloaded.Get(8).PresetId == DashboardPresets.Companion.Id &&
                   autosaveReloaded.Get(7).IsEnabled(DashboardWidgetKind.Mascot),
                "Autosave remains isolated per profile");
            var legacyJson = await File.ReadAllTextAsync(path);
            await File.WriteAllTextAsync(path, legacyJson.Replace("\"Version\": 5", "\"Version\": 3"));
            var legacyReloaded = new DashboardConfigurationStore(path);
            await legacyReloaded.InitializeAsync();
            Assert(legacyReloaded.Get(7).IsEnabled(DashboardWidgetKind.Mascot) &&
                   legacyReloaded.Get(7).Options(DashboardWidgetKind.ButtonMatrix).ShowLabels &&
                    File.ReadAllText(path).Contains("\"Version\": 5") &&
                    legacyReloaded.Get(8).VisualIdentity == VisualIdentity.Neon &&
                    legacyReloaded.Get(8).FunctionalPreset == FunctionalPreset.Companion,
                "v3 dashboard settings migrate to Visual System V1 without losing preferences");
            Assert(DashboardProfileResolver.Resolve([], null) is null,
                "Dashboard profile resolution tolerates an uninitialized active profile");

            var legacyVisualPath = Path.Combine(directory, "legacy-dashboard-config.json");
            await File.WriteAllTextAsync(legacyVisualPath,
                """
                {
                  "Version": 3,
                  "Configurations": [
                    {
                      "ProfileId": 42,
                      "PresetId": "companion",
                      "Theme": 0,
                      "Widgets": [],
                      "MascotState": 0
                    }
                  ]
                }
                """);
            var legacyVisualStore = new DashboardConfigurationStore(legacyVisualPath);
            await legacyVisualStore.InitializeAsync();
            var migratedVisual = legacyVisualStore.Get(42);
            Assert(migratedVisual.VisualIdentity == VisualIdentity.Hikari &&
                   migratedVisual.FunctionalPreset == FunctionalPreset.Companion &&
                   migratedVisual.EffectiveVisualOptions == DashboardVisualOptions.Default &&
                   File.ReadAllText(legacyVisualPath).Contains("\"Version\": 5"),
                "Legacy profiles receive safe Hikari defaults while preserving their preset");

            Assert(DashboardLayoutEngine.Build(customized).Select(widget => widget.Kind)
                    .SequenceEqual(DashboardCompositionResolver.Compose(customized).Select(slot => slot.Kind)),
                "Preview layout contains every structural slot despite persisted disabled flags");
            Assert(DashboardLayoutEngine.HitTest(defaults, new DashboardPoint(200, 60)) ==
                   DashboardWidgetKind.ButtonMatrix,
                "Dashboard hit testing resolves the recomposed Control action surface");
            Assert(DashboardLayoutEngine.HitTest(defaults, new DashboardPoint(40, 274)) is null,
                "Control keeps deliberate breathing room between the action surface and navigation");
            Assert(DashboardLayoutEngine.HitTest(defaults, new DashboardPoint(479, 319)) is null &&
                   DashboardLayoutEngine.HitTest(defaults, new DashboardPoint(-1, 0)) is null,
                "Dashboard hit testing handles deliberate whitespace and out-of-bounds coordinates");

            var controlMatrix = Slot(DashboardPresets.ControlInfo, DashboardWidgetKind.ButtonMatrix);
            var firstCell = ButtonMatrixLayout.Cell(controlMatrix, 0);
            var lastCell = ButtonMatrixLayout.Cell(controlMatrix, 8);
            var firstPoint = new DashboardPoint(firstCell.X + firstCell.Width / 2,
                firstCell.Y + firstCell.Height / 2);
            var lastPoint = new DashboardPoint(lastCell.X + lastCell.Width / 2,
                lastCell.Y + lastCell.Height / 2);
            var firstButton = DashboardInteractionResolver.Resolve(defaults,
                firstPoint, firstPoint, TimeSpan.FromMilliseconds(80));
            var lastButton = DashboardInteractionResolver.Resolve(defaults,
                lastPoint, lastPoint, TimeSpan.FromMilliseconds(80));
            Assert(firstButton?.Control == new ControlId(ControlType.Button, 0) &&
                   lastButton?.Control == new ControlId(ControlType.Button, 8),
                "Matrix touch resolves the first and last logical buttons");
            var navigationSlot = Slot(DashboardPresets.ControlInfo, DashboardWidgetKind.Navigation);
            var previous = DashboardInteractionResolver.Resolve(defaults,
                new DashboardPoint(navigationSlot.X + 10, navigationSlot.Y + 10),
                new DashboardPoint(navigationSlot.X + 10, navigationSlot.Y + 10),
                TimeSpan.FromMilliseconds(80));
            var home = DashboardInteractionResolver.Resolve(defaults,
                new DashboardPoint(navigationSlot.X + navigationSlot.Width / 2, navigationSlot.Y + 10),
                new DashboardPoint(navigationSlot.X + navigationSlot.Width / 2, navigationSlot.Y + 10),
                TimeSpan.FromMilliseconds(80));
            Assert(previous?.SystemNavigation == SystemNavigation.Previous &&
                   home?.SystemNavigation == SystemNavigation.Home,
                "Navigation touch regions expose Previous and Home semantics");
            var swipe = DashboardInteractionResolver.Resolve(defaults,
                new DashboardPoint(100, 100), new DashboardPoint(20, 100), TimeSpan.FromMilliseconds(120));
            Assert(swipe?.Gesture == TouchGesture.SwipeLeft && swipe.Action == TouchAction.Navigate,
                "Preview gestures recognize horizontal swipes");
            var longPress = DashboardInteractionResolver.Resolve(defaults,
                new DashboardPoint(20, 20), new DashboardPoint(20, 20), TimeSpan.FromMilliseconds(700));
            var mediaTouch = DashboardInteractionResolver.Resolve(defaults,
                new DashboardPoint(80, 90), new DashboardPoint(80, 90), TimeSpan.FromMilliseconds(80));
            Assert(longPress?.Gesture == TouchGesture.LongPress &&
                   mediaTouch?.Action == TouchAction.MediaControl,
                "Preview recognizes long press and media touch targets");

            var bindings = ProfileControls.All.Select(control => new BindingInfo(
                control,
                control.Type == ControlType.Button && control.Index == 0
                    ? new DeviceAction(ActionType.Keyboard, 0x04)
                    : new DeviceAction(ActionType.None))).ToArray();
            var projection = DashboardBindingProjection.Build(
                bindings,
                binding => binding.Action.Type == ActionType.Keyboard ? "A" : "Sin asignar",
                binding => binding.Control == new ControlId(ControlType.Button, 0)
                    ? "manual-icon" : "automatic-icon");
            Assert(projection.Buttons.Length == 9 && projection.Buttons[0].Summary == "A" &&
                   projection.Buttons[0].IconGlyph == "manual-icon",
                "Dashboard projects real button bindings and resolved icon overrides");
            Assert(projection.Encoder.Select(item => item.Control.Type).SequenceEqual(new[]
                   {
                       ControlType.EncoderCounterClockwise,
                       ControlType.EncoderPress,
                       ControlType.EncoderClockwise,
                   }), "Dashboard projects real encoder bindings in UI order");

            var nowPlaying = new UnavailableNowPlayingProvider().Current;
            var stats = new UnavailableSystemStatsProvider().Current;
            Assert(!nowPlaying.IsAvailable && stats.CpuUsagePercent is null &&
                   stats.RamUsagePercent is null,
                "Unavailable providers report no fabricated data");
            var playing = new NowPlayingState(true, true, "Tema", "Artista", "Álbum", .5, "Spotify");
            var pausedMissing = new NowPlayingState(true, false, "Sin título", "", "", null, "App");
            Assert(playing.IsPlaying && playing.Progress == .5 && !pausedMissing.IsPlaying &&
                   pausedMissing.Artwork is null,
                "Now Playing represents playing, paused and missing metadata safely");
            Assert(Enum.GetValues<MascotState>().All(state =>
                   MascotVisualEngine.Frame(state, 1, false) is not null) &&
                   MascotVisualEngine.Frame(MascotState.Sleep, 1, false).EyesClosed,
                "Mascot engine renders every state including closed-eye sleep");

            var labelsPath = Path.Combine(directory, "binding-labels.json");
            var labelStore = new BindingLabelStore(labelsPath);
            await labelStore.InitializeAsync();
            var labelControl = new ControlId(ControlType.Button, 0);
            await labelStore.SetAsync(7, labelControl, "Audio");
            var labelsReloaded = new BindingLabelStore(labelsPath);
            await labelsReloaded.InitializeAsync();
            Assert(labelsReloaded.Get(7, labelControl) == "Audio" &&
                   labelsReloaded.Get(8, labelControl) is null,
                "Short labels persist by profile and control while preserving fallback");

            await using (var realStats = new WindowsSystemStatsProvider())
            {
                await realStats.InitializeAsync();
                await Task.Delay(1100);
                var sample = await realStats.ReadAsync();
                Assert(sample.RamUsedBytes is not null && sample.RamTotalBytes > 0 &&
                       sample.CpuUsagePercent is null or >= 0 and <= 100,
                    "Windows source reports real CPU/RAM without hardware sensor data");
            }

            await File.WriteAllTextAsync(path, "{ invalid json");
            var corrupt = new DashboardConfigurationStore(path);
            await corrupt.InitializeAsync();
            Assert(corrupt.Get(4).PresetId == DashboardPresets.ControlInfo.Id,
                "Corrupt dashboard persistence falls back safely");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void TestButtonMatrixLayout()
    {
        Assert(ButtonMatrixLayout.Columns == 3 && ButtonMatrixLayout.Rows == 3 &&
               ButtonMatrixLayout.ControlCount == 9,
            "user action matrix topology is 3 columns by 3 rows");
        for (var row = 0; row < ButtonMatrixLayout.Rows; row++)
            for (var column = 0; column < ButtonMatrixLayout.Columns; column++)
                Assert(ButtonMatrixLayout.Index(row, column) == row * 3 + column,
                    "button matrix uses row-major control IDs");

        foreach (var preset in DashboardPresets.All)
        {
            var configuration = DashboardConfiguration.CreateDefault(1) with
                { PresetId = preset.Id };
            var slot = DashboardCompositionResolver.Compose(configuration)
                .Single(item => item.Kind == DashboardWidgetKind.ButtonMatrix);
            var cells = Enumerable.Range(0, ButtonMatrixLayout.ControlCount)
                .Select(index => ButtonMatrixLayout.Cell(slot.Bounds, index)).ToArray();
            Assert(cells.Length == 9 && cells.All(cell =>
                    cell.X >= slot.Bounds.X && cell.Y >= slot.Bounds.Y &&
                    cell.X + cell.Width <= slot.Bounds.X + slot.Bounds.Width + .001 &&
                    cell.Y + cell.Height <= slot.Bounds.Y + slot.Bounds.Height + .001),
                $"{preset.Name} contains all 9 user-action cells");
            Assert(cells.SelectMany((cell, index) => cells.Skip(index + 1)
                    .Select(other => Overlaps(cell, other))).All(overlap => !overlap),
                $"{preset.Name} matrix cells do not overlap");
            for (var index = 0; index < cells.Length; index++)
            {
                var cell = cells[index];
                var center = new DashboardPoint(
                    cell.X + cell.Width / 2, cell.Y + cell.Height / 2);
                var interaction = DashboardInteractionResolver.Resolve(
                    configuration, center, center, TimeSpan.FromMilliseconds(50));
                Assert(interaction?.Control == new ControlId(ControlType.Button, (byte)index),
                    $"{preset.Name} matrix cell {index} resolves its unchanged binding ID");
            }
        }
    }

    private static bool Overlaps(DashboardRect left, DashboardRect right) =>
        left.X < right.X + right.Width && left.X + left.Width > right.X &&
        left.Y < right.Y + right.Height && left.Y + left.Height > right.Y;

    private static void TestArtworkIdentityAndRaces()
    {
        var cache = new ArtworkRefreshState();
        var firstBytes = new byte[] { 1, 2, 3, 4 };
        var first = cache.Begin("song-a");
        Assert(cache.TryApply(first, "song-a", firstBytes, true),
            "first artwork request is accepted");
        var key = cache.Current.Key;
        var stored = cache.Current.Bytes;

        var sameContent = cache.Begin("song-b");
        cache.TryApply(sameContent, "song-b", new byte[] { 1, 2, 3, 4 }, true);
        Assert(cache.Current.Key == key && ReferenceEquals(cache.Current.Bytes, stored),
            "new media with identical artwork reuses its stable content identity and bytes");

        var stale = cache.Begin("song-c");
        var current = cache.Begin("song-d");
        Assert(!cache.TryApply(stale, "song-c", new byte[] { 9 }, true) &&
               cache.TryApply(current, "song-d", new byte[] { 5, 6 }, true) &&
               cache.Current.Key != key,
            "late artwork completion cannot replace the current media artwork");

        var beforeFailure = cache.Current;
        var failed = cache.Begin("song-e");
        cache.TryApply(failed, "song-e", null, false);
        Assert(ReferenceEquals(cache.Current, beforeFailure),
            "failed artwork refresh preserves the last stable image");
        Assert(ArtworkContentKey.Create(firstBytes) == ArtworkContentKey.Create(new byte[] { 1, 2, 3, 4 }) &&
               ArtworkContentKey.AssetId(key) == ArtworkContentKey.AssetId(key),
            "CRC32 plus byte length produces stable artwork and asset identities");
    }

    private static void TestDynamicProgress()
    {
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var metric = new ProgressValueTransition();
        var first = metric.Retarget(.20, start, TimeSpan.FromMilliseconds(300));
        Assert(first.From == .20 && first.To == .20 && first.Duration == TimeSpan.Zero,
            "First progress value is presented without a repeated entrance animation");

        var rising = metric.Retarget(.30, start.AddSeconds(1),
            TimeSpan.FromMilliseconds(300));
        Assert(Math.Abs(rising.From - .20) < .001 && rising.To == .30,
            "Progress rises from its previous visual value");
        var retarget = metric.Retarget(.55, start.AddMilliseconds(1150),
            TimeSpan.FromMilliseconds(300));
        Assert(retarget.From > .20 && retarget.From < .30,
            "Progress retargets from the in-flight visual value instead of zero");

        metric.Retarget(.70, start.AddSeconds(2), TimeSpan.Zero);
        var falling = metric.Retarget(.30, start.AddSeconds(3),
            TimeSpan.FromMilliseconds(300));
        Assert(falling.From == .70 && falling.To == .30,
            "Progress transitions downward from the previous value");
        var beforeUnavailable = metric.Current(start.AddMilliseconds(3150));
        var unavailable = metric.Retarget(null, start.AddMilliseconds(3150),
            TimeSpan.FromMilliseconds(300));
        Assert(!unavailable.Available &&
               Math.Abs(unavailable.From - beforeUnavailable!.Value) < .001,
            "Unavailable progress does not animate toward zero");
        metric.Reset();
        Assert(metric.Retarget(.40, start.AddSeconds(4), TimeSpan.FromMilliseconds(300))
                   .Duration == TimeSpan.Zero,
            "A structural reset discards previous visual state once");

        var media = new NowPlayingProgressTransition();
        var playing = new NowPlayingState(true, true, "Song", "Artist", "Album", .10,
            "Player", Position: TimeSpan.FromSeconds(10),
            Duration: TimeSpan.FromSeconds(100), SampledAt: start);
        media.Update(playing, start);
        Assert(Math.Abs(media.Current(start.AddSeconds(5))!.Value - .15) < .001,
            "Now Playing advances locally while Playing");

        var paused = playing with
        {
            IsPlaying = false,
            Progress = .15,
            Position = TimeSpan.FromSeconds(15),
            SampledAt = start.AddSeconds(5),
        };
        media.Update(paused, start.AddSeconds(5));
        Assert(Math.Abs(media.Current(start.AddSeconds(8))!.Value - .15) < .001,
            "Now Playing freezes while Paused");

        var seeked = paused with
        {
            Progress = .60,
            Position = TimeSpan.FromSeconds(60),
            SampledAt = start.AddSeconds(8),
        };
        var seek = media.Update(seeked, start.AddSeconds(8));
        Assert(seek.From > 0 && seek.SynchronizedValue == .60 &&
               seek.SynchronizationDuration > TimeSpan.Zero,
            "Seek retargets from the current position without returning to zero");

        var nextSong = seeked with
        {
            Title = "Next song",
            Progress = .025,
            Position = TimeSpan.FromSeconds(5),
            Duration = TimeSpan.FromSeconds(200),
        };
        var newMedia = media.Update(nextSong, start.AddSeconds(9));
        Assert(Math.Abs(newMedia.From - .025) < .001 &&
               newMedia.SynchronizationDuration == TimeSpan.Zero,
            "New media resets progress to its own real position");
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Test failed: {name}");
    }

    private static DashboardConfiguration Configuration(
        DashboardPreset preset, VisualIdentity identity = VisualIdentity.Hikari) =>
        DashboardConfiguration.CreateDefault(1) with
        {
            PresetId = preset.Id,
            FunctionalPreset = DashboardPresets.FunctionalFrom(preset.Id),
            VisualIdentity = identity,
            Widgets = preset.DefaultWidgets(),
        };
    private static DashboardRect Slot(DashboardPreset preset, DashboardWidgetKind kind) =>
        DashboardCompositionResolver.Compose(Configuration(preset))
            .Single(slot => slot.Kind == kind).Bounds;
    private static double Area(DashboardRect bounds) => bounds.Width * bounds.Height;
}
