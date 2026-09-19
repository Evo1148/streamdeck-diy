using StreamDeckDIY.Core.Display;
using System.Text.Json;

internal static class DisplayTests
{
    public static async Task RunAsync()
    {
        var hidden = new DisplayTextElement(
            "hidden", 0, 0, 20, 10, "hidden", 10, Visible: false);
        var first = new DisplayRectangleElement(
            "first", 0, 0, 20, 20, "#000000", ZIndex: 1);
        var second = new DisplayImageElement(
            "image", 20, 0, 20, 20, "logical-icon", ZIndex: 1);
        var back = new DisplayRectangleElement(
            "back", 0, 0, 40, 40, "#FFFFFF", ZIndex: 0);
        var scene = new DisplayScene("ordered", [first, hidden, second, back]);
        Check(scene.Elements.Count == 4 &&
              scene.OrderedVisibleElements.Select(value => value.Id)
                  .SequenceEqual(["back", "first", "image"]),
            "DisplayScene preserves elements and stable ZIndex order");

        var configuration = DisplayConfiguration.DevelopmentDefault;
        Check(configuration.Width == 480 && configuration.Height == 320,
            "DisplayConfiguration uses the real 480 x 320 landscape surface");
        var defaultText = new DisplayTextElement(
            "default-font", 0, 0, 100, 20, "Sistema", 12);
        var explicitText = defaultText with { FontFamily = "Consolas" };
        Check(defaultText.FontFamily is null &&
              DisplayFontFamily.Normalize(defaultText.FontFamily) is null,
            "Text without explicit FontFamily keeps the system default");
        Check(DisplayFontFamily.Normalize(explicitText.FontFamily) == "Consolas",
            "Explicit valid FontFamily is preserved");
        Check(DisplayFontFamily.Normalize("Unknown") is null &&
              DisplayFontFamily.Normalize(" unknown ") is null,
            "Unknown never becomes a FontFamily name");
        var mapped = DisplayCoordinateMapper.PreviewToLogical(
            240, 180, 480, 360, configuration);
        Check(mapped is { X: 240, Y: 160 },
            "Preview center maps to logical center");
        Check(DisplayCoordinateMapper.PreviewToLogical(
                  0, 0, 640, 320, configuration) is null,
            "Letterbox area does not produce logical coordinates");
        foreach (var scale in new[] { 1d, 1.5d, 2d, 2.5d })
        {
            var scaled = DisplayCoordinateMapper.PreviewToLogical(
                configuration.Width * scale / 2,
                configuration.Height * scale / 2,
                configuration.Width * scale,
                configuration.Height * scale,
                configuration);
            Check(scaled is { X: 240, Y: 160 },
                $"Preview scale {scale:0.0} maps to the same logical center");
        }

        var baseScene = DisplaySceneFactory.CreateHome(
            configuration, "General", false);
        Check(baseScene.Elements.OfType<DisplayTextElement>()
                  .All(element => element.FontFamily is null),
            "Initial scene uses the system font without an explicit family");
        var clock = new AdvancingClock();
        var engine = new DisplayEngine(
            baseScene, clock,
            new DisplayAnimationOptions(FrameInterval: TimeSpan.FromMilliseconds(100)));
        var provider = new HackerMatrixProfileVisualEffectProvider(configuration);
        var controller = new ProfileVisualFeedbackController(engine, provider);
        var sawOverlay = false;
        engine.StateChanged += (_, state) => sawOverlay |= state.OverlayScene is not null;
        var animation = controller.ShowActivatedAsync("Trabajo dinámico");
        Check(!animation.IsCompleted,
            "Overlay animation starts asynchronously without blocking caller");
        Check(engine.State.BaseScene == baseScene && engine.State.OverlayScene is not null,
            "Overlay does not destroy the base scene");
        await animation;
        Check(sawOverlay && engine.State.OverlayScene is null &&
              engine.State.BaseScene == baseScene,
            "Animation completion dismisses overlay and restores base state");

        var dynamicFeedback = provider.CreateActivated("Juegos personalizados");
        Check(dynamicFeedback.Scene.Elements.OfType<DisplayTextElement>()
                  .Any(element => element.Text == "Juegos personalizados") &&
              dynamicFeedback.Scene.Elements.OfType<DisplayTextElement>()
                  .All(element => element.Text != "Programacion"),
            "HackerMatrix receives the profile name dynamically");
        Check(dynamicFeedback.Scene.Elements.OfType<DisplayTextElement>()
                  .All(element => element.FontFamily == "Consolas"),
            "HackerMatrix keeps its explicit Consolas font");
        var animationKinds = dynamicFeedback.Scene.Elements
            .SelectMany(element => element.Animations ?? [])
            .Select(animationItem => animationItem.Kind)
            .ToHashSet();
        Check(Enum.GetValues<DisplayAnimationKind>().All(animationKinds.Contains),
            "HackerMatrix composes FadeIn, FadeOut, Slide and Pulse primitives");

        engine.ShowScene(DisplaySceneFactory.CreateHome(
            configuration, "Juegos", true));
        Check(engine.State.BaseScene.Elements.OfType<DisplayTextElement>()
                  .Any(element => element.Text == "Juegos") &&
              engine.State.BaseScene.Elements.OfType<DisplayTextElement>()
                  .Any(element => element.Text == "Conectado"),
            "Base scene updates active profile and connection state");

        var longEffect = dynamicFeedback.Effect with
        {
            Duration = TimeSpan.FromSeconds(5),
        };
        var pending = engine.ShowOverlayAsync(dynamicFeedback.Scene, longEffect);
        engine.DismissOverlay();
        await pending;
        Check(engine.State.OverlayScene is null,
            "Explicit dismiss restores base state safely");

        await TestEditorAsync(configuration, baseScene);
        await TestPagesAsync(configuration, baseScene);
    }

    private static async Task TestEditorAsync(
        DisplayConfiguration configuration,
        DisplayScene fallbackScene)
    {
        var overlap = new DisplayScene("editor",
        [
            new DisplayRectangleElement("lower", 20, 20, 80, 80, "#112233", ZIndex: 0),
            new DisplayTextElement("upper", 30, 30, 80, 40, "Top", 14,
                Color: "#ABCDEF", ZIndex: 1),
        ], "#010203");
        var editor = new DisplayEditor(configuration, overlap);
        Check(editor.SelectAt(new DisplayPoint(40, 40))?.Id == "upper",
            "Editor selects the topmost overlapping element");

        var text = editor.AddText();
        var rectangle = editor.AddRectangle();
        var image = editor.AddImage();
        Check(text is DisplayTextElement && rectangle is DisplayRectangleElement &&
              image is DisplayImageElement &&
              editor.Scene.Elements.Select(e => e.Id).Distinct().Count() ==
              editor.Scene.Elements.Count,
            "Editor creates all element types with unique IDs");

        var beforeDuplicate = editor.Scene.Elements.Count;
        var duplicate = editor.DuplicateSelected();
        Check(duplicate is DisplayImageElement &&
              editor.Scene.Elements.Count == beforeDuplicate + 1 &&
              duplicate.Id != image.Id,
            "Duplicate creates a selected independent element");
        editor.DeleteSelected();
        Check(editor.Scene.Elements.Count == beforeDuplicate && editor.Selected is null,
            "Delete removes the selected element and clears selection");

        editor.Select("lower");
        editor.MoveSelected(23, 27);
        Check(editor.Selected is { X: 25, Y: 25 },
            "Move snaps logical coordinates to the centralized 5 px grid");
        editor.MoveSelected(-500, 500);
        Check(editor.Selected is { } clamped &&
              clamped.X == -clamped.Width + editor.Settings.MinimumVisible &&
              clamped.Y == configuration.Height - editor.Settings.MinimumVisible,
            "Move clamps elements so they remain partially visible");
        editor.MoveSelected(20, 20);
        editor.ResizeSelected(3, 18);
        Check(editor.Selected is { Width: 10, Height: 20 },
            "Resize enforces minimum size and 5 px snap");

        editor.BringToFront();
        Check(editor.Selected?.ZIndex == editor.Scene.Elements.Count - 1 &&
              editor.Scene.Elements.Select(e => e.ZIndex)
                  .SequenceEqual(Enumerable.Range(0, editor.Scene.Elements.Count)),
            "Layer commands normalize contiguous ZIndex values");
        editor.SendToBack();
        Check(editor.Selected?.ZIndex == 0, "Selected layer can be sent to back");

        editor.SetBackground("#aabbcc");
        Check(editor.Scene.Background == "#AABBCC" &&
              DisplayColor.TryNormalize("#80abcdef", out var color) &&
              color == "#80ABCDEF",
            "Background and logical colors normalize independently of WinUI");

        var dragStart = editor.Scene;
        editor.BeginInteraction();
        editor.MoveSelected(40, 40, transient: true);
        editor.MoveSelected(60, 60, transient: true);
        editor.CommitInteraction();
        editor.Undo();
        Check(editor.Scene == dragStart,
            "A complete drag is one logical undo operation");
        editor.Redo();
        Check(editor.Selected is { X: 60, Y: 60 },
            "Redo restores the completed drag");

        var directory = Path.Combine(
            Path.GetTempPath(), $"StreamDeckDIY.DisplayTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "display-layout.json");
            var store = new DisplayLayoutStore(path);
            var page = new DisplayPage("page-1", "Principal", editor.Scene);
            var workspace = new DisplayWorkspace(configuration,
                [new DisplayProfilePages(1, "General", page.Id, [page])]);
            await store.SaveAsync(workspace);
            var loaded = await store.LoadAsync(workspace, 1);
            Check(loaded.Warning is null &&
                  loaded.Workspace.Configuration == configuration &&
                  loaded.Workspace.Profiles[0].Pages[0].Scene.Background ==
                    editor.Scene.Background &&
                  loaded.Workspace.Profiles[0].Pages[0].Scene.Elements
                    .OfType<DisplayTextElement>().Any(t => t.Color == "#ABCDEF"),
                "Versioned page layout roundtrip preserves configuration, colors and elements");

            var raceFirst = workspace with
            {
                Profiles = [workspace.Profiles[0] with { ProfileName = "Primero" }],
            };
            var raceLast = workspace with
            {
                Profiles = [workspace.Profiles[0] with { ProfileName = "Último" }],
            };
            var firstSave = store.SaveAsync(raceFirst);
            var lastSave = store.SaveAsync(raceLast);
            await Task.WhenAll(firstSave, lastSave);
            var raceReloaded = await new DisplayLayoutStore(path).LoadAsync(workspace, 1);
            Check(raceReloaded.Workspace.Profiles[0].ProfileName == "Último",
                "Concurrent display-layout writes preserve the last queued workspace");

            await File.WriteAllTextAsync(path, "{corrupt");
            var corrupt = await store.LoadAsync(workspace, 1);
            Check(corrupt.Warning is not null && corrupt.Workspace == workspace,
                "Corrupt layout safely falls back to the initial scene");

            var oldConfiguration = new DisplayConfiguration(320, 240, DisplayOrientation.Landscape);
            var oldScene = new DisplayScene("legacy-320",
            [
                new DisplayTextElement("text", 32, 24, 160, 48, "Texto", 14),
                new DisplayRectangleElement("rect", 100, 120, 80, 60, "#112233"),
            ]);
            var oldPage = new DisplayPage("old-page", "Principal", oldScene, Order: 0);
            var secondOldPage = new DisplayPage("old-page-2", "Secundaria", oldScene with { Id = "legacy-2" }, Order: 1);
            var oldDocument = new DisplayLayoutDocument(2, oldConfiguration,
                [new DisplayProfilePages(1, "General", oldPage.Id, [oldPage, secondOldPage])]);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(oldDocument));
            var migrated = await store.LoadAsync(workspace, 1);
            var migratedText = migrated.Workspace.Profiles[0].Pages[0].Scene.Elements
                .OfType<DisplayTextElement>().Single();
            Check(migrated.Migrated && migrated.Workspace.Configuration == configuration &&
                  migratedText is { X: 48, Y: 32, Width: 240, Height: 64, FontSize: 14 } &&
                  migrated.Workspace.Profiles[0].Pages.Select(p => p.Name)
                    .SequenceEqual(new[] { "Principal", "Secundaria" }) &&
                  File.ReadAllText(path).Contains("\"Version\": 3"),
                "320 x 240 layouts migrate atomically to 480 x 320 without scaling fonts");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        var recordingStore = new RecordingLayoutStore();
        var controlledDelay = new ControlledAutosaveDelay();
        await using (var autosave = new DisplayLayoutAutosave(
            recordingStore, TimeSpan.FromMilliseconds(500), controlledDelay))
        {
            var page = new DisplayPage("page", "Principal", editor.Scene);
            var workspace = new DisplayWorkspace(configuration,
                [new DisplayProfilePages(1, "General", page.Id, [page])]);
            autosave.Schedule(workspace);
            autosave.Schedule(workspace);
            autosave.Schedule(workspace);
            controlledDelay.ReleaseLatest();
            await recordingStore.Saved.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Check(recordingStore.SaveCount == 1,
                "Autosave debounce writes once rather than once per editor frame");
        }
    }

    private static async Task TestPagesAsync(
        DisplayConfiguration configuration,
        DisplayScene fallbackScene)
    {
        var id = 0;
        var manager = new DisplayPageManager(
            configuration,
            (profile, page) => DisplaySceneFactory.CreatePage(
                configuration, profile, page),
            () => $"page-{++id}");
        var profiles = new[]
        {
            new DisplayProfileReference(1, "General"),
            new DisplayProfileReference(2, "Juegos"),
        };
        manager.Load(new(configuration, []), profiles, 1);
        Check(manager.Workspace.Profiles.Count == 2 &&
              manager.ActivePage.Id == manager.ActiveProfile.DefaultPageId,
            "Each profile has an initial page and a stable DefaultPageId");

        var activeBefore = manager.ActivePageId;
        var created = manager.CreatePage("Música");
        Check(created.Id != activeBefore &&
              manager.SelectedPageId == created.Id &&
              manager.ActivePageId == activeBefore,
            "Creating and selecting a page does not activate it");
        manager.RenameSelected("Audio");
        Check(manager.SelectedPage.Name == "Audio", "Selected page can be renamed");
        var duplicate = manager.DuplicateSelected();
        Check(duplicate.Id != created.Id &&
              !ReferenceEquals(duplicate.Scene, created.Scene) &&
              !ReferenceEquals(duplicate.Scene.Elements, created.Scene.Elements),
            "Duplicate uses a new ID and a deep-copied DisplayScene");
        manager.MoveSelected(-1);
        Check(manager.EditingProfile.Pages.Select(p => p.Order)
                  .SequenceEqual(Enumerable.Range(0, manager.EditingProfile.Pages.Count)),
            "Page reorder normalizes order without renumbering IDs");
        manager.SetSelectedAsDefault();
        Check(manager.EditingProfile.DefaultPageId == duplicate.Id,
            "Selected page can become the profile default");
        var count = manager.EditingProfile.Pages.Count;
        Check(manager.DeleteSelected() && manager.EditingProfile.Pages.Count == count - 1,
            "A selected page can be deleted when alternatives exist");
        manager.Undo();
        Check(manager.EditingProfile.Pages.Count == count,
            "Page deletion participates in undo");
        manager.Redo();
        Check(manager.EditingProfile.Pages.Count == count - 1,
            "Page deletion participates in redo");

        manager.SelectProfileForEditing(2);
        Check(manager.EditingProfileId == 2 && manager.ActiveProfileId == 1 &&
              manager.SelectedPageId != manager.ActivePageId,
            "Editing profile and SelectedPage remain separate from ActivePage");
        Check(!manager.DeleteSelected(),
            "The only page of a profile cannot be deleted");

        manager.ActivateProfile(1);
        var first = manager.ActiveProfile.Pages.OrderBy(p => p.Order).First();
        var last = manager.ActiveProfile.Pages.OrderBy(p => p.Order).Last();
        manager.NavigateToPage(first.Id);
        Check(!manager.PreviousPage(), "PreviousPage stops at the first page");
        while (manager.NextPage()) { }
        Check(manager.ActivePageId == last.Id && !manager.NextPage(),
            "NextPage navigates explicitly and stops at the last page");
        manager.ActivateProfile(2);
        Check(manager.ActiveProfileId == 2 &&
              manager.ActivePageId == manager.ActiveProfile.DefaultPageId,
            "Successful profile visual activation resolves its default page");

        var clock = new AdvancingClock();
        var engine = new DisplayEngine(fallbackScene, clock,
            new DisplayAnimationOptions(FrameInterval: TimeSpan.FromMilliseconds(80)));
        foreach (var transition in Enum.GetValues<DisplayPageTransition>())
        {
            var target = fallbackScene with { Id = $"target-{transition}" };
            var sawTransition = false;
            engine.StateChanged += Observe;
            await engine.NavigateAsync(target, transition);
            engine.StateChanged -= Observe;
            Check(engine.State.BaseScene.Id == target.Id &&
                  engine.State.PageTransition is null &&
                  (transition == DisplayPageTransition.None || sawTransition),
                $"Page transition {transition} completes without an idle render loop");
            void Observe(object? _, DisplayState state) =>
                sawTransition |= state.PageTransition?.Kind == transition;
        }

        var directory = Path.Combine(
            Path.GetTempPath(), $"StreamDeckDIY.PageStoreTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "display-layout.json");
            var legacyJson = JsonSerializer.Serialize(new
            {
                Version = 1,
                Configuration = configuration,
                Scene = fallbackScene,
            });
            await File.WriteAllTextAsync(path, legacyJson);
            var store = new DisplayLayoutStore(path);
            var fallback = manager.Workspace;
            var migrated = await store.LoadAsync(fallback, 2);
            var migratedScene = migrated.Workspace.Profiles
                .Single(p => p.ProfileId == 2).Pages.First().Scene;
            Check(migrated.Migrated &&
                  migratedScene.Id == fallbackScene.Id &&
                  migratedScene.Elements.Count == fallbackScene.Elements.Count &&
                  migratedScene.Background == fallbackScene.Background,
                "v1.4 single layout migrates to the active profile default page");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Test failed: {name}");
    }

    private sealed class AdvancingClock : IDisplayAnimationClock
    {
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UnixEpoch;

        public async Task DelayAsync(
            TimeSpan duration,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            UtcNow += duration;
        }
    }

    private sealed class RecordingLayoutStore : IDisplayLayoutStore
    {
        public int SaveCount { get; private set; }
        public TaskCompletionSource Saved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DisplayLayoutLoadResult> LoadAsync(
            DisplayWorkspace fallback,
            uint activeProfileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DisplayLayoutLoadResult(fallback));
        public Task SaveAsync(
            DisplayWorkspace workspace,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            Saved.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class ControlledAutosaveDelay : IDisplayAutosaveDelay
    {
        private TaskCompletionSource? latest;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var source = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            latest = source;
            cancellationToken.Register(() => source.TrySetCanceled(cancellationToken));
            return source.Task;
        }
        public void ReleaseLatest() => latest?.TrySetResult();
    }
}
