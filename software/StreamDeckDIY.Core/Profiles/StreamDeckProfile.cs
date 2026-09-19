using StreamDeckDIY.Protocol.Models;

namespace StreamDeckDIY.Core.Profiles;

public enum SystemNavigation : byte { Previous = 9, Home = 10, Next = 11 }

public sealed record ProfilePage(uint Id, string Name, BindingInfo[] Bindings)
{
    public override string ToString() => Name;
}

public sealed record StreamDeckProfile(
    uint Id,
    string Name,
    BindingInfo[] Bindings)
{
    public ProfilePage[] Pages { get; init; } = [];
    public uint ActivePageId { get; init; } = 1;
    public uint DefaultPageId { get; init; } = 1;
    public uint NextPageId { get; init; } = 2;
    public BindingInfo[] LegacyBottomRowBindings { get; init; } = [];

    public ProfilePage ActivePage =>
        Pages.FirstOrDefault(page => page.Id == ActivePageId) ??
        throw new InvalidOperationException("El perfil no tiene una página activa válida.");

    public int ActivePageIndex => Array.FindIndex(Pages, page => page.Id == ActivePageId);
    public bool CanNavigatePrevious => ActivePageIndex > 0;
    public bool CanNavigateNext => ActivePageIndex >= 0 && ActivePageIndex < Pages.Length - 1;
    public override string ToString() => Name;
}
