namespace StreamDeckDIY.Core.Dashboard;

public enum VisualIdentity { Hikari, Neon, Lumen, Studio }
public enum FunctionalPreset { Control, Media, System, Companion }
public enum VisualAccentVariant { Default, Warm, Cool }
public enum VisualBackgroundVariant { Default, Soft, HighContrast }
public enum CompanionMood { Neutral, Happy, Busy, Sleeping, Alert }
public enum MascotVariant { Hikari, Neon }

public sealed record DashboardVisualOptions(
    VisualAccentVariant AccentVariant = VisualAccentVariant.Default,
    bool ShowClock = true,
    bool ShowSystemStats = true,
    bool ShowArtwork = true,
    bool ShowMascot = true,
    VisualBackgroundVariant BackgroundVariant = VisualBackgroundVariant.Default)
{
    public static DashboardVisualOptions Default { get; } = new();
}

public sealed record CompanionState(
    CompanionMood Mood,
    string Message,
    string? Status = null,
    MascotVariant MascotVariant = MascotVariant.Neon)
{
    public static CompanionState Default { get; } = new(
        CompanionMood.Neutral, CompanionMicrocopy.For(CompanionMood.Neutral));
}

public static class VisualSystemCatalog
{
    public static IReadOnlyList<VisualIdentity> SelectableIdentities { get; } =
        [VisualIdentity.Hikari, VisualIdentity.Neon];

    public static IReadOnlyList<FunctionalPreset> SelectablePresets { get; } =
        [FunctionalPreset.Control, FunctionalPreset.Companion];

    public static VisualIdentity Normalize(VisualIdentity identity) =>
        Enum.IsDefined(identity) ? identity : VisualIdentity.Hikari;

    public static FunctionalPreset Normalize(FunctionalPreset preset) =>
        Enum.IsDefined(preset) ? preset : FunctionalPreset.Control;
}

public sealed record DashboardVisualTokens(
    ushort Background,
    ushort Surface,
    ushort Card,
    ushort CardInactive,
    ushort TextPrimary,
    ushort TextSecondary,
    ushort AccentPrimary,
    ushort AccentSecondary,
    ushort Success,
    ushort Warning,
    ushort Error,
    ushort Border,
    ushort Track,
    byte CardRadius,
    short Gap,
    short Padding,
    byte TitleSize,
    byte BodySize,
    bool UsesDecorativeGrid,
    bool UsesActionCards,
    bool UsesWidgetFrames);

public static class DashboardVisualTokenResolver
{
    public static ushort CompanionBackground(
        VisualIdentity identity,DashboardVisualOptions? options=null)=>
        Resolve(identity,options).Background;

    public static DashboardVisualTokens Resolve(
        VisualIdentity identity, DashboardVisualOptions? options = null)
    {
        options ??= DashboardVisualOptions.Default;
        return VisualSystemCatalog.Normalize(identity) switch
        {
            VisualIdentity.Neon => Neon(options),
            VisualIdentity.Lumen => Lumen(options),
            VisualIdentity.Studio => Studio(options),
            _ => Hikari(options),
        };
    }

    private static DashboardVisualTokens Hikari(DashboardVisualOptions options)
    {
        var accent = options.AccentVariant switch
        {
            VisualAccentVariant.Cool => Color("#4D8290"),
            VisualAccentVariant.Warm => Color("#C47E35"),
            _ => Color("#B98531"),
        };
        var background = options.BackgroundVariant == VisualBackgroundVariant.HighContrast
            ? Color("#E9E1D5") : Color("#F4EFE6");
        return new(background, Color("#FBF8F1"), Color("#FFFDF8"), Color("#EEE7DB"),
            Color("#25241F"), Color("#777269"), accent, Color("#5E8D76"),
            Color("#4D9470"), Color("#C28A32"), Color("#C65C54"), Color("#DED6CA"),
            Color("#E5DED3"), 10, 8, 10, 11, 7, false, false, false);
    }

    private static DashboardVisualTokens Neon(DashboardVisualOptions options)
    {
        var accent = options.AccentVariant switch
        {
            VisualAccentVariant.Warm => Color("#FF65C8"),
            VisualAccentVariant.Cool => Color("#59EEFF"),
            _ => Color("#6DEBFF"),
        };
        var background = options.BackgroundVariant == VisualBackgroundVariant.Soft
            ? Color("#0D1020") : Color("#070914");
        return new(background, Color("#0E1324"), Color("#171D35"), Color("#0A0E1B"),
            Color("#F4F6FF"), Color("#96A3C8"), accent, Color("#EF61FF"),
            Color("#51E7A9"), Color("#FFBE63"), Color("#FF5D7D"), Color("#374167"),
            Color("#242B49"), 5, 5, 7, 10, 7, true, true, true);
    }

    private static DashboardVisualTokens Lumen(DashboardVisualOptions options) =>
        Neon(options) with
        {
            Background = Color("#15100C"), Surface = Color("#211914"),
            Card = Color("#2A211A"), AccentPrimary = Color("#F0B55F"),
            AccentSecondary = Color("#D88942"), Border = Color("#594432"),
            UsesDecorativeGrid = false,
        };

    private static DashboardVisualTokens Studio(DashboardVisualOptions options) =>
        Hikari(options) with
        {
            Background = Color("#111317"), Surface = Color("#191C22"),
            Card = Color("#22262E"), CardInactive = Color("#171A20"),
            TextPrimary = Color("#F1F2F4"), TextSecondary = Color("#A3A8B2"),
            AccentPrimary = Color("#6C9EFF"), Border = Color("#383E48"),
            Track = Color("#2A3039"), UsesDecorativeGrid = false,
        };

    private static ushort Color(string value) => DisplayLink.Rgb565.FromHex(value);
}
