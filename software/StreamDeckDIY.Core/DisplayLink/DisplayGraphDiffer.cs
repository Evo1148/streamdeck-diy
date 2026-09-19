namespace StreamDeckDIY.Core.DisplayLink;
public enum DisplayGraphChangeKind{Define,Patch,Delete,SetText,DefineTouch,DeleteTouch,DefineAnimation}
public sealed record DisplayGraphChange(DisplayGraphChangeKind Kind,ushort Id,DisplayNode? Node=null,
    DisplayNode? PreviousNode=null,string? Text=null,DisplayTouchRegion? Touch=null,DisplayGraphAnimation? Animation=null);
public static class DisplayGraphDiffer
{
 public static IReadOnlyList<DisplayGraphChange> Diff(DisplayGraph previous,DisplayGraph current)
 {
  var changes=new List<DisplayGraphChange>();var before=previous.Nodes.ToDictionary(n=>n.Id);var after=current.Nodes.ToDictionary(n=>n.Id);
  foreach(var old in previous.Nodes.Where(n=>!after.ContainsKey(n.Id)).OrderByDescending(n=>n.ZIndex))changes.Add(new(DisplayGraphChangeKind.Delete,old.Id));
  foreach(var node in current.Nodes){if(!before.TryGetValue(node.Id,out var old))changes.Add(new(DisplayGraphChangeKind.Define,node.Id,node));else if(node!=old){if(node with{Text=old.Text,ProgressAvailable=old.ProgressAvailable}!=old)changes.Add(new(DisplayGraphChangeKind.Patch,node.Id,node,old));if(node.Text!=old.Text)changes.Add(new(DisplayGraphChangeKind.SetText,node.Id,Text:node.Text??string.Empty));}}
  var oldTouch=previous.TouchRegions.ToDictionary(r=>r.Id);var newTouch=current.TouchRegions.ToDictionary(r=>r.Id);
  foreach(var r in previous.TouchRegions.Where(r=>!newTouch.ContainsKey(r.Id)))changes.Add(new(DisplayGraphChangeKind.DeleteTouch,r.Id));
  foreach(var r in current.TouchRegions.Where(r=>!oldTouch.TryGetValue(r.Id,out var old)||old!=r))changes.Add(new(DisplayGraphChangeKind.DefineTouch,r.Id,Touch:r));
  foreach(var a in current.Animations)changes.Add(new(DisplayGraphChangeKind.DefineAnimation,a.Id,Animation:a));return changes;
 }
 public static bool RequiresFullSync(DisplayGraph? previous,DisplayGraph current)=>previous is null||previous.Mode!=current.Mode||previous.Nodes.Count==0||Diff(previous,current).Count>32;
}
