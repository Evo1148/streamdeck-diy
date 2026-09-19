using StreamDeckDIY.Core.Dashboard;
using StreamDeckDIY.Core.Display;
using StreamDeckDIY.Core.Profiles;
using StreamDeckDIY.Protocol.DisplayLink;
using StreamDeckDIY.Protocol.Models;
namespace StreamDeckDIY.Core.DisplayLink;

public static class DisplayLocalControlParameter
{
 public static uint Encode(ControlId control)=>control.Type switch
 {
  ControlType.Button when control.Index<ProfileControls.UserButtonCount=>control.Index,
  ControlType.EncoderCounterClockwise when control.Index==0=>12,
  ControlType.EncoderPress when control.Index==0=>13,
  ControlType.EncoderClockwise when control.Index==0=>14,
  _=>throw new ArgumentOutOfRangeException(nameof(control)),
 };

 public static bool TryDecode(uint parameter,out ControlId control)
 {
  if(parameter<ProfileControls.UserButtonCount){control=new(ControlType.Button,(byte)parameter);return true;}
  control=parameter switch
  {
   12=>new(ControlType.EncoderCounterClockwise,0),
   13=>new(ControlType.EncoderPress,0),
   14=>new(ControlType.EncoderClockwise,0),
   _=>default,
  };
  return parameter is >=12 and <=14;
 }
}

public static class DashboardDisplayGraphCompiler
{
 private static readonly HashSet<string> UnknownIconGlyphs=[];

 public static DisplayGraph Compile(DashboardRuntimeState state,uint generation)
 {
  var options=state.Configuration.EffectiveVisualOptions;
  var tokens=DashboardVisualTokenResolver.Resolve(state.Configuration.VisualIdentity,options);
  var nodes=new List<DisplayNode>{new(1,0,DisplayNodeKind.Rectangle,
    new(0,0,DisplayGraphVisualContract.Width,DisplayGraphVisualContract.Height),-100,
    FillColor:tokens.Background)};
  CompileDecoration(tokens,nodes);
  if(IsHikariControl(state.Configuration))CompileHikariControlFoundation(state.Configuration,tokens,nodes);
  var touch=new List<DisplayTouchRegion>();
  foreach(var layout in DashboardLayoutEngine.Build(state.Configuration))
  {
   if(!ShouldRender(layout.Kind,options))continue;
   ushort root=(ushort)(1000+((int)layout.Kind*100));var bounds=Bounds(layout.Bounds);
   nodes.Add(new(root,0,DisplayNodeKind.Group,bounds,(short)((int)layout.Kind*10)));
   if(ShouldFrameWidget(layout.Kind,tokens,state.Configuration))nodes.Add(new((ushort)(root+1),root,
    DisplayNodeKind.Rectangle,new(0,0,bounds.Width,bounds.Height),FillColor:tokens.Surface,
    StrokeColor:tokens.Border,StrokeWidth:(byte)(tokens.UsesWidgetFrames?1:0),
    CornerRadius:tokens.CardRadius));
    CompileWidget(layout.Kind,root,bounds,state,tokens,nodes,touch);
  }
  var imageIds=nodes.Where(node=>node.Kind==DisplayNodeKind.Image)
   .Select(node=>node.AssetId).ToHashSet();
  var assets=new List<DisplayAssetReference>();
  if(state.NowPlaying is { Artwork: { Length: > 0 } artwork,ArtworkKey:not null })
  {
   var artworkId=ArtworkContentKey.AssetId(state.NowPlaying.ArtworkKey);
   if(imageIds.Contains(artworkId))assets.Add(new(artworkId,64,64,
    ArtworkContentKey.Crc32(artwork),64*64*2));
  }
  if(state.CompanionAsset is { } companionAsset&&imageIds.Contains(companionAsset.AssetId))
   assets.Add(new(companionAsset.AssetId,companionAsset.Width,companionAsset.Height,
    companionAsset.SourceCrc32,companionAsset.Width*companionAsset.Height*2));
  return new(generation,DisplayMode.Dashboard,nodes,touch,[],assets);
 }

 private static void CompileProfile(ushort root,DisplayBounds bounds,DashboardRuntimeState state,
  DashboardVisualTokens tokens,List<DisplayNode> nodes)
 {
  if(IsHikariControl(state.Configuration))
  {
   AddText(nodes,(ushort)(root+2),root,new(0,0,(short)(bounds.Width*3/5),bounds.Height),2,
    state.ProfileName,tokens.TitleSize,0,true,tokens.TextPrimary);
   var page=state.IsHome?"Inicio":state.PageName;
   AddText(nodes,(ushort)(root+3),root,new((short)(bounds.Width*3/5),1,
    (short)(bounds.Width-bounds.Width*3/5),bounds.Height),2,page,tokens.BodySize,2,
    color:tokens.TextSecondary);
   return;
  }
  AddText(nodes,(ushort)(root+2),root,new(2,0,(short)(bounds.Width-4),bounds.Height),2,
   $"{IdentityLabel(state.Configuration.VisualIdentity)}  /  {state.ProfileName}  /  {(state.IsHome ? "Home" : state.PageName)}",
   tokens.BodySize,0,true,tokens.TextPrimary);
 }
 private static void CompileWidget(DashboardWidgetKind kind,ushort root,DisplayBounds bounds,
  DashboardRuntimeState state,DashboardVisualTokens tokens,List<DisplayNode> nodes,
  List<DisplayTouchRegion> touch)
 {
   switch(kind){
   case DashboardWidgetKind.ButtonMatrix:CompileButtons(root,bounds,state,tokens,nodes,touch);break;
   case DashboardWidgetKind.Navigation:CompileNavigation(root,bounds,state,tokens,nodes,touch);break;
    case DashboardWidgetKind.Profile:CompileProfile(root,bounds,state,tokens,nodes);break;
   case DashboardWidgetKind.Clock:AddText(nodes,(ushort)(root+2),root,
    new(0,0,bounds.Width,bounds.Height),2,state.ClockText,14,2,true,tokens.AccentPrimary);break;
   case DashboardWidgetKind.NowPlaying:CompileMedia(root,bounds,state,tokens,nodes);break;
   case DashboardWidgetKind.SystemStats:CompileStats(root,bounds,state,tokens,nodes);break;
   case DashboardWidgetKind.Mascot:CompileMascot(root,bounds,state,tokens,nodes);break;
   }
 }

 private static void CompileButtons(ushort root,DisplayBounds bounds,DashboardRuntimeState state,
  DashboardVisualTokens tokens,List<DisplayNode> nodes,List<DisplayTouchRegion> touch)
 {
  var matrix=new DashboardRect(0,0,bounds.Width,bounds.Height);
  var showLabels=state.Configuration.Options(DashboardWidgetKind.ButtonMatrix).ShowLabels;
  if(state.IsHome)
  {
   AddText(nodes,(ushort)(root+2),root,new(12,18,(short)(bounds.Width-24),32),2,
    "HOME",18,1,true,tokens.AccentPrimary);
   AddText(nodes,(ushort)(root+3),root,new(18,58,(short)(bounds.Width-36),40),2,
    state.ProfileName,12,1,true,tokens.TextPrimary);
   AddText(nodes,(ushort)(root+4),root,new(18,104,(short)(bounds.Width-36),36),2,
    $"{state.PageCount} {(state.PageCount==1?"página":"páginas")}",tokens.BodySize,1,
    color:tokens.TextSecondary);
   return;
  }
   if(!tokens.UsesActionCards)
   {
    for(ushort column=1;column<ButtonMatrixLayout.Columns;column++)nodes.Add(new(
     (ushort)(root+50+column),root,DisplayNodeKind.Line,
     new((short)(bounds.Width*column/ButtonMatrixLayout.Columns),14,1,(short)(bounds.Height-28)),1,
     StrokeColor:tokens.Border,StrokeWidth:1));
    for(ushort row=1;row<ButtonMatrixLayout.Rows;row++)nodes.Add(new(
     (ushort)(root+53+row),root,DisplayNodeKind.Line,
     new(14,(short)(bounds.Height*row/ButtonMatrixLayout.Rows),(short)(bounds.Width-28),1),1,
     StrokeColor:tokens.Border,StrokeWidth:1));
  }
  for(var index=0;index<ButtonMatrixLayout.ControlCount;index++)
  {
   var cell=ButtonMatrixLayout.Cell(matrix,index);var x=(short)cell.X;var y=(short)cell.Y;
   var width=(short)(cell.X+cell.Width-x);var height=(short)(cell.Y+cell.Height-y);
   var item=index<state.Buttons.Count?state.Buttons[index]:null;ushort id=(ushort)(root+2+index*3);
   if(tokens.UsesActionCards)nodes.Add(new(id,root,DisplayNodeKind.Rectangle,
    new((short)(x+3),(short)(y+3),(short)(width-6),(short)(height-6)),1,
    FillColor:item?.Action.Type==ActionType.None?tokens.CardInactive:tokens.Card,
    StrokeColor:index%3==0?tokens.AccentSecondary:tokens.Border,StrokeWidth:1,
    CornerRadius:tokens.CardRadius));
   var iconSize=(short)Math.Clamp(height-(showLabels?32:14),16,
    tokens.UsesActionCards?24:28);
   nodes.Add(new((ushort)(id+1),root,DisplayNodeKind.Icon,new(
    (short)(x+(width-iconSize)/2),(short)(y+(showLabels?8:(height-iconSize)/2)),iconSize,iconSize),2,
    FillColor:item?.Action.Type==ActionType.None?tokens.TextSecondary:
     tokens.UsesActionCards?(index%3==0?tokens.AccentSecondary:tokens.AccentPrimary):
     tokens.AccentPrimary,
    IconId:ResolveIconId(item)));
   if(showLabels)AddText(nodes,(ushort)(id+2),root,new((short)(x+6),(short)(y+height-20),
     (short)(width-12),14),3,CompactLabel(item?.Summary,width-12,tokens.BodySize),tokens.BodySize,1,
     color:item?.Action.Type==ActionType.None?tokens.TextSecondary:tokens.TextPrimary);
   var control=new ControlId(ControlType.Button,(byte)index);
   touch.Add(new((ushort)(100+index),new((short)(bounds.X+x),(short)(bounds.Y+y),width,height),1,
    DisplayGestureMask.Tap|DisplayGestureMask.LongPress,DisplayTouchActionKind.LocalControl,
    DisplayLocalControlParameter.Encode(control)));
  }
 }

 private static void CompileNavigation(ushort root,DisplayBounds bounds,DashboardRuntimeState state,
  DashboardVisualTokens tokens,List<DisplayNode> nodes,List<DisplayTouchRegion> touch)
 {
  var commands=new[]{StreamDeckDIY.Core.Profiles.SystemNavigation.Previous,
   StreamDeckDIY.Core.Profiles.SystemNavigation.Home,
   StreamDeckDIY.Core.Profiles.SystemNavigation.Next};
  var icons=new[]{DashboardIconCatalog.Left,DashboardIconCatalog.Grid,DashboardIconCatalog.Right};
  if(!tokens.UsesWidgetFrames)nodes.Add(new((ushort)(root+20),root,DisplayNodeKind.Line,
   new(8,0,(short)(bounds.Width-16),1),1,StrokeColor:tokens.Border,StrokeWidth:1));
  for(var index=0;index<3;index++)
  {
   var x=(short)(bounds.Width*index/3);var right=(short)(bounds.Width*(index+1)/3);
   var width=(short)(right-x);
   var disabled=!state.IsHome&&((index==0&&state.PageIndex<=0)||
    (index==2&&state.PageIndex>=state.PageCount-1));
   var iconSize=(short)Math.Min(index==1?20:24,bounds.Height-6);
   nodes.Add(new((ushort)(root+2+index),root,DisplayNodeKind.Icon,
    new((short)(x+(width-iconSize)/2),(short)((bounds.Height-iconSize)/2),iconSize,iconSize),2,
    FillColor:disabled?tokens.TextSecondary:index==1?tokens.AccentPrimary:tokens.TextPrimary,
    IconId:icons[index]));
   touch.Add(new((ushort)(109+index),new((short)(bounds.X+x),bounds.Y,width,bounds.Height),1,
    DisplayGestureMask.Tap,DisplayTouchActionKind.LocalControl,(uint)commands[index]));
  }
 }

 private static void CompileMedia(ushort root,DisplayBounds bounds,DashboardRuntimeState state,
  DashboardVisualTokens tokens,List<DisplayNode> nodes)
 {
  var options=state.Configuration.Options(DashboardWidgetKind.NowPlaying);
  var showArtwork=options.ShowArtwork&&state.Configuration.EffectiveVisualOptions.ShowArtwork&&
   state.NowPlaying.ArtworkKey is not null&&bounds.Height>=58;
  var compactHikari=IsHikariControl(state.Configuration);
  var artworkSize=showArtwork?(short)Math.Clamp(Math.Min(bounds.Height-(compactHikari?32:18),bounds.Width/3),32,72):(short)0;
  var textX=(short)(showArtwork?artworkSize+14:8);var textWidth=(short)(bounds.Width-textX-8);
  if(showArtwork)nodes.Add(new((ushort)(root+2),root,DisplayNodeKind.Image,
   new(8,8,artworkSize,artworkSize),1,AssetId:ArtworkContentKey.AssetId(state.NowPlaying.ArtworkKey)));
  var title=state.NowPlaying.IsAvailable?state.NowPlaying.Title:"Sin reproducción";
  AddText(nodes,(ushort)(root+3),root,new(textX,7,textWidth,16),3,
   CompactLabel(title,textWidth,tokens.BodySize),tokens.BodySize,0,true,tokens.TextPrimary);
  if(bounds.Height>=48)AddText(nodes,(ushort)(root+4),root,new(textX,25,textWidth,14),3,
    CompactLabel(state.NowPlaying.Artist,textWidth,tokens.BodySize),tokens.BodySize,0,
    color:tokens.TextSecondary);
  if(bounds.Height>=54&&!compactHikari)AddText(nodes,(ushort)(root+6),root,new(textX,(short)(bounds.Height-23),
    textWidth,14),3,state.NowPlaying.IsPlaying?"<    II    >":"<     >     >",
    tokens.BodySize,1,color:tokens.TextPrimary);
  var progress=NowPlayingTimeline.ProgressAt(state.NowPlaying,DateTimeOffset.Now);
  nodes.Add(new((ushort)(root+7),root,DisplayNodeKind.Progress,new(8,(short)(bounds.Height-(compactHikari?7:9)),
    (short)(bounds.Width-16),3),4,FillColor:tokens.AccentPrimary,StrokeColor:tokens.Track,CornerRadius:2,
   ProgressValue:Permille(progress,1000),ProgressAvailable:progress.HasValue));
 }

 private static void CompileStats(ushort root,DisplayBounds bounds,DashboardRuntimeState state,
  DashboardVisualTokens tokens,List<DisplayNode> nodes)
 {
  var options=state.Configuration.Options(DashboardWidgetKind.SystemStats);
  var metrics=new List<(string Name,double? Value,string Detail,ushort Color)>();
  if(options.ShowCpu)metrics.Add(("CPU",state.SystemStats.CpuUsagePercent,
   options.ShowTemperatures?SystemStatsFormatting.FormatTemperature(state.SystemStats.CpuTemperatureC):string.Empty,
    tokens.Success));
  if(options.ShowGpu)metrics.Add(("GPU",state.SystemStats.GpuUsagePercent,
   options.ShowTemperatures?SystemStatsFormatting.FormatTemperature(state.SystemStats.GpuTemperatureC):string.Empty,
    tokens.AccentSecondary));
  if(options.ShowRam)metrics.Add(("RAM",state.SystemStats.RamUsagePercent,string.Empty,
    tokens.AccentPrimary));
  if(metrics.Count==0)return;
  if(bounds.Width>=250&&bounds.Height>=150)
  {
   for(var index=0;index<metrics.Count;index++)
   {
    var left=(short)(8+(bounds.Width-16)*index/metrics.Count);
    var right=(short)(8+(bounds.Width-16)*(index+1)/metrics.Count);var width=(short)(right-left-4);
    ushort id=(ushort)(root+2+index*5);var metric=metrics[index];
    nodes.Add(new(id,root,DisplayNodeKind.Rectangle,new(left,8,width,(short)(bounds.Height-34)),1,
     FillColor:tokens.Card,StrokeColor:tokens.Border,StrokeWidth:1,CornerRadius:tokens.CardRadius));
     AddText(nodes,(ushort)(id+1),root,new((short)(left+7),14,(short)(width-14),14),2,
      metric.Name,tokens.BodySize,0,color:tokens.TextSecondary);
    AddText(nodes,(ushort)(id+2),root,new((short)(left+7),31,(short)(width-14),25),2,
      SystemStatsFormatting.FormatPercent(metric.Value),14,0,true,tokens.TextPrimary);
     AddText(nodes,(ushort)(id+3),root,new((short)(left+7),59,(short)(width-14),12),2,
      metric.Detail,tokens.BodySize,0,color:tokens.TextSecondary);
    nodes.Add(new((ushort)(id+4),root,DisplayNodeKind.Progress,new((short)(left+7),
      (short)(bounds.Height-39),(short)(width-14),4),2,FillColor:metric.Color,StrokeColor:tokens.Track,
     CornerRadius:2,ProgressValue:Permille(metric.Value,10),ProgressAvailable:metric.Value.HasValue));
   }
   var detail=SystemStatsPresentation.Detail(state.SystemStats);
    AddText(nodes,(ushort)(root+30),root,new(8,(short)(bounds.Height-20),(short)(bounds.Width-16),14),2,
     detail,tokens.BodySize,0,color:tokens.TextSecondary);
   return;
  }
  var rowHeight=(short)Math.Max(10,(bounds.Height-8)/metrics.Count);
  for(var index=0;index<metrics.Count;index++)
  {
   var y=(short)(4+index*rowHeight);var metric=metrics[index];ushort id=(ushort)(root+2+index*2);
   AddText(nodes,id,root,new(7,y,(short)Math.Min(62,bounds.Width/2),(short)(rowHeight-2)),2,
     $"{metric.Name} {SystemStatsFormatting.FormatPercent(metric.Value)}",tokens.BodySize,0,
     color:tokens.TextPrimary);
   nodes.Add(new((ushort)(id+1),root,DisplayNodeKind.Progress,new((short)Math.Min(72,bounds.Width/2),
    (short)(y+(rowHeight-4)/2),(short)Math.Max(8,bounds.Width-Math.Min(79,bounds.Width/2)),4),2,
     FillColor:metric.Color,StrokeColor:tokens.Track,CornerRadius:2,ProgressValue:Permille(metric.Value,10),
    ProgressAvailable:metric.Value.HasValue));
  }
 }

 private static void CompileMascot(ushort root,DisplayBounds bounds,DashboardRuntimeState state,
  DashboardVisualTokens tokens,List<DisplayNode> nodes)
 {
  var companion=state.Companion??CompanionState.Default;
  var hero=DashboardPresets.FunctionalFrom(state.Configuration.PresetId)==
   FunctionalPreset.Companion;
  if(state.CompanionAsset is { } sprite)
  {
   if(hero)
   {
    // The precomposed sprite and this quiet stage share the same real background.
    nodes.Add(new((ushort)(root+1),root,DisplayNodeKind.Rectangle,
     new(0,0,bounds.Width,bounds.Height),0,FillColor:tokens.Background));
    var heroImageSize=(short)Math.Min(160,Math.Min(bounds.Height-14,
     Math.Max(112,bounds.Width*3/5)));
    var heroImageX=(short)8;
    var heroImageY=(short)((bounds.Height-heroImageSize)/2);
    nodes.Add(new((ushort)(root+2),root,DisplayNodeKind.Image,
     new(heroImageX,heroImageY,heroImageSize,heroImageSize),2,AssetId:sprite.AssetId));
    var textX=(short)(heroImageX+heroImageSize+12);
    var textWidth=(short)Math.Max(48,bounds.Width-textX-10);
    AddText(nodes,(ushort)(root+8),root,new(textX,42,textWidth,38),3,
     CompactLabel(companion.Message,textWidth,tokens.BodySize),tokens.BodySize,0,true,tokens.AccentPrimary);
    var context=state.IsHome?state.ProfileName:
     $"{state.ProfileName} · {state.PageName}";
    AddText(nodes,(ushort)(root+9),root,new(textX,88,textWidth,30),3,
     CompactLabel(context,textWidth,tokens.BodySize),tokens.BodySize,0,
     color:tokens.TextSecondary);
    nodes.Add(new((ushort)(root+11),root,DisplayNodeKind.Circle,
     new(textX,132,6,6),3,FillColor:tokens.Success));
    nodes.Add(new((ushort)(root+12),root,DisplayNodeKind.Line,
     new((short)(textX+12),135,(short)Math.Max(18,textWidth-12),1),3,
     StrokeColor:tokens.Border,StrokeWidth:1));
    return;
   }

   var imageSize=(short)Math.Min(96,Math.Min(bounds.Width-16,bounds.Height-16));
   if(bounds.Width<220||bounds.Height<150)
   {
    nodes.Add(new((ushort)(root+2),root,DisplayNodeKind.Image,
     new((short)((bounds.Width-imageSize)/2),(short)((bounds.Height-imageSize)/2),imageSize,imageSize),2,
     AssetId:sprite.AssetId));
    return;
   }
   var imageX=(short)Math.Max(16,(120-imageSize)/2+16);
   var imageY=(short)Math.Max(16,(bounds.Height-imageSize)/2);
   nodes.Add(new((ushort)(root+2),root,DisplayNodeKind.Image,
    new(imageX,imageY,imageSize,imageSize),2,AssetId:sprite.AssetId));
   AddText(nodes,(ushort)(root+9),root,new(152,54,(short)(bounds.Width-166),40),3,
    CompactLabel(companion.Message,bounds.Width-166,tokens.BodySize),tokens.BodySize,0,true,tokens.TextPrimary);
   return;
  }

  var width=(short)Math.Clamp(bounds.Width-24,42,96);
  var height=(short)Math.Clamp(bounds.Height-18,34,72);
  var x=(short)((bounds.Width-width)/2);var y=(short)((bounds.Height-height)/2);
  nodes.Add(new((ushort)(root+2),root,DisplayNodeKind.Rectangle,new(x,y,width,height),2,
   FillColor:tokens.AccentSecondary,CornerRadius:(byte)Math.Min(18,height/3)));
  var eyeWidth=(short)Math.Max(2,width/10);
  var eyeHeight=(short)(state.Mascot.EyesClosed?2:Math.Max(4,height/6));
  nodes.Add(new((ushort)(root+3),root,DisplayNodeKind.Rectangle,
   new((short)(x+width/4-eyeWidth/2),(short)(y+height/2-eyeHeight/2),eyeWidth,eyeHeight),3,
   FillColor:tokens.Background,CornerRadius:2));
  nodes.Add(new((ushort)(root+4),root,DisplayNodeKind.Rectangle,
   new((short)(x+3*width/4-eyeWidth/2),(short)(y+height/2-eyeHeight/2),eyeWidth,eyeHeight),3,
   FillColor:tokens.Background,CornerRadius:2));
 }

 private static void AddText(List<DisplayNode> nodes,ushort id,ushort parent,DisplayBounds bounds,
  short z,string? text,byte size,byte alignment,bool bold=false,ushort? color=null)=>
  nodes.Add(new(id,parent,DisplayNodeKind.Text,bounds,z,FillColor:color??0xFFFF,
   Text:text??string.Empty,FontId:(byte)(bold?2:1),FontSize:size,Alignment:alignment));
 private static ushort Permille(double? value,double scale)=>(ushort)Math.Clamp((value??0)*scale,0,1000);
 private static DisplayBounds Bounds(DashboardRect r)=>new((short)r.X,(short)r.Y,(short)r.Width,(short)r.Height);
 private static bool ShouldFrameWidget(DashboardWidgetKind kind,DashboardVisualTokens tokens,
  DashboardConfiguration configuration)=>
  !(kind==DashboardWidgetKind.Mascot&&
    DashboardPresets.FunctionalFrom(configuration.PresetId)==FunctionalPreset.Companion)&&
  kind is not DashboardWidgetKind.Profile and not DashboardWidgetKind.Clock &&
  (tokens.UsesWidgetFrames||kind is DashboardWidgetKind.ButtonMatrix);
 private static bool ShouldRender(DashboardWidgetKind kind,DashboardVisualOptions options)=>kind switch
 {
  DashboardWidgetKind.Clock=>options.ShowClock,
  DashboardWidgetKind.SystemStats=>options.ShowSystemStats,
  DashboardWidgetKind.Mascot=>options.ShowMascot,
  _=>true,
 };
 private static string IdentityLabel(VisualIdentity identity)=>identity switch
 {
  VisualIdentity.Neon=>"NEON",VisualIdentity.Lumen=>"LUMEN",
  VisualIdentity.Studio=>"STUDIO",_=>"HIKARI",
 };
 private static bool IsHikariControl(DashboardConfiguration configuration)=>
  configuration.VisualIdentity==VisualIdentity.Hikari&&
  DashboardPresets.FunctionalFrom(configuration.PresetId)==FunctionalPreset.Control;
 private static ushort ResolveIconId(DashboardControlState? item,ushort fallback=DashboardIconCatalog.Grid)
 {
  if(item is null||item.Action.Type==ActionType.None)return DashboardIconCatalog.Grid;
  var label=DisplayGraphVisualContract.NormalizeBitmapText(item.Summary);
  if(label.Contains("AUDIO")||label.Contains("VOLUM")||label.Contains("SILEN"))
   return DashboardIconCatalog.Volume;
  if(label.Contains("OPENAI")||label.Contains("CHAT"))return DashboardIconCatalog.Message;
  if(label.Contains("CODIG")||label.Contains("PROGRAM")||label.Contains("VISUAL"))
   return DashboardIconCatalog.Tools;
  if(label.Contains("MUSIC")||label.Contains("SPOTIFY"))return DashboardIconCatalog.Music;
  if(label.Contains("JUEG")||label.Contains("GAME"))return DashboardIconCatalog.Play;
  if(label.Contains("NOTA")||label.Contains("ARCHIV"))return DashboardIconCatalog.Folder;
  if(item.Action.Type is ActionType.Keyboard or ActionType.KeyboardShortcut)
   return DashboardIconCatalog.Keyboard;
  if(item.Action.Type==ActionType.ConsumerControl)return DashboardIconCatalog.Volume;
  if(DashboardIconCatalog.TryFromGlyph(item.IconGlyph,out var resolved))return resolved;
  var glyph=item.IconGlyph??string.Empty;
  lock(UnknownIconGlyphs)if(UnknownIconGlyphs.Add(glyph))
   System.Diagnostics.Debug.WriteLine($"ICON UNKNOWN glyph=U+{(glyph.Length>0?(int)glyph[0]:0):X4} fallback={fallback}");
  return fallback;
 }
 private static string CompactLabel(string? value,int width,byte fontSize)
 {
  if(string.IsNullOrWhiteSpace(value))return "Libre";
  var normalized=DisplayGraphVisualContract.NormalizeBitmapText(value);
  if(normalized.Contains("SIN ASIGNAR"))return "Libre";
  if(normalized.Contains("SALIDA DE AUDIO")||normalized.Contains("CAMBIAR AUDIO"))return "Audio";
  if(normalized.Contains("SILENCIAR"))return "Silenciar";
  if(normalized.Contains("VOLUMEN +")||normalized.Contains("VOLUME UP"))return "Vol +";
  if(normalized.Contains("VOLUMEN -")||normalized.Contains("VOLUME DOWN"))return "Vol -";
  if(normalized.Contains("BLOC DE NOTAS"))return "Notas";
  var capacity=Math.Max(1,(width+DisplayGraphVisualContract.FontScale(fontSize))/
   DisplayGraphVisualContract.GlyphAdvance(fontSize));
  if(normalized.Length<=capacity)return value.Trim();
  var words=value.Split(' ',StringSplitOptions.RemoveEmptyEntries)
   .Where(word=>word.ToUpperInvariant() is not "ABRIR" and not "CAMBIAR" and not "ALTERNAR" and not "EJECUTAR")
   .ToArray();
  for(var count=Math.Min(2,words.Length);count>0;count--)
  {
   var candidate=string.Join(' ',words.TakeLast(count));
   if(DisplayGraphVisualContract.NormalizeBitmapText(candidate).Length<=capacity)return candidate;
  }
  var fallback=words.FirstOrDefault()??value.Trim();
  return fallback[..Math.Min(fallback.Length,capacity)];
 }
 private static void CompileDecoration(DashboardVisualTokens tokens,List<DisplayNode> nodes)
 {
  if(tokens.UsesDecorativeGrid)
  {
   for(ushort index=0;index<4;index++)nodes.Add(new((ushort)(2+index),0,
    DisplayNodeKind.Line,new(0,(short)(64+index*64),480,1),-90,
    StrokeColor:tokens.Border,StrokeWidth:1,Opacity:90));
   nodes.Add(new(6,0,DisplayNodeKind.Line,new(16,14,96,1),-80,
    StrokeColor:tokens.AccentPrimary,StrokeWidth:2));
   nodes.Add(new(7,0,DisplayNodeKind.Line,new(368,304,96,1),-80,
    StrokeColor:tokens.AccentSecondary,StrokeWidth:2));
  }
  else
  {
   nodes.Add(new(2,0,DisplayNodeKind.Line,new(18,306,52,1),-80,
    StrokeColor:tokens.AccentPrimary,StrokeWidth:2));
  }
 }

 private static void CompileHikariControlFoundation(DashboardConfiguration configuration,
  DashboardVisualTokens tokens,List<DisplayNode> nodes)
 {
  var layout=DashboardLayoutEngine.Build(configuration);
  var media=layout.Single(item=>item.Kind==DashboardWidgetKind.NowPlaying).Bounds;
  var stats=layout.Single(item=>item.Kind==DashboardWidgetKind.SystemStats).Bounds;
  var left=(short)(Math.Min(media.X,stats.X)-6);
  var top=(short)(Math.Min(media.Y,stats.Y)-4);
  var right=(short)(Math.Max(media.X+media.Width,stats.X+stats.Width)+8);
  var bottom=(short)(Math.Max(media.Y+media.Height,stats.Y+stats.Height)+10);
  nodes.Add(new(20,0,DisplayNodeKind.Rectangle,new(left,top,(short)(right-left),(short)(bottom-top)),-30,
   FillColor:tokens.Surface,CornerRadius:tokens.CardRadius));
  nodes.Add(new(21,0,DisplayNodeKind.Line,new((short)(left+14),(short)(stats.Y-10),
   (short)(right-left-28),1),-20,
   StrokeColor:tokens.Border,StrokeWidth:1));
  nodes.Add(new(22,0,DisplayNodeKind.Line,new(18,40,44,1),-20,
   StrokeColor:tokens.AccentPrimary,StrokeWidth:2));
 }
}

public static class DisplaySceneGraphAdapter
{
 public static DisplayGraph Compile(DisplayScene scene,uint generation)
 {
  var nodes=new List<DisplayNode>{new(1,0,DisplayNodeKind.Rectangle,new(0,0,480,320),-100,FillColor:Rgb565.FromHex(scene.Background))};var used=new HashSet<ushort>{1};
  foreach(var e in scene.Elements){var id=StableId(e.Id,used);var bounds=new DisplayBounds((short)e.X,(short)e.Y,(short)e.Width,(short)e.Height);nodes.Add(e switch{DisplayTextElement t=>new(id,0,DisplayNodeKind.Text,bounds,(short)t.ZIndex,t.Visible,(byte)Math.Clamp(t.Opacity*255,0,255),Text:t.Text,FontId:0,FontSize:(byte)Math.Clamp(t.FontSize,1,255),Alignment:(byte)t.Alignment),DisplayRectangleElement r=>new(id,0,DisplayNodeKind.Rectangle,bounds,(short)r.ZIndex,r.Visible,(byte)Math.Clamp(r.Opacity*255,0,255),FillColor:Rgb565.FromHex(r.Fill),CornerRadius:(byte)Math.Clamp(r.CornerRadius,0,255)),DisplayImageElement i=>new(id,0,DisplayNodeKind.Image,bounds,(short)i.ZIndex,i.Visible,(byte)Math.Clamp(i.Opacity*255,0,255),AssetId:StableAssetId(i.AssetId)),_=>throw new NotSupportedException()});}
  return new(generation,DisplayMode.Scene,nodes,[],[]);
 }
 private static ushort StableId(string value,HashSet<ushort> used){uint h=2166136261;foreach(var c in value){h^=c;h*=16777619;}ushort id=(ushort)(2+h%65000);while(!used.Add(id))id++;return id;}private static ushort StableAssetId(string v){uint h=2166136261;foreach(var c in v){h^=c;h*=16777619;}return (ushort)(1+h%65534);}
}

public static class SnakeOverlayDemo
{
 public static DisplayGraph Create(uint generation)
 {
  var nodes=new List<DisplayNode>{new(1,0,DisplayNodeKind.Rectangle,new(0,0,480,320),-10,FillColor:0x0841),new(500,0,DisplayNodeKind.Group,new(20,100,220,40),20)};
  for(ushort i=0;i<6;i++)nodes.Add(new((ushort)(501+i),500,DisplayNodeKind.Circle,new((short)(i*24),8,20,20),(short)i,FillColor:i==5?(ushort)0xFFE0:(ushort)0x07E0));
  var animation=new DisplayGraphAnimation(1,500,DisplayAnimationProperty.X,1800,RepeatCount:0,Loop:true,Keyframes:[new(0,20,DisplayEasing.Linear),new(1800,240,DisplayEasing.EaseInOut)]);
  var touch=new DisplayTouchRegion(500,new(20,100,220,40),50,DisplayGestureMask.Tap,DisplayTouchActionKind.None,0,DisplayTouchMode.PassThrough);
  return new(generation,DisplayMode.Dashboard,nodes,[touch],[animation]);
 }
}


