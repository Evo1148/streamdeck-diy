using System.Windows.Input;
using Microsoft.UI.Xaml;
using Windows.Storage;
using StreamDeckDIY.Core.Shell;

namespace StreamDeckDIY.App.ViewModels;

public sealed class AppShellViewModel : ObservableObject
{
    private const string SettingKey = "CurrentAppModule";
    private readonly AppShellNavigation navigation;

    public AppShellViewModel()
    {
        var saved = ApplicationData.Current.LocalSettings.Values[SettingKey];
        var initial = saved is int value && Enum.IsDefined((AppModule)value)
            ? (AppModule)value : AppModule.Controls;
        navigation = new(initial);
        navigation.CurrentModuleChanged += OnCurrentModuleChanged;
        NavigateCommand = new RelayCommand(Navigate);
    }

    public ICommand NavigateCommand { get; }
    public AppModule CurrentModule => navigation.CurrentModule;
    public string CurrentTitle => CurrentModule switch
    {
        AppModule.Controls => "Controles",
        AppModule.Profiles => "Perfiles",
        AppModule.Display => "Pantalla",
        AppModule.Actions => "Acciones",
        AppModule.Automation => "Automatización",
        AppModule.Macros => "Macros",
        AppModule.Device => "Dispositivo",
        AppModule.VisualEditor => "Editor visual",
        _ => "StreamDeck DIY",
    };

    public Visibility ControlsVisibility => VisibilityFor(AppModule.Controls);
    public Visibility ProfilesVisibility => VisibilityFor(AppModule.Profiles);
    public Visibility DisplayVisibility => VisibilityFor(AppModule.Display);
    public Visibility ActionsVisibility => VisibilityFor(AppModule.Actions);
    public Visibility AutomationVisibility => VisibilityFor(AppModule.Automation);
    public Visibility MacrosVisibility => VisibilityFor(AppModule.Macros);
    public Visibility DeviceVisibility => VisibilityFor(AppModule.Device);
    public Visibility VisualEditorVisibility => VisibilityFor(AppModule.VisualEditor);
    public bool IsControlsActive => IsActive(AppModule.Controls);
    public bool IsProfilesActive => IsActive(AppModule.Profiles);
    public bool IsDisplayActive => IsActive(AppModule.Display);
    public bool IsActionsActive => IsActive(AppModule.Actions);
    public bool IsAutomationActive => IsActive(AppModule.Automation);
    public bool IsMacrosActive => IsActive(AppModule.Macros);
    public bool IsDeviceActive => IsActive(AppModule.Device);
    public bool IsVisualEditorActive => IsActive(AppModule.VisualEditor);

    private void Navigate(object? parameter)
    {
        if (Enum.TryParse<AppModule>(Convert.ToString(parameter), out var module))
        {
            if (!navigation.Navigate(module)) NotifyNavigationState();
        }
    }

    private Visibility VisibilityFor(AppModule module) =>
        CurrentModule == module ? Visibility.Visible : Visibility.Collapsed;

    private bool IsActive(AppModule module) => CurrentModule == module;

    private void OnCurrentModuleChanged(object? sender, EventArgs args)
    {
        ApplicationData.Current.LocalSettings.Values[SettingKey] = (int)CurrentModule;
        NotifyNavigationState();
    }

    private void NotifyNavigationState()
    {
        OnPropertyChanged(nameof(CurrentModule));
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(ControlsVisibility));
        OnPropertyChanged(nameof(ProfilesVisibility));
        OnPropertyChanged(nameof(DisplayVisibility));
        OnPropertyChanged(nameof(ActionsVisibility));
        OnPropertyChanged(nameof(AutomationVisibility));
        OnPropertyChanged(nameof(MacrosVisibility));
        OnPropertyChanged(nameof(DeviceVisibility));
        OnPropertyChanged(nameof(VisualEditorVisibility));
        OnPropertyChanged(nameof(IsControlsActive));
        OnPropertyChanged(nameof(IsProfilesActive));
        OnPropertyChanged(nameof(IsDisplayActive));
        OnPropertyChanged(nameof(IsActionsActive));
        OnPropertyChanged(nameof(IsAutomationActive));
        OnPropertyChanged(nameof(IsMacrosActive));
        OnPropertyChanged(nameof(IsDeviceActive));
        OnPropertyChanged(nameof(IsVisualEditorActive));
    }

    private sealed class RelayCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute(parameter);
    }
}
