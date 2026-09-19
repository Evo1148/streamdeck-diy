namespace StreamDeckDIY.Core.Display;

public readonly record struct DisplayPoint(double X, double Y);

public static class DisplayCoordinateMapper
{
    public static DisplayPoint? PreviewToLogical(
        double previewX,
        double previewY,
        double previewWidth,
        double previewHeight,
        DisplayConfiguration configuration)
    {
        if (previewWidth <= 0 || previewHeight <= 0 ||
            configuration.Width <= 0 || configuration.Height <= 0)
            return null;

        var scale = Math.Min(
            previewWidth / configuration.Width,
            previewHeight / configuration.Height);
        var renderedWidth = configuration.Width * scale;
        var renderedHeight = configuration.Height * scale;
        var offsetX = (previewWidth - renderedWidth) / 2;
        var offsetY = (previewHeight - renderedHeight) / 2;
        var logicalX = (previewX - offsetX) / scale;
        var logicalY = (previewY - offsetY) / scale;
        if (logicalX < 0 || logicalY < 0 ||
            logicalX > configuration.Width || logicalY > configuration.Height)
            return null;
        return new DisplayPoint(logicalX, logicalY);
    }
}
