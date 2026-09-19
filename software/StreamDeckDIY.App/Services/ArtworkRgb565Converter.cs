using System.Runtime.InteropServices;
using WinRT;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace StreamDeckDIY.App.Services;

internal static class ArtworkRgb565Converter
{
    public const ushort Width = 64;
    public const ushort Height = 64;

    public static Task<byte[]?> ConvertAsync(
        byte[] encodedImage, CancellationToken cancellationToken) =>
        ConvertAsync(encodedImage, Width, Height, 0, cancellationToken);

    public static async Task<byte[]?> ConvertAsync(
        byte[] encodedImage, ushort width, ushort height, ushort background,
        CancellationToken cancellationToken)
    {
        if (encodedImage.Length == 0) return null;
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(encodedImage);
            await writer.StoreAsync().AsTask(cancellationToken);
            await writer.FlushAsync().AsTask(cancellationToken);
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        var transform = new BitmapTransform
        {
            ScaledWidth = width,
            ScaledHeight = height,
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb).AsTask(cancellationToken);
        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        reference.As<IMemoryBufferByteAccess>()
            .GetBuffer(out var address, out var capacity);
        var description = buffer.GetPlaneDescription(0);
        var required = description.StartIndex +
            (height - 1) * description.Stride + width * 4;
        if (required > capacity) throw new InvalidDataException("Decoded artwork buffer is incomplete.");
        if (capacity > int.MaxValue)
            throw new InvalidDataException("Decoded artwork buffer is too large.");
        var pixels = new byte[(int)capacity];
        Marshal.Copy(address, pixels, 0, pixels.Length);
        return ConvertBgra8ToRgb565(
            pixels, description.StartIndex, description.Stride, width, height,
            cancellationToken, background);
    }

    internal static byte[] ConvertBgra8ToRgb565(
        ReadOnlySpan<byte> pixels, int startIndex, int stride,
        int width, int height, CancellationToken cancellationToken = default,
        ushort background = 0)
    {
        if (width <= 0 || height <= 0 || startIndex < 0 || stride < width * 4)
            throw new ArgumentOutOfRangeException(nameof(stride));
        var required = checked(startIndex + (height - 1) * stride + width * 4);
        if (required > pixels.Length)
            throw new InvalidDataException("Decoded artwork buffer is incomplete.");
        var result = new byte[checked(width * height * 2)];
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = startIndex + y * stride;
            for (var x = 0; x < width; x++)
            {
                var source = row + x * 4;
                var alpha = pixels[source + 3];
                var backgroundRed = (byte)((((background >> 11) & 0x1F) * 255 + 15) / 31);
                var backgroundGreen = (byte)((((background >> 5) & 0x3F) * 255 + 31) / 63);
                var backgroundBlue = (byte)(((background & 0x1F) * 255 + 15) / 31);
                var blue = (byte)(pixels[source] + (backgroundBlue * (255 - alpha) + 127) / 255);
                var green = (byte)(pixels[source + 1] + (backgroundGreen * (255 - alpha) + 127) / 255);
                var red = (byte)(pixels[source + 2] + (backgroundRed * (255 - alpha) + 127) / 255);
                var value = (ushort)(((red & 0xF8) << 8) |
                    ((green & 0xFC) << 3) | (blue >> 3));
                var target = (y * width + x) * 2;
                result[target] = (byte)value;
                result[target + 1] = (byte)(value >> 8);
            }
        }
        return result;
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        void GetBuffer(out IntPtr buffer, out uint capacity);
    }
}
