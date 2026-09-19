using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using WinRT;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using StreamDeckDIY.Core.Dashboard;

namespace StreamDeckDIY.App.Services;

internal sealed class CompanionAssetProcessor
{
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly Dictionary<string,CompanionFrameProfile> profiles=[];
    private readonly Dictionary<string,CompanionResolvedAsset> prepared=[];

    public async Task<CompanionResolvedAsset> PrepareAsync(
        CompanionResolvedAsset source,VisualIdentity identity,ushort background,
        CancellationToken cancellationToken=default)
    {
        var key=CompanionRasterPolicy.ProcessingKey(
            source.ContentKey,identity,background);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if(prepared.TryGetValue(key,out var cached))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"COMPANION PROCESS cache-hit key={key}");
                return cached;
            }
            System.Diagnostics.Debug.WriteLine(
                $"COMPANION PROCESS cache-miss key={key}");
            var profileKey=PackProfileKey(source.Pack);
            if(!profiles.TryGetValue(profileKey,out var profile))
            {
                profile=await BuildProfileAsync(source.Pack,cancellationToken);
                profiles[profileKey]=profile;
            }

            var decoded=await DecodeAsync(source.PngBytes,cancellationToken);
            var bounds=CompanionRasterPolicy.FindAlphaBounds(
                decoded.Pixels,decoded.StartIndex,decoded.Stride,
                decoded.Width,decoded.Height);
            if(bounds.IsEmpty)throw new InvalidDataException(
                "El sprite Companion no contiene píxeles visibles.");
            var geometry=new CompanionSourceGeometry(decoded.Width,decoded.Height,bounds);
            var crop=CompanionRasterPolicy.CalculateCrop(profile,geometry);
            var placement=CompanionRasterPolicy.Fit(crop);
            var scaled=ResizeCrop(decoded,crop,placement.Width,placement.Height);
            var pixels=Compose(scaled,placement,background);
            var png=await EncodePngAsync(pixels,
                CompanionRasterPolicy.RenderSize,CompanionRasterPolicy.RenderSize,
                cancellationToken);
            var crc=ArtworkContentKey.Crc32(png);
            var result=source with
            {
                ContentKey=$"{key}:{crc:X8}",
                PngBytes=png,
                Width=CompanionPackFormat.RenderWidth,
                Height=CompanionPackFormat.RenderHeight,
                SourceCrc32=crc,
            };
            if(prepared.Count>=64)prepared.Clear();
            prepared[key]=result;
            return result;
        }
        finally{gate.Release();}
    }

    private static async Task<CompanionFrameProfile> BuildProfileAsync(
        CompanionPack pack,CancellationToken cancellationToken)
    {
        var geometries=new List<CompanionSourceGeometry>();
        foreach(var path in pack.AssetPaths.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var bytes=await File.ReadAllBytesAsync(path,cancellationToken);
            var decoded=await DecodeAsync(bytes,cancellationToken);
            var bounds=CompanionRasterPolicy.FindAlphaBounds(
                decoded.Pixels,decoded.StartIndex,decoded.Stride,
                decoded.Width,decoded.Height);
            geometries.Add(new(decoded.Width,decoded.Height,bounds));
        }
        return CompanionRasterPolicy.CreatePackProfile(geometries);
    }

    private static string PackProfileKey(CompanionPack pack)
    {
        var files=pack.AssetPaths.Values.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path=>path,StringComparer.OrdinalIgnoreCase)
            .Select(path=>
            {
                var info=new FileInfo(path);
                return $"{path}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
            });
        return $"{pack.Id}:{pack.Version}:{string.Join("|",files)}";
    }

    private static byte[] Compose(
        DecodedImage source,CompanionPlacement placement,ushort background)
    {
        var size=CompanionRasterPolicy.RenderSize;
        var result=new byte[size*size*4];
        var bg=CompanionRasterPolicy.Background(background);
        for(var index=0;index<result.Length;index+=4)
        {
            result[index]=bg.Blue;result[index+1]=bg.Green;
            result[index+2]=bg.Red;result[index+3]=255;
        }
        for(var y=0;y<placement.Height;y++)
        {
            var sourceRow=source.StartIndex+y*source.Stride;
            var targetRow=((placement.Y+y)*size+placement.X)*4;
            for(var x=0;x<placement.Width;x++)
            {
                var sourceIndex=sourceRow+x*4;
                var value=CompanionRasterPolicy.CompositePremultiplied(
                    source.Pixels[sourceIndex],source.Pixels[sourceIndex+1],
                    source.Pixels[sourceIndex+2],source.Pixels[sourceIndex+3],
                    background);
                var target=targetRow+x*4;
                result[target]=value.Blue;result[target+1]=value.Green;
                result[target+2]=value.Red;result[target+3]=255;
            }
        }
        return result;
    }

    private static DecodedImage ResizeCrop(
        DecodedImage source,CompanionCrop crop,int width,int height)
    {
        var output=new byte[checked(width*height*4)];
        for(var y=0;y<height;y++)
        {
            var sourceY=crop.Y+(y+.5)*crop.Height/height-.5;
            sourceY=Math.Clamp(sourceY,crop.Y,crop.Y+crop.Height-1);
            var y0=(int)Math.Floor(sourceY);
            var y1=Math.Min(crop.Y+crop.Height-1,y0+1);
            var fy=sourceY-y0;
            for(var x=0;x<width;x++)
            {
                var sourceX=crop.X+(x+.5)*crop.Width/width-.5;
                sourceX=Math.Clamp(sourceX,crop.X,crop.X+crop.Width-1);
                var x0=(int)Math.Floor(sourceX);
                var x1=Math.Min(crop.X+crop.Width-1,x0+1);
                var fx=sourceX-x0;
                var target=(y*width+x)*4;
                for(var channel=0;channel<4;channel++)
                {
                    var p00=source.Pixels[source.StartIndex+y0*source.Stride+x0*4+channel];
                    var p10=source.Pixels[source.StartIndex+y0*source.Stride+x1*4+channel];
                    var p01=source.Pixels[source.StartIndex+y1*source.Stride+x0*4+channel];
                    var p11=source.Pixels[source.StartIndex+y1*source.Stride+x1*4+channel];
                    var top=p00+(p10-p00)*fx;
                    var bottom=p01+(p11-p01)*fx;
                    output[target+channel]=(byte)Math.Clamp(
                        (int)Math.Round(top+(bottom-top)*fy),0,255);
                }
            }
        }
        return new(output,0,width*4,width,height);
    }

    private static async Task<DecodedImage> DecodeAsync(
        byte[] encoded,CancellationToken token)
    {
        using var stream=new InMemoryRandomAccessStream();
        using(var writer=new DataWriter(stream))
        {
            writer.WriteBytes(encoded);
            await writer.StoreAsync().AsTask(token);
            await writer.FlushAsync().AsTask(token);
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder=await BitmapDecoder.CreateAsync(stream).AsTask(token);
        using var bitmap=await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb).AsTask(token);
        using var buffer=bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference=buffer.CreateReference();
        reference.As<IMemoryBufferByteAccess>().GetBuffer(out var address,out var capacity);
        if(capacity>int.MaxValue)throw new InvalidDataException("Companion buffer is too large.");
        var pixels=new byte[(int)capacity];
        Marshal.Copy(address,pixels,0,pixels.Length);
        var description=buffer.GetPlaneDescription(0);
        var required=description.StartIndex+
            (bitmap.PixelHeight-1)*description.Stride+bitmap.PixelWidth*4;
        if(required>capacity)throw new InvalidDataException("Companion buffer is incomplete.");
        return new(pixels,description.StartIndex,description.Stride,
            bitmap.PixelWidth,bitmap.PixelHeight);
    }
    private static async Task<byte[]> EncodePngAsync(
        byte[] pixels,int width,int height,CancellationToken token)
    {
        using var stream=new InMemoryRandomAccessStream();
        var encoder=await BitmapEncoder.CreateAsync(
            BitmapEncoder.PngEncoderId,stream).AsTask(token);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,
            (uint)width,(uint)height,96,96,pixels);
        await encoder.FlushAsync().AsTask(token);
        if(stream.Size>uint.MaxValue)throw new InvalidDataException(
            "Encoded Companion asset is too large.");
        var result=new byte[(int)stream.Size];
        using var reader=new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size).AsTask(token);
        reader.ReadBytes(result);
        return result;
    }

    private sealed record DecodedImage(
        byte[] Pixels,int StartIndex,int Stride,int Width,int Height);

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        void GetBuffer(out IntPtr buffer,out uint capacity);
    }
}

