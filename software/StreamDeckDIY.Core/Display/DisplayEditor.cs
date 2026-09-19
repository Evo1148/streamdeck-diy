namespace StreamDeckDIY.Core.Display;

public enum DisplayEditorMode { Edit, Preview }
public sealed record DisplayEditorSettings(
    double GridSize = 5, bool ShowGrid = true, bool SnapEnabled = true,
    double MinimumSize = 10, double MinimumVisible = 10, int HistoryLimit = 75);
public sealed class DisplaySceneChangedEventArgs(DisplayScene scene, bool isTransient) : EventArgs
{
    public DisplayScene Scene { get; } = scene;
    public bool IsTransient { get; } = isTransient;
}

public sealed class DisplayEditor
{
    private sealed record EditorSession(
        DisplayScene Scene,
        string? SelectedId,
        DisplayScene[] Undo,
        DisplayScene[] Redo);
    private readonly List<DisplayScene> undo = [];
    private readonly List<DisplayScene> redo = [];
    private readonly Dictionary<string, EditorSession> sessions = [];
    private DisplayScene? interactionStart;
    private string? documentKey;
    private int nextId = 1;

    public DisplayEditor(DisplayConfiguration configuration, DisplayScene scene,
        DisplayEditorSettings? settings = null)
    {
        Configuration = configuration;
        Settings = settings ?? new();
        Scene = NormalizeScene(scene);
        RecalculateNextId();
    }

    public event EventHandler<DisplaySceneChangedEventArgs>? SceneChanged;
    public event EventHandler? StateChanged;
    public DisplayConfiguration Configuration { get; private set; }
    public DisplayScene Scene { get; private set; }
    public DisplayEditorSettings Settings { get; private set; }
    public DisplayEditorMode Mode { get; private set; } = DisplayEditorMode.Edit;
    public string? SelectedId { get; private set; }
    public DisplayElement? Selected => Scene.Elements.FirstOrDefault(e => e.Id == SelectedId);
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public void Load(DisplayConfiguration configuration, DisplayScene scene)
    {
        Configuration = configuration;
        Scene = NormalizeScene(scene);
        SelectedId = null;
        undo.Clear(); redo.Clear();
        RecalculateNextId();
        Raise(false);
    }

    public void SwitchDocument(
        string key,
        DisplayConfiguration configuration,
        DisplayScene scene)
    {
        if (documentKey is not null)
            sessions[documentKey] = new(Scene, SelectedId, undo.ToArray(), redo.ToArray());
        documentKey = key;
        Configuration = configuration;
        if (sessions.TryGetValue(key, out var session))
        {
            Scene = session.Scene;
            SelectedId = session.SelectedId;
            undo.Clear(); undo.AddRange(session.Undo);
            redo.Clear(); redo.AddRange(session.Redo);
        }
        else
        {
            Scene = NormalizeScene(scene);
            SelectedId = null;
            undo.Clear(); redo.Clear();
        }
        RecalculateNextId();
        Raise(false);
    }

    public void SetMode(DisplayEditorMode mode)
    {
        Mode = mode;
        if (mode == DisplayEditorMode.Preview) SelectedId = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetGrid(bool show, bool snap)
    {
        Settings = Settings with { ShowGrid = show, SnapEnabled = snap };
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public DisplayElement AddText() => Add(new DisplayTextElement(
        NextId("text"), 90, 104, 140, 32, "Nuevo texto", 18,
        DisplayTextAlignment.Center, "#FFFFFF"));
    public DisplayElement AddRectangle() => Add(new DisplayRectangleElement(
        NextId("rectangle"), 100, 85, 120, 70, "#243447", 8));
    public DisplayElement AddImage() => Add(new DisplayImageElement(
        NextId("image"), 110, 70, 100, 100, "placeholder"));

    public void Select(string? id)
    {
        SelectedId = id is not null && Scene.Elements.Any(e => e.Id == id) ? id : null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public DisplayElement? SelectAt(DisplayPoint point)
    {
        var selected = Scene.Elements.Select((element, index) => (element, index))
            .Where(i => i.element.Visible && i.element.Opacity > 0 &&
                point.X >= i.element.X && point.X <= i.element.X + i.element.Width &&
                point.Y >= i.element.Y && point.Y <= i.element.Y + i.element.Height)
            .OrderByDescending(i => i.element.ZIndex).ThenByDescending(i => i.index)
            .Select(i => i.element).FirstOrDefault();
        Select(selected?.Id);
        return selected;
    }

    public void BeginInteraction() => interactionStart ??= Scene;

    public void MoveSelected(double x, double y, bool transient = false)
    {
        if (Selected is not { } selected) return;
        x = Math.Clamp(Snap(x), -selected.Width + Settings.MinimumVisible,
            Configuration.Width - Settings.MinimumVisible);
        y = Math.Clamp(Snap(y), -selected.Height + Settings.MinimumVisible,
            Configuration.Height - Settings.MinimumVisible);
        Replace(selected with { X = x, Y = y }, transient, !transient);
    }

    public void ResizeSelected(double width, double height, bool transient = false)
    {
        if (Selected is not { } selected) return;
        width = Math.Max(Settings.MinimumSize, Snap(width));
        height = Math.Max(Settings.MinimumSize, Snap(height));
        width = Math.Min(width, Configuration.Width - Math.Max(0, selected.X) + Math.Max(0, -selected.X));
        height = Math.Min(height, Configuration.Height - Math.Max(0, selected.Y) + Math.Max(0, -selected.Y));
        Replace(selected with { Width = width, Height = height }, transient, !transient);
    }

    public void CommitInteraction()
    {
        if (interactionStart is null) return;
        if (!Equals(interactionStart, Scene))
        {
            PushUndo(interactionStart);
            redo.Clear();
            Raise(false);
        }
        interactionStart = null;
    }

    public void CancelInteraction()
    {
        if (interactionStart is null) return;
        Scene = interactionStart;
        interactionStart = null;
        Raise(false);
    }

    public void UpdateSelected(Func<DisplayElement, DisplayElement> update)
    {
        if (Selected is not { } selected) return;
        Replace(Validate(update(selected)), false, true);
    }

    public void UpdateRuntime(Func<DisplayElement, DisplayElement> update)
    {
        var elements = Scene.Elements.Select(update).ToArray();
        Scene = Scene with { Elements = elements };
        Raise(true);
    }

    public void SetBackground(string color)
    {
        if (DisplayColor.TryNormalize(color, out var value) && value != Scene.Background)
            Change(Scene with { Background = value });
    }

    public DisplayElement? DuplicateSelected()
    {
        if (Selected is not { } source) return null;
        var id = NextId(source switch
        {
            DisplayTextElement => "text", DisplayRectangleElement => "rectangle", _ => "image",
        });
        var maxZ = Scene.Elements.Count == 0 ? 0 : Scene.Elements.Max(e => e.ZIndex) + 1;
        var copy = source with
        {
            Id = id,
            X = Math.Min(source.X + Settings.GridSize, Configuration.Width - Settings.MinimumVisible),
            Y = Math.Min(source.Y + Settings.GridSize, Configuration.Height - Settings.MinimumVisible),
            ZIndex = maxZ,
        };
        return Add(copy);
    }

    public void DeleteSelected()
    {
        if (SelectedId is null) return;
        var elements = Scene.Elements.Where(e => e.Id != SelectedId).ToArray();
        SelectedId = null;
        Change(Scene with { Elements = Normalize(elements) });
    }

    public void MoveLayerUp() => ReorderSelected(1);
    public void MoveLayerDown() => ReorderSelected(-1);
    public void BringToFront() => ReorderSelected(int.MaxValue);
    public void SendToBack() => ReorderSelected(int.MinValue);

    public void Undo()
    {
        if (!CanUndo) return;
        redo.Add(Scene);
        Scene = undo[^1]; undo.RemoveAt(undo.Count - 1);
        EnsureSelection(); Raise(false);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        PushUndo(Scene);
        Scene = redo[^1]; redo.RemoveAt(redo.Count - 1);
        EnsureSelection(); Raise(false);
    }

    private DisplayElement Add(DisplayElement element)
    {
        Change(Scene with { Elements = Normalize(Scene.Elements.Append(element)) });
        SelectedId = element.Id;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Selected!;
    }

    private void ReorderSelected(int direction)
    {
        if (Selected is null) return;
        var ordered = Scene.Elements.OrderBy(e => e.ZIndex).ToList();
        var index = ordered.FindIndex(e => e.Id == SelectedId);
        var destination = direction switch
        {
            int.MaxValue => ordered.Count - 1, int.MinValue => 0,
            > 0 => Math.Min(ordered.Count - 1, index + 1),
            _ => Math.Max(0, index - 1),
        };
        if (index == destination) return;
        var item = ordered[index]; ordered.RemoveAt(index); ordered.Insert(destination, item);
        Change(Scene with { Elements = Normalize(ordered) });
    }

    private void Replace(DisplayElement updated, bool transient, bool record)
    {
        var old = Scene.Elements.FirstOrDefault(e => e.Id == updated.Id);
        if (old is null || Equals(old, updated)) return;
        var next = Scene with
        {
            Elements = Normalize(Scene.Elements
                .Select(e => e.Id == updated.Id ? updated : e)
                .OrderBy(e => e.ZIndex)),
        };
        if (record) Change(next);
        else { Scene = next; Raise(transient); }
    }

    private void Change(DisplayScene next)
    {
        if (Equals(Scene, next)) return;
        PushUndo(Scene); redo.Clear(); Scene = next; Raise(false);
    }

    private void PushUndo(DisplayScene scene)
    {
        undo.Add(scene);
        if (undo.Count > Settings.HistoryLimit) undo.RemoveAt(0);
    }

    private void Raise(bool transient)
    {
        SceneChanged?.Invoke(this, new(Scene, transient));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private double Snap(double value) => Settings.SnapEnabled
        ? Math.Round(value / Settings.GridSize, MidpointRounding.AwayFromZero) * Settings.GridSize : value;

    private DisplayElement Validate(DisplayElement element)
    {
        static double Safe(double value, double fallback) => double.IsFinite(value) ? value : fallback;
        return element with
        {
            X = Safe(element.X, 0), Y = Safe(element.Y, 0),
            Width = Math.Max(Settings.MinimumSize, Safe(element.Width, Settings.MinimumSize)),
            Height = Math.Max(Settings.MinimumSize, Safe(element.Height, Settings.MinimumSize)),
            Opacity = Math.Clamp(Safe(element.Opacity, 1), 0, 1),
        };
    }

    private DisplayScene NormalizeScene(DisplayScene scene) => scene with
    {
        Background = DisplayColor.Normalize(scene.Background, "#09111A"),
        Elements = Normalize(scene.Elements.Select(Validate).OrderBy(e => e.ZIndex)),
    };
    private static DisplayElement[] Normalize(IEnumerable<DisplayElement> elements) =>
        elements.Select((element, index) => element with { ZIndex = index }).ToArray();
    private void EnsureSelection()
    {
        if (SelectedId is not null && Scene.Elements.All(e => e.Id != SelectedId)) SelectedId = null;
    }
    private string NextId(string prefix)
    {
        string id;
        do id = $"{prefix}-{nextId++}"; while (Scene.Elements.Any(e => e.Id == id));
        return id;
    }
    private void RecalculateNextId()
    {
        nextId = 1;
        while (Scene.Elements.Any(e => e.Id.EndsWith($"-{nextId}", StringComparison.Ordinal))) nextId++;
    }
}
