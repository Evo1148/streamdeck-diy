using Microsoft.UI.Xaml;
using StreamDeckDIY.App.Views;

namespace StreamDeckDIY.App;

public partial class App : Application
{
    private Window? window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
    }
}
