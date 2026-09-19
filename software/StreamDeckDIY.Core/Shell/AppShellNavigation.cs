namespace StreamDeckDIY.Core.Shell;

public enum AppModule
{
    Controls,
    Profiles,
    Display,
    Actions,
    Automation,
    Macros,
    Device,
    VisualEditor,
}

public sealed class AppShellNavigation
{
    public AppShellNavigation(AppModule initialModule = AppModule.Controls)
    {
        CurrentModule = Enum.IsDefined(initialModule)
            ? initialModule : AppModule.Controls;
    }

    public event EventHandler? CurrentModuleChanged;
    public AppModule CurrentModule { get; private set; }

    public bool Navigate(AppModule module)
    {
        if (!Enum.IsDefined(module) || module == CurrentModule) return false;
        CurrentModule = module;
        CurrentModuleChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
