using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Microsoft.UI.Dispatching;
using StreamDeckDIY.App.Services;
using StreamDeckDIY.Core.Dashboard;
using StreamDeckDIY.Core.Devices;
using StreamDeckDIY.Core.Display;
using StreamDeckDIY.Core.HostActions;
using StreamDeckDIY.Core.Profiles;
using StreamDeckDIY.Protocol.Models;

namespace StreamDeckDIY.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject, IAsyncDisposable
{
    public event EventHandler? PageChanged;
    private readonly DashboardConfigurationStore store;
    private readonly CompanionPackCatalog companionPacks;
    private readonly CompanionAssetProcessor companionAssetProcessor = new();
    private readonly ICompanionPackInteraction companionInteraction;
    private readonly DashboardAutosave autosave;
    private readonly IProfileService profiles;
    private readonly BindingIconStore icons;
    private readonly BindingLabelStore labels;
    private readonly IHostActionRegistry hostActions;
    private readonly IDeviceService device;
    private readonly INowPlayingProvider nowPlaying;
    private readonly ISystemStatsProvider systemStats;
    private readonly IClockProvider clock;
    private readonly DisplayConfiguration displayConfiguration;
    private readonly DispatcherQueue dispatcher;
    private readonly DispatcherQueueTimer clockTimer;
    private readonly DispatcherQueueTimer mascotTimer;
    private readonly SemaphoreSlim profileGate = new(1, 1);
    private DashboardConfiguration configuration = DashboardConfiguration.CreateDefault(0);
    private DashboardPreset selectedPreset = DashboardPresets.ControlInfo;
    private DashboardTheme selectedTheme = DashboardTheme.Dark;
    private VisualIdentity selectedVisualIdentity = VisualIdentity.Hikari;
    private FunctionalPreset selectedFunctionalPreset = FunctionalPreset.Control;
    private DashboardPreviewScale selectedPreviewScale = DashboardPreviewScale.Fit;
    private string activeProfileName = "Sin perfil";
    private string clockText = "--:--";
    private string interactionText = "Pulsa la preview para simular touch.";
    private NowPlayingState mediaState = new(false, false, "Sin reproducción", "", "", null, "");
    private SystemStatsState statsState = SystemStatsState.Unavailable;
    private int mascotPhase;
    private readonly SystemStatsAlertGate statsAlert = new();
    private DateTimeOffset reactionUntil;
    private bool suppressSave;
    private bool showTouchTargets;
    private double fitFactor = 1;
    private (uint ProfileId, ControlId Control, string? Label, string? Icon)? draft;
    private CompanionPack? selectedCompanionPack;
    private CompanionResolvedAsset? companionAsset;
    private CompanionMood? previewCompanionMood;
    private string companionStatusText = "Biblioteca Companion pendiente.";
    private string? resolvedCompanionKey;
    private readonly CompanionRequestCoordinator companionRequests = new();
    private ImageSource? companionPreviewImageSource;

    public DashboardViewModel(DashboardConfigurationStore store, CompanionPackCatalog companionPacks,
        ICompanionPackInteraction companionInteraction, IProfileService profiles,
        BindingIconStore icons, BindingLabelStore labels, IHostActionRegistry hostActions,
        IDeviceService device, INowPlayingProvider nowPlaying, ISystemStatsProvider systemStats,
        IClockProvider clock, DisplayConfiguration displayConfiguration, DispatcherQueue dispatcher)
    {
        this.store = store; this.companionPacks = companionPacks;
        this.companionInteraction = companionInteraction;
        this.profiles = profiles; this.icons = icons; this.labels = labels;
        this.hostActions = hostActions; this.device = device; this.nowPlaying = nowPlaying;
        this.systemStats = systemStats; this.clock = clock;
        this.displayConfiguration = displayConfiguration; this.dispatcher = dispatcher;
        clockTimer = dispatcher.CreateTimer(); clockTimer.Interval = TimeSpan.FromMinutes(1);
        clockTimer.IsRepeating = true; clockTimer.Tick += OnClockTick;
        mascotTimer = dispatcher.CreateTimer(); mascotTimer.Interval = TimeSpan.FromMilliseconds(350);
        mascotTimer.IsRepeating = true; mascotTimer.Tick += OnMascotTick;
        autosave = new DashboardAutosave(store.SaveAsync, TimeSpan.FromMilliseconds(650));
        autosave.StateChanged += OnAutosaveStateChanged;
        ImportCompanionPackCommand = new AsyncRelayCommand(_ => ImportCompanionPackAsync());
        DeleteCompanionPackCommand = new AsyncRelayCommand(_ => DeleteCompanionPackAsync());
        PreviewCompanionMoodCommand = new AsyncRelayCommand(PreviewCompanionMoodAsync);
    }

    public event EventHandler? RenderStateChanged;
    public IReadOnlyList<DashboardPreset> Presets => DashboardPresets.All;
    public IReadOnlyList<DashboardTheme> Themes { get; } = Enum.GetValues<DashboardTheme>();
    public IReadOnlyList<VisualIdentity> VisualIdentities => VisualSystemCatalog.SelectableIdentities;
    public IReadOnlyList<FunctionalPreset> FunctionalPresets => VisualSystemCatalog.SelectablePresets;
    public IReadOnlyList<DashboardPreviewScale> PreviewScales { get; } = Enum.GetValues<DashboardPreviewScale>();
    public ObservableCollection<DashboardControlState> Buttons { get; } = [];
    public ObservableCollection<DashboardControlState> Encoder { get; } = [];
    public ObservableCollection<CompanionPack> InstalledCompanionPacks { get; } = [];
    public ICommand ImportCompanionPackCommand { get; }
    public ICommand DeleteCompanionPackCommand { get; }
    public ICommand PreviewCompanionMoodCommand { get; }
    public CompanionPack? SelectedCompanionPack
    {
        get => selectedCompanionPack;
        set
        {
            if (!SetProperty(ref selectedCompanionPack, value)) return;
            OnPropertyChanged(nameof(CanDeleteCompanionPack));
            OnPropertyChanged(nameof(CompanionPackName));
            OnPropertyChanged(nameof(CompanionPackAuthor));
            companionRequests.Invalidate();
            if (value is null || suppressSave) return;
            configuration = configuration with { CompanionPackId = value.Id };
            QueueSave();
            _ = RefreshCompanionAssetAsync(CurrentCompanionState().Mood);
        }
    }
    public string CompanionStatusText
    {
        get => companionStatusText;
        private set => SetProperty(ref companionStatusText, value);
    }
    public bool CanDeleteCompanionPack => SelectedCompanionPack is { IsOfficial: false };
    public string CompanionPackName => SelectedCompanionPack?.Name ?? "Sin pack";
    public string CompanionPackAuthor => string.IsNullOrWhiteSpace(SelectedCompanionPack?.Author)
        ? "Autor no indicado" : $"por {SelectedCompanionPack.Author}";
    public string CompanionMoodText => companionAsset is { } asset
        ? CompanionMicrocopy.For(asset.RequestedMood) : "Sin sprite activo";
    public ImageSource? CompanionPreviewImageSource
    {
        get => companionPreviewImageSource;
        private set => SetProperty(ref companionPreviewImageSource, value);
    }
    public Visibility CompanionSectionVisibility =>
        SelectedFunctionalPreset == FunctionalPreset.Companion ? Visibility.Visible : Visibility.Collapsed;

    public DashboardPreset SelectedPreset
    {
        get => selectedPreset;
        set { if (value is not null) SelectedFunctionalPreset = DashboardPresets.FunctionalFrom(value.Id); }
    }
    public VisualIdentity SelectedVisualIdentity
    {
        get => selectedVisualIdentity;
        set
        {
            if (!SetProperty(ref selectedVisualIdentity, value) || suppressSave) return;
            configuration = configuration with { VisualIdentity = value };
            OnPropertyChanged(nameof(AppearanceSummary));
            QueueSave();
            _ = RefreshCompanionSelectionAsync();
            NotifyRender();
        }
    }
    public FunctionalPreset SelectedFunctionalPreset
    {
        get => selectedFunctionalPreset;
        set
        {
            value = VisualSystemCatalog.Normalize(value);
            if (!SetProperty(ref selectedFunctionalPreset, value)) return;
            var preset = DashboardPresets.For(value);
            if (selectedPreset != preset)
            {
                selectedPreset = preset;
                OnPropertyChanged(nameof(SelectedPreset));
            }
            OnPropertyChanged(nameof(AppearanceSummary));
            OnPropertyChanged(nameof(CompanionSectionVisibility));
            if (suppressSave) return;
            var options = configuration.Widgets.ToDictionary(w => w.Kind, w => w.Options ?? new());
            configuration = configuration with
            {
                FunctionalPreset = value,
                PresetId = preset.Id,
                Widgets = preset.DefaultWidgets().Select(w => w with { Options = options[w.Kind] }).ToArray(),
            };
            NotifyWidgets(); QueueSave();
        }
    }
    public DashboardTheme SelectedTheme
    {
        get => selectedTheme;
        set { if (!SetProperty(ref selectedTheme, value) || suppressSave) return; configuration = configuration with { Theme = value }; QueueSave(); NotifyRender(); }
    }
    public DashboardPreviewScale SelectedPreviewScale
    {
        get => selectedPreviewScale;
        set { if (SetProperty(ref selectedPreviewScale, value)) { OnPropertyChanged(nameof(PreviewWidth)); OnPropertyChanged(nameof(PreviewHeight)); } }
    }
    public double PreviewWidth => displayConfiguration.Width * PreviewFactor;
    public double PreviewHeight => displayConfiguration.Height * PreviewFactor;
    private double PreviewFactor => SelectedPreviewScale switch
    { DashboardPreviewScale.Logical1To1 => 1, DashboardPreviewScale.Percent150 => 1.5,
      DashboardPreviewScale.Percent200 => 2, DashboardPreviewScale.Percent250 => 2.5, _ => fitFactor };
    public string ActiveProfileName { get => activeProfileName; private set => SetProperty(ref activeProfileName, value); }
    public string AppearanceSummary => $"{SelectedVisualIdentity} · {SelectedFunctionalPreset} · {ActiveProfileName}";
    public string ClockText { get => clockText; private set => SetProperty(ref clockText, value); }
    public string InteractionText { get => interactionText; private set => SetProperty(ref interactionText, value); }
    public string AutosaveStatusText => autosave.State switch
    {
        DashboardSaveState.Dirty => "Guardando…",
        DashboardSaveState.Saving => "Guardando…",
        DashboardSaveState.Error => "Error al guardar",
        _ => "Guardado",
    };
    public bool IsDashboardDirty => autosave.IsDirty;
    public string NowPlayingDetail => mediaState.IsAvailable
        ? string.Join(" · ", new[] { mediaState.Title, mediaState.Artist }.Where(s => !string.IsNullOrWhiteSpace(s)))
        : "Sin reproducción";
    public string SystemStatsDetail => SystemStatsPresentation.Detail(statsState);
    public string HardwareStatsStatusText => SystemStatsPresentation.HardwareStatus(statsState);
    public string CpuSensorStatusText => SystemStatsPresentation.CpuSensorStatus(statsState);
    public string GpuSensorStatusText => SystemStatsPresentation.GpuSensorStatus(statsState);
    public string TemperatureSensorStatusText => SystemStatsPresentation.TemperatureSensorStatus(statsState);
    public string HardwareStatsDiagnosticText => SystemStatsPresentation.Diagnostic(statsState);
    public bool ButtonMatrixEnabled { get => Enabled(DashboardWidgetKind.ButtonMatrix); set => SetEnabled(DashboardWidgetKind.ButtonMatrix, value); }
    public bool EncoderEnabled { get => Enabled(DashboardWidgetKind.Encoder); set => SetEnabled(DashboardWidgetKind.Encoder, value); }
    public bool ProfileEnabled { get => Enabled(DashboardWidgetKind.Profile); set => SetEnabled(DashboardWidgetKind.Profile, value); }
    public bool ClockEnabled { get => Enabled(DashboardWidgetKind.Clock); set => SetEnabled(DashboardWidgetKind.Clock, value); }
    public bool NowPlayingEnabled { get => Enabled(DashboardWidgetKind.NowPlaying); set => SetEnabled(DashboardWidgetKind.NowPlaying, value); }
    public bool SystemStatsEnabled { get => Enabled(DashboardWidgetKind.SystemStats); set => SetEnabled(DashboardWidgetKind.SystemStats, value); }
    public bool MascotEnabled { get => Enabled(DashboardWidgetKind.Mascot); set => SetEnabled(DashboardWidgetKind.Mascot, value); }
    public bool ShowLabels { get => configuration.Options(DashboardWidgetKind.ButtonMatrix).ShowLabels; set => SetOption(DashboardWidgetKind.ButtonMatrix, o => o with { ShowLabels = value }, nameof(ShowLabels)); }
    public bool Use24HourClock { get => configuration.Options(DashboardWidgetKind.Clock).Use24HourClock; set => SetOption(DashboardWidgetKind.Clock, o => o with { Use24HourClock = value }, nameof(Use24HourClock), UpdateClock); }
    public bool ShowArtwork { get => configuration.Options(DashboardWidgetKind.NowPlaying).ShowArtwork; set => SetOption(DashboardWidgetKind.NowPlaying, o => o with { ShowArtwork = value }, nameof(ShowArtwork)); }
    public bool ShowCpu { get => configuration.Options(DashboardWidgetKind.SystemStats).ShowCpu; set => SetPrimaryMetric(nameof(ShowCpu), value); }
    public bool ShowGpu { get => configuration.Options(DashboardWidgetKind.SystemStats).ShowGpu; set => SetPrimaryMetric(nameof(ShowGpu), value); }
    public bool ShowRam { get => configuration.Options(DashboardWidgetKind.SystemStats).ShowRam; set => SetPrimaryMetric(nameof(ShowRam), value); }
    public bool ShowTemperatures { get => configuration.Options(DashboardWidgetKind.SystemStats).ShowTemperatures; set => SetOption(DashboardWidgetKind.SystemStats, o => o with { ShowTemperatures = value }, nameof(ShowTemperatures)); }
    public bool ShowNetwork { get => configuration.Options(DashboardWidgetKind.SystemStats).ShowNetwork; set => SetOption(DashboardWidgetKind.SystemStats, o => o with { ShowNetwork = value }, nameof(ShowNetwork)); }
    public bool ShowDisk { get => configuration.Options(DashboardWidgetKind.SystemStats).ShowDisk; set => SetOption(DashboardWidgetKind.SystemStats, o => o with { ShowDisk = value }, nameof(ShowDisk)); }
    public bool ReducedMascotMotion { get => configuration.Options(DashboardWidgetKind.Mascot).ReducedMascotMotion; set => SetOption(DashboardWidgetKind.Mascot, o => o with { ReducedMascotMotion = value }, nameof(ReducedMascotMotion)); }
    public bool ShowTouchTargets
    {
        get => showTouchTargets;
        set { if (SetProperty(ref showTouchTargets, value)) NotifyRender(); }
    }

    public DashboardRuntimeState RenderState()
    {
        StreamDeckProfile? profile;
        try { profile = profiles.ActiveProfile; }
        catch (InvalidOperationException) { profile = null; }
        return new(PreviewConfiguration(), ActiveProfileName, ClockText,
            mediaState, statsState,
            MascotVisualEngine.Frame(CurrentMascotState(), mascotPhase, ReducedMascotMotion),
            Buttons.ToArray(), Encoder.ToArray(), CurrentCompanionState(),
            profile?.ActivePage.Name ?? "Página 1", profile?.ActivePageIndex ?? 0,
            profile?.Pages.Length ?? 1, profiles.IsHome, companionAsset);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
        await companionPacks.InitializeAsync(cancellationToken);
        RefreshCompanionPackList();
        nowPlaying.StateChanged += OnNowPlayingChanged;
        systemStats.StateChanged += OnSystemStatsChanged;
        try { await nowPlaying.InitializeAsync(cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { InteractionText = $"Multimedia no disponible: {exception.Message}"; }
        try { await systemStats.InitializeAsync(cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { InteractionText = $"Estadísticas no disponibles: {exception.Message}"; }
        mediaState = nowPlaying.Current;
        ApplySystemStatsState(systemStats.Current);
        await ActivateProfileAsync(cancellationToken); UpdateClock(); clockTimer.Start(); UpdateMascotTimer();
    }

    public async Task ActivateProfileAsync(CancellationToken cancellationToken = default)
    {
        await profileGate.WaitAsync(cancellationToken);
        try
        {
            await autosave.FlushAsync(cancellationToken);

            StreamDeckProfile? active;
            try { active = profiles.ActiveProfile; } catch (InvalidOperationException) { active = null; }
            var profile = DashboardProfileResolver.Resolve(profiles.Profiles, active?.Id);
            suppressSave = true;
            configuration = profile is null ? DashboardConfiguration.CreateDefault(0) : store.Get(profile.Id);
            previewCompanionMood = null;
            companionRequests.Invalidate();
            configuration = configuration.WithOptions(DashboardWidgetKind.ButtonMatrix,
                options => options with { ShowTouchTargets = false });
            SelectedVisualIdentity = configuration.VisualIdentity;
            SelectedFunctionalPreset = configuration.FunctionalPreset;
            SelectedTheme = configuration.Theme;
            ShowTouchTargets = false;
            suppressSave = false;
            ActiveProfileName = profile?.Name ?? "Sin perfil";
            OnPropertyChanged(nameof(AppearanceSummary));
            if (profile is null) { Buttons.Clear(); Encoder.Clear(); }
            else RefreshBindings(profile);
            await RefreshCompanionSelectionAsync(cancellationToken);
            NotifyOptions(); NotifyWidgets(); UpdateClock();
        }
        finally { profileGate.Release(); }
    }

    public void ProfileActivated()
    {
        reactionUntil = DateTimeOffset.Now.AddSeconds(1.5);
        configuration = configuration with { MascotState = MascotState.Happy };
        UpdateMascotTimer(); NotifyRender();
    }

    public void SetDraftPresentation(uint profileId, ControlId control, string? label, string? icon)
    { draft = (profileId, control, label, icon); RefreshBindings(); }
    public void ClearDraftPresentation() { draft = null; RefreshBindings(); }
    public void RefreshBindings()
    {
        try { RefreshBindings(profiles.ActiveProfile); }
        catch (InvalidOperationException) { Buttons.Clear(); Encoder.Clear(); NotifyRender(); }
    }

    public async Task HandleInteractionAsync(DashboardPoint start, DashboardPoint end,
        TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var interaction = DashboardInteractionResolver.Resolve(configuration, start, end, duration);
        if (interaction is null) { InteractionText = "Fuera de las regiones táctiles."; return; }
        if (interaction.Action == TouchAction.ExecuteBinding && interaction.Control is { } control)
        {
            await ExecuteControlAsync(control, cancellationToken);
        }
        else if (interaction.Action == TouchAction.SystemNavigation &&
                 interaction.SystemNavigation is { } navigation)
        {
            await profiles.NavigateAsync(profiles.ActiveProfile.Id, navigation, cancellationToken);
            await ActivateProfileAsync(cancellationToken);
            InteractionText = $"Navegación: {navigation}.";
            PageChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (interaction.Action == TouchAction.MediaControl && interaction.MediaCommand is { } command)
            InteractionText = await nowPlaying.ControlAsync(command, cancellationToken)
                ? $"Control multimedia: {command}" : "La sesión multimedia no admite esta acción.";
        else if (interaction.Action == TouchAction.MascotInteracted)
        { reactionUntil = DateTimeOffset.Now.AddSeconds(1.5); configuration = configuration with { MascotState = MascotState.Happy }; UpdateMascotTimer(); InteractionText = "Mascota: reacción feliz."; }
        else InteractionText = interaction.Gesture is TouchGesture.SwipeLeft or TouchGesture.SwipeRight
            ? $"{interaction.Gesture} preparado para navegación futura." : $"Touch: {interaction.Widget}.";
        NotifyRender();
    }

    public async Task ExecuteControlAsync(
        ControlId control, CancellationToken cancellationToken = default,
        uint? touchTraceId = null)
    {
        var state = Buttons.Concat(Encoder).FirstOrDefault(item =>
            item.Control == control);
        if (state is null)
        {
            LogTouch(touchTraceId,
                $"TOUCH BINDING DROP trace={touchTraceId} reason=control_not_found control={control.Type}:{control.Index}");
            return;
        }
        LogTouch(touchTraceId,
            $"TOUCH BINDING trace={touchTraceId} type={state.Action.Type} control={control.Type}:{control.Index}");
        if (!device.IsConnected)
        {
            LogTouch(touchTraceId,
                $"TOUCH EXEC FAIL trace={touchTraceId} reason=device_not_connected");
            InteractionText = "Control identificado; dispositivo no conectado.";
            return;
        }
        var path = state.Action.Type switch
        {
            ActionType.Keyboard or ActionType.KeyboardShortcut => "Keyboard",
            ActionType.ConsumerControl => "ConsumerControl",
            ActionType.HostAction => "HostAction",
            _ => state.Action.Type.ToString(),
        };
        var detail = state.Action.Type == ActionType.HostAction
            ? $" host_action_id={state.Action.HostActionId}"
            : string.Empty;
        LogTouch(touchTraceId,
            $"TOUCH EXEC trace={touchTraceId} path={path}{detail}");
        LogTouch(touchTraceId, $"TOUCH EXEC BEGIN trace={touchTraceId}");
        try
        {
            await device.ExecuteActionTestAsync(state.Action, cancellationToken);
            LogTouch(touchTraceId, $"TOUCH EXEC OK trace={touchTraceId}");
            InteractionText = $"Ejecutado mediante firmware: {state.Summary}";
        }
        catch (Exception exception)
        {
            LogTouch(touchTraceId,
                $"TOUCH EXEC FAIL trace={touchTraceId} reason={exception.GetType().Name} detail={exception.Message}");
            throw;
        }
    }

    private static void LogTouch(uint? traceId, string message)
    {
        if (traceId.HasValue) System.Diagnostics.Debug.WriteLine(message);
    }

    public void SetInteractionError(string message) =>
        InteractionText = $"No se pudo ejecutar la interacción: {message}";

    public void SetPreviewViewport(double width, double height)
    {
        if (width <= 20 || height <= 20) return;
        fitFactor = Math.Max(.25, Math.Min(
            (width - 20) / displayConfiguration.Width,
            (height - 20) / displayConfiguration.Height));
        if (SelectedPreviewScale == DashboardPreviewScale.Fit)
        { OnPropertyChanged(nameof(PreviewWidth)); OnPropertyChanged(nameof(PreviewHeight)); }
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAutosaveAsync();
        companionRequests.Invalidate();
        clockTimer.Stop(); mascotTimer.Stop();
        clockTimer.Tick -= OnClockTick; mascotTimer.Tick -= OnMascotTick;
        nowPlaying.StateChanged -= OnNowPlayingChanged; systemStats.StateChanged -= OnSystemStatsChanged;
        autosave.StateChanged -= OnAutosaveStateChanged;
        await autosave.DisposeAsync();
        profileGate.Dispose();
        await nowPlaying.DisposeAsync(); await systemStats.DisposeAsync();
    }

    public Task FlushBeforeProfileSwitchAsync(CancellationToken cancellationToken) =>
        autosave.FlushAsync(cancellationToken);

    public async Task FlushAutosaveAsync()
    {
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
        {
            try { await autosave.FlushAsync(timeout.Token); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                InteractionText = $"No se pudo guardar dashboard-config.json: {exception.Message}";
                System.Diagnostics.Debug.WriteLine(InteractionText);
            }
            catch (OperationCanceledException)
            {
                InteractionText = "No se pudo completar el guardado de dashboard-config.json al cerrar.";
                System.Diagnostics.Debug.WriteLine(InteractionText);
            }
        }
    }

    private void RefreshBindings(StreamDeckProfile profile)
    {
        var projected = DashboardBindingProjection.Build(profile.Bindings,
            binding => ResolveLabel(profile.Id, binding), binding => ResolveIcon(profile.Id, binding));
        Buttons.Clear(); foreach (var item in projected.Buttons) Buttons.Add(item);
        Encoder.Clear(); foreach (var item in projected.Encoder) Encoder.Add(item);
        NotifyRender();
    }
    private string ResolveLabel(uint profileId, BindingInfo binding)
    {
        var manual = draft is { } d && d.ProfileId == profileId && d.Control == binding.Control ? d.Label : labels.Get(profileId, binding.Control);
        return string.IsNullOrWhiteSpace(manual) ? BindingPresentation.Describe(binding.Action, id => hostActions.GetById(id)) : manual.Trim();
    }
    private string ResolveIcon(uint profileId, BindingInfo binding)
    {
        var manual = draft is { } d && d.ProfileId == profileId && d.Control == binding.Control ? d.Icon : icons.Get(profileId, binding.Control);
        return BindingPresentation.ResolveIcon(manual, binding.Action, id => hostActions.GetById(id));
    }
    private bool Enabled(DashboardWidgetKind kind) => configuration.IsEnabled(kind);
    private void SetEnabled(DashboardWidgetKind kind, bool enabled)
    { if (Enabled(kind) == enabled) return; configuration = configuration.WithWidget(kind, enabled); NotifyWidgets(); QueueSave(); }
    private void SetOption(DashboardWidgetKind kind, Func<DashboardWidgetOptions, DashboardWidgetOptions> update,
        string property, Action? after = null)
    {
        var current = configuration.Options(kind);
        var changed = update(current);
        if (changed == current) return;
        configuration = configuration.WithOptions(kind, _ => changed);
        OnPropertyChanged(property); after?.Invoke(); QueueSave(); NotifyRender();
    }
    private void SetPrimaryMetric(string property, bool value)
    {
        var options = configuration.Options(DashboardWidgetKind.SystemStats);
        var changed = property switch
        {
            nameof(ShowCpu) => options with { ShowCpu = value },
            nameof(ShowGpu) => options with { ShowGpu = value },
            _ => options with { ShowRam = value },
        };
        if (!changed.ShowCpu && !changed.ShowGpu && !changed.ShowRam)
        { OnPropertyChanged(property); return; }
        SetOption(DashboardWidgetKind.SystemStats, _ => changed, property);
    }
    private void NotifyWidgets()
    {
        foreach (var name in new[] { nameof(ButtonMatrixEnabled), nameof(EncoderEnabled), nameof(ProfileEnabled),
            nameof(ClockEnabled), nameof(NowPlayingEnabled), nameof(SystemStatsEnabled), nameof(MascotEnabled) }) OnPropertyChanged(name);
        UpdateMascotTimer(); NotifyRender();
    }
    private void NotifyOptions()
    { foreach (var name in new[] { nameof(ShowLabels), nameof(Use24HourClock), nameof(ShowArtwork), nameof(ShowCpu), nameof(ShowGpu), nameof(ShowRam), nameof(ShowTemperatures), nameof(ShowNetwork), nameof(ShowDisk), nameof(ReducedMascotMotion), nameof(ShowTouchTargets) }) OnPropertyChanged(name); }
    private void UpdateClock() { ClockText = clock.Now.ToLocalTime().ToString(Use24HourClock ? "HH:mm" : "h:mm tt"); NotifyRender(); }
    private void OnClockTick(DispatcherQueueTimer sender, object args) => UpdateClock();
    private void OnMascotTick(DispatcherQueueTimer sender, object args)
    {
        mascotPhase++;
        if (reactionUntil != default && DateTimeOffset.Now >= reactionUntil)
        { reactionUntil = default; configuration = configuration with { MascotState = mediaState.IsPlaying ? MascotState.Music : MascotState.Idle }; }
        NotifyRender(); UpdateMascotTimer();
    }
    private MascotState CurrentMascotState() => reactionUntil != default ? configuration.MascotState
        : statsAlert.IsAlert ? MascotState.Alert
        : mediaState.IsPlaying ? MascotState.Music : configuration.MascotState;
    private CompanionState CurrentCompanionState()
    {
        var mood = previewCompanionMood ?? CurrentMascotState() switch
        {
            MascotState.Happy => CompanionMood.Happy,
            MascotState.Thinking or MascotState.Music => CompanionMood.Busy,
            MascotState.Sleep => CompanionMood.Sleeping,
            MascotState.Alert => CompanionMood.Alert,
            _ => CompanionMood.Neutral,
        };
        var message = CompanionMicrocopy.For(mood);
        return new(mood, message, ActiveProfileName,
            SelectedVisualIdentity == VisualIdentity.Hikari ? MascotVariant.Hikari : MascotVariant.Neon);
    }
    private void UpdateMascotTimer()
    { if (MascotEnabled && !ReducedMascotMotion) mascotTimer.Start(); else mascotTimer.Stop(); }
    private void OnNowPlayingChanged(object? sender, NowPlayingState state) => dispatcher.TryEnqueue(() =>
    { mediaState = state; OnPropertyChanged(nameof(NowPlayingDetail)); UpdateMascotTimer();
      _ = RefreshCompanionAssetAsync(CurrentCompanionState().Mood); NotifyRender(); });
    private void OnSystemStatsChanged(object? sender, SystemStatsState state) => dispatcher.TryEnqueue(() =>
        ApplySystemStatsState(state));
    private void ApplySystemStatsState(SystemStatsState? state)
    {
        statsState = state ?? SystemStatsState.Unavailable;
        statsAlert.Update(statsState);
        foreach (var property in new[] { nameof(SystemStatsDetail), nameof(HardwareStatsStatusText),
            nameof(CpuSensorStatusText), nameof(GpuSensorStatusText), nameof(TemperatureSensorStatusText),
            nameof(HardwareStatsDiagnosticText) }) OnPropertyChanged(property);
        _ = RefreshCompanionAssetAsync(CurrentCompanionState().Mood);
        NotifyRender();
    }

    private void RefreshCompanionPackList()
    {
        InstalledCompanionPacks.Clear();
        foreach (var pack in companionPacks.Packs) InstalledCompanionPacks.Add(pack);
    }

    private async Task RefreshCompanionSelectionAsync(CancellationToken token = default)
    {
        try
        {
            var selected = companionPacks.ResolvePack(configuration.CompanionPackId, SelectedVisualIdentity);
            var previous = suppressSave;
            suppressSave = true;
            SelectedCompanionPack = selected;
            suppressSave = previous;
            OnPropertyChanged(nameof(CanDeleteCompanionPack));
            CompanionStatusText = $"{selected.Name} · {selected.Version}";
            await RefreshCompanionAssetAsync(CurrentCompanionState().Mood, token);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            companionAsset = null;
            CompanionPreviewImageSource = null;
            OnPropertyChanged(nameof(CompanionMoodText));
            CompanionStatusText = $"Fallback simple: {exception.Message}";
            NotifyRender();
        }
    }

    private async Task RefreshCompanionAssetAsync(CompanionMood mood, CancellationToken token = default)
    {
        var identity = SelectedVisualIdentity;
        var packId = SelectedCompanionPack?.Id ?? configuration.CompanionPackId ?? "default";
        var background = DashboardVisualTokenResolver.CompanionBackground(
            identity, configuration.EffectiveVisualOptions);
        var requestKey = CompanionRasterPolicy.RequestKey(
            packId, identity, mood, background);
        if (requestKey == appliedCompanionRequestKey ||
            requestKey == processingCompanionRequestKey) return;

        var requestVersion = Interlocked.Increment(ref companionRequestVersion);
        processingCompanionRequestKey = requestKey;
        System.Diagnostics.Debug.WriteLine(
            $"COMPANION RESOLVE request={request.Version} key={requestKey}");
        try
        {
            var resolved = await companionPacks.ResolveAssetAsync(
                SelectedCompanionPack?.Id ?? configuration.CompanionPackId,
                identity, mood, token);
            if (!companionRequests.IsCurrent(request)) return;

            var prepared = resolved is null
                ? null : await companionAssetProcessor.PrepareAsync(
                    resolved, identity, background, token);
            if (!companionRequests.IsCurrent(request)) return;

            var preview = CompanionPreviewImageSource;
            if (prepared?.ContentKey != resolvedCompanionKey)
                preview = prepared is null
                    ? null : await CreatePreviewImageAsync(prepared.PngBytes);
            if (!companionRequests.IsCurrent(request)) return;

            companionAsset = prepared;
            resolvedCompanionKey = prepared?.ContentKey;
            appliedCompanionRequestKey = requestKey;
            CompanionPreviewImageSource = preview;
            OnPropertyChanged(nameof(CompanionMoodText));
            CompanionStatusText = prepared is null
                ? "No hay sprite válido; se usa el placeholder seguro."
                : $"{prepared.Pack.Name} · {CompanionMicrocopy.For(prepared.ResolvedMood)}";
            NotifyRender();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            if (!companionRequests.IsCurrent(request)) return;
            companionAsset = null; resolvedCompanionKey = null;
            CompanionPreviewImageSource = null;
            OnPropertyChanged(nameof(CompanionMoodText));
            CompanionStatusText = $"Sprite no disponible: {exception.Message}";
            NotifyRender();
        }
        finally
        {
            companionRequests.Abandon(request);
        }
    }
    private static async Task<ImageSource> CreatePreviewImageAsync(byte[] bytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer());
        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private async Task ImportCompanionPackAsync()
    {
        var path = await companionInteraction.PickArchiveAsync();
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var imported = await companionPacks.ImportAsync(path);
            RefreshCompanionPackList();
            SelectedCompanionPack = imported;
            OnPropertyChanged(nameof(CanDeleteCompanionPack));
            CompanionStatusText = $"Importado: {imported.Name}";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { CompanionStatusText = $"Pack rechazado: {exception.Message}"; }
    }

    private async Task DeleteCompanionPackAsync()
    {
        var selected = SelectedCompanionPack;
        if (selected is null || selected.IsOfficial) return;
        if (!await companionInteraction.ConfirmDeleteAsync(selected.Name)) return;
        await companionPacks.DeleteAsync(selected.Id);
        configuration = configuration with { CompanionPackId = null };
        QueueSave(); RefreshCompanionPackList();
        await RefreshCompanionSelectionAsync();
    }

    private async Task PreviewCompanionMoodAsync(object? parameter)
    {
        if (parameter is not string text || !Enum.TryParse<CompanionMood>(text, true, out var mood)) return;
        previewCompanionMood = mood;
        await RefreshCompanionAssetAsync(mood);
    }

    private void QueueSave()
    {
        if (suppressSave || configuration.ProfileId == 0) return;
        autosave.Queue(configuration);
        OnPropertyChanged(nameof(IsDashboardDirty));
    }
    private void OnAutosaveStateChanged(object? sender, DashboardSaveStateChangedEventArgs args)
    {
        void Notify()
        {
            OnPropertyChanged(nameof(AutosaveStatusText));
            OnPropertyChanged(nameof(IsDashboardDirty));
        }
        if (dispatcher.HasThreadAccess) Notify(); else dispatcher.TryEnqueue(Notify);
    }
    private DashboardConfiguration PreviewConfiguration() => showTouchTargets
        ? configuration.WithOptions(DashboardWidgetKind.ButtonMatrix,
            options => options with { ShowTouchTargets = true })
        : configuration;
    private void NotifyRender() => RenderStateChanged?.Invoke(this, EventArgs.Empty);
}


