using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using StreamDeckDIY.App.Services;
using StreamDeckDIY.Core.Audio;
using StreamDeckDIY.Core.Devices;
using StreamDeckDIY.Core.Dashboard;
using StreamDeckDIY.Core.Display;
using StreamDeckDIY.Core.DisplayLink;
using StreamDeckDIY.Core.HostActions;
using StreamDeckDIY.Core.Profiles;
using StreamDeckDIY.Core.Profiles.Automation;
using StreamDeckDIY.Protocol.Models;
using StreamDeckDIY.Protocol.DisplayLink;

namespace StreamDeckDIY.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IDeviceService deviceService;
    private readonly IHostActionExecutor hostActionExecutor;
    private readonly IHostActionRegistry hostActionRegistry;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly IProfileService profileService;
    private readonly IProfileInteraction profileInteraction;
    private readonly BindingIconStore bindingIconStore;
    private readonly BindingLabelStore bindingLabelStore;
    private readonly IDisplayEngine displayEngine;
    private readonly DisplaySyncService displaySync;
    private readonly ProfileVisualFeedbackController profileVisualFeedback;
    private readonly CancellationTokenSource lifetime = new();
    private int actionTestTrace;

    private readonly object dashboardAssetTransferSync = new();
    private Task dashboardAssetTransferTask = Task.CompletedTask;
    private DashboardRuntimeState? pendingDashboardState;
    private bool dashboardAssetWorkerRunning;
    private uint dashboardAssetBootSession;
    private string? residentArtworkKey;
    private ushort residentArtworkAssetId;
    private string? residentCompanionKey;
    private string? cachedArtworkKey;
    private byte[]? cachedArtworkRgb565;
    private string? cachedCompanionKey;
    private byte[]? cachedCompanionRgb565;
    private string statusText = "Buscando dispositivo";
    private string firmwareText = "—";
    private string protocolText = "—";
    private string buttonsText = "—";
    private string encodersText = "—";
    private string capabilitiesText = "—";
    private string errorText = string.Empty;
    private string feedbackText = "Selecciona un control.";
    private string selectedControlText = "Botón 1";
    private string selectedControlSummary = "Sin asignar";
    private string selectedControlIconGlyph = "\uE80A";
    private DeviceAction selectedPresentationAction = new(ActionType.None);
    private BindingIconOption selectedIconOption;
    private bool suppressIconRefresh;
    private bool isBusy = true;
    private bool isConnected;
    private bool isEditorBusy;
    private ControlId selectedControl = new(ControlType.Button, 0);
    private ActionType selectedActionType = ActionType.None;
    private KeyOption selectedKey;
    private ConsumerOption selectedConsumer;
    private bool useControl;
    private bool useShift;
    private bool useAlt;
    private bool useGui;
    private HostActionDefinition? selectedHostAction;
    private string missingHostActionText = string.Empty;
    private string previewPointerText = "Pulsa la preview para obtener coordenadas lógicas.";
    private string selectedScreenLabel = string.Empty;
    private string displayLinkProtocolText = "--";
    private string displayLinkBackendText = "--";
    private string displayLinkResolutionText = "--";
    private string displayLinkGenerationText = "--";
    private string displayLinkNodesText = "--";
    private string displayLinkAssetsText = "--";
    private string displayLinkHostText = "--";
    private string displayLinkErrorText = "DisplayLink no disponible";
    private ProfilePage? selectedPage;
    private bool suppressPageSelection;

    public MainViewModel(IDeviceService deviceService,
                         IHostActionExecutor hostActionExecutor,
                         IHostActionRegistry hostActionRegistry,
                         IHostActionInteraction hostActionInteraction,
                         IDeviceFirmwareInteraction deviceFirmwareInteraction,
                         IAudioOutputService audioOutputService,
                         IProfileService profileService,
                         BindingIconStore bindingIconStore,
                         BindingLabelStore bindingLabelStore,
                         IProfileInteraction profileInteraction,
                         IAutoProfileService autoProfileService,
                         DisplayConfiguration displayConfiguration,
                         IDisplayEngine displayEngine,
                         DisplaySyncService displaySync,
                         DisplayEditorViewModel displayEditor,
                         ProfileVisualFeedbackController profileVisualFeedback,
                         DashboardConfigurationStore dashboardStore,
                         CompanionPackCatalog companionPacks,
                         ICompanionPackInteraction companionInteraction,
                         INowPlayingProvider nowPlayingProvider,
                         ISystemStatsProvider systemStatsProvider,
                         IClockProvider clockProvider,
                         DispatcherQueue dispatcherQueue)
    {
        this.deviceService = deviceService;
        this.hostActionExecutor = hostActionExecutor;
        this.hostActionRegistry = hostActionRegistry;
        this.profileService = profileService;
        this.profileInteraction = profileInteraction;
        this.bindingIconStore = bindingIconStore;
        this.bindingLabelStore = bindingLabelStore;
        DisplayConfiguration = displayConfiguration;
        this.displayEngine = displayEngine;
        this.displaySync = displaySync;
        DisplayEditor = displayEditor;
        this.profileVisualFeedback = profileVisualFeedback;
        this.dispatcherQueue = dispatcherQueue;
        deviceService.ConnectionChanged += OnDeviceConnectionChanged;
        displaySync.StateChanged += OnDisplayLinkStateChanged;
        displaySync.TouchReceived += OnDisplayTouchReceived;
        Dashboard = new DashboardViewModel(dashboardStore, companionPacks, companionInteraction,
            profileService, bindingIconStore, bindingLabelStore, hostActionRegistry, deviceService,
            nowPlayingProvider, systemStatsProvider, clockProvider, displayConfiguration, dispatcherQueue);
        Dashboard.PageChanged += OnDashboardPageChanged;
        Shell = new AppShellViewModel();
        deviceService.HostActionTriggered += OnHostActionTriggered;
        HostActions = new HostActionLibraryViewModel(
            hostActionRegistry, hostActionInteraction, audioOutputService);
        FirmwareUpdate = new DeviceFirmwareUpdateViewModel(
            deviceService, deviceFirmwareInteraction);
        HostActions.ActionsChanged += OnHostActionsChanged;
        Profiles = new ProfileViewModel(profileService, profileInteraction);
        Profiles.ActiveProfileChanged += OnActiveProfileChanged;
        Profiles.ProfilesChanged += OnProfilesChanged;
        Profiles.SelectedProfileChanged += OnSelectedProfileChanged;
        ProfileAutomation = new ProfileAutomationViewModel(
            autoProfileService, profileService, dispatcherQueue);
        ProfileAutomation.ActiveProfileChanged += OnAutomaticProfileChanged;
        KeyOptions = CreateKeyOptions();
        ConsumerOptions =
        [
            new("Volume Up", ConsumerControlAction.VolumeUp),
            new("Volume Down", ConsumerControlAction.VolumeDown),
            new("Mute", ConsumerControlAction.Mute),
            new("Play/Pause", ConsumerControlAction.PlayPause),
        ];
        ActionTypes =
        [
            ActionType.None,
            ActionType.Keyboard,
            ActionType.KeyboardShortcut,
            ActionType.ConsumerControl,
            ActionType.HostAction,
        ];
        ActionTypeOptions = ActionTypes
            .Select(type => new ActionTypeOption(BindingPresentation.FriendlyType(type), type))
            .ToArray();
        selectedKey = KeyOptions[0];
        selectedConsumer = ConsumerOptions[0];
        selectedIconOption = BindingPresentation.IconOptions[0];
        SelectButtonCommand = new AsyncRelayCommand(
            parameter => SelectButtonAsync(Convert.ToByte(parameter)));
        SelectEncoderCommand = new AsyncRelayCommand(SelectEncoderAsync);
        SelectControlCommand = new AsyncRelayCommand(SelectPresentedControlAsync);
        InitializeControlPresentationSlots();
        SaveCommand = new AsyncRelayCommand(
            parameter => IsExplicitCommand(parameter, "Save")
                ? SaveAsync()
                : Task.CompletedTask);
        TestCommand = new AsyncRelayCommand(
            parameter => IsExplicitCommand(parameter, "Test")
                ? TestAsync()
                : Task.CompletedTask);
        SimulateProfileVisualCommand = new AsyncRelayCommand(
            _ => ShowProfileActivatedVisualAsync());
        PreviousPageCommand = new AsyncRelayCommand(_ => NavigatePageAsync(SystemNavigation.Previous));
        HomePageCommand = new AsyncRelayCommand(_ => NavigatePageAsync(SystemNavigation.Home));
        NextPageCommand = new AsyncRelayCommand(_ => NavigatePageAsync(SystemNavigation.Next));
        AddPageCommand = new AsyncRelayCommand(_ => AddPageAsync());
    }

    public IReadOnlyList<ActionType> ActionTypes { get; }
    public IReadOnlyList<ActionTypeOption> ActionTypeOptions { get; }
    public IReadOnlyList<KeyOption> KeyOptions { get; }
    public IReadOnlyList<ConsumerOption> ConsumerOptions { get; }
    public IReadOnlyList<BindingIconOption> IconOptions => BindingPresentation.IconOptions;
    public HostActionLibraryViewModel HostActions { get; }
    public DeviceFirmwareUpdateViewModel FirmwareUpdate { get; }
    public AppShellViewModel Shell { get; }
    public ProfileViewModel Profiles { get; }
    public ProfileAutomationViewModel ProfileAutomation { get; }
    public ICommand SelectButtonCommand { get; }
    public ICommand SelectEncoderCommand { get; }
    public ICommand SelectControlCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand TestCommand { get; }
    public ICommand SimulateProfileVisualCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand HomePageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand AddPageCommand { get; }
    public IDisplayEngine DisplayEngine => displayEngine;
    public DisplayEditorViewModel DisplayEditor { get; }
    public DashboardViewModel Dashboard { get; }
    public DisplayConfiguration DisplayConfiguration { get; }
    public string DisplayResolutionText =>
        $"Resolución lógica de desarrollo: {DisplayConfiguration.Width:0} × " +
        $"{DisplayConfiguration.Height:0}";
    public string ActiveProfileName => CurrentProfileName();
    public ObservableCollection<ControlPresentationItem> ButtonPresentations { get; } = [];
    public ObservableCollection<ControlPresentationItem> EncoderPresentations { get; } = [];
    public ObservableCollection<ProfilePage> Pages { get; } = [];
    public ObservableCollection<BindingHostActionOption> BindingHostActionOptions { get; } = [];

    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public string FirmwareText { get => firmwareText; private set => SetProperty(ref firmwareText, value); }
    public string ProtocolText { get => protocolText; private set => SetProperty(ref protocolText, value); }
    public string ButtonsText { get => buttonsText; private set => SetProperty(ref buttonsText, value); }
    public string EncodersText { get => encodersText; private set => SetProperty(ref encodersText, value); }
    public string CapabilitiesText { get => capabilitiesText; private set => SetProperty(ref capabilitiesText, value); }
    public string DisplayLinkProtocolText { get => displayLinkProtocolText; private set => SetProperty(ref displayLinkProtocolText, value); }
    public string DisplayLinkBackendText { get => displayLinkBackendText; private set => SetProperty(ref displayLinkBackendText, value); }
    public string DisplayLinkResolutionText { get => displayLinkResolutionText; private set => SetProperty(ref displayLinkResolutionText, value); }
    public string DisplayLinkGenerationText { get => displayLinkGenerationText; private set => SetProperty(ref displayLinkGenerationText, value); }
    public string DisplayLinkNodesText { get => displayLinkNodesText; private set => SetProperty(ref displayLinkNodesText, value); }
    public string DisplayLinkAssetsText { get => displayLinkAssetsText; private set => SetProperty(ref displayLinkAssetsText, value); }
    public string DisplayLinkHostText { get => displayLinkHostText; private set => SetProperty(ref displayLinkHostText, value); }
    public string DisplayLinkErrorText { get => displayLinkErrorText; private set => SetProperty(ref displayLinkErrorText, value); }
    public string ErrorText { get => errorText; private set => SetProperty(ref errorText, value); }
    public string FeedbackText { get => feedbackText; private set => SetProperty(ref feedbackText, value); }
    public ProfilePage? SelectedPage
    {
        get => selectedPage;
        set
        {
            if (!SetProperty(ref selectedPage, value) || suppressPageSelection || value is null) return;
            _ = SelectPageAsync(value.Id);
        }
    }
    public string PagePositionText => profileService.IsHome
        ? "Home"
        : EditingProfile() is { } profile
            ? $"{profile.ActivePageIndex + 1} / {profile.Pages.Length}"
            : "—";
    public string SelectedControlText { get => selectedControlText; private set => SetProperty(ref selectedControlText, value); }
    public string SelectedControlSummary { get => selectedControlSummary; private set => SetProperty(ref selectedControlSummary, value); }
    public string SelectedControlIconGlyph { get => selectedControlIconGlyph; private set => SetProperty(ref selectedControlIconGlyph, value); }
    public BindingIconOption SelectedIconOption
    {
        get => selectedIconOption;
        set
        {
            if (!SetProperty(ref selectedIconOption, value) || suppressIconRefresh) return;
            RefreshSelectedIconPreview();
            UpdateDashboardDraft();
            RefreshControlPresentations();
            FeedbackText = "Cambios sin guardar.";
        }
    }

    public bool IsBusy { get => isBusy; private set => SetProperty(ref isBusy, value); }
    public bool IsConnected
    {
        get => isConnected;
        private set
        {
            if (!SetProperty(ref isConnected, value)) return;
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(ConnectedIndicatorVisibility));
            OnPropertyChanged(nameof(DisconnectedIndicatorVisibility));
            RefreshBaseDisplay();
        }
    }
    public Visibility ConnectedIndicatorVisibility => IsConnected
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DisconnectedIndicatorVisibility => IsConnected
        ? Visibility.Collapsed : Visibility.Visible;
    public bool IsEditorBusy { get => isEditorBusy; private set { if (SetProperty(ref isEditorBusy, value)) OnPropertyChanged(nameof(CanEdit)); } }
    public bool CanEdit => Profiles.SelectedProfile is not null && !IsEditorBusy;
    public bool UsesKey => SelectedActionType is ActionType.Keyboard or ActionType.KeyboardShortcut;
    public bool UsesModifiers => SelectedActionType == ActionType.KeyboardShortcut;
    public bool UsesConsumer => SelectedActionType == ActionType.ConsumerControl;
    public bool UsesHostAction => SelectedActionType == ActionType.HostAction;
    public Visibility NoneEditorVisibility => SelectedActionType == ActionType.None
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility KeyEditorVisibility => UsesKey
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ModifiersEditorVisibility => UsesModifiers
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ConsumerEditorVisibility => UsesConsumer
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HostActionEditorVisibility => UsesHostAction
        ? Visibility.Visible : Visibility.Collapsed;
    public ActionTypeOption SelectedActionTypeOption
    {
        get => ActionTypeOptions.First(option => option.Type == SelectedActionType);
        set
        {
            if (value is not null) SelectedActionType = value.Type;
        }
    }

    public ActionType SelectedActionType
    {
        get => selectedActionType;
        set
        {
            if (!SetProperty(ref selectedActionType, value)) return;
            OnPropertyChanged(nameof(UsesKey));
            OnPropertyChanged(nameof(UsesModifiers));
            OnPropertyChanged(nameof(UsesConsumer));
            OnPropertyChanged(nameof(UsesHostAction));
            OnPropertyChanged(nameof(NoneEditorVisibility));
            OnPropertyChanged(nameof(KeyEditorVisibility));
            OnPropertyChanged(nameof(ModifiersEditorVisibility));
            OnPropertyChanged(nameof(ConsumerEditorVisibility));
            OnPropertyChanged(nameof(HostActionEditorVisibility));
            OnPropertyChanged(nameof(SelectedActionTypeOption));
        }
    }

    public KeyOption SelectedKey { get => selectedKey; set => SetProperty(ref selectedKey, value); }
    public ConsumerOption SelectedConsumer { get => selectedConsumer; set => SetProperty(ref selectedConsumer, value); }
    public bool UseControl { get => useControl; set => SetProperty(ref useControl, value); }
    public bool UseShift { get => useShift; set => SetProperty(ref useShift, value); }
    public bool UseAlt { get => useAlt; set => SetProperty(ref useAlt, value); }
    public bool UseGui { get => useGui; set => SetProperty(ref useGui, value); }
    public HostActionDefinition? SelectedHostAction
    {
        get => selectedHostAction;
        set
        {
            if (!SetProperty(ref selectedHostAction, value)) return;
            if (value is not null) MissingHostActionText = string.Empty;
            OnPropertyChanged(nameof(SelectedBindingHostActionOption));
            OnPropertyChanged(nameof(SelectedHostActionDetail));
        }
    }
    public BindingHostActionOption? SelectedBindingHostActionOption
    {
        get => SelectedHostAction is null ? null : BindingHostActionOptions.FirstOrDefault(
            option => option.Definition.Id == SelectedHostAction.Id);
        set => SelectedHostAction = value?.Definition;
    }
    public string SelectedHostActionDetail => SelectedBindingHostActionOption?.Detail ?? string.Empty;
    public string MissingHostActionText
    {
        get => missingHostActionText;
        private set => SetProperty(ref missingHostActionText, value);
    }
    public string PreviewPointerText
    {
        get => previewPointerText;
        private set => SetProperty(ref previewPointerText, value);
    }
    public string SelectedScreenLabel
    {
        get => selectedScreenLabel;
        set
        {
            if (!SetProperty(ref selectedScreenLabel, value)) return;
            SelectedControlSummary = string.IsNullOrWhiteSpace(value)
                ? BindingPresentation.Describe(
                    selectedPresentationAction, id => hostActionRegistry.GetById(id))
                : value.Trim();
            UpdateDashboardDraft();
            RefreshControlPresentations();
            FeedbackText = "Cambios sin guardar.";
        }
    }

    public async Task InitializeAsync()
    {
        StatusText = "Buscando dispositivo";
        IsBusy = true;
        try
        {
            await bindingIconStore.InitializeAsync(lifetime.Token);
            await bindingLabelStore.InitializeAsync(lifetime.Token);
            await HostActions.InitializeAsync(lifetime.Token);
            RefreshBindingHostActionOptions();
            await ProfileAutomation.InitializeAsync(lifetime.Token);
            try
            {
                await Profiles.InitializeAsync(lifetime.Token);
                RefreshPages();
                OnPropertyChanged(nameof(ActiveProfileName));
            }
            catch (InvalidDataException exception)
            {
                StatusText = "Error al cargar perfiles";
                ErrorText = exception.Message;
                return;
            }
            await Dashboard.InitializeAsync(lifetime.Token);
            await DisplayEditor.InitializeAsync(
                VisualProfiles(), SafeActiveProfileId(), lifetime.Token);
            ProfileAutomation.RefreshProfiles();
            await SelectButtonAsync(0);
            if (!await deviceService.ConnectAsync(lifetime.Token))
            {
                StatusText = "No conectado";
                ErrorText = "No se pudo conectar con StreamDeck DIY. Los perfiles locales siguen disponibles.";
                return;
            }

            var info = await deviceService.GetDeviceInfoAsync(lifetime.Token);
            FirmwareText = info.FirmwareVersion.ToString(3);
            ProtocolText = info.ProtocolVersion.ToString();
            ButtonsText = info.ButtonCount.ToString();
            EncodersText = info.EncoderCount.ToString();
            CapabilitiesText = $"0x{(ushort)info.Capabilities:X4}";
            StatusText = "Conectado";
            IsConnected = true;
            await profileService.SynchronizeWithDeviceAsync(lifetime.Token);
            Profiles.Refresh();
            RefreshPages();
            await Dashboard.ActivateProfileAsync(lifetime.Token);
            await displaySync.InitializeAsync(lifetime.Token);
            QueueDashboardDisplayGraph();
            RefreshControlPresentations();
            DisplayEditor.SynchronizeProfiles(
                VisualProfiles(), profileService.ActiveProfile.Id,
                Profiles.SelectedProfile?.Id);
            RefreshBaseDisplay();
            ProfileAutomation.RefreshProfiles();
            await ProfileAutomation.StartAsync(lifetime.Token);
            await ProfileAutomation.ReevaluateAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleCommunicationError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Dashboard.FlushAutosaveAsync();
        deviceService.ConnectionChanged -= OnDeviceConnectionChanged;
        deviceService.HostActionTriggered -= OnHostActionTriggered;
        displaySync.StateChanged -= OnDisplayLinkStateChanged;
        displaySync.TouchReceived -= OnDisplayTouchReceived;
        HostActions.ActionsChanged -= OnHostActionsChanged;
        Profiles.ActiveProfileChanged -= OnActiveProfileChanged;
        Profiles.ProfilesChanged -= OnProfilesChanged;
        Profiles.SelectedProfileChanged -= OnSelectedProfileChanged;
        Dashboard.PageChanged -= OnDashboardPageChanged;
        ProfileAutomation.ActiveProfileChanged -= OnAutomaticProfileChanged;
        lifetime.Cancel();
        displayEngine.DismissOverlay();
        await ProfileAutomation.DisposeAsync();
        await HostActions.DisposeAsync();
        await DisplayEditor.DisposeAsync();
        await Dashboard.DisposeAsync();
        try { await dashboardAssetTransferTask; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        await displaySync.DisposeAsync();
        deviceService.Disconnect();
        await deviceService.DisposeAsync();
        lifetime.Dispose();
    }

    private async Task SelectButtonAsync(byte index)
    {
        if (index >= ProfileControls.UserButtonCount) return;
        await SelectControlAsync(new ControlId(ControlType.Button, index));
    }

    private async Task SelectPageAsync(uint pageId)
    {
        if (IsEditorBusy || EditingProfile() is not { } profile) return;
        IsEditorBusy = true;
        try
        {
            await profileService.SelectPageAsync(profile.Id, pageId, lifetime.Token);
            Profiles.Refresh(profile.Id);
            RefreshPages();
            RefreshControlPresentations();
            if (profile.Id == SafeActiveProfileId())
                await Dashboard.ActivateProfileAsync(lifetime.Token);
            FeedbackText = $"Página '{SelectedPage?.Name}' activa.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            FeedbackText = $"No se pudo cambiar de página: {exception.Message}";
        }
        finally { IsEditorBusy = false; }
    }

    private async Task NavigatePageAsync(SystemNavigation navigation)
    {
        if (IsEditorBusy || profileService.Profiles.Count == 0) return;
        IsEditorBusy = true;
        try
        {
            await profileService.NavigateAsync(profileService.ActiveProfile.Id, navigation, lifetime.Token);
            Profiles.Refresh();
            RefreshPages();
            RefreshPages();
            RefreshControlPresentations();
            await Dashboard.ActivateProfileAsync(lifetime.Token);
            RefreshBaseDisplay();
            FeedbackText = navigation == SystemNavigation.Home
                ? "Home del perfil."
                : $"Página {PagePositionText}.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            FeedbackText = $"No se pudo navegar: {exception.Message}";
        }
        finally { IsEditorBusy = false; }
    }

    private async Task AddPageAsync()
    {
        if (IsEditorBusy || EditingProfile() is not { } profile) return;
        var name = await profileInteraction.RequestNameAsync(
            "Nueva página", $"Página {profile.Pages.Length + 1}");
        if (name is null) return;
        var page = await profileService.AddPageAsync(profile.Id, name, lifetime.Token);
        Profiles.Refresh(profile.Id);
        RefreshPages();
        await SelectPageAsync(page.Id);
    }

    private void RefreshPages()
    {
        suppressPageSelection = true;
        try
        {
            Pages.Clear();
            if (EditingProfile() is not { } profile)
            {
                SelectedPage = null;
                return;
            }
            foreach (var page in profile.Pages) Pages.Add(page);
            SelectedPage = Pages.FirstOrDefault(page => page.Id == profile.ActivePageId);
            OnPropertyChanged(nameof(PagePositionText));
        }
        finally { suppressPageSelection = false; }
    }

    private Task SelectEncoderAsync(object? parameter)
    {
        if (!Enum.TryParse(Convert.ToString(parameter), out ControlType type) ||
            type is not (ControlType.EncoderCounterClockwise or
                         ControlType.EncoderPress or
                         ControlType.EncoderClockwise))
        {
            return Task.CompletedTask;
        }
        return SelectControlAsync(new ControlId(type, 0));
    }

    private Task SelectPresentedControlAsync(object? parameter) =>
        parameter is ControlPresentationItem item
            ? SelectControlAsync(item.Control)
            : Task.CompletedTask;

    private Task SelectControlAsync(ControlId control)
    {
        if (IsEditorBusy || EditingProfile() is not { } profile)
            return Task.CompletedTask;
        IsEditorBusy = true;
        try
        {
            var binding = profile.Bindings.Single(value => value.Control == control);
            selectedControl = control;
            Dashboard.ClearDraftPresentation();
            SelectedControlText = ControlText(control);
            ApplyAction(binding.Action);
            selectedPresentationAction = binding.Action;
            LoadSelectedIcon(profile.Id);
            selectedScreenLabel = bindingLabelStore.Get(profile.Id, control) ?? string.Empty;
            OnPropertyChanged(nameof(SelectedScreenLabel));
            SelectedControlSummary = PresentationLabel(profile.Id, binding);
            RefreshSelectedIconPreview();
            Dashboard.RefreshBindings();
            RefreshControlPresentations();
            FeedbackText = "Configuración local cargada.";
            ErrorText = string.Empty;
        }
        catch (Exception exception)
        {
            FeedbackText = $"No se pudo cargar la bind: {exception.Message}";
        }
        finally { IsEditorBusy = false; }
        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        if (!CanEdit) return;
        IsEditorBusy = true;
        try
        {
            var targetProfileId = EditingProfile()?.Id ??
                throw new InvalidOperationException("No hay un perfil seleccionado.");
            var targetControl = SelectedControl();
            var targetAction = BuildAction();
            var targetIcon = SelectedIconOption.Key;
            var targetLabel = SelectedScreenLabel;

            var synchronization = await profileService.SaveBindingAsync(
                targetProfileId, targetControl, targetAction, lifetime.Token);
            await bindingIconStore.SetAsync(
                targetProfileId, targetControl, targetIcon, lifetime.Token);
            await bindingLabelStore.SetAsync(
                targetProfileId, targetControl, targetLabel, lifetime.Token);
            Dashboard.ClearDraftPresentation();
            Profiles.Refresh();
            selectedPresentationAction = targetAction;
            SelectedControlSummary = string.IsNullOrWhiteSpace(SelectedScreenLabel)
                ? BindingPresentation.Describe(
                    selectedPresentationAction, id => hostActionRegistry.GetById(id))
                : SelectedScreenLabel.Trim();
            RefreshSelectedIconPreview();
            RefreshControlPresentations();
            Dashboard.RefreshBindings();
            FeedbackText = synchronization.DeviceSyncPending
                ? string.IsNullOrWhiteSpace(synchronization.DeviceSyncError)
                    ? "Guardado localmente; se sincronizará al conectar."
                    : $"Guardado localmente; sincronización pendiente: {synchronization.DeviceSyncError}"
                : "Guardado.";
            ErrorText = string.Empty;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            FeedbackText = $"No se pudo guardar: {exception.Message}";
        }
        finally
        {
            IsEditorBusy = false;
        }
    }

    private static bool IsExplicitCommand(object? parameter, string expected) =>
        string.Equals(Convert.ToString(parameter), expected,
                      StringComparison.Ordinal);

    private async Task TestAsync()
    {
        if (!CanEdit) return;
        if (!IsConnected)
        {
            FeedbackText = "No se puede probar sin el dispositivo conectado.";
            return;
        }
        IsEditorBusy = true;
        var trace = unchecked((uint)Interlocked.Increment(ref actionTestTrace));
        var action = BuildAction();
        System.Diagnostics.Debug.WriteLine(
            $"TEST BINDING trace={trace} type={action.Type} control={selectedControl.Type}:{selectedControl.Index}");
        try
        {
            System.Diagnostics.Debug.WriteLine($"TEST EXEC BEGIN trace={trace}");
            await deviceService.ExecuteActionTestAsync(action, lifetime.Token);
            System.Diagnostics.Debug.WriteLine($"TEST EXEC OK trace={trace}");
            FeedbackText = "Prueba enviada.";
            ErrorText = string.Empty;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"TEST EXEC FAIL trace={trace} reason={exception.GetType().Name} detail={exception.Message}");
            FeedbackText = $"No se pudo probar: {exception.Message}";
        }
        finally
        {
            IsEditorBusy = false;
        }
    }

    private ControlId SelectedControl() =>
        selectedControl;

    private static string ControlText(ControlId control) => control.Type switch
    {
        ControlType.Button => $"Botón {control.Index + 1}",
        ControlType.EncoderCounterClockwise => "Giro izquierda",
        ControlType.EncoderPress => "Pulsación del encoder",
        ControlType.EncoderClockwise => "Giro derecha",
        _ => "Control",
    };

    private DeviceAction BuildAction()
    {
        return SelectedActionType switch
        {
            ActionType.None => new DeviceAction(ActionType.None),
            ActionType.Keyboard => new DeviceAction(
                ActionType.Keyboard, SelectedKey.KeyCode),
            ActionType.KeyboardShortcut => new DeviceAction(
                ActionType.KeyboardShortcut, SelectedKey.KeyCode, BuildModifiers()),
            ActionType.ConsumerControl => new DeviceAction(
                ActionType.ConsumerControl,
                ConsumerControl: SelectedConsumer.Action),
            ActionType.HostAction => new DeviceAction(
                ActionType.HostAction,
                HostActionId: SelectedHostAction?.Id ??
                    throw new InvalidOperationException(
                        string.IsNullOrEmpty(MissingHostActionText)
                            ? "Selecciona una Host Action configurada."
                            : MissingHostActionText)),
            _ => throw new InvalidOperationException("Tipo de acción no compatible."),
        };
    }

    private ActionModifiers BuildModifiers()
    {
        var modifiers = ActionModifiers.None;
        if (UseControl) modifiers |= ActionModifiers.Control;
        if (UseShift) modifiers |= ActionModifiers.Shift;
        if (UseAlt) modifiers |= ActionModifiers.Alt;
        if (UseGui) modifiers |= ActionModifiers.Gui;
        return modifiers;
    }

    private void ApplyAction(DeviceAction action)
    {
        SelectedActionType = action.Type;
        SelectedKey = KeyOptions.FirstOrDefault(option => option.KeyCode == action.KeyCode)
            ?? KeyOptions[0];
        SelectedConsumer = ConsumerOptions.FirstOrDefault(
            option => option.Action == action.ConsumerControl) ?? ConsumerOptions[0];
        UseControl = action.Modifiers.HasFlag(ActionModifiers.Control);
        UseShift = action.Modifiers.HasFlag(ActionModifiers.Shift);
        UseAlt = action.Modifiers.HasFlag(ActionModifiers.Alt);
        UseGui = action.Modifiers.HasFlag(ActionModifiers.Gui);
        if (action.Type == ActionType.HostAction)
        {
            SelectedHostAction = hostActionRegistry.GetById(action.HostActionId);
            MissingHostActionText = SelectedHostAction is null
                ? $"Acción {action.HostActionId} no encontrada"
                : string.Empty;
        }
        else
        {
            SelectedHostAction = null;
            MissingHostActionText = string.Empty;
        }
    }

    private void OnHostActionTriggered(
        object? sender, HostActionTriggeredEventArgs eventArgs)
    {
        _ = ExecuteHostActionAsync(eventArgs.ActionId);
    }

    private async Task ExecuteHostActionAsync(uint actionId)
    {
        string feedback;
        try
        {
            var result = await hostActionExecutor.ExecuteAsync(actionId, lifetime.Token);
            feedback = result.Message;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            feedback = $"No se pudo ejecutar Host Action {actionId}: {exception.Message}";
        }
        dispatcherQueue.TryEnqueue(() => FeedbackText = feedback);
    }

    private void OnHostActionsChanged(object? sender, EventArgs eventArgs)
    {
        RefreshBindingHostActionOptions();
        RefreshControlPresentations();
        Dashboard.RefreshBindings();
        if (SelectedHostAction is null) return;
        var selectedId = SelectedHostAction.Id;
        var updated = hostActionRegistry.GetById(selectedId);
        SelectedHostAction = updated;
        if (updated is null)
            MissingHostActionText = $"Acción {selectedId} no encontrada";
    }

    private async void OnActiveProfileChanged(object? sender, EventArgs eventArgs)
    {
        try { await HandleProfileChangedAsync(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private async void OnAutomaticProfileChanged(object? sender, EventArgs eventArgs)
    {
        try
        {
            Profiles.Refresh();
            OnPropertyChanged(nameof(ActiveProfileName));
            await Dashboard.ActivateProfileAsync(lifetime.Token);
            Dashboard.ClearDraftPresentation();
            _ = HandleSuccessfulProfileActivationAsync();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private async Task HandleProfileChangedAsync()
    {
        OnPropertyChanged(nameof(ActiveProfileName));
        RefreshPages();
        RefreshControlPresentations();
        await Dashboard.ActivateProfileAsync(lifetime.Token);
        _ = HandleSuccessfulProfileActivationAsync();
    }

    private void OnProfilesChanged(object? sender, EventArgs eventArgs)
    {
        RefreshPages();
        ProfileAutomation.RefreshProfiles();
        DisplayEditor.SynchronizeProfiles(
            VisualProfiles(),
            profileService.ActiveProfile.Id,
            Profiles.SelectedProfile?.Id);
    }

    private void OnSelectedProfileChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(CanEdit));
        if (Profiles.SelectedProfile is not { } profile) return;
        DisplayEditor.SelectProfileForEditing(profile.Id);
        RefreshPages();
        RefreshControlPresentations();
        _ = SelectControlAsync(selectedControl);
    }

    private void OnDashboardPageChanged(object? sender, EventArgs eventArgs)
    {
        Profiles.Refresh();
        RefreshPages();
        RefreshControlPresentations();
        RefreshBaseDisplay();
    }

    private void HandleCommunicationError(Exception exception)
    {
        StatusText = "Error de comunicación";
        ErrorText = exception.Message;
        IsConnected = false;
        deviceService.Disconnect();
    }

    public void SetPreviewPointer(DisplayPoint? point)
    {
        PreviewPointerText = point is { } value
            ? $"Coordenada lógica: {value.X:0}, {value.Y:0}"
            : "Fuera del área lógica de pantalla.";
    }

    public void SetDashboardInteractionError(string message) =>
        Dashboard.SetInteractionError(message);

    public void QueueDashboardDisplayGraph()
    {
        var state = Dashboard.RenderState();
        lock (dashboardAssetTransferSync)
        {
            pendingDashboardState = state;
            if (dashboardAssetWorkerRunning) return;
            dashboardAssetWorkerRunning = true;
            dashboardAssetTransferTask = Task.Run(ProcessDashboardStatesAsync);
        }
    }

    private async Task ProcessDashboardStatesAsync()
    {
        while (!lifetime.IsCancellationRequested)
        {
            DashboardRuntimeState? state;
            lock (dashboardAssetTransferSync)
            {
                state = pendingDashboardState;
                pendingDashboardState = null;
                if (state is null)
                {
                    dashboardAssetWorkerRunning = false;
                    return;
                }
            }
            await SynchronizeDashboardStateAsync(state);
        }
    }

    private async Task SynchronizeDashboardStateAsync(DashboardRuntimeState state)
    {
        var token = lifetime.Token;
        try
        {
            if (displaySync.Lifecycle is not (DisplayLinkLifecycleState.Synchronizing or
                    DisplayLinkLifecycleState.Ready) ||
                displaySync.Info is null) return;

            var bootSession = displaySync.Info.BootSessionId;
            if (bootSession != dashboardAssetBootSession)
            {
                dashboardAssetBootSession = bootSession;
                residentArtworkKey = null;
                residentArtworkAssetId = 0;
                residentCompanionKey = null;
            }

            var graph = DashboardDisplayGraphCompiler.Compile(state, 0);
            var identity = $"{state.Configuration.ProfileId}:{state.Configuration.PresetId}";
            var uploads = new List<DisplayAssetUpload>(2);
            var releases = new List<ushort>(2);

            string? nextArtworkKey = null;
            ushort nextArtworkId = 0;
            if (state.NowPlaying is { Artwork: { Length: > 0 } artwork, ArtworkKey: not null } &&
                graph.Nodes.Any(node => node.Kind == DisplayNodeKind.Image &&
                    node.AssetId == ArtworkContentKey.AssetId(state.NowPlaying.ArtworkKey)))
            {
                nextArtworkKey = state.NowPlaying.ArtworkKey;
                nextArtworkId = ArtworkContentKey.AssetId(nextArtworkKey);
                if (!string.Equals(nextArtworkKey, residentArtworkKey, StringComparison.Ordinal))
                {
                    if (!string.Equals(nextArtworkKey, cachedArtworkKey, StringComparison.Ordinal) ||
                        cachedArtworkRgb565 is null)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"ASSET RESOLVE artwork id=0x{nextArtworkId:X4} key={nextArtworkKey}");
                        cachedArtworkRgb565 =
                            await ArtworkRgb565Converter.ConvertAsync(artwork, token);
                        cachedArtworkKey = nextArtworkKey;
                    }
                    if (cachedArtworkRgb565 is null) return;
                    uploads.Add(new(nextArtworkId, nextArtworkId,
                        ArtworkRgb565Converter.Width, ArtworkRgb565Converter.Height,
                        cachedArtworkRgb565));
                }
            }
            if (residentArtworkAssetId != 0 && residentArtworkAssetId != nextArtworkId)
                releases.Add(residentArtworkAssetId);

            string? nextCompanionKey = null;
            if (state.CompanionAsset is { } companion &&
                graph.Nodes.Any(node => node.Kind == DisplayNodeKind.Image &&
                    node.AssetId == companion.AssetId))
            {
                var background = DashboardVisualTokenResolver.CompanionBackground(
                    state.Configuration.VisualIdentity,
                    state.Configuration.EffectiveVisualOptions);
                nextCompanionKey = companion.ContentKey;
                if (!string.Equals(nextCompanionKey, residentCompanionKey, StringComparison.Ordinal))
                {
                    if (!string.Equals(nextCompanionKey, cachedCompanionKey, StringComparison.Ordinal) ||
                        cachedCompanionRgb565 is null)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"ASSET RESOLVE companion id=0x{companion.AssetId:X4} key={nextCompanionKey}");
                        cachedCompanionRgb565 = await ArtworkRgb565Converter.ConvertAsync(
                            companion.PngBytes, companion.Width, companion.Height, background, token);
                        cachedCompanionKey = nextCompanionKey;
                    }
                    if (cachedCompanionRgb565 is null) return;
                    uploads.Add(new(companion.AssetId, companion.AssetId,
                        companion.Width, companion.Height, cachedCompanionRgb565));
                }
            }
            if (residentCompanionKey is not null && nextCompanionKey is null)
                releases.Add(CompanionPackFormat.ActiveAssetId);

            if (!await displaySync.CommitGraphWithAssetsAsync(
                    graph, identity, uploads, releases, token)) return;

            residentArtworkKey = nextArtworkKey;
            residentArtworkAssetId = nextArtworkId;
            residentCompanionKey = nextCompanionKey;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Dashboard asset synchronization deferred: {exception.Message}");
        }
    }

    private void OnDisplayLinkStateChanged(object? sender, DisplayLinkDiagnosticState state) =>
        dispatcherQueue.TryEnqueue(() =>
        {
            DisplayLinkProtocolText = state.Protocol;
            DisplayLinkBackendText = state.Backend;
            DisplayLinkResolutionText = state.Resolution;
            DisplayLinkGenerationText = state.Available ? state.Generation.ToString() : "--";
            DisplayLinkNodesText = state.Available ? state.Nodes.ToString() : "--";
            DisplayLinkAssetsText = state.Available ? $"{state.AssetBytes} bytes" : "--";
            DisplayLinkHostText = state.Lifecycle == DisplayLinkLifecycleState.Ready
                ? (state.HostOnline ? "Online" : "Iniciando heartbeat")
                : state.Lifecycle switch
                {
                    DisplayLinkLifecycleState.Handshaking => "Iniciando...",
                    DisplayLinkLifecycleState.Synchronizing => "Sincronizando...",
                    DisplayLinkLifecycleState.Unsupported => "No compatible",
                    DisplayLinkLifecycleState.Faulted => "Error",
                    _ => "Desconectado",
                };
            DisplayLinkErrorText = string.IsNullOrWhiteSpace(state.LastError) ? "Ninguno" : state.LastError;
            var bootSession = displaySync.Info?.BootSessionId ?? 0;
            if (state.Lifecycle is DisplayLinkLifecycleState.Synchronizing or
                    DisplayLinkLifecycleState.Ready &&
                bootSession != 0 && bootSession != dashboardAssetBootSession)
                QueueDashboardDisplayGraph();
        });

    private void OnDeviceConnectionChanged(
        object? sender, DeviceConnectionChangedEventArgs args) =>
        dispatcherQueue.TryEnqueue(() =>
        {
            IsConnected = args.IsConnected;
            var bootloaderRequested =
                args.DisconnectReason == DeviceDisconnectReason.BootloaderRequested;
            StatusText = args.IsConnected
                ? "Conectado"
                : bootloaderRequested ? "Modo actualización" : "Desconectado";
            if (bootloaderRequested)
                ErrorText = string.Empty;
            else if (!args.IsConnected)
                ErrorText = "StreamDeck DIY desconectado. La configuración local continúa disponible.";
            else
                ErrorText = string.Empty;
        });

    private void OnDisplayTouchReceived(object? sender, DisplayTouchEvent touch)
    {
        if (touch.Gesture != (byte)DisplayGestureMask.Tap)
        {
            System.Diagnostics.Debug.WriteLine(
                $"TOUCH ROUTE DROP trace={touch.EventId} reason=unsupported_gesture gesture={touch.Gesture}");
            return;
        }
        if (!displaySync.TryResolveTouchRegion(touch, out var region))
        {
            System.Diagnostics.Debug.WriteLine(
                $"TOUCH ROUTE DROP trace={touch.EventId} reason=region_not_active");
            return;
        }
        if (region.ActionKind != DisplayTouchActionKind.LocalControl)
        {
            System.Diagnostics.Debug.WriteLine(
                $"TOUCH ROUTE DROP trace={touch.EventId} reason=action_kind_{region.ActionKind}");
            return;
        }
        if (region.ActionParameter is >= (uint)SystemNavigation.Previous and
            <= (uint)SystemNavigation.Next)
        {
            var navigation = (SystemNavigation)region.ActionParameter;
            dispatcherQueue.TryEnqueue(async () =>
            {
                try { await NavigatePageAsync(navigation); }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            });
            return;
        }
        if (!DisplayLocalControlParameter.TryDecode(
                region.ActionParameter, out var control))
        {
            System.Diagnostics.Debug.WriteLine(
                $"TOUCH ROUTE DROP trace={touch.EventId} reason=invalid_control control={region.ActionParameter}");
            return;
        }

        System.Diagnostics.Debug.WriteLine(
            $"TOUCH ROUTE trace={touch.EventId} control={region.ActionParameter}");
        if (!dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await Dashboard.ExecuteControlAsync(
                    control, lifetime.Token, touch.EventId);
                FeedbackText =
                    $"Touch DisplayLink: región {touch.RegionId}, control {region.ActionParameter}.";
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                Dashboard.SetInteractionError(exception.Message);
            }
        }))
        {
            System.Diagnostics.Debug.WriteLine(
                $"TOUCH ROUTE DROP trace={touch.EventId} reason=dispatcher_rejected");
        }
    }

    private async Task HandleSuccessfulProfileActivationAsync()
    {
        try
        {
            RefreshBaseDisplay();
            await DisplayEditor.ActivateProfileAsync(
                profileService.ActiveProfile.Id, lifetime.Token);
            Dashboard.ProfileActivated();
            await ShowProfileActivatedVisualAsync();
            _ = SelectControlAsync(selectedControl);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task ShowProfileActivatedVisualAsync()
    {
        if (DisplayEditor.ActiveProfileVisual != ProfileActivationVisual.HackerMatrix)
            return;
        try
        {
            await profileVisualFeedback.ShowActivatedAsync(
                CurrentProfileName(), lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private void RefreshBaseDisplay() =>
        DisplayEditor.UpdateRuntimeState(
            SafeActiveProfileId(), CurrentProfileName(), IsConnected);

    private uint SafeActiveProfileId()
    {
        try { return profileService.ActiveProfile.Id; }
        catch (InvalidOperationException) { return 0; }
    }

    private IReadOnlyList<DisplayProfileReference> VisualProfiles() =>
        profileService.Profiles
            .Select(profile => new DisplayProfileReference(profile.Id, profile.Name))
            .ToArray();

    private string CurrentProfileName()
    {
        try
        {
            return profileService.ActiveProfile.Name;
        }
        catch (InvalidOperationException)
        {
            return "Sin perfil";
        }
    }

    private static IReadOnlyList<KeyOption> CreateKeyOptions()
    {
        var keys = new List<KeyOption>();
        for (byte index = 0; index < 26; index++)
        {
            keys.Add(new(((char)('A' + index)).ToString(), (byte)(0x04 + index)));
        }
        keys.AddRange(
        [
            new("1", 0x1E), new("2", 0x1F), new("3", 0x20),
            new("4", 0x21), new("5", 0x22), new("6", 0x23),
            new("7", 0x24), new("8", 0x25), new("9", 0x26),
            new("0", 0x27), new("Enter", 0x28), new("Escape", 0x29),
            new("Tab", 0x2B), new("Space", 0x2C),
        ]);
        return keys;
    }

    private void RefreshControlPresentations()
    {
        var profile = EditingProfile();
        if (profile is null) return;

        foreach (var binding in profile.Bindings)
        {
            if (binding.Control.Type == ControlType.Button &&
                binding.Control.Index >= ProfileControls.UserButtonCount)
                continue;
            var label = ControlText(binding.Control);
            var item = ButtonPresentations.Concat(EncoderPresentations)
                .First(value => value.Control == binding.Control);
            var iconKey = binding.Control == selectedControl
                ? SelectedIconOption.Key
                : bindingIconStore.Get(profile.Id, binding.Control);
            item.Update(
                binding.Control.Type == ControlType.Button
                    ? (binding.Control.Index + 1).ToString()
                    : binding.Control.Type switch
                    {
                        ControlType.EncoderCounterClockwise => "Giro",
                        ControlType.EncoderPress => "Pulsar",
                        ControlType.EncoderClockwise => "Giro",
                        _ => string.Empty,
                    },
                binding.Control.Type switch
                {
                    ControlType.EncoderCounterClockwise => "Giro izquierda",
                    ControlType.EncoderPress => "Pulsación",
                    ControlType.EncoderClockwise => "Giro derecha",
                    _ => label,
                },
                PresentationLabel(profile.Id, binding),
                BindingPresentation.ResolveIcon(
                    iconKey, binding.Action, id => hostActionRegistry.GetById(id)),
                binding.Control.Type switch
                {
                    ControlType.EncoderCounterClockwise => "↶",
                    ControlType.EncoderPress => "●",
                    ControlType.EncoderClockwise => "↷",
                    _ => BindingPresentation.IconGlyph(
                        binding.Action, id => hostActionRegistry.GetById(id)),
                },
                BindingPresentation.IsSelected(binding.Control, selectedControl));
        }
    }

    private void InitializeControlPresentationSlots()
    {
        for (byte index = 0; index < ProfileControls.UserButtonCount; index++)
            ButtonPresentations.Add(new ControlPresentationItem(
                new ControlId(ControlType.Button, index), SelectControlCommand));
        foreach (var type in new[]
                 {
                     ControlType.EncoderCounterClockwise,
                     ControlType.EncoderPress,
                     ControlType.EncoderClockwise,
                 })
            EncoderPresentations.Add(new ControlPresentationItem(
                new ControlId(type, 0), SelectControlCommand));
    }

    private void LoadSelectedIcon(uint profileId)
    {
        var key = bindingIconStore.Get(profileId, selectedControl);
        suppressIconRefresh = true;
        SelectedIconOption = IconOptions.FirstOrDefault(option => option.Key == key) ??
            IconOptions[0];
        suppressIconRefresh = false;
    }

    private void RefreshSelectedIconPreview() =>
        SelectedControlIconGlyph = BindingPresentation.ResolveIcon(
            SelectedIconOption.Key,
            selectedPresentationAction,
            id => hostActionRegistry.GetById(id));

    private string PresentationLabel(uint profileId, BindingInfo binding)
    {
        var manual = binding.Control == selectedControl &&
            profileId == EditingProfile()?.Id ? SelectedScreenLabel :
            bindingLabelStore.Get(profileId, binding.Control);
        return string.IsNullOrWhiteSpace(manual)
            ? BindingPresentation.Describe(binding.Action, id => hostActionRegistry.GetById(id))
            : manual.Trim();
    }

    private void UpdateDashboardDraft()
    {
        var profileId = EditingProfile()?.Id;
        if (profileId is null || profileId != SafeActiveProfileId())
        {
            Dashboard.ClearDraftPresentation();
            return;
        }
        Dashboard.SetDraftPresentation(
            profileId.Value, selectedControl, SelectedScreenLabel, SelectedIconOption.Key);
    }

    private StreamDeckProfile? EditingProfile() =>
        Profiles.SelectedProfile ?? Profiles.ActiveProfile;

    private void RefreshBindingHostActionOptions()
    {
        var selectedId = SelectedHostAction?.Id;
        BindingHostActionOptions.Clear();
        foreach (var action in hostActionRegistry.GetAll())
        {
            BindingHostActionOptions.Add(new BindingHostActionOption(
                action,
                BindingPresentation.FriendlyHostActionName(action),
                string.IsNullOrWhiteSpace(action.Summary)
                    ? action.KindDisplayName
                    : $"{action.KindDisplayName} · {action.Summary}"));
        }
        if (selectedId.HasValue)
            SelectedHostAction = hostActionRegistry.GetById(selectedId.Value);
        OnPropertyChanged(nameof(SelectedBindingHostActionOption));
        OnPropertyChanged(nameof(SelectedHostActionDetail));
    }
}

public sealed record KeyOption(string Name, byte KeyCode)
{
    public override string ToString() => Name;
}

public sealed record ConsumerOption(string Name, ConsumerControlAction Action)
{
    public override string ToString() => Name;
}
