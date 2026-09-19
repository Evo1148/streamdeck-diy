using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using StreamDeckDIY.Core.Display;
using Windows.UI;

namespace StreamDeckDIY.App.Display;

public sealed class WinUIDisplayPreviewRenderer(
    Canvas canvas,
    DisplayConfiguration configuration) : IDisplayPreviewRenderer
{
    public void Render(DisplayState state, DisplayEditorRenderState editorState)
    {
        canvas.Width = configuration.Width;
        canvas.Height = configuration.Height;
        var renderedBase = state.PageTransition?.ToScene ?? state.BaseScene;
        canvas.Background = Brush(DisplayColor.Normalize(renderedBase.Background, "#09111A"));
        canvas.Children.Clear();
        if (state.PageTransition is { } transition)
            RenderTransition(transition);
        else
            RenderScene(state.BaseScene, null, 0);
        if (state.OverlayScene is not null)
            RenderScene(state.OverlayScene, state.Effect, state.AnimationProgress);
        if (editorState.IsEditing)
        {
            if (editorState.ShowGrid) RenderGrid(editorState.GridSize);
            RenderSelection(state.BaseScene, editorState.SelectedId);
        }
    }

    private void RenderScene(
        DisplayScene scene,
        VisualEffectDefinition? effect,
        double progress,
        double offsetX = 0,
        double globalOpacity = 1)
    {
        foreach (var element in scene.OrderedVisibleElements)
        {
            var control = CreateControl(element);
            control.Width = element.Width;
            control.Height = element.Height;
            Canvas.SetLeft(control, element.X + offsetX);
            Canvas.SetTop(control, element.Y + SlideOffset(element, progress));
            Canvas.SetZIndex(control, element.ZIndex);
            control.Opacity = CalculateOpacity(element, progress) * globalOpacity;
            canvas.Children.Add(control);
        }
    }

    private void RenderTransition(DisplayPageTransitionState transition)
    {
        var progress = Math.Clamp(transition.Progress, 0, 1);
        switch (transition.Kind)
        {
        case DisplayPageTransition.Fade:
            RenderScene(transition.FromScene, null, 0, globalOpacity: 1 - progress);
            RenderScene(transition.ToScene, null, 0, globalOpacity: progress);
            break;
        case DisplayPageTransition.SlideLeft:
            RenderScene(transition.FromScene, null, 0,
                offsetX: -configuration.Width * progress);
            RenderScene(transition.ToScene, null, 0,
                offsetX: configuration.Width * (1 - progress));
            break;
        case DisplayPageTransition.SlideRight:
            RenderScene(transition.FromScene, null, 0,
                offsetX: configuration.Width * progress);
            RenderScene(transition.ToScene, null, 0,
                offsetX: -configuration.Width * (1 - progress));
            break;
        default:
            RenderScene(transition.ToScene, null, 0);
            break;
        }
    }

    private static FrameworkElement CreateControl(DisplayElement element) => element switch
    {
        DisplayRectangleElement rectangle => new Rectangle
        {
            Fill = Brush(rectangle.Fill),
            RadiusX = rectangle.CornerRadius,
            RadiusY = rectangle.CornerRadius,
        },
        DisplayTextElement text => CreateTextControl(text),
        DisplayImageElement image => new Border
        {
            BorderBrush = Brush("#506070"),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = image.AssetId,
                FontSize = 10,
                Foreground = Brush("#90A0B0"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        },
        _ => throw new NotSupportedException(
            $"Elemento visual no compatible: {element.GetType().Name}"),
    };

    private static TextBlock CreateTextControl(DisplayTextElement text)
    {
        var control = new TextBlock
        {
            Text = text.Text,
            FontSize = text.FontSize,
            Foreground = Brush(text.Color),
            TextAlignment = text.Alignment switch
            {
                DisplayTextAlignment.Center => TextAlignment.Center,
                DisplayTextAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            },
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        var fontFamily = DisplayFontFamily.Normalize(text.FontFamily);
        if (fontFamily is not null)
            control.FontFamily = new FontFamily(fontFamily);
        return control;
    }

    private static double CalculateOpacity(DisplayElement element, double progress)
    {
        var opacity = element.Opacity;
        foreach (var animation in element.Animations ?? [])
        {
            switch (animation.Kind)
            {
            case DisplayAnimationKind.FadeIn:
                opacity *= Math.Clamp(progress / 0.2, 0, 1);
                break;
            case DisplayAnimationKind.FadeOut:
                opacity *= progress < 0.78
                    ? 1 : Math.Clamp((1 - progress) / 0.22, 0, 1);
                break;
            case DisplayAnimationKind.Pulse:
                opacity *= 1 - animation.Magnitude *
                    (0.5 + 0.5 * Math.Sin(
                        progress * animation.Cycles * Math.PI * 2));
                break;
            }
        }
        return Math.Clamp(opacity, 0, 1);
    }

    private static double SlideOffset(DisplayElement element, double progress) =>
        (element.Animations ?? [])
            .Where(animation => animation.Kind == DisplayAnimationKind.Slide)
            .Sum(animation => animation.Magnitude * progress);

    private static SolidColorBrush Brush(string value)
    {
        var hex = value.TrimStart('#');
        var alpha = (byte)255;
        var offset = 0;
        if (hex.Length == 8)
        {
            alpha = Convert.ToByte(hex[..2], 16);
            offset = 2;
        }
        if (hex.Length is not (6 or 8))
            throw new FormatException($"Color visual no válido: {value}");
        return new SolidColorBrush(Color.FromArgb(
            alpha,
            Convert.ToByte(hex.Substring(offset, 2), 16),
            Convert.ToByte(hex.Substring(offset + 2, 2), 16),
            Convert.ToByte(hex.Substring(offset + 4, 2), 16)));
    }

    private void RenderGrid(double size)
    {
        var brush = Brush("#1E8A8A8A");
        for (var x = size; x < configuration.Width; x += size)
        {
            var line = new Line
            {
                X1 = x, X2 = x, Y1 = 0, Y2 = configuration.Height,
                Stroke = brush, StrokeThickness = 0.35,
                IsHitTestVisible = false,
            };
            Canvas.SetZIndex(line, 10000);
            canvas.Children.Add(line);
        }
        for (var y = size; y < configuration.Height; y += size)
        {
            var line = new Line
            {
                X1 = 0, X2 = configuration.Width, Y1 = y, Y2 = y,
                Stroke = brush, StrokeThickness = 0.35,
                IsHitTestVisible = false,
            };
            Canvas.SetZIndex(line, 10000);
            canvas.Children.Add(line);
        }
    }

    private void RenderSelection(DisplayScene scene, string? selectedId)
    {
        var selected = scene.Elements.FirstOrDefault(e => e.Id == selectedId);
        if (selected is null) return;
        var border = new Border
        {
            Width = selected.Width,
            Height = selected.Height,
            BorderBrush = Brush("#FF7C3AED"),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(border, selected.X);
        Canvas.SetTop(border, selected.Y);
        Canvas.SetZIndex(border, 10001);
        canvas.Children.Add(border);

        var handle = new Rectangle
        {
            Width = 7,
            Height = 7,
            Fill = Brush("#FF7C3AED"),
            Stroke = Brush("#FFFFFFFF"),
            StrokeThickness = 0.8,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(handle, selected.X + selected.Width - 3.5);
        Canvas.SetTop(handle, selected.Y + selected.Height - 3.5);
        Canvas.SetZIndex(handle, 10002);
        canvas.Children.Add(handle);
    }
}
