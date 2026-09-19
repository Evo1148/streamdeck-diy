using StreamDeckDIY.Protocol.DisplayLink;
using StreamDeckDIY.Core.Dashboard;
using StreamDeckDIY.Protocol.Models;

namespace StreamDeckDIY.Core.DisplayLink;

public sealed record DisplayParityGoldenScene(string Id, DisplayGraph Graph);

public static class DisplayParityGoldenScenes
{
    private static readonly ushort Background = Rgb565.FromHex("#0B0E14");
    private static readonly ushort Card = Rgb565.FromHex("#181D29");
    private static readonly ushort Primary = Rgb565.FromHex("#F2F4F8");
    private static readonly ushort Accent = Rgb565.FromHex("#A974FF");

    public static IReadOnlyList<DisplayParityGoldenScene> All { get; } =
    [
        new("PARITY-01", Buttons()),
        new("PARITY-02", NowPlaying()),
        new("PARITY-03", Stats()),
        new("PARITY-04", Encoder()),
        new("PARITY-05", Shapes()),
        new("PARITY-06", LongText()),
        new("PARITY-07", Images()),
        new("ASSET-PARITY-01", AssetParity()),
        new("COMPANION-HERO-01", CompanionHero()),
    ];

    private static DisplayGraph Buttons()
    {
        var nodes = Base();
        for (var index = 0; index < 12; index++)
        {
            var column = index % 3;
            var row = index / 3;
            var x = (short)(12 + column * 154);
            var y = (short)(10 + row * 76);
            var id = (ushort)(10 + index * 3);
            nodes.Add(new(id, 0, DisplayNodeKind.Rectangle,
                new(x, y, 144, 68), 1, FillColor: Card, CornerRadius: 7));
            nodes.Add(new((ushort)(id + 1), 0, DisplayNodeKind.Icon,
                new((short)(x + 58), (short)(y + 8), 28, 28), 2,
                FillColor: Accent, IconId: (ushort)(1 + index % 24)));
            nodes.Add(new((ushort)(id + 2), 0, DisplayNodeKind.Text,
                new((short)(x + 8), (short)(y + 43), 128, 14), 3,
                FillColor: Primary, Text: $"Botón {index + 1}",
                FontId: 1, FontSize: 7, Alignment: 1));
        }
        return Graph(nodes);
    }

    private static DisplayGraph NowPlaying() => Graph(
    [
        ..Base(),
        new(10,0,DisplayNodeKind.Image,new(18,28,128,128),1,AssetId:1),
        new(11,0,DisplayNodeKind.Text,new(166,34,290,24),2,FillColor:Primary,
            Text:"Título de reproducción",FontId:2,FontSize:14),
        new(12,0,DisplayNodeKind.Text,new(166,66,290,18),2,FillColor:Primary,
            Text:"Artista",FontId:1,FontSize:7),
        new(13,0,DisplayNodeKind.Text,new(166,116,290,18),2,FillColor:Primary,
            Text:"<    II    >",FontId:1,FontSize:7,Alignment:1),
        new(14,0,DisplayNodeKind.Progress,new(18,176,438,6),2,FillColor:Accent,
            StrokeColor:Rgb565.FromHex("#252A35"),CornerRadius:3,ProgressValue:625),
    ], [new(1,128,128,0,32768)]);

    private static DisplayGraph Stats() => Graph(
    [
        ..Base(),
        new(10,0,DisplayNodeKind.Rectangle,new(14,30,142,200),1,FillColor:Card,CornerRadius:8),
        new(11,0,DisplayNodeKind.Rectangle,new(169,30,142,200),1,FillColor:Card,CornerRadius:8),
        new(12,0,DisplayNodeKind.Rectangle,new(324,30,142,200),1,FillColor:Card,CornerRadius:8),
        Text(20,"CPU",22,48,126,18,7),Text(21,"37 %",22,78,126,30,14,true),
        Text(22,"GPU",177,48,126,18,7),Text(23,"48 %",177,78,126,30,14,true),
        Text(24,"RAM",332,48,126,18,7),Text(25,"52 %",332,78,126,30,14,true),
        new(30,0,DisplayNodeKind.Progress,new(22,190,126,6),2,FillColor:0x47F0,StrokeColor:0x2104,CornerRadius:3,ProgressValue:370),
        new(31,0,DisplayNodeKind.Progress,new(177,190,126,6),2,FillColor:Accent,StrokeColor:0x2104,CornerRadius:3,ProgressValue:480),
        new(32,0,DisplayNodeKind.Progress,new(332,190,126,6),2,FillColor:0x641F,StrokeColor:0x2104,CornerRadius:3,ProgressValue:520),
    ]);

    private static DisplayGraph Encoder() => Graph(
    [
        ..Base(),
        new(10,0,DisplayNodeKind.Rectangle,new(12,224,146,76),1,FillColor:Card,CornerRadius:6),
        new(11,0,DisplayNodeKind.Icon,new(22,246,26,26),2,FillColor:Accent,IconId:DashboardIconCatalog.Left),
        Text(12,"Giro izquierda",56,238,94,46,7),
        new(20,0,DisplayNodeKind.Rectangle,new(167,224,146,76),1,FillColor:Card,CornerRadius:6),
        new(21,0,DisplayNodeKind.Icon,new(177,246,26,26),2,FillColor:Accent,IconId:DashboardIconCatalog.Press),
        Text(22,"Pulsación",211,238,94,46,7),
        new(30,0,DisplayNodeKind.Rectangle,new(322,224,146,76),1,FillColor:Card,CornerRadius:6),
        new(31,0,DisplayNodeKind.Icon,new(332,246,26,26),2,FillColor:Accent,IconId:DashboardIconCatalog.Right),
        Text(32,"Giro derecha",366,238,94,46,7),
    ]);

    private static DisplayGraph Shapes() => Graph(
    [
        ..Base(),
        new(10,0,DisplayNodeKind.Rectangle,new(40,40,180,120),1,FillColor:0xF800,StrokeColor:0xFFFF,StrokeWidth:3,CornerRadius:18),
        new(11,0,DisplayNodeKind.Circle,new(150,90,120,120),2,FillColor:0x07E0,Opacity:210),
        new(12,0,DisplayNodeKind.Line,new(80,70,260,160),3,StrokeColor:0x001F,StrokeWidth:4),
        new(13,0,DisplayNodeKind.Progress,new(80,250,320,12),4,FillColor:Accent,StrokeColor:0x2104,CornerRadius:6,ProgressValue:500),
        Text(14,"Z-ORDER + CLIPPING",238,112,190,24,14,true),
    ]);

    private static DisplayGraph LongText() => Graph(
    [
        ..Base(),
        new(10,0,DisplayNodeKind.Rectangle,new(40,70,400,70),1,FillColor:Card,CornerRadius:8),
        Text(11,"Cambiar salida de audio predeterminada de Windows",52,88,376,28,14,true),
        new(12,0,DisplayNodeKind.Rectangle,new(120,180,240,48),1,FillColor:Card,CornerRadius:6),
        Text(13,"Texto que requiere puntos suspensivos",130,192,220,20,7),
    ]);

    private static DisplayGraph Images() => Graph(
    [
        ..Base(),
        new(10,0,DisplayNodeKind.Image,new(30,30,180,120),1,AssetId:1),
        new(11,0,DisplayNodeKind.Image,new(250,30,120,180),1,AssetId:1),
        Text(12,"FILL 180 x 120",30,164,180,18,7),
        Text(13,"FILL 120 x 180",250,224,120,18,7),
    ], [new(1,64,64,0,8192)]);

    private static DisplayGraph AssetParity()
    {
        var nodes=Base();
        for(var index=0;index<9;index++)
        {
            var column=index%3;var row=index/3;
            nodes.Add(new((ushort)(10+index),0,DisplayNodeKind.Icon,
                new((short)(18+column*88),(short)(18+row*72),34,34),2,
                FillColor:Accent,IconId:(ushort)(index+1)));
        }
        nodes.Add(new(40,0,DisplayNodeKind.Image,new(292,18,64,64),2,
            AssetId:0x0101));
        nodes.Add(new(41,0,DisplayNodeKind.Image,new(366,18,96,96),2,
            AssetId:DisplayAssetIds.ActiveCompanion));
        nodes.Add(new(42,0,DisplayNodeKind.Progress,new(292,102,154,6),2,
            FillColor:Accent,StrokeColor:Card,CornerRadius:3,ProgressValue:625));
        nodes.Add(Text(43,"ASSET PARITY",292,120,154,18,7,true));
        nodes.Add(new(44,0,DisplayNodeKind.Icon,new(120,274,24,24),2,
            FillColor:Primary,IconId:DashboardIconCatalog.Left));
        nodes.Add(new(45,0,DisplayNodeKind.Icon,new(228,274,24,24),2,
            FillColor:Accent,IconId:DashboardIconCatalog.Grid));
        nodes.Add(new(46,0,DisplayNodeKind.Icon,new(336,274,24,24),2,
            FillColor:Primary,IconId:DashboardIconCatalog.Right));
        return Graph(nodes,
        [
            new(0x0101,64,64,0x12345678,8192),
            new(DisplayAssetIds.ActiveCompanion,96,96,0x87654321,18432),
        ]);
    }

    private static DisplayGraph CompanionHero()
    {
        var nodes = Base();
        nodes.Add(new(100,0,DisplayNodeKind.Rectangle,new(6,22,268,188),0,
            FillColor:Background,StrokeColor:Card,StrokeWidth:1,CornerRadius:8));
        nodes.Add(new(101,0,DisplayNodeKind.Image,new(10,34,160,160),2,
            AssetId:DisplayAssetIds.ActiveCompanion));
        nodes.Add(Text(102,"TODO LISTO.",178,52,86,26,7,true));
        nodes.Add(new(103,0,DisplayNodeKind.Text,new(178,88,86,34),3,
            FillColor:Rgb565.FromHex("#96A3C8"),Text:"General · Inicio",
            FontId:1,FontSize:7));
        nodes.Add(new(104,0,DisplayNodeKind.Circle,new(178,140,6,6),3,
            FillColor:Rgb565.FromHex("#51E7A9")));
        nodes.Add(new(105,0,DisplayNodeKind.Line,new(190,143,66,1),3,
            StrokeColor:Rgb565.FromHex("#374167"),StrokeWidth:1));
        nodes.Add(new(110,0,DisplayNodeKind.Image,new(286,22,64,64),2,
            AssetId:0x0101));
        nodes.Add(Text(111,"MIDNIGHT",360,32,104,18,7,true));
        nodes.Add(Text(112,"Lofi Girl",360,54,104,16,7));
        for(var index=0;index<9;index++)
        {
            var column=index%3;var row=index/3;
            nodes.Add(new((ushort)(120+index),0,DisplayNodeKind.Icon,
                new((short)(286+column*58),(short)(100+row*42),28,28),2,
                FillColor:index<3?Accent:Primary,IconId:(ushort)(1+index)));
        }
        nodes.Add(Text(140,"CPU 27",8,222,72,14,7));
        nodes.Add(new(141,0,DisplayNodeKind.Progress,new(8,240,72,5),2,
            FillColor:Accent,StrokeColor:Card,CornerRadius:2,ProgressValue:270));
        nodes.Add(Text(142,"GPU 43",96,222,72,14,7));
        nodes.Add(new(143,0,DisplayNodeKind.Progress,new(96,240,72,5),2,
            FillColor:Rgb565.FromHex("#6DEBFF"),StrokeColor:Card,CornerRadius:2,
            ProgressValue:430));
        nodes.Add(Text(144,"RAM 50",184,222,72,14,7));
        nodes.Add(new(145,0,DisplayNodeKind.Progress,new(184,240,72,5),2,
            FillColor:Rgb565.FromHex("#51E7A9"),StrokeColor:Card,CornerRadius:2,
            ProgressValue:500));
        nodes.Add(new(150,0,DisplayNodeKind.Icon,new(124,278,22,22),2,
            FillColor:Primary,IconId:DashboardIconCatalog.Left));
        nodes.Add(new(151,0,DisplayNodeKind.Icon,new(229,278,22,22),2,
            FillColor:Accent,IconId:DashboardIconCatalog.Grid));
        nodes.Add(new(152,0,DisplayNodeKind.Icon,new(334,278,22,22),2,
            FillColor:Primary,IconId:DashboardIconCatalog.Right));
        return Graph(nodes,
        [
            new(0x0101,64,64,0x12345678,8192),
            new(DisplayAssetIds.ActiveCompanion,96,96,0x87654321,18432),
        ]);
    }
    private static List<DisplayNode> Base() =>
        [new(1,0,DisplayNodeKind.Rectangle,new(0,0,480,320),-100,FillColor:Background)];
    private static DisplayNode Text(ushort id,string value,short x,short y,short width,
        short height,byte size,bool bold=false)=>new(id,0,DisplayNodeKind.Text,
        new(x,y,width,height),5,FillColor:Primary,Text:value,FontId:(byte)(bold?2:1),
        FontSize:size,Alignment:1);
    private static DisplayGraph Graph(IReadOnlyList<DisplayNode> nodes,
        IReadOnlyList<DisplayAssetReference>? assets=null)=>new(1,DisplayMode.Scene,nodes,[],[],assets??[]);
}

public static class VisualSystemGoldenScenes
{
    public static IReadOnlyList<DisplayParityGoldenScene> All { get; } =
    [
        new("HIKARI-CONTROL", DashboardDisplayGraphCompiler.Compile(
            State(VisualIdentity.Hikari, FunctionalPreset.Control), 101)),
        new("NEON-CONTROL", DashboardDisplayGraphCompiler.Compile(
            State(VisualIdentity.Neon, FunctionalPreset.Control), 102)),
        new("HIKARI-COMPANION", DashboardDisplayGraphCompiler.Compile(
            State(VisualIdentity.Hikari, FunctionalPreset.Companion), 103)),
        new("NEON-COMPANION", DashboardDisplayGraphCompiler.Compile(
            State(VisualIdentity.Neon, FunctionalPreset.Companion), 104)),
    ];

    private static DashboardRuntimeState State(
        VisualIdentity identity, FunctionalPreset functionalPreset)
    {
        var preset = DashboardPresets.For(functionalPreset);
        var configuration = DashboardConfiguration.CreateDefault(1) with
        {
            VisualIdentity = identity,
            FunctionalPreset = functionalPreset,
            PresetId = preset.Id,
            Widgets = preset.DefaultWidgets(),
        };
        var buttons = Enumerable.Range(0, ButtonMatrixLayout.ControlCount).Select(index =>
        {
            var action = index < 6
                ? new DeviceAction(ActionType.HostAction, HostActionId: (uint)(index + 1))
                : new DeviceAction(ActionType.None);
            var labels = new[] { "Audio", "OpenAI", "Notas", "Musica", "Luces", "Foco" };
            var glyphs = new[] { "\uE767", "\uE774", "\uE8B7", "\uE8D6", "\uE713", "\uE90F" };
            return new DashboardControlState(new ControlId(ControlType.Button, (byte)index),
                index < labels.Length ? labels[index] : "Sin asignar",
                index < glyphs.Length ? glyphs[index] : "", action);
        }).ToArray();
        var encoder = new[]
        {
            new DashboardControlState(new(ControlType.EncoderCounterClockwise, 0),
                "Volumen -", "", new DeviceAction(ActionType.ConsumerControl,
                    ConsumerControl: ConsumerControlAction.VolumeDown)),
            new DashboardControlState(new(ControlType.EncoderPress, 0),
                "Silenciar", "", new DeviceAction(ActionType.ConsumerControl,
                    ConsumerControl: ConsumerControlAction.Mute)),
            new DashboardControlState(new(ControlType.EncoderClockwise, 0),
                "Volumen +", "", new DeviceAction(ActionType.ConsumerControl,
                    ConsumerControl: ConsumerControlAction.VolumeUp)),
        };
        var stats = new SystemStatsState
        {
            CpuUsagePercent = 27,
            CpuTemperatureC = 54,
            GpuUsagePercent = 43,
            GpuTemperatureC = 61,
            RamUsedBytes = 10_000,
            RamTotalBytes = 20_000,
        };
        return new(configuration, "General", "21:52",
            new NowPlayingState(true, true, "Midnight Drive", "Lofi Girl", "Night", .42,
                "Media", ArtworkKey: "golden-artwork"), stats,
            MascotVisualEngine.Frame(MascotState.Happy, 0, true), buttons, encoder,
            new CompanionState(CompanionMood.Happy, "Tu espacio esta listo", "General"));
    }
}

