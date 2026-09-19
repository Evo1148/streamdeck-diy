namespace StreamDeckDIY.Core.Dashboard;

public readonly record struct CompanionPixelBounds(int X,int Y,int Width,int Height)
{
    public int Right=>X+Width;
    public int Bottom=>Y+Height;
    public bool IsEmpty=>Width<=0||Height<=0;
}

public readonly record struct CompanionSourceGeometry(
    int Width,int Height,CompanionPixelBounds ContentBounds);

public readonly record struct CompanionFrameProfile(
    double WidthFraction,double HeightFraction);

public readonly record struct CompanionCrop(int X,int Y,int Width,int Height);

public readonly record struct CompanionPlacement(int X,int Y,int Width,int Height);

public readonly record struct CompanionBgr(byte Blue,byte Green,byte Red);

public static class CompanionRasterPolicy
{
    public const int AlphaThreshold=4;
    public const int RenderSize=96;
    public const int SafePadding=7;
    public const string ProcessingVersion="hero-v3";

    public static string RequestKey(string packId,VisualIdentity identity,
        CompanionMood mood,ushort background)=>
        $"{packId}:{identity}:{mood}:{ProcessingVersion}:size={RenderSize}:padding={SafePadding}:alpha={AlphaThreshold}:bg={background:X4}";

    public static string ProcessingKey(string sourceContentKey,VisualIdentity identity,
        ushort background)=>
        $"{sourceContentKey}:{ProcessingVersion}:identity={identity}:size={RenderSize}:padding={SafePadding}:alpha={AlphaThreshold}:bg={background:X4}";

    public static CompanionPixelBounds FindAlphaBounds(
        ReadOnlySpan<byte> bgra,int startIndex,int stride,int width,int height,
        byte alphaThreshold=AlphaThreshold)
    {
        if(width<=0||height<=0||startIndex<0||stride<width*4)
            throw new ArgumentOutOfRangeException(nameof(stride));
        var required=checked(startIndex+(height-1)*stride+width*4);
        if(required>bgra.Length)throw new InvalidDataException("Pixel buffer is incomplete.");
        var left=width;var top=height;var right=-1;var bottom=-1;
        for(var y=0;y<height;y++)
        {
            var row=startIndex+y*stride;
            for(var x=0;x<width;x++)
            {
                if(bgra[row+x*4+3]<=alphaThreshold)continue;
                left=Math.Min(left,x);top=Math.Min(top,y);
                right=Math.Max(right,x);bottom=Math.Max(bottom,y);
            }
        }
        if(right<left||bottom<top)return default;
        left=Math.Max(0,left-2);top=Math.Max(0,top-2);
        right=Math.Min(width-1,right+2);bottom=Math.Min(height-1,bottom+2);
        return new(left,top,right-left+1,bottom-top+1);
    }

    public static CompanionFrameProfile CreatePackProfile(
        IEnumerable<CompanionSourceGeometry> geometries)
    {
        var values=geometries.ToArray();
        if(values.Length==0||values.Any(value=>value.Width<=0||value.Height<=0||
                value.ContentBounds.IsEmpty))
            throw new InvalidDataException("Companion pack has no visible content.");
        var width=values.Max(value=>
            value.ContentBounds.Width/(double)value.Width);
        var height=values.Max(value=>
            value.ContentBounds.Height/(double)value.Height);
        return new(Math.Clamp(width,.05,1),Math.Clamp(height,.05,1));
    }

    public static CompanionCrop CalculateCrop(
        CompanionFrameProfile profile,CompanionSourceGeometry source)
    {
        if(source.Width<=0||source.Height<=0||source.ContentBounds.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(source));
        var width=Math.Clamp((int)Math.Ceiling(profile.WidthFraction*source.Width),
            source.ContentBounds.Width,source.Width);
        var height=Math.Clamp((int)Math.Ceiling(profile.HeightFraction*source.Height),
            source.ContentBounds.Height,source.Height);
        var centerX=source.ContentBounds.X+source.ContentBounds.Width/2.0;
        var centerY=source.ContentBounds.Y+source.ContentBounds.Height/2.0;
        var x=Math.Clamp((int)Math.Round(centerX-width/2.0),0,source.Width-width);
        var y=Math.Clamp((int)Math.Round(centerY-height/2.0),0,source.Height-height);
        return new(x,y,width,height);
    }

    public static CompanionPlacement Fit(
        CompanionCrop crop,int targetWidth=RenderSize,int targetHeight=RenderSize,
        int padding=SafePadding)
    {
        if(crop.Width<=0||crop.Height<=0||targetWidth<=padding*2||
            targetHeight<=padding*2)throw new ArgumentOutOfRangeException(nameof(crop));
        var availableWidth=targetWidth-padding*2;
        var availableHeight=targetHeight-padding*2;
        var scale=Math.Min(availableWidth/(double)crop.Width,
            availableHeight/(double)crop.Height);
        var width=Math.Max(1,(int)Math.Round(crop.Width*scale));
        var height=Math.Max(1,(int)Math.Round(crop.Height*scale));
        return new((targetWidth-width)/2,(targetHeight-height)/2,width,height);
    }

    public static CompanionBgr Background(ushort rgb565)=>new(
        (byte)(((rgb565&0x1F)*255+15)/31),
        (byte)((((rgb565>>5)&0x3F)*255+31)/63),
        (byte)((((rgb565>>11)&0x1F)*255+15)/31));

    public static CompanionBgr CompositePremultiplied(
        byte blue,byte green,byte red,byte alpha,ushort background)
    {
        var bg=Background(background);
        return new(
            (byte)Math.Min(255,blue+(bg.Blue*(255-alpha)+127)/255),
            (byte)Math.Min(255,green+(bg.Green*(255-alpha)+127)/255),
            (byte)Math.Min(255,red+(bg.Red*(255-alpha)+127)/255));
    }
}

public static class CompanionMicrocopy
{
    public static string For(CompanionMood mood)=>mood switch
    {
        CompanionMood.Happy=>"Todo listo.",
        CompanionMood.Busy=>"En foco.",
        CompanionMood.Sleeping=>"Descansando.",
        CompanionMood.Alert=>"Ojo, revisa esto.",
        _=>"Aquí estoy.",
    };
}
