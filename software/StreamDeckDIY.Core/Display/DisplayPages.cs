namespace StreamDeckDIY.Core.Display;

public enum DisplayPageTransition { None, Fade, SlideLeft, SlideRight }
public enum ProfileActivationVisual { None, HackerMatrix }

public sealed record DisplayPage(
    string Id,
    string Name,
    DisplayScene Scene,
    DisplayPageTransition Transition = DisplayPageTransition.Fade,
    int Order = 0);

public sealed record DisplayProfilePages(
    uint ProfileId,
    string ProfileName,
    string DefaultPageId,
    IReadOnlyList<DisplayPage> Pages,
    ProfileActivationVisual ActivationVisual = ProfileActivationVisual.HackerMatrix);

public sealed record DisplayWorkspace(
    DisplayConfiguration Configuration,
    IReadOnlyList<DisplayProfilePages> Profiles);

public sealed record DisplayProfileReference(uint Id, string Name);

public sealed class DisplayPagesChangedEventArgs(
    bool workspaceChanged,
    bool selectionChanged,
    bool activePageChanged,
    bool transient = false) : EventArgs
{
    public bool WorkspaceChanged { get; } = workspaceChanged;
    public bool SelectionChanged { get; } = selectionChanged;
    public bool ActivePageChanged { get; } = activePageChanged;
    public bool IsTransient { get; } = transient;
}

public sealed class DisplayPageManager
{
    private sealed record Snapshot(
        DisplayWorkspace Workspace,
        uint EditingProfileId,
        uint ActiveProfileId,
        string SelectedPageId,
        string ActivePageId);

    private readonly Func<string> idFactory;
    private readonly Func<string, string, DisplayScene> sceneFactory;
    private readonly List<Snapshot> undo = [];
    private readonly List<Snapshot> redo = [];
    private const int HistoryLimit = 75;

    public DisplayPageManager(
        DisplayConfiguration configuration,
        Func<string, string, DisplayScene> sceneFactory,
        Func<string>? idFactory = null)
    {
        this.sceneFactory = sceneFactory;
        this.idFactory = idFactory ?? (() => $"page-{Guid.NewGuid():N}");
        Workspace = new(configuration, []);
    }

    public event EventHandler<DisplayPagesChangedEventArgs>? Changed;
    public DisplayWorkspace Workspace { get; private set; }
    public uint EditingProfileId { get; private set; }
    public uint ActiveProfileId { get; private set; }
    public string SelectedPageId { get; private set; } = string.Empty;
    public string ActivePageId { get; private set; } = string.Empty;
    public DisplayProfilePages EditingProfile => Profile(EditingProfileId);
    public DisplayProfilePages ActiveProfile => Profile(ActiveProfileId);
    public DisplayPage SelectedPage => Page(EditingProfile, SelectedPageId);
    public DisplayPage ActivePage => Page(ActiveProfile, ActivePageId);
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public void Load(
        DisplayWorkspace workspace,
        IReadOnlyList<DisplayProfileReference> profiles,
        uint activeProfileId)
    {
        Workspace = Reconcile(workspace, profiles);
        var firstProfile = Workspace.Profiles.First();
        ActiveProfileId = Workspace.Profiles.Any(p => p.ProfileId == activeProfileId)
            ? activeProfileId : firstProfile.ProfileId;
        EditingProfileId = ActiveProfileId;
        ActivePageId = ValidDefault(ActiveProfile);
        SelectedPageId = ActivePageId;
        undo.Clear();
        redo.Clear();
        Raise(true, true, true);
    }

    public void SynchronizeProfiles(IReadOnlyList<DisplayProfileReference> profiles)
    {
        if (profiles.Count == 0) return;
        Workspace = Reconcile(Workspace, profiles);
        if (!Workspace.Profiles.Any(p => p.ProfileId == ActiveProfileId))
            ActiveProfileId = Workspace.Profiles[0].ProfileId;
        if (!Workspace.Profiles.Any(p => p.ProfileId == EditingProfileId))
            EditingProfileId = ActiveProfileId;
        ActivePageId = ResolvePageId(ActiveProfile, ActivePageId);
        SelectedPageId = ResolvePageId(EditingProfile, SelectedPageId);
        Raise(true, true, true);
    }

    public void SelectProfileForEditing(uint profileId)
    {
        if (!Workspace.Profiles.Any(p => p.ProfileId == profileId) ||
            EditingProfileId == profileId) return;
        EditingProfileId = profileId;
        SelectedPageId = ValidDefault(EditingProfile);
        Raise(false, true, false);
    }

    public void ActivateProfile(uint profileId)
    {
        if (!Workspace.Profiles.Any(p => p.ProfileId == profileId)) return;
        ActiveProfileId = profileId;
        EditingProfileId = profileId;
        ActivePageId = ValidDefault(ActiveProfile);
        SelectedPageId = ActivePageId;
        Raise(false, true, true);
    }

    public void SelectPage(string pageId)
    {
        if (SelectedPageId == pageId || EditingProfile.Pages.All(p => p.Id != pageId)) return;
        SelectedPageId = pageId;
        Raise(false, true, false);
    }

    public DisplayPage CreatePage(string name)
    {
        name = ValidateName(name);
        var before = Capture();
        var id = UniqueId();
        var page = new DisplayPage(
            id, name, sceneFactory(EditingProfile.ProfileName, name),
            DisplayPageTransition.Fade, EditingProfile.Pages.Count);
        ReplaceEditingProfile(EditingProfile with
        {
            Pages = EditingProfile.Pages.Append(page).ToArray(),
        });
        SelectedPageId = id;
        Commit(before);
        Raise(true, true, false);
        return page;
    }

    public void RenameSelected(string name)
    {
        name = ValidateName(name);
        if (SelectedPage.Name == name) return;
        var before = Capture();
        ReplacePage(SelectedPage with { Name = name });
        Commit(before);
        Raise(true, true, false);
    }

    public DisplayPage DuplicateSelected(string? name = null)
    {
        var before = Capture();
        var source = SelectedPage;
        var id = UniqueId();
        var page = source with
        {
            Id = id,
            Name = ValidateName(name ?? $"{source.Name} copia"),
            Scene = CloneScene(source.Scene, $"scene-{id}"),
            Order = EditingProfile.Pages.Count,
        };
        ReplaceEditingProfile(EditingProfile with
        {
            Pages = EditingProfile.Pages.Append(page).ToArray(),
        });
        SelectedPageId = id;
        Commit(before);
        Raise(true, true, false);
        return page;
    }

    public bool DeleteSelected()
    {
        if (EditingProfile.Pages.Count <= 1) return false;
        var before = Capture();
        var removedId = SelectedPageId;
        var pages = Normalize(EditingProfile.Pages.Where(p => p.Id != removedId));
        var defaultId = EditingProfile.DefaultPageId == removedId
            ? pages[0].Id : EditingProfile.DefaultPageId;
        ReplaceEditingProfile(EditingProfile with
        {
            Pages = pages,
            DefaultPageId = defaultId,
        });
        SelectedPageId = pages[0].Id;
        if (EditingProfileId == ActiveProfileId && ActivePageId == removedId)
            ActivePageId = defaultId;
        Commit(before);
        Raise(true, true, ActivePageId == defaultId);
        return true;
    }

    public void MoveSelected(int delta)
    {
        var pages = EditingProfile.Pages.OrderBy(p => p.Order).ToList();
        var index = pages.FindIndex(p => p.Id == SelectedPageId);
        var destination = Math.Clamp(index + delta, 0, pages.Count - 1);
        if (index == destination) return;
        var before = Capture();
        var page = pages[index];
        pages.RemoveAt(index);
        pages.Insert(destination, page);
        ReplaceEditingProfile(EditingProfile with { Pages = Normalize(pages) });
        Commit(before);
        Raise(true, true, false);
    }

    public void SetSelectedAsDefault()
    {
        if (EditingProfile.DefaultPageId == SelectedPageId) return;
        var before = Capture();
        ReplaceEditingProfile(EditingProfile with { DefaultPageId = SelectedPageId });
        Commit(before);
        Raise(true, true, false);
    }

    public bool NavigateToPage(string pageId)
    {
        if (ActiveProfile.Pages.All(p => p.Id != pageId) || ActivePageId == pageId)
            return false;
        ActivePageId = pageId;
        Raise(false, false, true);
        return true;
    }

    public bool NextPage()
    {
        var pages = ActiveProfile.Pages.OrderBy(p => p.Order).ToArray();
        var index = Array.FindIndex(pages, p => p.Id == ActivePageId);
        return index >= 0 && index < pages.Length - 1 && NavigateToPage(pages[index + 1].Id);
    }

    public bool PreviousPage()
    {
        var pages = ActiveProfile.Pages.OrderBy(p => p.Order).ToArray();
        var index = Array.FindIndex(pages, p => p.Id == ActivePageId);
        return index > 0 && NavigateToPage(pages[index - 1].Id);
    }

    public void UpdateSelectedScene(DisplayScene scene, bool transient)
    {
        ReplacePage(SelectedPage with { Scene = scene });
        Raise(!transient, false, EditingProfileId == ActiveProfileId &&
            SelectedPageId == ActivePageId, transient);
    }

    public void UpdateRuntime(
        uint profileId,
        Func<DisplayElement, DisplayElement> update)
    {
        var profile = Profile(profileId);
        var updated = profile with
        {
            Pages = profile.Pages.Select(page => page with
            {
                Scene = page.Scene with
                {
                    Elements = page.Scene.Elements.Select(update).ToArray(),
                },
            }).ToArray(),
        };
        Workspace = Workspace with
        {
            Profiles = Workspace.Profiles
                .Select(p => p.ProfileId == profileId ? updated : p).ToArray(),
        };
        Raise(false, profileId == EditingProfileId, profileId == ActiveProfileId, true);
    }

    public void SetSelectedTransition(DisplayPageTransition transition)
    {
        if (SelectedPage.Transition == transition) return;
        var before = Capture();
        ReplacePage(SelectedPage with { Transition = transition });
        Commit(before);
        Raise(true, true, false);
    }

    public void Undo()
    {
        if (!CanUndo) return;
        redo.Add(Capture());
        Restore(undo[^1]);
        undo.RemoveAt(undo.Count - 1);
        Raise(true, true, true);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        Push(undo, Capture());
        Restore(redo[^1]);
        redo.RemoveAt(redo.Count - 1);
        Raise(true, true, true);
    }

    private DisplayWorkspace Reconcile(
        DisplayWorkspace source,
        IReadOnlyList<DisplayProfileReference> references)
    {
        var requested = references.Count == 0
            ? [new DisplayProfileReference(0, "Sin perfil")]
            : references;
        var existing = source.Profiles.ToDictionary(p => p.ProfileId);
        var profiles = requested.Select(reference =>
        {
            if (existing.TryGetValue(reference.Id, out var current))
            {
                var pages = current.Pages.Count == 0
                    ? [CreateDefaultPage(reference.Name)]
                    : Normalize(current.Pages);
                return current with
                {
                    ProfileName = reference.Name,
                    Pages = pages,
                    DefaultPageId = pages.Any(p => p.Id == current.DefaultPageId)
                        ? current.DefaultPageId : pages[0].Id,
                };
            }
            var page = CreateDefaultPage(reference.Name);
            return new DisplayProfilePages(
                reference.Id, reference.Name, page.Id, [page]);
        }).ToArray();
        return source with { Profiles = profiles };
    }

    private DisplayPage CreateDefaultPage(string profileName)
    {
        var id = UniqueId();
        return new(id, "Principal", sceneFactory(profileName, "Principal"));
    }

    private string UniqueId()
    {
        string id;
        do id = idFactory(); while (Workspace.Profiles.SelectMany(p => p.Pages).Any(p => p.Id == id));
        return id;
    }

    private DisplayProfilePages Profile(uint id) =>
        Workspace.Profiles.First(p => p.ProfileId == id);
    private static DisplayPage Page(DisplayProfilePages profile, string id) =>
        profile.Pages.First(p => p.Id == id);
    private static string ValidDefault(DisplayProfilePages profile) =>
        ResolvePageId(profile, profile.DefaultPageId);
    private static string ResolvePageId(DisplayProfilePages profile, string id) =>
        profile.Pages.Any(p => p.Id == id) ? id : profile.Pages.OrderBy(p => p.Order).First().Id;

    private void ReplacePage(DisplayPage page)
    {
        ReplaceEditingProfile(EditingProfile with
        {
            Pages = EditingProfile.Pages.Select(p => p.Id == page.Id ? page : p).ToArray(),
        });
    }

    private void ReplaceEditingProfile(DisplayProfilePages profile)
    {
        Workspace = Workspace with
        {
            Profiles = Workspace.Profiles
                .Select(p => p.ProfileId == profile.ProfileId ? profile : p).ToArray(),
        };
    }

    private static DisplayPage[] Normalize(IEnumerable<DisplayPage> pages) =>
        pages.Select((page, index) => page with { Order = index }).ToArray();

    private static DisplayScene CloneScene(DisplayScene scene, string id) => scene with
    {
        Id = id,
        Elements = scene.Elements.Select(CloneElement).ToArray(),
    };

    private static DisplayElement CloneElement(DisplayElement element) => element switch
    {
        DisplayTextElement text => text with
        {
            Animations = text.Animations?.Select(a => a with { }).ToArray(),
        },
        DisplayRectangleElement rectangle => rectangle with
        {
            Animations = rectangle.Animations?.Select(a => a with { }).ToArray(),
        },
        DisplayImageElement image => image with
        {
            Animations = image.Animations?.Select(a => a with { }).ToArray(),
        },
        _ => throw new NotSupportedException(),
    };

    private Snapshot Capture() => new(
        Workspace, EditingProfileId, ActiveProfileId, SelectedPageId, ActivePageId);
    private void Restore(Snapshot snapshot)
    {
        Workspace = snapshot.Workspace;
        EditingProfileId = snapshot.EditingProfileId;
        ActiveProfileId = snapshot.ActiveProfileId;
        SelectedPageId = snapshot.SelectedPageId;
        ActivePageId = snapshot.ActivePageId;
    }
    private void Commit(Snapshot before)
    {
        Push(undo, before);
        redo.Clear();
    }
    private static void Push(List<Snapshot> history, Snapshot snapshot)
    {
        history.Add(snapshot);
        if (history.Count > HistoryLimit) history.RemoveAt(0);
    }
    private void Raise(bool workspace, bool selection, bool active, bool transient = false) =>
        Changed?.Invoke(this, new(workspace, selection, active, transient));
    private static string ValidateName(string name)
    {
        var value = name?.Trim();
        return string.IsNullOrEmpty(value)
            ? throw new ArgumentException("El nombre de la página es obligatorio.", nameof(name))
            : value;
    }
}
