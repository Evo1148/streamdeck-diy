using StreamDeckDIY.Core.Shell;

internal static class AppShellTests
{
    public static void Run()
    {
        var shell = new AppShellNavigation(AppModule.Controls);
        var changes = 0;
        shell.CurrentModuleChanged += (_, _) => changes++;

        Assert(shell.CurrentModule == AppModule.Controls,
            "Shell starts in the requested module");
        Assert(shell.Navigate(AppModule.Display) &&
               shell.CurrentModule == AppModule.Display && changes == 1,
            "Shell changes module and publishes one notification");
        Assert(!shell.Navigate(AppModule.Display) && changes == 1,
            "Selecting the active module is idempotent");
        Assert(shell.Navigate(AppModule.VisualEditor) &&
               shell.CurrentModule == AppModule.VisualEditor && changes == 2,
            "Advanced visual editor remains a separate navigation target");

        var fallback = new AppShellNavigation((AppModule)999);
        Assert(fallback.CurrentModule == AppModule.Controls,
            "Shell falls back safely for an invalid persisted module");
        Assert(!fallback.Navigate((AppModule)999) &&
               fallback.CurrentModule == AppModule.Controls,
            "Shell ignores invalid navigation targets");
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Test failed: {name}");
    }
}
