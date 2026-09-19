using System.Text.Json.Serialization;

namespace StreamDeckDIY.Core.Display;

public enum DisplayOrientation { Landscape, Portrait }
public sealed record DisplayConfiguration(double Width, double Height, DisplayOrientation Orientation)
{
    public static DisplayConfiguration DevelopmentDefault { get; } = new(480, 320, DisplayOrientation.Landscape);
}
public enum DisplayTextAlignment { Left, Center, Right }
public enum DisplayImageStretch { Uniform, Fill, None }
public enum DisplayAnimationKind { FadeIn, FadeOut, Slide, Pulse }
public sealed record DisplayAnimation(DisplayAnimationKind Kind, double Magnitude = 1, double Cycles = 1);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(DisplayTextElement), "text")]
[JsonDerivedType(typeof(DisplayRectangleElement), "rectangle")]
[JsonDerivedType(typeof(DisplayImageElement), "image")]
public abstract record DisplayElement(
    string Id, double X, double Y, double Width, double Height,
    int ZIndex = 0, bool Visible = true,
    IReadOnlyList<DisplayAnimation>? Animations = null, double Opacity = 1);

public sealed record DisplayTextElement(
    string Id, double X, double Y, double Width, double Height,
    string Text, double FontSize,
    DisplayTextAlignment Alignment = DisplayTextAlignment.Left,
    string Color = "#FFFFFF", int ZIndex = 0, bool Visible = true,
    IReadOnlyList<DisplayAnimation>? Animations = null,
    string? FontFamily = null, double Opacity = 1)
    : DisplayElement(Id, X, Y, Width, Height, ZIndex, Visible, Animations, Opacity);

public static class DisplayFontFamily
{
    public static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ||
               string.Equals(normalized, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? null : normalized;
    }
}

public static class DisplayColor
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim().ToUpperInvariant();
        if (text[0] != '#') text = $"#{text}";
        if (text.Length is not (7 or 9) || text.Skip(1).Any(c => !Uri.IsHexDigit(c)))
            return false;
        normalized = text;
        return true;
    }

    public static string Normalize(string? value, string fallback = "#000000") =>
        TryNormalize(value, out var normalized) ? normalized : fallback;
}

public sealed record DisplayRectangleElement(
    string Id, double X, double Y, double Width, double Height, string Fill,
    double CornerRadius = 0, int ZIndex = 0, bool Visible = true,
    IReadOnlyList<DisplayAnimation>? Animations = null, double Opacity = 1)
    : DisplayElement(Id, X, Y, Width, Height, ZIndex, Visible, Animations, Opacity);

public sealed record DisplayImageElement(
    string Id, double X, double Y, double Width, double Height, string AssetId,
    int ZIndex = 0, bool Visible = true,
    IReadOnlyList<DisplayAnimation>? Animations = null,
    DisplayImageStretch Stretch = DisplayImageStretch.Uniform, double Opacity = 1)
    : DisplayElement(Id, X, Y, Width, Height, ZIndex, Visible, Animations, Opacity);

public sealed record DisplayScene(
    string Id, IReadOnlyList<DisplayElement> Elements, string Background = "#09111A")
{
    public IReadOnlyList<DisplayElement> OrderedVisibleElements => Elements
        .Select((element, index) => (element, index))
        .Where(item => item.element.Visible)
        .OrderBy(item => item.element.ZIndex).ThenBy(item => item.index)
        .Select(item => item.element).ToArray();
}

public enum VisualEffectKind { HackerMatrix }
public sealed record VisualEffectDefinition(VisualEffectKind Kind, TimeSpan Duration);
public sealed record DisplayPageTransitionState(
    DisplayPageTransition Kind,
    DisplayScene FromScene,
    DisplayScene ToScene,
    double Progress);
public sealed record DisplayState(
    DisplayScene BaseScene, DisplayScene? OverlayScene = null,
    VisualEffectDefinition? Effect = null, double AnimationProgress = 0,
    DisplayPageTransitionState? PageTransition = null);
