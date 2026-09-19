using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System.Runtime.InteropServices.WindowsRuntime;
using StreamDeckDIY.Core.Dashboard;
using StreamDeckDIY.Core.DisplayLink;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;

namespace StreamDeckDIY.App.Dashboard;

public sealed class DashboardPreviewRenderer(Canvas canvas) : IDashboardRenderer
{
    private readonly DisplayGraphPreviewRenderer graphRenderer = new(canvas);

    public void Render(DashboardRuntimeState state)
    {
        var images = new Dictionary<ushort, byte[]>();
        if (state.NowPlaying is { Artwork: { Length: > 0 } artwork, ArtworkKey: not null })
            images[ArtworkContentKey.AssetId(state.NowPlaying.ArtworkKey)] = artwork;
        if (state.CompanionAsset is { PngBytes.Length: > 0 } companion)
            images[companion.AssetId] = companion.PngBytes;
        graphRenderer.Render(DashboardDisplayGraphCompiler.Compile(state, 0), images);
        if (state.Configuration.Options(DashboardWidgetKind.ButtonMatrix).ShowTouchTargets)
            RenderTouchTargets(state.Configuration);
    }

    private void RenderTouchTargets(DashboardConfiguration configuration)
    {
        foreach (var slot in DashboardLayoutEngine.Build(configuration))
        {
            var outline = new Border
            {
                Width = slot.Bounds.Width,
                Height = slot.Bounds.Height,
                BorderBrush = Brush("#80C36BFF"),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(outline, slot.Bounds.X);
            Canvas.SetTop(outline, slot.Bounds.Y);
            Canvas.SetZIndex(outline, 32000);
            canvas.Children.Add(outline);
        }
    }

    private static SolidColorBrush Brush(string value) =>
        DisplayGraphPreviewRenderer.Brush(value);
}

public sealed class DisplayGraphPreviewRenderer(Canvas canvas)
{
    private readonly Dictionary<ushort, PreviewImageEntry> imageCache = [];
    private BitmapImage? lastArtworkImage;
    private BitmapImage? lastCompanionImage;

    public void Render(DisplayGraph graph, IReadOnlyDictionary<ushort, byte[]>? images = null)
    {
        canvas.Width = DisplayGraphVisualContract.Width;
        canvas.Height = DisplayGraphVisualContract.Height;
        canvas.Background = Brush("#000000");
        canvas.Children.Clear();
        var byId = graph.Nodes.ToDictionary(node => node.Id);
        foreach (var entry in graph.Nodes.Select((node, index) =>
                     (Node: node, Index: index, Resolved: Resolve(node, byId)))
                 .Where(entry => entry.Node.Kind != DisplayNodeKind.Group &&
                                 entry.Resolved is { Visible: true })
                 .OrderBy(entry => entry.Resolved!.Value.ZIndex)
                 .ThenBy(entry => entry.Index))
        {
            var resolved = entry.Resolved!.Value;
            if (resolved.Bounds.Width <= 0 || resolved.Bounds.Height <= 0 ||
                resolved.Clip.Width <= 0 || resolved.Clip.Height <= 0) continue;
            var control = CreateControl(entry.Node, resolved, images);
            control.Width = resolved.Bounds.Width;
            control.Height = resolved.Bounds.Height;
            control.Opacity = resolved.Opacity / 255d;
            control.Clip = new RectangleGeometry
            {
                Rect = new Rect(
                    resolved.Clip.X - resolved.Bounds.X,
                    resolved.Clip.Y - resolved.Bounds.Y,
                    resolved.Clip.Width,
                    resolved.Clip.Height),
            };
            if (entry.Node.Rotation != 0)
            {
                control.RenderTransformOrigin = new Point(.5, .5);
                control.RenderTransform = new RotateTransform { Angle = entry.Node.Rotation };
            }
            Canvas.SetLeft(control, resolved.Bounds.X);
            Canvas.SetTop(control, resolved.Bounds.Y);
            Canvas.SetZIndex(control, resolved.ZIndex);
            canvas.Children.Add(control);
        }
    }

    private FrameworkElement CreateControl(
        DisplayNode node, ResolvedNode resolved,
        IReadOnlyDictionary<ushort, byte[]>? images) =>
        node.Kind switch
        {
            DisplayNodeKind.Rectangle => new Border
            {
                Background = ColorBrush(node.FillColor),
                BorderBrush = node.StrokeWidth == 0 ? null : ColorBrush(node.StrokeColor),
                BorderThickness = new Thickness(node.StrokeWidth),
                CornerRadius = new CornerRadius(node.CornerRadius),
            },
            DisplayNodeKind.Circle => new Ellipse
            {
                Fill = ColorBrush(node.FillColor),
                Stroke = node.StrokeWidth == 0 ? null : ColorBrush(node.StrokeColor),
                StrokeThickness = node.StrokeWidth,
            },
            DisplayNodeKind.Text => Text(node),
            DisplayNodeKind.Icon => Icon(node),
            DisplayNodeKind.Image => Image(node,
                images is not null && images.TryGetValue(node.AssetId, out var bytes) ? bytes : null),
            DisplayNodeKind.Progress => Progress(node, resolved.Bounds.Width, resolved.Bounds.Height),
            DisplayNodeKind.Line => new Line
            {
                X1 = 0,
                Y1 = 0,
                X2 = Math.Max(0, resolved.Bounds.Width - 1),
                Y2 = Math.Max(0, resolved.Bounds.Height - 1),
                Stroke = ColorBrush(node.StrokeColor == 0 ? node.FillColor : node.StrokeColor),
                StrokeThickness = Math.Max(1, (int)node.StrokeWidth),
            },
            _ => new Grid(),
        };

    private static TextBlock Text(DisplayNode node) => new()
    {
        Text = DisplayGraphVisualContract.FitSingleLine(
            node.Text, node.Bounds.Width, node.FontSize == 0 ? (byte)8 : node.FontSize),
        FontFamily = new FontFamily("Consolas"),
        FontSize = DisplayGraphVisualContract.FontPixelHeight(
            node.FontSize == 0 ? (byte)8 : node.FontSize),
        FontWeight = node.FontId >= 2 ? FontWeights.Bold : FontWeights.Normal,
        Foreground = ColorBrush(node.FillColor == 0 ? (ushort)0xFFFF : node.FillColor),
        TextAlignment = node.Alignment switch
        {
            1 => TextAlignment.Center,
            2 => TextAlignment.Right,
            _ => TextAlignment.Left,
        },
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
    };

    private static FrameworkElement Icon(DisplayNode node) => new FontIcon
    {
        FontFamily = new FontFamily("Segoe Fluent Icons"),
        Glyph = IconGlyph(node.IconId),
        FontSize = Math.Max(8, Math.Min(node.Bounds.Width, node.Bounds.Height) * .72),
        Foreground = ColorBrush(node.FillColor == 0 ? (ushort)0xFFFF : node.FillColor),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private FrameworkElement Image(DisplayNode node, byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return ImageFallback(node);
        var key = ArtworkContentKey.Create(bytes);
        if (imageCache.TryGetValue(node.AssetId, out var cached) &&
            string.Equals(cached.ContentKey, key, StringComparison.Ordinal))
            return ImageLayer(node, cached);

        var previous = cached?.Bitmap ?? (DisplayAssetIds.IsArtwork(node.AssetId)
            ? lastArtworkImage
            : DisplayAssetIds.IsCompanion(node.AssetId) ? lastCompanionImage : null);
        var entry = new PreviewImageEntry(key, previous) { Loading = true };
        imageCache[node.AssetId] = entry;
        var layer = ImageLayer(node, entry);
        _ = SetImageAsync(node.AssetId, bytes, entry);
        return layer;
    }

    private static FrameworkElement ImageLayer(DisplayNode node, PreviewImageEntry entry)
    {
        var grid = new Grid();
        var fallback = ImageFallback(node);
        var image = new Image { Stretch = Stretch.Fill, Source = entry.Bitmap };
        if (entry.Bitmap is not null) fallback.Visibility = Visibility.Collapsed;
        grid.Children.Add(fallback);
        grid.Children.Add(image);
        if (entry.Loading) entry.Targets.Add(new(image, fallback));
        return grid;
    }

    private static FrameworkElement ImageFallback(DisplayNode node)
    {
        var grid = new Grid { Background = ColorBrush(0x2104) };
        grid.Children.Add(new Line
        {
            X1 = 0, Y1 = 0, X2 = node.Bounds.Width, Y2 = node.Bounds.Height,
            Stroke = ColorBrush(0x7BEF), StrokeThickness = 1,
        });
        grid.Children.Add(new Line
        {
            X1 = node.Bounds.Width, Y1 = 0, X2 = 0, Y2 = node.Bounds.Height,
            Stroke = ColorBrush(0x7BEF), StrokeThickness = 1,
        });
        return grid;
    }

    private static FrameworkElement Progress(DisplayNode node, short width, short height)
    {
        var track = new Border
        {
            Background = ColorBrush(node.StrokeColor == 0 ? Darken(node.FillColor) : node.StrokeColor),
            CornerRadius = new CornerRadius(node.CornerRadius),
        };
        if (!node.ProgressAvailable) return track;
        var fill = new Border
        {
            Width = width * Math.Min(1000, (int)node.ProgressValue) / 1000d,
            Height = height,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = ColorBrush(node.FillColor == 0 ? (ushort)0xFFFF : node.FillColor),
            CornerRadius = new CornerRadius(node.CornerRadius),
        };
        return new Grid { Children = { track, fill } };
    }

    private async Task SetImageAsync(
        ushort assetId, byte[] bytes, PreviewImageEntry requested)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            if (!imageCache.TryGetValue(assetId, out var current) ||
                !ReferenceEquals(current, requested)) return;

            current.Bitmap = bitmap;
            current.Loading = false;
            foreach (var target in current.Targets)
            {
                target.Image.Source = bitmap;
                target.Fallback.Visibility = Visibility.Collapsed;
            }
            current.Targets.Clear();
            if (DisplayAssetIds.IsArtwork(assetId)) lastArtworkImage = bitmap;
            if (DisplayAssetIds.IsCompanion(assetId)) lastCompanionImage = bitmap;
            foreach (var obsolete in imageCache.Keys.Where(id => id != assetId &&
                         (DisplayAssetIds.IsArtwork(id) == DisplayAssetIds.IsArtwork(assetId)) &&
                         (DisplayAssetIds.IsCompanion(id) == DisplayAssetIds.IsCompanion(assetId)))
                     .ToArray())
                imageCache.Remove(obsolete);
            System.Diagnostics.Debug.WriteLine(
                $"ASSET READY preview id=0x{assetId:X4} key={current.ContentKey}");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
        {
            if (imageCache.TryGetValue(assetId, out var current) &&
                ReferenceEquals(current, requested))
            {
                current.Loading = false;
                current.Targets.Clear();
            }
        }
    }

    private static ResolvedNode? Resolve(
        DisplayNode node, IReadOnlyDictionary<ushort, DisplayNode> nodes)
    {
        var chain = new List<DisplayNode>();
        var current = node;
        for (var depth = 0; depth < 9; depth++)
        {
            chain.Add(current);
            if (current.ParentId == 0) break;
            if (!nodes.TryGetValue(current.ParentId, out current)) return null;
        }
        if (chain[^1].ParentId != 0) return null;
        chain.Reverse();
        short x = 0, y = 0;
        var z = 0;
        var opacity = 255;
        var visible = true;
        var clip = new DisplayBounds(0, 0,
            DisplayGraphVisualContract.Width, DisplayGraphVisualContract.Height);
        foreach (var part in chain)
        {
            x = (short)(x + part.Bounds.X);
            y = (short)(y + part.Bounds.Y);
            var absolute = new DisplayBounds(x, y, part.Bounds.Width, part.Bounds.Height);
            if (part.Kind == DisplayNodeKind.Group) clip = Intersection(clip, absolute);
            z += part.ZIndex;
            opacity = (opacity * part.Opacity + 127) / 255;
            visible &= part.Visible;
        }
        var bounds = new DisplayBounds(x, y, node.Bounds.Width, node.Bounds.Height);
        if (node.Kind != DisplayNodeKind.Group) clip = Intersection(clip, bounds);
        return new(bounds, clip, z, (byte)opacity, visible);
    }

    private static DisplayBounds Intersection(DisplayBounds left, DisplayBounds right)
    {
        var x0 = Math.Max(left.X, right.X);
        var y0 = Math.Max(left.Y, right.Y);
        var x1 = Math.Min(left.X + left.Width, right.X + right.Width);
        var y1 = Math.Min(left.Y + left.Height, right.Y + right.Height);
        return x1 <= x0 || y1 <= y0
            ? new DisplayBounds(0, 0, 0, 0)
            : new DisplayBounds((short)x0, (short)y0, (short)(x1 - x0), (short)(y1 - y0));
    }

    private static string IconGlyph(ushort iconId) => iconId switch
    {
        DashboardIconCatalog.Volume => "\uE767",
        DashboardIconCatalog.Web => "\uE774",
        DashboardIconCatalog.Settings => "\uE713",
        DashboardIconCatalog.Folder => "\uE8B7",
        DashboardIconCatalog.Keyboard => "\uE765",
        DashboardIconCatalog.Play => "\uE768",
        DashboardIconCatalog.Application => "\uE8A5",
        DashboardIconCatalog.Sequence => "\uE8FD",
        DashboardIconCatalog.Microphone => "\uE720",
        DashboardIconCatalog.Headphones => "\uE7F6",
        DashboardIconCatalog.Music => "\uE8D6",
        DashboardIconCatalog.Link => "\uE71B",
        DashboardIconCatalog.Power => "\uE7E8",
        DashboardIconCatalog.Tools => "\uE90F",
        DashboardIconCatalog.Left => "\uE72B",
        DashboardIconCatalog.Right => "\uE72A",
        DashboardIconCatalog.Up => "\uE74A",
        DashboardIconCatalog.Down => "\uE74B",
        DashboardIconCatalog.Message => "\uE8BD",
        DashboardIconCatalog.Person => "\uE77B",
        DashboardIconCatalog.Alert => "\uE7BA",
        DashboardIconCatalog.Success => "\uE73E",
        DashboardIconCatalog.Press => "\uE73E",
        _ => "\uE80A",
    };

    private static ushort Darken(ushort value) => (ushort)((value >> 1) & 0x7BEF);
    private static SolidColorBrush ColorBrush(ushort value) => Brush(Rgb565.ToHex(value));
    internal static SolidColorBrush Brush(string value)
    {
        var text = value.TrimStart('#');
        var argb = text.Length == 8 ? Convert.ToUInt32(text, 16) :
            0xFF000000u | Convert.ToUInt32(text, 16);
        return new SolidColorBrush(Color.FromArgb(
            (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
    }

    private sealed class PreviewImageEntry(string contentKey, BitmapImage? bitmap)
    {
        public string ContentKey { get; } = contentKey;
        public BitmapImage? Bitmap { get; set; } = bitmap;
        public bool Loading { get; set; }
        public List<PreviewImageTarget> Targets { get; } = [];
    }

    private readonly record struct PreviewImageTarget(Image Image, FrameworkElement Fallback);
    private readonly record struct ResolvedNode(
        DisplayBounds Bounds, DisplayBounds Clip, int ZIndex, byte Opacity, bool Visible);
}
