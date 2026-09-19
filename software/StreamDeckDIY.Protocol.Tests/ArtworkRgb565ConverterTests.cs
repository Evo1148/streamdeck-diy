using StreamDeckDIY.App.Services;

internal static class ArtworkRgb565ConverterTests
{
    public static async Task RunAsync()
    {
        TestStrideAndBgraOrdering();
        TestTransparencyComposite();
        var encoded = CreateSolidRedBmp();
        for (var iteration = 0; iteration < 5; iteration++)
        {
            var result = await ArtworkRgb565Converter.ConvertAsync(
                encoded, CancellationToken.None);
            Assert(result is not null, "Artwork conversion returns pixels");
            var converted = result!;
            Assert(converted.Length == ArtworkRgb565Converter.Width *
                ArtworkRgb565Converter.Height * 2,
                "Artwork conversion preserves dimensions");
            Assert(converted.Where((_, index) => index % 2 == 0)
                       .All(value => value == 0x00) &&
                   converted.Where((_, index) => index % 2 == 1)
                       .All(value => value == 0xF8),
                "Repeated decoded red artwork is RGB565 little-endian");
        }
    }

    private static void TestStrideAndBgraOrdering()
    {
        const int start = 3;
        const int stride = 12;
        var pixels = new byte[23];
        WriteBgra(pixels, start, 0, 0, 255);
        WriteBgra(pixels, start + 4, 0, 255, 0);
        WriteBgra(pixels, start + stride, 255, 0, 0);
        WriteBgra(pixels, start + stride + 4, 255, 255, 255);

        var result = ArtworkRgb565Converter.ConvertBgra8ToRgb565(
            pixels, start, stride, 2, 2);
        Assert(result.SequenceEqual(new byte[]
        {
            0x00, 0xF8, 0xE0, 0x07,
            0x1F, 0x00, 0xFF, 0xFF,
        }), "BGRA ordering, StartIndex, stride and RGB565 endianness");
    }

    private static void TestTransparencyComposite()
    {
        var transparent = new byte[] { 0, 0, 0, 0 };
        var result = ArtworkRgb565Converter.ConvertBgra8ToRgb565(
            transparent, 0, 4, 1, 1, background: 0xFFFF);
        Assert(result.SequenceEqual(new byte[] { 0xFF, 0xFF }),
            "Transparent companion pixels composite onto the theme background before RGB565");
    }

    private static void WriteBgra(
        byte[] pixels, int offset, byte blue, byte green, byte red)
    {
        pixels[offset] = blue;
        pixels[offset + 1] = green;
        pixels[offset + 2] = red;
        pixels[offset + 3] = 255;
    }

    private static byte[] CreateSolidRedBmp()
    {
        var bmp = new byte[58];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        Write32(bmp, 2, bmp.Length);
        Write32(bmp, 10, 54);
        Write32(bmp, 14, 40);
        Write32(bmp, 18, 1);
        Write32(bmp, 22, 1);
        bmp[26] = 1;
        bmp[28] = 24;
        Write32(bmp, 34, 4);
        bmp[56] = 255;
        return bmp;
    }

    private static void Write32(byte[] target, int offset, int value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
        target[offset + 2] = (byte)(value >> 16);
        target[offset + 3] = (byte)(value >> 24);
    }

    private static void Assert(bool value, string name)
    {
        if (!value) throw new InvalidOperationException($"Test failed: {name}");
    }
}
