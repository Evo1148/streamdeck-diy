using StreamDeckDIY.Core.Display;
using StreamDeckDIY.Core.Profiles;
using StreamDeckDIY.Protocol.Models;

namespace StreamDeckDIY.Core.Dashboard;

public enum DashboardWidgetKind { ButtonMatrix, Encoder, Profile, Clock, NowPlaying, SystemStats, Mascot, Navigation }
public enum DashboardTheme { Dark, Graphite, Violet }
public enum MascotState { Idle, Happy, Thinking, Alert, Music, Sleep }
public enum TouchGesture { Tap, LongPress, SwipeLeft, SwipeRight }
public enum TouchAction { None, ExecuteBinding, MediaControl, MascotInteracted, Navigate, SystemNavigation }
public enum DashboardMediaCommand { PlayPause, Next, Previous }
public enum DashboardPreviewScale { Fit, Logical1To1, Percent150, Percent200, Percent250 }

public readonly record struct DashboardPoint(double X, double Y);
public readonly record struct DashboardRect(double X, double Y, double Width, double Height)
{
    public bool Contains(DashboardPoint point) => point.X >= X && point.Y >= Y &&
        point.X < X + Width && point.Y < Y + Height;
}

public static class ButtonMatrixLayout
{
    public const int Columns = 3;
    public const int Rows = 3;
    public const int ControlCount = Columns * Rows;

    public static int Index(int row, int column) => row * Columns + column;

    public static DashboardRect Cell(DashboardRect bounds, int index)
    {
        if ((uint)index >= ControlCount) throw new ArgumentOutOfRangeException(nameof(index));
        var column = index % Columns;
        var row = index / Columns;
        var left = bounds.X + bounds.Width * column / Columns;
        var top = bounds.Y + bounds.Height * row / Rows;
        return new(left, top,
            bounds.X + bounds.Width * (column + 1) / Columns - left,
            bounds.Y + bounds.Height * (row + 1) / Rows - top);
    }

    public static int IndexAt(DashboardRect bounds, DashboardPoint point)
    {
        var column = Math.Clamp(
            (int)((point.X - bounds.X) / bounds.Width * Columns), 0, Columns - 1);
        var row = Math.Clamp(
            (int)((point.Y - bounds.Y) / bounds.Height * Rows), 0, Rows - 1);
        return Index(row, column);
    }
}

public sealed record DashboardWidgetOptions(
    bool ShowLabels = true, bool Use24HourClock = true, bool ShowArtwork = true,
    bool ShowCpu = true, bool ShowGpu = true, bool ShowRam = true,
    bool ShowTemperatures = true, bool ShowNetwork = false, bool ShowDisk = false,
    bool ReducedMascotMotion = false, bool ShowTouchTargets = false);
public sealed record DashboardWidgetInstance(
    DashboardWidgetKind Kind, bool Enabled, DashboardWidgetOptions? Options = null);
public sealed record DashboardConfiguration(
    uint ProfileId, string PresetId, DashboardTheme Theme,
    DashboardWidgetInstance[] Widgets, MascotState MascotState = MascotState.Idle,
    VisualIdentity VisualIdentity = VisualIdentity.Hikari,
    FunctionalPreset FunctionalPreset = FunctionalPreset.Control,
    DashboardVisualOptions? VisualOptions = null,
    string? CompanionPackId = null)
{
    public static DashboardConfiguration CreateDefault(uint profileId) => new(
        profileId, DashboardPresets.ControlInfo.Id, DashboardTheme.Dark,
        DashboardPresets.ControlInfo.DefaultWidgets(), VisualOptions: DashboardVisualOptions.Default);
    public DashboardVisualOptions EffectiveVisualOptions =>
        VisualOptions ?? DashboardVisualOptions.Default;
    public bool IsEnabled(DashboardWidgetKind kind) =>
        DashboardWidgetPolicy.IsAlwaysVisible(PresetId, kind) ||
        (Widgets.FirstOrDefault(w => w.Kind == kind)?.Enabled ?? false);
    public DashboardWidgetOptions Options(DashboardWidgetKind kind) => Widgets.FirstOrDefault(w => w.Kind == kind)?.Options ?? new();
    public DashboardConfiguration WithWidget(DashboardWidgetKind kind, bool enabled) =>
        this with { Widgets = Widgets.Select(w => w.Kind == kind ? w with { Enabled = enabled } : w).ToArray() };
    public DashboardConfiguration WithOptions(DashboardWidgetKind kind, Func<DashboardWidgetOptions, DashboardWidgetOptions> update) =>
        this with { Widgets = Widgets.Select(w => w.Kind == kind ? w with { Options = update(w.Options ?? new()) } : w).ToArray() };
}

public enum DashboardRegionKind
{
    Header,
    Clock,
    PrimaryAction,
    Context,
    SecondaryInfo,
    Companion,
    Navigation,
}

public sealed record DashboardRegion(
    DashboardRegionKind Region, DashboardWidgetKind Widget, int Priority);
public sealed record DashboardSlot(DashboardWidgetKind Kind, DashboardRect Bounds);

public static class DashboardWidgetPolicy
{
    private static readonly IReadOnlySet<DashboardWidgetKind> StructuralWidgets =
        Enum.GetValues<DashboardWidgetKind>().ToHashSet();

    public static bool IsAlwaysVisible(string? presetId, DashboardWidgetKind kind) =>
        StructuralWidgets.Contains(kind) &&
        DashboardPresets.Resolve(presetId).Regions.Any(region => region.Widget == kind);
}
public sealed record DashboardPreset(string Id, string Name, IReadOnlyList<DashboardRegion> Regions,
    IReadOnlySet<DashboardWidgetKind> PrimaryWidgets)
{
    public DashboardWidgetInstance[] DefaultWidgets() => Enum.GetValues<DashboardWidgetKind>()
        .Select(kind => new DashboardWidgetInstance(kind, PrimaryWidgets.Contains(kind), new())).ToArray();
    public override string ToString() => Name;
}

public static class DashboardPresets
{
    public static DashboardPreset ControlInfo { get; } = new("control-info", "Control + Info",
    [
        Region(DashboardRegionKind.Header, DashboardWidgetKind.Profile, 10),
        Region(DashboardRegionKind.Clock, DashboardWidgetKind.Clock, 20),
        Region(DashboardRegionKind.PrimaryAction, DashboardWidgetKind.ButtonMatrix, 30),
        Region(DashboardRegionKind.Context, DashboardWidgetKind.NowPlaying, 40),
        Region(DashboardRegionKind.SecondaryInfo, DashboardWidgetKind.SystemStats, 50),
        Region(DashboardRegionKind.Navigation, DashboardWidgetKind.Navigation, 60),
    ], Set(DashboardWidgetKind.ButtonMatrix, DashboardWidgetKind.Profile,
        DashboardWidgetKind.Clock, DashboardWidgetKind.NowPlaying, DashboardWidgetKind.SystemStats,
        DashboardWidgetKind.Navigation));
    public static DashboardPreset Media { get; } = new("media", "Media",
    [
        Region(DashboardRegionKind.Context, DashboardWidgetKind.NowPlaying, 10),
        Region(DashboardRegionKind.PrimaryAction, DashboardWidgetKind.ButtonMatrix, 20),
        Region(DashboardRegionKind.Navigation, DashboardWidgetKind.Navigation, 30),
        Region(DashboardRegionKind.Header, DashboardWidgetKind.Profile, 40),
        Region(DashboardRegionKind.Clock, DashboardWidgetKind.Clock, 50),
        Region(DashboardRegionKind.Companion, DashboardWidgetKind.Mascot, 60),
        Region(DashboardRegionKind.SecondaryInfo, DashboardWidgetKind.SystemStats, 70),
    ], Set(DashboardWidgetKind.NowPlaying, DashboardWidgetKind.ButtonMatrix,
        DashboardWidgetKind.Profile, DashboardWidgetKind.Clock, DashboardWidgetKind.Navigation));
    public static DashboardPreset System { get; } = new("system", "System",
    [
        Region(DashboardRegionKind.SecondaryInfo, DashboardWidgetKind.SystemStats, 10),
        Region(DashboardRegionKind.PrimaryAction, DashboardWidgetKind.ButtonMatrix, 20),
        Region(DashboardRegionKind.Navigation, DashboardWidgetKind.Navigation, 30),
        Region(DashboardRegionKind.Header, DashboardWidgetKind.Profile, 40),
        Region(DashboardRegionKind.Clock, DashboardWidgetKind.Clock, 50),
        Region(DashboardRegionKind.Context, DashboardWidgetKind.NowPlaying, 60),
        Region(DashboardRegionKind.Companion, DashboardWidgetKind.Mascot, 70),
    ], Set(DashboardWidgetKind.SystemStats, DashboardWidgetKind.ButtonMatrix,
        DashboardWidgetKind.Profile, DashboardWidgetKind.Clock, DashboardWidgetKind.Navigation));
    public static DashboardPreset Companion { get; } = new("companion", "Companion",
    [
        Region(DashboardRegionKind.Header, DashboardWidgetKind.Profile, 10),
        Region(DashboardRegionKind.Clock, DashboardWidgetKind.Clock, 20),
        Region(DashboardRegionKind.Companion, DashboardWidgetKind.Mascot, 30),
        Region(DashboardRegionKind.PrimaryAction, DashboardWidgetKind.ButtonMatrix, 40),
        Region(DashboardRegionKind.Navigation, DashboardWidgetKind.Navigation, 50),
        Region(DashboardRegionKind.Context, DashboardWidgetKind.NowPlaying, 60),
        Region(DashboardRegionKind.SecondaryInfo, DashboardWidgetKind.SystemStats, 70),
    ], Set(DashboardWidgetKind.Mascot, DashboardWidgetKind.ButtonMatrix,
        DashboardWidgetKind.Profile, DashboardWidgetKind.Clock, DashboardWidgetKind.NowPlaying,
        DashboardWidgetKind.Navigation));
    public static IReadOnlyList<DashboardPreset> All { get; } = [ControlInfo, Media, System, Companion];
    public static DashboardPreset Resolve(string? id) => All.FirstOrDefault(p => p.Id == id) ?? ControlInfo;
    public static DashboardPreset For(FunctionalPreset preset) => preset switch
    {
        FunctionalPreset.Media => Media,
        FunctionalPreset.System => System,
        FunctionalPreset.Companion => Companion,
        _ => ControlInfo,
    };
    public static FunctionalPreset FunctionalFrom(string? id) => Resolve(id) switch
    {
        var value when value == Media => FunctionalPreset.Media,
        var value when value == System => FunctionalPreset.System,
        var value when value == Companion => FunctionalPreset.Companion,
        _ => FunctionalPreset.Control,
    };
    private static DashboardRegion Region(
        DashboardRegionKind region, DashboardWidgetKind widget, int priority) =>
        new(region, widget, priority);
    private static IReadOnlySet<DashboardWidgetKind> Set(params DashboardWidgetKind[] values) => values.ToHashSet();
}

public static class DashboardCompositionResolver
{
    public static IReadOnlyList<DashboardSlot> Compose(DashboardConfiguration configuration)
    {
        var preset = DashboardPresets.Resolve(configuration.PresetId);
        var functional = DashboardPresets.FunctionalFrom(preset.Id);
        var identity = VisualSystemCatalog.Normalize(configuration.VisualIdentity);
        var composition = (identity, functional) switch
        {
            (VisualIdentity.Neon, FunctionalPreset.Control) => NeonControl,
            (VisualIdentity.Neon, FunctionalPreset.Companion) => NeonCompanion,
            (VisualIdentity.Hikari, FunctionalPreset.Companion) => HikariCompanion,
            (_, FunctionalPreset.Media) => Media,
            (_, FunctionalPreset.System) => System,
            _ => HikariControl,
        };
        var widgets = preset.Regions.Select(region => region.Widget).ToHashSet();
        return composition.Where(slot => widgets.Contains(slot.Kind)).ToArray();
    }

    private static readonly DashboardSlot[] HikariControl =
    [
        Slot(DashboardWidgetKind.Profile, 18, 10, 304, 28),
        Slot(DashboardWidgetKind.Clock, 394, 10, 68, 28),
        Slot(DashboardWidgetKind.NowPlaying, 18, 52, 132, 92),
        Slot(DashboardWidgetKind.SystemStats, 18, 160, 132, 72),
        Slot(DashboardWidgetKind.ButtonMatrix, 166, 50, 300, 220),
        Slot(DashboardWidgetKind.Navigation, 166, 282, 300, 26),
    ];
    private static readonly DashboardSlot[] NeonControl =
    [
        Slot(DashboardWidgetKind.Profile, 14, 8, 300, 28),
        Slot(DashboardWidgetKind.Clock, 390, 8, 76, 28),
        Slot(DashboardWidgetKind.ButtonMatrix, 14, 48, 326, 220),
        Slot(DashboardWidgetKind.NowPlaying, 350, 48, 116, 96),
        Slot(DashboardWidgetKind.SystemStats, 350, 156, 116, 112),
        Slot(DashboardWidgetKind.Navigation, 14, 282, 452, 26),
    ];
    private static readonly DashboardSlot[] HikariCompanion =
    [
        Slot(DashboardWidgetKind.Profile, 18, 10, 304, 28),
        Slot(DashboardWidgetKind.Clock, 394, 10, 68, 28),
        Slot(DashboardWidgetKind.Mascot, 18, 50, 280, 182),
        Slot(DashboardWidgetKind.ButtonMatrix, 318, 50, 148, 142),
        Slot(DashboardWidgetKind.Navigation, 318, 204, 148, 26),
        Slot(DashboardWidgetKind.NowPlaying, 18, 246, 210, 60),
        Slot(DashboardWidgetKind.SystemStats, 244, 246, 222, 60),
    ];
    private static readonly DashboardSlot[] NeonCompanion =
    [
        Slot(DashboardWidgetKind.Profile, 14, 8, 300, 28),
        Slot(DashboardWidgetKind.Clock, 390, 8, 76, 28),
        Slot(DashboardWidgetKind.ButtonMatrix, 14, 48, 132, 142),
        Slot(DashboardWidgetKind.Navigation, 14, 202, 132, 30),
        Slot(DashboardWidgetKind.Mascot, 160, 46, 306, 178),
        Slot(DashboardWidgetKind.NowPlaying, 14, 244, 272, 62),
        Slot(DashboardWidgetKind.SystemStats, 300, 244, 166, 62),
    ];
    private static readonly DashboardSlot[] Media =
    [
        Slot(DashboardWidgetKind.NowPlaying, 0, 0, 338, 166),
        Slot(DashboardWidgetKind.ButtonMatrix, 0, 172, 338, 104),
        Slot(DashboardWidgetKind.Navigation, 0, 284, 338, 28),
        Slot(DashboardWidgetKind.Profile, 344, 0, 82, 34),
        Slot(DashboardWidgetKind.Clock, 428, 0, 52, 34),
        Slot(DashboardWidgetKind.Mascot, 344, 40, 136, 160),
        Slot(DashboardWidgetKind.SystemStats, 344, 206, 136, 106),
    ];
    private static readonly DashboardSlot[] System =
    [
        Slot(DashboardWidgetKind.SystemStats, 0, 0, 326, 170),
        Slot(DashboardWidgetKind.ButtonMatrix, 0, 176, 326, 106),
        Slot(DashboardWidgetKind.Navigation, 0, 290, 326, 26),
        Slot(DashboardWidgetKind.Profile, 332, 0, 94, 34),
        Slot(DashboardWidgetKind.Clock, 428, 0, 52, 34),
        Slot(DashboardWidgetKind.NowPlaying, 332, 40, 148, 90),
        Slot(DashboardWidgetKind.Mascot, 332, 136, 148, 184),
    ];
    private static DashboardSlot Slot(
        DashboardWidgetKind kind, double x, double y, double width, double height) =>
        new(kind, new DashboardRect(x, y, width, height));
}

public sealed record DashboardWidgetLayout(DashboardWidgetKind Kind, DashboardRect Bounds);
public static class DashboardLayoutEngine
{
    public static IReadOnlyList<DashboardWidgetLayout> Build(DashboardConfiguration configuration) =>
        DashboardCompositionResolver.Compose(configuration).Where(s => configuration.IsEnabled(s.Kind))
            .Select(s => new DashboardWidgetLayout(s.Kind, s.Bounds)).ToArray();
    public static DashboardWidgetKind? HitTest(DashboardConfiguration configuration, DashboardPoint point) =>
        Build(configuration).LastOrDefault(w => w.Bounds.Contains(point))?.Kind;
}

public sealed record DashboardInteraction(DashboardWidgetKind Widget, TouchGesture Gesture,
    DashboardPoint Point, TouchAction Action, ControlId? Control = null,
    DashboardMediaCommand? MediaCommand = null,
    SystemNavigation? SystemNavigation = null);
public static class DashboardInteractionResolver
{
    public static DashboardInteraction? Resolve(DashboardConfiguration configuration,
        DashboardPoint start, DashboardPoint end, TimeSpan duration)
    {
        var widget = DashboardLayoutEngine.HitTest(configuration, start);
        if (widget is null) return null;
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var gesture = Math.Abs(dx) >= 40 && Math.Abs(dx) > Math.Abs(dy)
            ? (dx < 0 ? TouchGesture.SwipeLeft : TouchGesture.SwipeRight)
            : duration >= TimeSpan.FromMilliseconds(600) ? TouchGesture.LongPress : TouchGesture.Tap;
        if (gesture is TouchGesture.SwipeLeft or TouchGesture.SwipeRight)
            return new(widget.Value, gesture, end, TouchAction.Navigate);
        var slot = DashboardLayoutEngine.Build(configuration).First(s => s.Kind == widget);
        if (widget == DashboardWidgetKind.ButtonMatrix)
        {
            return new(widget.Value, gesture, start, TouchAction.ExecuteBinding,
                new ControlId(ControlType.Button,
                    (byte)ButtonMatrixLayout.IndexAt(slot.Bounds, start)));
        }
        if (widget == DashboardWidgetKind.Navigation)
        {
            var part = Math.Clamp(
                (int)((start.X - slot.Bounds.X) / slot.Bounds.Width * 3), 0, 2);
            var navigation = part switch
            {
                0 => StreamDeckDIY.Core.Profiles.SystemNavigation.Previous,
                1 => StreamDeckDIY.Core.Profiles.SystemNavigation.Home,
                _ => StreamDeckDIY.Core.Profiles.SystemNavigation.Next,
            };
            return new(widget.Value, gesture, start, TouchAction.SystemNavigation,
                SystemNavigation: navigation);
        }
        if (widget == DashboardWidgetKind.Encoder)
        {
            var index = Math.Clamp((int)((start.X - slot.Bounds.X) / slot.Bounds.Width * 3), 0, 2);
            var type = index switch { 0 => ControlType.EncoderCounterClockwise, 1 => ControlType.EncoderPress, _ => ControlType.EncoderClockwise };
            return new(widget.Value, gesture, start, TouchAction.ExecuteBinding, new ControlId(type, 0));
        }
        if (widget == DashboardWidgetKind.NowPlaying)
        {
            var part = Math.Clamp((int)((start.X - slot.Bounds.X) / slot.Bounds.Width * 3), 0, 2);
            var command = part switch { 0 => DashboardMediaCommand.Previous, 1 => DashboardMediaCommand.PlayPause, _ => DashboardMediaCommand.Next };
            return new(widget.Value, gesture, start, TouchAction.MediaControl, MediaCommand: command);
        }
        return new(widget.Value, gesture, start, widget == DashboardWidgetKind.Mascot
            ? TouchAction.MascotInteracted : TouchAction.None);
    }
}

public sealed record DashboardControlState(ControlId Control, string Summary, string IconGlyph, DeviceAction Action);
public static class DashboardProfileResolver
{
    public static StreamDeckProfile? Resolve(IReadOnlyCollection<StreamDeckProfile> profiles, uint? activeProfileId) =>
        activeProfileId is uint id ? profiles.FirstOrDefault(profile => profile.Id == id) : null;
}
public sealed record MascotVisualFrame(double BodyScale, double VerticalOffset, bool EyesClosed,
    double LookOffset, string Accent, string Indicator);
public static class MascotVisualEngine
{
    public static MascotVisualFrame Frame(MascotState state, int phase, bool reducedMotion) => state switch
    {
        MascotState.Happy => new(1.05, reducedMotion ? 0 : -3, phase % 4 == 0, 0, "#72E6A6", "♥"),
        MascotState.Thinking => new(1, 0, false, phase % 2 == 0 ? -2 : 2, "#A88BFF", "?"),
        MascotState.Alert => new(1.03, 0, false, 0, "#FF6B78", "!"),
        MascotState.Music => new(1.02, reducedMotion ? 0 : (phase % 2 == 0 ? -2 : 2), false, 0, "#C36BFF", "♪"),
        MascotState.Sleep => new(1, 1, true, 0, "#70809A", "z"),
        _ => new(1 + (reducedMotion ? 0 : (phase % 4) * .005), reducedMotion ? 0 : phase % 2,
            phase % 12 == 0, 0, "#8E5CFF", string.Empty),
    };
}

public sealed record DashboardRuntimeState(DashboardConfiguration Configuration, string ProfileName,
    string ClockText, NowPlayingState NowPlaying, SystemStatsState SystemStats,
    MascotVisualFrame Mascot, IReadOnlyList<DashboardControlState> Buttons,
    IReadOnlyList<DashboardControlState> Encoder, CompanionState? Companion = null,
    string PageName = "Página 1", int PageIndex = 0, int PageCount = 1,
    bool IsHome = false, CompanionResolvedAsset? CompanionAsset = null);
public interface IDashboardRenderer { void Render(DashboardRuntimeState state); }
public static class DashboardBindingProjection
{
    public static (DashboardControlState[] Buttons, DashboardControlState[] Encoder) Build(
        IReadOnlyCollection<BindingInfo> bindings, Func<BindingInfo, string> summary, Func<BindingInfo, string> icon)
    {
        var states = bindings.Select(binding => new DashboardControlState(
            binding.Control, summary(binding), icon(binding), binding.Action));
        return (states.Where(s => s.Control.Type == ControlType.Button &&
                s.Control.Index < ProfileControls.UserButtonCount)
                .OrderBy(s => s.Control.Index).ToArray(),
            states.Where(s => s.Control.Type != ControlType.Button).OrderBy(s => s.Control.Type switch
            { ControlType.EncoderCounterClockwise => 0, ControlType.EncoderPress => 1,
              ControlType.EncoderClockwise => 2, _ => 3 }).ToArray());
    }
}

public sealed record NowPlayingState(bool IsAvailable, bool IsPlaying, string Title, string Artist,
    string Album, double? Progress, string Source, byte[]? Artwork = null,
    TimeSpan? Position = null, TimeSpan? Duration = null,
    DateTimeOffset? SampledAt = null, string? ArtworkKey = null);
public interface INowPlayingProvider : IAsyncDisposable
{
    event EventHandler<NowPlayingState>? StateChanged;
    NowPlayingState Current { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<bool> ControlAsync(DashboardMediaCommand command, CancellationToken cancellationToken = default);
}
public sealed class UnavailableNowPlayingProvider : INowPlayingProvider
{
    public event EventHandler<NowPlayingState>? StateChanged { add { } remove { } }
    public NowPlayingState Current { get; } = new(false, false, "Sin reproducción", "", "", null, "");
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> ControlAsync(DashboardMediaCommand command, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
public interface IClockProvider { DateTimeOffset Now { get; } }
public sealed class SystemClockProvider : IClockProvider { public DateTimeOffset Now => DateTimeOffset.Now; }
