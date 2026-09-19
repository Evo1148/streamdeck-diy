using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using StreamDeckDIY.App.Display;
using StreamDeckDIY.App.Services;
using StreamDeckDIY.Core.Display;

namespace StreamDeckDIY.App.ViewModels;

public sealed class DisplayLayerItem(DisplayElement element, bool isSelected)
{
    public string Id { get; } = element.Id;
    public string Type { get; } = element switch
    {
        DisplayTextElement => "Texto",
        DisplayRectangleElement => "Rectángulo",
        DisplayImageElement => "Imagen",
        _ => "Elemento",
    };
    public string VisibilityText { get; } = element.Visible ? "Visible" : "Oculto";
    public string DisplayName { get; } = FriendlyName(element);
    public string TypeGlyph => element switch
    {
        DisplayTextElement => "T",
        DisplayRectangleElement => "▭",
        DisplayImageElement => "▧",
        _ => "•",
    };
    public string VisibilityGlyph { get; } = element.Visible ? "●" : "○";
    public Visibility SelectionVisibility { get; } = isSelected
        ? Visibility.Visible : Visibility.Collapsed;

    private static string FriendlyName(DisplayElement element) => element.Id switch
    {
        "header" => "Cabecera",
        "accent" => "Acento",
        "title" => "Título",
        "profile-label" => "Etiqueta de perfil",
        "profile" => "Perfil activo",
        "page-chip" => "Fondo de página",
        "page" => "Nombre de página",
        "status-dot" => "Indicador de estado",
        "status" => "Estado del dispositivo",
        _ => GenericName(element),
    };

    private static string GenericName(DisplayElement element)
    {
        var suffix = element.Id.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(part => int.TryParse(part, out _));
        var type = element switch
        {
            DisplayTextElement => "Texto",
            DisplayRectangleElement => "Rectángulo",
            DisplayImageElement => "Imagen",
            _ => "Elemento",
        };
        return suffix is null ? type : $"{type} {suffix}";
    }
}

public sealed class DisplayPageItem(
    DisplayPage page, bool isDefault, bool isActive, bool isSelected)
{
    public string Id { get; } = page.Id;
    public string Name { get; } = page.Name;
    public Visibility DefaultVisibility { get; } = isDefault
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ActiveVisibility { get; } = isActive
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SelectionVisibility { get; } = isSelected
        ? Visibility.Visible : Visibility.Collapsed;
}

public sealed record DisplayZoomOption(string Name, double Scale)
{
    public override string ToString() => Name;
}

public sealed class DisplayEditorViewModel : ObservableObject, IAsyncDisposable
{
    private enum PointerOperation { None, Move, Resize }
    private readonly DisplayEditor editor;
    private readonly DisplayPageManager pages;
    private readonly IDisplayLayoutStore store;
    private readonly DisplayLayoutAutosave autosave;
    private readonly IDisplayEngine displayEngine;
    private readonly IDisplayPageInteraction interaction;
    private bool refreshing;
    private bool loadingEditor;
    private bool initialized;
    private PointerOperation pointerOperation;
    private DisplayPoint pointerOrigin;
    private DisplayElement? elementOrigin;
    private DisplayLayerItem? selectedLayer;
    private DisplayPageItem? selectedPageItem;
    private string feedback = "Guardado";
    private string background;
    private DisplayZoomOption selectedZoom;

    public DisplayEditorViewModel(
        DisplayEditor editor,
        DisplayPageManager pages,
        IDisplayLayoutStore store,
        DisplayLayoutAutosave autosave,
        IDisplayEngine displayEngine,
        IDisplayPageInteraction interaction)
    {
        this.editor = editor;
        this.pages = pages;
        this.store = store;
        this.autosave = autosave;
        this.displayEngine = displayEngine;
        this.interaction = interaction;
        background = editor.Scene.Background;
        ZoomOptions =
        [
            new("100 %", 1.0),
            new("125 %", 1.25),
            new("150 %", 1.5),
            new("200 %", 2.0),
        ];
        selectedZoom = ZoomOptions[^1];
        editor.SceneChanged += OnSceneChanged;
        editor.StateChanged += OnEditorStateChanged;
        pages.Changed += OnPagesChanged;
        autosave.SaveFailed += OnSaveFailed;

        AddTextCommand = new RelayCommand(_ => editor.AddText());
        AddRectangleCommand = new RelayCommand(_ => editor.AddRectangle());
        AddImageCommand = new RelayCommand(_ => editor.AddImage());
        DuplicateCommand = new RelayCommand(_ => editor.DuplicateSelected());
        DeleteCommand = new RelayCommand(_ => editor.DeleteSelected());
        UndoCommand = new RelayCommand(_ => editor.Undo());
        RedoCommand = new RelayCommand(_ => editor.Redo());
        LayerUpCommand = new RelayCommand(_ => editor.MoveLayerUp());
        LayerDownCommand = new RelayCommand(_ => editor.MoveLayerDown());
        BringFrontCommand = new RelayCommand(_ => editor.BringToFront());
        SendBackCommand = new RelayCommand(_ => editor.SendToBack());
        SaveCommand = new AsyncRelayCommand(_ => SaveNowAsync());
        NewPageCommand = new AsyncRelayCommand(_ => CreatePageAsync());
        RenamePageCommand = new AsyncRelayCommand(_ => RenamePageAsync());
        DuplicatePageCommand = new RelayCommand(_ =>
        {
            if (initialized) pages.DuplicateSelected();
        });
        DeletePageCommand = new AsyncRelayCommand(_ => DeletePageAsync());
        PageUpCommand = new RelayCommand(_ =>
        {
            if (initialized) pages.MoveSelected(-1);
        });
        PageDownCommand = new RelayCommand(_ =>
        {
            if (initialized) pages.MoveSelected(1);
        });
        SetDefaultPageCommand = new RelayCommand(_ =>
        {
            if (initialized) pages.SetSelectedAsDefault();
        });
        PageUndoCommand = new RelayCommand(_ => pages.Undo());
        PageRedoCommand = new RelayCommand(_ => pages.Redo());
        PreviousPageCommand = new AsyncRelayCommand(_ => NavigateAsync(previous: true));
        NextPageCommand = new AsyncRelayCommand(_ => NavigateAsync(previous: false));
        RefreshEditor();
    }

    public event EventHandler? RenderStateChanged;
    public ObservableCollection<DisplayLayerItem> Layers { get; } = [];
    public ObservableCollection<DisplayPageItem> Pages { get; } = [];
    public IReadOnlyList<DisplayTextAlignment> Alignments { get; } =
        Enum.GetValues<DisplayTextAlignment>();
    public IReadOnlyList<DisplayImageStretch> ImageStretches { get; } =
        Enum.GetValues<DisplayImageStretch>();
    public IReadOnlyList<DisplayPageTransition> PageTransitions { get; } =
        Enum.GetValues<DisplayPageTransition>();
    public IReadOnlyList<DisplayZoomOption> ZoomOptions { get; }

    public ICommand AddTextCommand { get; }
    public ICommand AddRectangleCommand { get; }
    public ICommand AddImageCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand LayerUpCommand { get; }
    public ICommand LayerDownCommand { get; }
    public ICommand BringFrontCommand { get; }
    public ICommand SendBackCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand NewPageCommand { get; }
    public ICommand RenamePageCommand { get; }
    public ICommand DuplicatePageCommand { get; }
    public ICommand DeletePageCommand { get; }
    public ICommand PageUpCommand { get; }
    public ICommand PageDownCommand { get; }
    public ICommand SetDefaultPageCommand { get; }
    public ICommand PageUndoCommand { get; }
    public ICommand PageRedoCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public DisplayEditor Editor => editor;
    public ProfileActivationVisual ActiveProfileVisual => initialized
        ? pages.ActiveProfile.ActivationVisual : ProfileActivationVisual.HackerMatrix;
    public DisplayZoomOption SelectedZoom
    {
        get => selectedZoom;
        set
        {
            if (value is null || !SetProperty(ref selectedZoom, value)) return;
            OnPropertyChanged(nameof(PreviewWidth));
            OnPropertyChanged(nameof(PreviewHeight));
        }
    }
    public double PreviewWidth => editor.Configuration.Width * SelectedZoom.Scale;
    public double PreviewHeight => editor.Configuration.Height * SelectedZoom.Scale;

    public bool IsEditMode
    {
        get => editor.Mode == DisplayEditorMode.Edit;
        set
        {
            var mode = value ? DisplayEditorMode.Edit : DisplayEditorMode.Preview;
            if (editor.Mode == mode) return;
            editor.SetMode(mode);
            if (value) LoadSelectedPageIntoEditor();
            else displayEngine.ShowScene(pages.ActivePage.Scene);
            RaiseModeProperties();
            RefreshPages();
            RenderStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public bool IsPreviewMode
    {
        get => !IsEditMode;
        set { if (value) IsEditMode = false; }
    }
    public Visibility EditorControlsVisibility =>
        IsEditMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PreviewControlsVisibility =>
        IsPreviewMode ? Visibility.Visible : Visibility.Collapsed;
    public bool ShowGrid
    {
        get => editor.Settings.ShowGrid;
        set
        {
            editor.SetGrid(value, SnapEnabled);
            OnPropertyChanged();
            RenderStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public bool SnapEnabled
    {
        get => editor.Settings.SnapEnabled;
        set { editor.SetGrid(ShowGrid, value); OnPropertyChanged(); }
    }
    public string Feedback { get => feedback; private set => SetProperty(ref feedback, value); }
    public string CurrentProfileText => initialized
        ? $"Páginas de {pages.EditingProfile.ProfileName}" : "Páginas";
    public string PagePositionText
    {
        get
        {
            if (!initialized) return "Página —";
            var ordered = pages.ActiveProfile.Pages.OrderBy(p => p.Order).ToArray();
            var index = Array.FindIndex(ordered, page => page.Id == pages.ActivePageId);
            return $"Página {index + 1} / {ordered.Length}  ·  {pages.ActivePage.Name}";
        }
    }
    public DisplayPageItem? SelectedPageItem
    {
        get => selectedPageItem;
        set
        {
            if (!SetProperty(ref selectedPageItem, value) || refreshing || value is null) return;
            pages.SelectPage(value.Id);
        }
    }
    public DisplayPageTransition SelectedPageTransition
    {
        get => initialized ? pages.SelectedPage.Transition : DisplayPageTransition.None;
        set { if (!refreshing && initialized) pages.SetSelectedTransition(value); }
    }
    public bool CanDeletePage => initialized && pages.EditingProfile.Pages.Count > 1;

    public string Background
    {
        get => background;
        set
        {
            if (!SetProperty(ref background, value) || refreshing) return;
            if (DisplayColor.TryNormalize(value, out var color))
            {
                editor.SetBackground(color);
                Feedback = "Fondo actualizado.";
            }
            else Feedback = "Color inválido. Usa #RRGGBB o #AARRGGBB.";
        }
    }
    public DisplayLayerItem? SelectedLayer
    {
        get => selectedLayer;
        set
        {
            if (!SetProperty(ref selectedLayer, value) || refreshing) return;
            editor.Select(value?.Id);
        }
    }
    public bool HasSelection => editor.Selected is not null;
    public Visibility InspectorVisibility => HasSelection ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyInspectorVisibility => HasSelection ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TextVisibility => editor.Selected is DisplayTextElement ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RectangleVisibility => editor.Selected is DisplayRectangleElement ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ImageVisibility => editor.Selected is DisplayImageElement ? Visibility.Visible : Visibility.Collapsed;
    public string SelectedId => editor.Selected?.Id ?? "Sin selección";
    public double X { get => editor.Selected?.X ?? 0; set => Update(e => e with { X = value }); }
    public double Y { get => editor.Selected?.Y ?? 0; set => Update(e => e with { Y = value }); }
    public double Width { get => editor.Selected?.Width ?? 0; set => Update(e => e with { Width = value }); }
    public double Height { get => editor.Selected?.Height ?? 0; set => Update(e => e with { Height = value }); }
    public int ZIndex { get => editor.Selected?.ZIndex ?? 0; set => Update(e => e with { ZIndex = value }); }
    public double Z { get => ZIndex; set { if (double.IsFinite(value)) ZIndex = (int)Math.Round(value); } }
    public bool Visible { get => editor.Selected?.Visible ?? false; set => Update(e => e with { Visible = value }); }
    public double Opacity { get => editor.Selected?.Opacity ?? 1; set => Update(e => e with { Opacity = value }); }
    public string Text
    {
        get => (editor.Selected as DisplayTextElement)?.Text ?? string.Empty;
        set => Update(e => e is DisplayTextElement text ? text with { Text = value ?? string.Empty } : e);
    }
    public double FontSize
    {
        get => (editor.Selected as DisplayTextElement)?.FontSize ?? 18;
        set => Update(e => e is DisplayTextElement text && double.IsFinite(value) && value > 0
            ? text with { FontSize = value } : e);
    }
    public string FontFamily
    {
        get => (editor.Selected as DisplayTextElement)?.FontFamily ?? string.Empty;
        set => Update(e => e is DisplayTextElement text
            ? text with { FontFamily = DisplayFontFamily.Normalize(value) } : e);
    }
    public DisplayTextAlignment Alignment
    {
        get => (editor.Selected as DisplayTextElement)?.Alignment ?? DisplayTextAlignment.Left;
        set => Update(e => e is DisplayTextElement text ? text with { Alignment = value } : e);
    }
    public string TextColor
    {
        get => (editor.Selected as DisplayTextElement)?.Color ?? "#FFFFFF";
        set => UpdateColor(value, (e, color) =>
            e is DisplayTextElement text ? text with { Color = color } : e);
    }
    public string Fill
    {
        get => (editor.Selected as DisplayRectangleElement)?.Fill ?? "#243447";
        set => UpdateColor(value, (e, color) =>
            e is DisplayRectangleElement rectangle ? rectangle with { Fill = color } : e);
    }
    public double CornerRadius
    {
        get => (editor.Selected as DisplayRectangleElement)?.CornerRadius ?? 0;
        set => Update(e => e is DisplayRectangleElement rectangle && double.IsFinite(value)
            ? rectangle with { CornerRadius = Math.Max(0, value) } : e);
    }
    public string AssetId
    {
        get => (editor.Selected as DisplayImageElement)?.AssetId ?? string.Empty;
        set => Update(e => e is DisplayImageElement image
            ? image with { AssetId = value ?? string.Empty } : e);
    }
    public DisplayImageStretch ImageStretch
    {
        get => (editor.Selected as DisplayImageElement)?.Stretch ?? DisplayImageStretch.Uniform;
        set => Update(e => e is DisplayImageElement image ? image with { Stretch = value } : e);
    }

    public async Task InitializeAsync(
        IReadOnlyList<DisplayProfileReference> profiles,
        uint activeProfileId,
        CancellationToken cancellationToken = default)
    {
        loadingEditor = true;
        try
        {
            pages.Load(new(editor.Configuration, []), profiles, activeProfileId);
            var result = await store.LoadAsync(pages.Workspace, activeProfileId, cancellationToken);
            pages.Load(result.Workspace, profiles, activeProfileId);
            initialized = true;
            LoadSelectedPageIntoEditor();
            displayEngine.ShowScene(pages.ActivePage.Scene);
            if (result.Warning is not null) Feedback = result.Warning;
            if (result.Migrated) autosave.Schedule(pages.Workspace);
        }
        finally { loadingEditor = false; }
        RefreshPages();
        RefreshEditor();
    }

    public void SynchronizeProfiles(
        IReadOnlyList<DisplayProfileReference> profiles,
        uint activeProfileId,
        uint? editingProfileId = null)
    {
        if (!initialized) return;
        pages.SynchronizeProfiles(profiles);
        if (editingProfileId is uint selected)
            pages.SelectProfileForEditing(selected);
        if (pages.ActiveProfileId != activeProfileId)
            pages.ActivateProfile(activeProfileId);
    }

    public void SelectProfileForEditing(uint profileId)
    {
        if (!initialized) return;
        pages.SelectProfileForEditing(profileId);
    }

    public async Task ActivateProfileAsync(
        uint profileId,
        CancellationToken cancellationToken = default)
    {
        if (!initialized) return;
        pages.ActivateProfile(profileId);
        LoadSelectedPageIntoEditor();
        await displayEngine.NavigateAsync(
            pages.ActivePage.Scene, pages.ActivePage.Transition, cancellationToken);
    }

    public void UpdateRuntimeState(uint profileId, string profileName, bool connected)
    {
        if (!initialized) return;
        pages.UpdateRuntime(profileId, element => element switch
        {
            DisplayTextElement { Id: "profile" } text => text with { Text = profileName },
            DisplayTextElement { Id: "page" } text => text with
                { Text = PageNameForScene(text, profileId) },
            DisplayTextElement { Id: "status" } text => text with
                { Text = connected ? "Conectado" : "Desconectado" },
            DisplayRectangleElement { Id: "status-dot" } rectangle => rectangle with
                { Fill = connected ? "#4ADE80" : "#F87171" },
            _ => element,
        });
    }

    public DisplayEditorRenderState RenderState() => new(
        IsEditMode, ShowGrid, editor.Settings.GridSize, editor.SelectedId);

    public void PointerPressed(DisplayPoint point)
    {
        if (!IsEditMode) return;
        var selected = editor.Selected;
        if (selected is not null &&
            Math.Abs(point.X - (selected.X + selected.Width)) <= 8 &&
            Math.Abs(point.Y - (selected.Y + selected.Height)) <= 8)
            pointerOperation = PointerOperation.Resize;
        else
        {
            selected = editor.SelectAt(point);
            pointerOperation = selected is null ? PointerOperation.None : PointerOperation.Move;
        }
        if (pointerOperation == PointerOperation.None) return;
        pointerOrigin = point;
        elementOrigin = selected;
        editor.BeginInteraction();
    }

    public void PointerMoved(DisplayPoint point)
    {
        if (elementOrigin is null) return;
        var dx = point.X - pointerOrigin.X;
        var dy = point.Y - pointerOrigin.Y;
        if (pointerOperation == PointerOperation.Move)
            editor.MoveSelected(elementOrigin.X + dx, elementOrigin.Y + dy, true);
        else if (pointerOperation == PointerOperation.Resize)
            editor.ResizeSelected(elementOrigin.Width + dx, elementOrigin.Height + dy, true);
    }

    public void PointerReleased()
    {
        if (pointerOperation != PointerOperation.None) editor.CommitInteraction();
        pointerOperation = PointerOperation.None;
        elementOrigin = null;
    }

    public async ValueTask DisposeAsync()
    {
        editor.SceneChanged -= OnSceneChanged;
        editor.StateChanged -= OnEditorStateChanged;
        pages.Changed -= OnPagesChanged;
        autosave.SaveFailed -= OnSaveFailed;
        if (initialized) await autosave.FlushAsync(pages.Workspace);
        await autosave.DisposeAsync();
    }

    private async Task CreatePageAsync()
    {
        if (!initialized) return;
        var name = await interaction.RequestNameAsync("Nueva página", "Nueva página");
        if (name is null) return;
        try { pages.CreatePage(name); }
        catch (ArgumentException exception) { Feedback = exception.Message; }
    }

    private async Task RenamePageAsync()
    {
        if (!initialized) return;
        var name = await interaction.RequestNameAsync("Renombrar página", pages.SelectedPage.Name);
        if (name is null) return;
        try { pages.RenameSelected(name); }
        catch (ArgumentException exception) { Feedback = exception.Message; }
    }

    private async Task DeletePageAsync()
    {
        if (!initialized) return;
        if (!CanDeletePage)
        {
            Feedback = "Cada perfil debe conservar al menos una página.";
            return;
        }
        if (await interaction.ConfirmDeleteAsync(pages.SelectedPage.Name))
            pages.DeleteSelected();
    }

    private async Task NavigateAsync(bool previous)
    {
        if (!initialized) return;
        var changed = previous ? pages.PreviousPage() : pages.NextPage();
        if (!changed) return;
        await displayEngine.NavigateAsync(
            pages.ActivePage.Scene, pages.ActivePage.Transition);
        RefreshPages();
    }

    private async Task SaveNowAsync()
    {
        if (!initialized) return;
        await autosave.FlushAsync(pages.Workspace);
        Feedback = "Diseño visual guardado.";
    }

    private void LoadSelectedPageIntoEditor()
    {
        if (!initialized) return;
        loadingEditor = true;
        try
        {
            editor.SwitchDocument(
                pages.SelectedPage.Id, pages.Workspace.Configuration,
                pages.SelectedPage.Scene);
            if (IsEditMode) displayEngine.ShowScene(editor.Scene);
        }
        finally { loadingEditor = false; }
        RefreshEditor();
    }

    private void Update(Func<DisplayElement, DisplayElement> update)
    {
        if (!refreshing) editor.UpdateSelected(update);
    }

    private void UpdateColor(string value, Func<DisplayElement, string, DisplayElement> update)
    {
        if (refreshing) return;
        if (DisplayColor.TryNormalize(value, out var color))
        {
            editor.UpdateSelected(element => update(element, color));
            Feedback = "Color actualizado.";
        }
        else Feedback = "Color inválido. Usa #RRGGBB o #AARRGGBB.";
    }

    private void OnSceneChanged(object? sender, DisplaySceneChangedEventArgs args)
    {
        if (!loadingEditor && initialized)
            pages.UpdateSelectedScene(args.Scene, args.IsTransient);
        if (IsEditMode) displayEngine.ShowScene(args.Scene);
        RefreshEditor();
        RenderStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditorStateChanged(object? sender, EventArgs args)
    {
        RefreshEditor();
        RenderStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPagesChanged(object? sender, DisplayPagesChangedEventArgs args)
    {
        if (initialized && args.SelectionChanged && IsEditMode && !loadingEditor)
            LoadSelectedPageIntoEditor();
        if (initialized && args.ActivePageChanged && IsPreviewMode)
            displayEngine.ShowScene(pages.ActivePage.Scene);
        if (args.WorkspaceChanged && !args.IsTransient)
            autosave.Schedule(pages.Workspace);
        RefreshPages();
    }

    private void RefreshPages()
    {
        if (!initialized) return;
        refreshing = true;
        try
        {
            Pages.Clear();
            foreach (var page in pages.EditingProfile.Pages.OrderBy(p => p.Order))
                Pages.Add(new(page,
                    page.Id == pages.EditingProfile.DefaultPageId,
                    pages.EditingProfileId == pages.ActiveProfileId &&
                        page.Id == pages.ActivePageId,
                    page.Id == pages.SelectedPageId));
            selectedPageItem = Pages.FirstOrDefault(item => item.Id == pages.SelectedPageId);
            OnPropertyChanged(nameof(SelectedPageItem));
            OnPropertyChanged(nameof(SelectedPageTransition));
            OnPropertyChanged(nameof(CurrentProfileText));
            OnPropertyChanged(nameof(PagePositionText));
            OnPropertyChanged(nameof(CanDeletePage));
            OnPropertyChanged(nameof(ActiveProfileVisual));
        }
        finally { refreshing = false; }
    }

    private void RefreshEditor()
    {
        refreshing = true;
        try
        {
            var selectedId = editor.SelectedId;
            Layers.Clear();
            foreach (var element in editor.Scene.Elements.OrderByDescending(e => e.ZIndex))
                Layers.Add(new(element, element.Id == selectedId));
            selectedLayer = Layers.FirstOrDefault(layer => layer.Id == selectedId);
            background = editor.Scene.Background;
            OnPropertyChanged(nameof(SelectedLayer));
            OnPropertyChanged(nameof(Background));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(InspectorVisibility));
            OnPropertyChanged(nameof(EmptyInspectorVisibility));
            OnPropertyChanged(nameof(TextVisibility));
            OnPropertyChanged(nameof(RectangleVisibility));
            OnPropertyChanged(nameof(ImageVisibility));
            OnPropertyChanged(nameof(SelectedId));
            OnPropertyChanged(nameof(X)); OnPropertyChanged(nameof(Y));
            OnPropertyChanged(nameof(Width)); OnPropertyChanged(nameof(Height));
            OnPropertyChanged(nameof(ZIndex)); OnPropertyChanged(nameof(Z));
            OnPropertyChanged(nameof(Visible)); OnPropertyChanged(nameof(Opacity));
            OnPropertyChanged(nameof(Text)); OnPropertyChanged(nameof(FontSize));
            OnPropertyChanged(nameof(FontFamily)); OnPropertyChanged(nameof(Alignment));
            OnPropertyChanged(nameof(TextColor)); OnPropertyChanged(nameof(Fill));
            OnPropertyChanged(nameof(CornerRadius)); OnPropertyChanged(nameof(AssetId));
            OnPropertyChanged(nameof(ImageStretch));
        }
        finally { refreshing = false; }
    }

    private string PageNameForScene(DisplayTextElement element, uint profileId)
    {
        var profile = pages.Workspace.Profiles.First(p => p.ProfileId == profileId);
        return profile.Pages.FirstOrDefault(p => p.Scene.Elements.Any(e => ReferenceEquals(e, element)))?.Name
            ?? element.Text;
    }

    private void RaiseModeProperties()
    {
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(IsPreviewMode));
        OnPropertyChanged(nameof(EditorControlsVisibility));
        OnPropertyChanged(nameof(PreviewControlsVisibility));
    }

    private void OnSaveFailed(object? sender, Exception exception) =>
        Feedback = $"No se pudo guardar el diseño: {exception.Message}";

    private sealed class RelayCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute(parameter);
    }
}
