using StreamDeckDIY.Protocol.DisplayLink;
namespace StreamDeckDIY.Core.DisplayLink;

public enum DisplayNodeKind:byte{Group=0,Rectangle=1,Circle=2,Text=3,Icon=4,Image=5,Progress=6,Line=7}
public enum DisplayAnimationProperty:byte{X=1,Y=2,Opacity=3,ScaleX=4,ScaleY=5,Rotation=6}
public enum DisplayEasing:byte{Linear=0,EaseIn=1,EaseOut=2,EaseInOut=3,Step=4}
public enum DisplayTouchMode:byte{Capture=1,PassThrough=2,Ignore=3}
public enum DisplayTouchActionKind:byte{None=0,LocalControl=1,HostInteraction=2}
[Flags]public enum DisplayGestureMask:byte{Tap=1,LongPress=2,SwipeLeft=4,SwipeRight=8}
public readonly record struct DisplayBounds(short X,short Y,short Width,short Height);
public sealed record DisplayNode(ushort Id,ushort ParentId,DisplayNodeKind Kind,DisplayBounds Bounds,
    short ZIndex=0,bool Visible=true,byte Opacity=255,ushort FillColor=0,ushort StrokeColor=0,
    byte StrokeWidth=0,byte CornerRadius=0,string? Text=null,byte FontId=0,byte FontSize=0,
    byte Alignment=0,ushort IconId=0,ushort AssetId=0,ushort ProgressValue=0,short Rotation=0,
    bool ProgressAvailable=true);
public sealed record DisplayTouchRegion(ushort Id,DisplayBounds Bounds,short Priority,DisplayGestureMask Gestures,
    DisplayTouchActionKind ActionKind,uint ActionParameter,DisplayTouchMode Mode=DisplayTouchMode.Capture);
public sealed record DisplayKeyframe(ushort TimeMilliseconds,int Value,DisplayEasing Easing);
public sealed record DisplayGraphAnimation(ushort Id,ushort TargetNodeId,DisplayAnimationProperty Property,
    ushort DurationMilliseconds,ushort DelayMilliseconds=0,ushort RepeatCount=0,bool Loop=false,
    IReadOnlyList<DisplayKeyframe>? Keyframes=null);
public sealed record DisplayAssetReference(ushort Id,ushort Width,ushort Height,uint Crc32,int ByteLength);
public sealed record DisplayGraph(uint Generation,DisplayMode Mode,IReadOnlyList<DisplayNode> Nodes,
    IReadOnlyList<DisplayTouchRegion> TouchRegions,IReadOnlyList<DisplayGraphAnimation> Animations,
    IReadOnlyList<DisplayAssetReference>? Assets=null)
{
    public static DisplayGraph Empty(uint generation,DisplayMode mode=DisplayMode.Dashboard)=>new(generation,mode,[],[],[],[]);
}
public static class Rgb565
{
    public static ushort FromHex(string? value){if(string.IsNullOrWhiteSpace(value))return 0;var s=value.Trim().TrimStart('#');if(s.Length<6||!uint.TryParse(s[..6],System.Globalization.NumberStyles.HexNumber,null,out var rgb))return 0;return (ushort)((((rgb>>19)&0x1F)<<11)|(((rgb>>10)&0x3F)<<5)|((rgb>>3)&0x1F));}
    public static string ToHex(ushort value)
    {
        var red=(value>>11)&0x1F;var green=(value>>5)&0x3F;var blue=value&0x1F;
        var r=(red*255+15)/31;var g=(green*255+31)/63;var b=(blue*255+15)/31;
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}

public static class DisplayGraphVisualContract
{
    public const short Width=480;
    public const short Height=320;
    public static int FontScale(byte fontSize)=>Math.Max(1,(fontSize+6)/7);
    public static int FontPixelHeight(byte fontSize)=>7*FontScale(fontSize);
    public static int GlyphAdvance(byte fontSize)=>6*FontScale(fontSize);
    public static string NormalizeBitmapText(string? text)
    {
        if(string.IsNullOrEmpty(text))return string.Empty;
        var decomposed=text.Normalize(System.Text.NormalizationForm.FormD);
        return string.Concat(decomposed.Where(character=>
            System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)!=
            System.Globalization.UnicodeCategory.NonSpacingMark)).ToUpperInvariant();
    }
    public static string FitSingleLine(string? text,short width,byte fontSize)
    {
        text=NormalizeBitmapText(text);if(text.Length==0||width<=0)return string.Empty;
        var scale=FontScale(fontSize);var capacity=Math.Max(0,(width+scale)/GlyphAdvance(fontSize));
        var glyphs=text.EnumerateRunes().Select(rune=>rune.ToString()).ToArray();
        if(glyphs.Length<=capacity)return text;
        if(capacity<=0)return string.Empty;
        if(capacity<=3)return new string('.',capacity);
        return string.Concat(glyphs.Take(capacity-3))+"...";
    }
}

public sealed record DashboardIconContract(string Name, ushort Id, string Glyph);

public static class DashboardIconCatalog
{
    public const ushort Volume=1,Web=2,Settings=3,Folder=4,Keyboard=5,Play=6,
        Application=7,Sequence=8,Microphone=9,Headphones=10,Music=11,Link=12,
        Power=13,Tools=14,Left=15,Right=16,Up=17,Down=18,Message=19,
        Person=20,Alert=21,Success=22,Grid=23,Press=24;

    public static IReadOnlyList<DashboardIconContract> Contracts { get; } =
    [
        new(nameof(Volume),Volume,"\uE767"),new(nameof(Web),Web,"\uE774"),
        new(nameof(Settings),Settings,"\uE713"),new(nameof(Folder),Folder,"\uE8B7"),
        new(nameof(Keyboard),Keyboard,"\uE765"),new(nameof(Play),Play,"\uE768"),
        new(nameof(Application),Application,"\uE8A5"),new(nameof(Sequence),Sequence,"\uE8FD"),
        new(nameof(Microphone),Microphone,"\uE720"),new(nameof(Headphones),Headphones,"\uE7F6"),
        new(nameof(Music),Music,"\uE8D6"),new(nameof(Link),Link,"\uE71B"),
        new(nameof(Power),Power,"\uE7E8"),new(nameof(Tools),Tools,"\uE90F"),
        new(nameof(Left),Left,"\uE72B"),new(nameof(Right),Right,"\uE72A"),
        new(nameof(Up),Up,"\uE74A"),new(nameof(Down),Down,"\uE74B"),
        new(nameof(Message),Message,"\uE8BD"),new(nameof(Person),Person,"\uE77B"),
        new(nameof(Alert),Alert,"\uE7BA"),new(nameof(Success),Success,"\uE73E"),
        new(nameof(Grid),Grid,"\uE80A"),new(nameof(Press),Press,"\uE73E"),
    ];

    public static bool TryFromGlyph(string? glyph,out ushort iconId)
    {
        iconId=glyph switch
        {
            "\uE767"=>Volume,"\uE774"=>Web,"\uE713"=>Settings,"\uE8B7"=>Folder,
            "\uE765" or "\uE8A7"=>Keyboard,"\uE768" or "\uE769"=>Play,
            "\uE8A5" or "\uE945"=>Application,"\uE8FD"=>Sequence,
            "\uE720"=>Microphone,"\uE7F6"=>Headphones,"\uE8D6"=>Music,
            "\uE71B"=>Link,"\uE7E8"=>Power,"\uE90F"=>Tools,"\uE72B"=>Left,
            "\uE72A"=>Right,"\uE74A"=>Up,"\uE74B"=>Down,"\uE8BD"=>Message,
            "\uE77B"=>Person,"\uE7BA"=>Alert,"\uE73E"=>Success,"\uE80A"=>Grid,
            _=>0,
        };
        return iconId!=0;
    }

    public static ushort FromGlyph(string? glyph)=>
        TryFromGlyph(glyph,out var iconId)?iconId:Grid;
}
