using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using StreamDeckDIY.App.Display;
using StreamDeckDIY.App.Dashboard;
using StreamDeckDIY.App.ViewModels;
using StreamDeckDIY.App.Services;
using StreamDeckDIY.Core.Devices;
using StreamDeckDIY.Core.Dashboard;
using StreamDeckDIY.Core.Display;
using StreamDeckDIY.Core.DisplayLink;
using StreamDeckDIY.Core.HostActions;
using StreamDeckDIY.Core.Profiles;
using StreamDeckDIY.Core.Profiles.Automation;
using StreamDeckDIY.Protocol.ProtocolV1;
using StreamDeckDIY.Transport.Hid;

namespace StreamDeckDIY.App.Views;

public sealed partial class MainWindow : Window
{
    private bool initialized;
    private readonly IDisplayPreviewRenderer previewRenderer;
    private readonly IDashboardRenderer dashboardRenderer;
    private DashboardPoint? dashboardPointerStart;
    private DateTimeOffset dashboardPointerPressedAt;

    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        var registry = new HostActionRegistry(Path.Combine(
            ApplicationData.Current.LocalFolder.Path, "host-actions.json"));
        var audioOutputService = new WindowsAudioOutputService();
        var definitionExecutor = new HostActionDefinitionExecutor(
            new WindowsHostActionLauncher(), audioOutputService);
        var macroExecutor = new MacroExecutor(
            registry, definitionExecutor, new SystemAsyncDelay());
        var deviceService = new DeviceService(
            new HidTransport(), new ProtocolV1Codec());
        var profileService = new ProfileService(
            new ProfileStore(Path.Combine(
                ApplicationData.Current.LocalFolder.Path, "profiles.json")),
            deviceService);
        var displaySync = new DisplaySyncService(deviceService);
        var bindingIconStore = new BindingIconStore(Path.Combine(
            ApplicationData.Current.LocalFolder.Path, "binding-icons.json"));
        var bindingLabelStore = new BindingLabelStore(Path.Combine(
            ApplicationData.Current.LocalFolder.Path, "binding-labels.json"));
        var dashboardStore = new DashboardConfigurationStore(Path.Combine(
            ApplicationData.Current.LocalFolder.Path, "dashboard-config.json"));
        var companionPacks = new CompanionPackCatalog(
            Path.Combine(ApplicationData.Current.LocalFolder.Path, "CompanionPacks"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "Companions"));
        var automationService = new AutoProfileService(
            new ProfileAutomationStore(Path.Combine(
                ApplicationData.Current.LocalFolder.Path,
                "profile-automation.json")),
            profileService,
            new ForegroundApplicationWatcher(
                new WindowsForegroundApplicationProvider(),
                TimeSpan.FromMilliseconds(500)));
        var displayConfiguration = DisplayConfiguration.DevelopmentDefault;
        var initialScene = DisplaySceneFactory.CreateHome(
            displayConfiguration, "Sin perfil", false);
        var displayEngine = new DisplayEngine(initialScene);
        var displayEditor = new DisplayEditor(displayConfiguration, initialScene);
        var pageManager = new DisplayPageManager(
            displayConfiguration,
            (profileName, pageName) => DisplaySceneFactory.CreatePage(
                displayConfiguration, profileName, pageName));
        var layoutStore = new DisplayLayoutStore(Path.Combine(
            ApplicationData.Current.LocalFolder.Path, "display-layout.json"));
        var displayEditorViewModel = new DisplayEditorViewModel(
            displayEditor,
            pageManager,
            layoutStore,
            new DisplayLayoutAutosave(layoutStore),
            displayEngine,
            new WindowsDisplayPageInteraction(this));
        var profileVisualFeedback = new ProfileVisualFeedbackController(
            displayEngine,
            new HackerMatrixProfileVisualEffectProvider(displayConfiguration));
        ViewModel = new MainViewModel(
            deviceService,
            new HostActionExecutor(registry, definitionExecutor, macroExecutor),
            registry,
            new WindowsHostActionInteraction(this),
            new WindowsDeviceFirmwareInteraction(this),
            audioOutputService,
            profileService,
            bindingIconStore,
            bindingLabelStore,
            new WindowsProfileInteraction(this),
            automationService,
            displayConfiguration,
            displayEngine,
            displaySync,
            displayEditorViewModel,
            profileVisualFeedback,
            dashboardStore,
            companionPacks,
            new WindowsCompanionPackInteraction(this),
            new WindowsNowPlayingProvider(),
            new SystemStatsService(
                new WindowsSystemStatsProvider(),
                new LibreHardwareMonitorSensorProvider()),
            new SystemClockProvider(),
            DispatcherQueue);
        profileService.SetBeforeProfileSwitch(
            ViewModel.Dashboard.FlushBeforeProfileSwitchAsync);
        InitializeComponent();
        previewRenderer = new WinUIDisplayPreviewRenderer(
            DisplayPreviewCanvas, displayConfiguration);
        dashboardRenderer = new DashboardPreviewRenderer(DashboardPreviewCanvas);
        displayEngine.StateChanged += OnDisplayStateChanged;
        displayEditorViewModel.RenderStateChanged += OnEditorRenderStateChanged;
        ViewModel.Dashboard.RenderStateChanged += OnDashboardRenderStateChanged;
        RenderPreview(displayEngine.State);
        dashboardRenderer.Render(ViewModel.Dashboard.RenderState());
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        await ViewModel.InitializeAsync();
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        ViewModel.DisplayEngine.StateChanged -= OnDisplayStateChanged;
        ViewModel.DisplayEditor.RenderStateChanged -= OnEditorRenderStateChanged;
        ViewModel.Dashboard.RenderStateChanged -= OnDashboardRenderStateChanged;
        await ViewModel.DisposeAsync();
    }

    private void OnDisplayStateChanged(object? sender, DisplayState state)
    {
        if (DispatcherQueue.HasThreadAccess)
            RenderPreview(state);
        else
            DispatcherQueue.TryEnqueue(() => RenderPreview(state));
    }

    private void OnEditorRenderStateChanged(object? sender, EventArgs args) =>
        RenderPreview(ViewModel.DisplayEngine.State);

    private void OnDashboardRenderStateChanged(object? sender, EventArgs args)
    {
        dashboardRenderer.Render(ViewModel.Dashboard.RenderState());
        ViewModel.QueueDashboardDisplayGraph();
    }

    private void RenderPreview(DisplayState state) =>
        previewRenderer.Render(state, ViewModel.DisplayEditor.RenderState());

    private void OnPreviewPointerPressed(
        object sender,
        PointerRoutedEventArgs args)
    {
        var position = args.GetCurrentPoint(PreviewHost).Position;
        var logical = DisplayCoordinateMapper.PreviewToLogical(
            position.X,
            position.Y,
            PreviewHost.ActualWidth,
            PreviewHost.ActualHeight,
            ViewModel.DisplayConfiguration);
        ViewModel.SetPreviewPointer(logical);
        if (logical is null || !ViewModel.DisplayEditor.IsEditMode) return;
        ViewModel.DisplayEditor.PointerPressed(logical.Value);
        PreviewHost.CapturePointer(args.Pointer);
        args.Handled = true;
    }

    private void OnPreviewPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (!ViewModel.DisplayEditor.IsEditMode) return;
        var point = args.GetCurrentPoint(DisplayPreviewCanvas).Position;
        ViewModel.DisplayEditor.PointerMoved(new DisplayPoint(point.X, point.Y));
    }

    private void OnPreviewPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        ViewModel.DisplayEditor.PointerReleased();
        PreviewHost.ReleasePointerCapture(args.Pointer);
        args.Handled = true;
    }

    private void OnDashboardPreviewPointerPressed(
        object sender,
        PointerRoutedEventArgs args)
    {
        if (DashboardPreviewHost.ActualWidth <= 0 || DashboardPreviewHost.ActualHeight <= 0)
            return;
        dashboardPointerStart = DashboardLogicalPoint(args);
        dashboardPointerPressedAt = DateTimeOffset.Now;
        DashboardPreviewHost.CapturePointer(args.Pointer);
        args.Handled = true;
    }

    private async void OnDashboardPreviewPointerReleased(
        object sender,
        PointerRoutedEventArgs args)
    {
        var start = dashboardPointerStart;
        dashboardPointerStart = null;
        DashboardPreviewHost.ReleasePointerCapture(args.Pointer);
        if (start is null) return;
        try
        {
            await ViewModel.Dashboard.HandleInteractionAsync(
                start.Value, DashboardLogicalPoint(args),
                DateTimeOffset.Now - dashboardPointerPressedAt);
        }
        catch (Exception exception)
        {
            ViewModel.SetDashboardInteractionError(exception.Message);
        }
        args.Handled = true;
    }

    private DashboardPoint DashboardLogicalPoint(PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(DashboardPreviewHost).Position;
        var configuration = ViewModel.DisplayConfiguration;
        return new DashboardPoint(
            point.X * configuration.Width / DashboardPreviewHost.ActualWidth,
            point.Y * configuration.Height / DashboardPreviewHost.ActualHeight);
    }

    private void OnDashboardPreviewViewportSizeChanged(
        object sender,
        SizeChangedEventArgs args) =>
        ViewModel.Dashboard.SetPreviewViewport(args.NewSize.Width, args.NewSize.Height);
}
