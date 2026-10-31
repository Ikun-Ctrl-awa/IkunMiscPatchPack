using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Relics;
namespace AdaptiveTopBar;


public static class Entry {
 static readonly ConditionalWeakTable<Control,BarLayout> layouts=new();
 static readonly List<WeakReference<Control>> bars=new();
 public static void Initialize(){
  var h=new Harmony("local.adaptive-top-bar.v1");h.Patch(typeof(NTopBar).GetMethod("_Ready")!,postfix:new HarmonyMethod(typeof(Entry),nameof(Ready)){priority=Priority.Last});
  h.Patch(AccessTools.Method(typeof(NTopBar),"UpdateNavigation"),postfix:new HarmonyMethod(typeof(Entry),nameof(Ready)));
  h.Patch(typeof(NTopBar).GetMethod("AnimHide")!,prefix:new HarmonyMethod(typeof(Entry),nameof(Hide)));
  MonsterIntents.Install(h);
  ((SceneTree)Engine.GetMainLoop()).ProcessFrame+=Tick;
  // 原生 HBox 和模组的逐帧排列可能在 ProcessFrame 之后运行；绘制前保持最终几何位置。
  RenderingServer.FramePreDraw+=Paint;
  GD.Print("[AdaptiveTopBar] initialized; automatic rows, live mod controls, original parents preserved");
 }
 static void Ready(NTopBar __instance)=>Attach(__instance).InvalidateFocus();
 public static BarLayout Attach(Control bar){if(!layouts.TryGetValue(bar,out var l)){l=new BarLayout(bar);layouts.Add(bar,l);bars.Add(new(bar));}return l;}
 static void Tick(){DixieCompatibility.TryInstall();HextechCompatibility.TryInstall();for(int i=bars.Count-1;i>=0;i--){if(!bars[i].TryGetTarget(out var bar)||!GodotObject.IsInstanceValid(bar)){bars.RemoveAt(i);continue;}if(bar.IsInsideTree())layouts.GetValue(bar,_=>throw new InvalidOperationException()).Queue();}}
 static void Paint(){var live=new List<Control>();foreach(var weak in bars)if(weak.TryGetTarget(out var bar)&&GodotObject.IsInstanceValid(bar)&&bar.IsInsideTree()){layouts.GetValue(bar,_=>throw new InvalidOperationException()).Enforce();live.Add(bar);}HextechCompatibility.Paint(live);TopBarHoverTips.Paint(live);MonsterIntents.Paint(live);}
 static bool Hide(NTopBar __instance){
  if(!layouts.TryGetValue(__instance,out var layout)||layout.Rows<=1)return true;
  var field=AccessTools.Field(typeof(NTopBar),"_hideTween");(field.GetValue(__instance) as Tween)?.Kill();
  __instance.FocusBehaviorRecursive=Control.FocusBehaviorRecursiveEnum.Disabled;var tween=__instance.CreateTween();field.SetValue(__instance,tween);
  tween.TweenProperty(__instance,"position:y",-layout.Height-24,.25).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);return false;
 }
}

public sealed class BarLayout {
 readonly Control bar;readonly Dictionary<Control,Pose> originals=new();readonly HashSet<Container> watched=new();
 readonly List<Control> lastItems=new();readonly Dictionary<Control,Vector2> shifted=new();
 readonly List<Control> flattenedControls=new();
 readonly List<(Control Copy,Control Source)> potionBackgrounds=new();
 NinePatchRect? expandedBackground;
 bool queued,active,focusDirty=true;ulong lastFrame=ulong.MaxValue;double nextCheck;float oldHeight=-1;
 public int Rows{get;private set;}=1;public float Height{get;private set;}=80;
 public float BackgroundBottom{get;private set;}=100;
 public IReadOnlyList<Control> Items=>lastItems;
 public FlowResult? LastPlan{get;private set;}
 public BarLayout(Control bar){this.bar=bar;}
 public void InvalidateFocus()=>focusDirty=true;
 void Remember(Control c){if(!originals.ContainsKey(c))originals.Add(c,new(c));if(c is Container container&&watched.Add(container))container.SortChildren+=Queue;}
 public void Queue(){
  if(queued||!GodotObject.IsInstanceValid(bar)||!bar.IsInsideTree())return;
  double now=Time.GetTicksMsec();if(now<nextCheck)return;queued=true;
  Callable.From(()=>{queued=false;if(GodotObject.IsInstanceValid(bar)&&bar.IsInsideTree())Reflow();}).CallDeferred();
 }
 public void Reflow(){
  if(lastFrame==Engine.GetProcessFrames())return;lastFrame=Engine.GetProcessFrames();nextCheck=Time.GetTicksMsec()+100;
  if(bar.Size.X<64)return;
  var left=bar.GetNodeOrNull<Container>("LeftAlignedStuff");var right=bar.GetNodeOrNull<Container>("RightAlignedStuff");if(left==null||right==null)return;
  Remember(left);Remember(right);
  var units=new List<(Control Node,float Gap,bool Right)>();var flattened=new HashSet<Control>();var potionItems=new List<Control>();
  void Collect(Control c,bool rhs,float gap){
   if(!c.Visible||c.IsQueuedForDeletion()||c.HasMeta("adaptive_topbar_ignore"))return;Remember(c);
   if(c.Name.ToString() is "RoomIcons" or "Modifiers"){
    flattened.Add(c);bool first=true;foreach(var child in c.GetChildren().OfType<Control>().Where(n=>n.Visible)){Collect(child,rhs,first?gap:6);first=false;}return;
   }
   if(c.Name=="PotionMarginifier"&&c.GetCombinedMinimumSize().X>bar.Size.X-40){
    var holders=c.GetNodeOrNull<Control>("PotionContainer/MarginContainer/PotionHolders");
    if(holders!=null){foreach(var ancestor in Ancestors(holders,c)){Remember(ancestor);flattened.Add(ancestor);}flattened.Add(c);
     bool first=true;foreach(var holder in holders.GetChildren().OfType<Control>().Where(n=>n.Visible)){Collect(holder,rhs,first?gap+16:4);potionItems.Add(holder);first=false;}return;}
   }
   if(c is HBoxContainer&&c.GetCombinedMinimumSize().X>bar.Size.X-32&&c.MouseFilter==Control.MouseFilterEnum.Ignore){
    flattened.Add(c);foreach(var child in c.GetChildren().OfType<Control>())Collect(child,rhs,8);return;
   }
   units.Add((c,gap,rhs));
  }
  foreach(var c in left.GetChildren().OfType<Control>())Collect(c,false,24);
  foreach(var c in bar.GetChildren().OfType<Control>()){
   if(c==left||c==right||c.Name.ToString() is "BgImage" or "TrailContainer" or "GoldPopup" or "ActiveScreenProxy"||c.HasMeta("adaptive_topbar_decoration"))continue;
   if(c.Visible)Collect(c,false,12);
  }
  foreach(var c in right.GetChildren().OfType<Control>())Collect(c,true,0);
  var input=units.Select(u=>{var m=u.Node.GetCombinedMinimumSize();var saved=originals[u.Node];float w=Math.Max(m.X,saved.Size.X);float h=Math.Max(m.Y,Math.Min(80,saved.Size.Y));return new FlowItem(w*Math.Abs(saved.Scale.X),h*Math.Abs(saved.Scale.Y),u.Gap,u.Right);}).ToArray();
  var plan=FlowLayout.Pack(bar.Size.X,input);LastPlan=plan;lastItems.Clear();lastItems.AddRange(units.Select(u=>u.Node));
  bool needsLayout=plan.Rows>1||plan.Items.Any(p=>p.Scale<1)||potionItems.Count>0;
  if(!needsLayout){if(active)Restore();Rows=1;Height=80;BackgroundBottom=100;DixieCompatibility.Update(bar,0);return;}
  active=true;Rows=plan.Rows;Height=plan.Height;flattenedControls.Clear();flattenedControls.AddRange(flattened);
  bool changed=ApplyGeometry();
  UpdateBackground();
  UpdatePotionBackgrounds(potionItems,units,plan);
  ShiftBelow(BackgroundBottom-100);
  if(changed||focusDirty){UpdateFocus();focusDirty=false;}
  if(Math.Abs(oldHeight-Height)>1){oldHeight=Height;GD.Print($"[AdaptiveTopBar] rows={Rows} height={Height:0} width={bar.Size.X:0} controls={units.Count}");}
  bar.SetMeta("adaptive_topbar_rows",Rows);
 }
 public void Enforce(){if(active&&LastPlan!=null){ApplyGeometry();UpdateBackground();ShiftBelow(BackgroundBottom-100);}}
 void UpdateBackground(){
  // 保留原版纹理的石质上沿、破损下沿和阴影，只延展中段，避免整张图纵向拉长。
  var bg=bar.GetNodeOrNull<Control>("BgImage");if(bg==null)return;Remember(bg);
  float bottom=Height+32;
  var inverse=bar.GetGlobalTransformWithCanvas().AffineInverse();
  // 按布局控件计算背景，不测量内部齿轮旋转、悬停缩放或提示框的瞬时边界。
  // 这些动画不占布局空间，否则会逐帧改变遗物栏和版本信息的位置。
  foreach(var c in lastItems.Where(GodotObject.IsInstanceValid)){
   if(!c.IsVisibleInTree()||c.IsQueuedForDeletion())continue;
   var rect=c.GetGlobalRect();if(rect.Size.Y>160||rect.Size.X>bar.Size.X*2)continue;
   bottom=Math.Max(bottom,(inverse*rect.End).Y+16);
  }
  BackgroundBottom=bottom;
  float width=Math.Max(originals[bg].Size.X*Math.Abs(originals[bg].Scale.X),bar.Size.X+16);
  bg.SetAnchorsPreset(Control.LayoutPreset.TopLeft);bg.Scale=Vector2.One;bg.Position=new((bar.Size.X-width)/2,-2);bg.Size=new(width,bottom+2);bg.MouseFilter=Control.MouseFilterEnum.Ignore;
  if(bg is TextureRect image&&image.Texture!=null){
   if(expandedBackground==null){
    expandedBackground=new NinePatchRect{Name="AdaptiveStoneBackground",MouseFilter=Control.MouseFilterEnum.Ignore};
    expandedBackground.SetMeta("adaptive_topbar_decoration",true);bar.AddChild(expandedBackground);bar.MoveChild(expandedBackground,0);
   }
   expandedBackground.Texture=image.Texture;float th=image.Texture.GetHeight(),tw=image.Texture.GetWidth();
   expandedBackground.PatchMarginTop=(int)Math.Min(8,th/8);expandedBackground.PatchMarginBottom=(int)Math.Min(32,th*.32f);
   expandedBackground.PatchMarginLeft=expandedBackground.PatchMarginRight=(int)Math.Min(48,tw/8);
   expandedBackground.Position=bg.Position;expandedBackground.Size=bg.Size;expandedBackground.Modulate=bg.Modulate;
   expandedBackground.SelfModulate=originals[bg].SelfModulate;expandedBackground.Visible=bg.Visible;
   bg.SelfModulate=Colors.Transparent;
  }
  bar.SetMeta("adaptive_topbar_bottom",BackgroundBottom);
 }
 bool ApplyGeometry(){
  var left=bar.GetNodeOrNull<Control>("LeftAlignedStuff");var right=bar.GetNodeOrNull<Control>("RightAlignedStuff");if(left==null||right==null||LastPlan==null)return false;
  SetOrigin(left);SetOrigin(right);foreach(var c in flattenedControls)if(GodotObject.IsInstanceValid(c))SetOrigin(c);
  bool changed=false;
  foreach(var p in LastPlan.Items){var node=lastItems[p.Index];if(!GodotObject.IsInstanceValid(node)||node.IsQueuedForDeletion())continue;var pose=originals[node];
   node.SetAnchorsPreset(Control.LayoutPreset.TopLeft);var parent=(Control)node.GetParent();
   var position=parent.GetGlobalTransformWithCanvas().AffineInverse()*(bar.GetGlobalTransformWithCanvas()*new Vector2(p.X,p.Y));
   var scale=pose.Scale*p.Scale;position-=node.PivotOffset*(Vector2.One-scale);
   var size=new Vector2(p.Width/Math.Abs(pose.Scale.X)/p.Scale,p.Height/Math.Abs(pose.Scale.Y)/p.Scale);
   if(node.Position.DistanceTo(position)>.2f||node.Size.DistanceTo(size)>.2f)changed=true;
   if(node.Position.DistanceTo(position)>.1f)node.Position=position;
   if(node.Size.DistanceTo(size)>.1f)node.Size=size;
   if(node.Scale.DistanceTo(scale)>.001f)node.Scale=scale;
  }
  return changed;
 }
 static IEnumerable<Control> Ancestors(Control node,Control until){for(Control? c=node;c!=null&&c!=until;c=c.GetParent() as Control)yield return c;}
 void SetOrigin(Control c){Remember(c);c.SetAnchorsPreset(Control.LayoutPreset.TopLeft);if(c.Position!=Vector2.Zero)c.Position=Vector2.Zero;c.ClipContents=false;}
 void UpdatePotionBackgrounds(List<Control> potions,List<(Control Node,float Gap,bool Right)> units,FlowResult plan){
  foreach(var (copy,_) in potionBackgrounds)if(GodotObject.IsInstanceValid(copy))copy.Visible=false;
  if(potions.Count==0){RestorePotionSources();return;}
  var container=bar.GetNodeOrNull<Control>("LeftAlignedStuff/PotionMarginifier/PotionContainer");if(container==null)return;
  var rects=plan.Items.Where(p=>potions.Contains(units[p.Index].Node)).GroupBy(p=>p.Row).Select(g=>new Rect2(g.Min(p=>p.X)-12,g.Min(p=>p.Y)-4,g.Max(p=>p.X+p.Width)-g.Min(p=>p.X)+24,g.Max(p=>p.Height)+8)).ToArray();
  foreach(string name in new[]{"PotionBg","PotionErrorBg"}){
   var source=container.GetNodeOrNull<Control>(name);if(source==null)continue;Remember(source);
   var copies=potionBackgrounds.Where(p=>p.Source==source).ToList();
   for(int i=0;i<rects.Length;i++){
    Control copy;if(i<copies.Count)copy=copies[i].Copy;else{copy=(Control)source.Duplicate();copy.Name="AdaptivePotionBackground"+potionBackgrounds.Count;copy.SetMeta("adaptive_topbar_decoration",true);bar.AddChild(copy);bar.MoveChild(copy,1);potionBackgrounds.Add((copy,source));}
    copy.SetAnchorsPreset(Control.LayoutPreset.TopLeft);copy.Position=rects[i].Position;copy.Size=rects[i].Size;copy.MouseFilter=Control.MouseFilterEnum.Ignore;copy.Modulate=source.Modulate;copy.SelfModulate=originals[source].SelfModulate;copy.Visible=source.Visible;
   }source.SelfModulate=Colors.Transparent;
  }
 }
 void RestorePotionSources(){foreach(var pair in potionBackgrounds)if(GodotObject.IsInstanceValid(pair.Source))pair.Source.SelfModulate=originals[pair.Source].SelfModulate;}
 void ShiftBelow(float dy){
  foreach(string name in new[]{"RelicInventory","MultiplayerPlayerContainer","DebugInfo"}){
   var c=bar.GetParent()?.FindChild(name,true,false) as Control;if(c==null)continue;
   var originalField=c is NRelicInventory?AccessTools.Field(typeof(NRelicInventory),"_originalPos"):null;
   if(!shifted.TryGetValue(c,out var initial)){initial=originalField?.GetValue(c) is Vector2 cached?cached:c.Position;shifted.Add(c,initial);}
   // 采用正常显示位置，而非读取入场动画的瞬时位置；多行时另留背景下沿的间距。
   var target=initial+new Vector2(0,dy+(dy>0?(name=="RelicInventory"?24:16):0));
   if(originalField!=null){var current=(Vector2)originalField.GetValue(c)!;if(current.DistanceTo(target)>.1f){
     (AccessTools.Field(typeof(NRelicInventory),"_curTween").GetValue(c) as Tween)?.Kill();
     originalField.SetValue(c,target);
     c.Position=c.FocusBehaviorRecursive==Control.FocusBehaviorRecursiveEnum.Disabled?c.Position+target-current:target;
   }}else if(c.Position.DistanceTo(target)>.1f)c.Position=target;
  }
  DixieCompatibility.Update(bar,dy);
 }
 void UpdateFocus(){
  var focus=Descendants(bar).Where(c=>c.Visible&&c.IsVisibleInTree()&&c.FocusMode!=Control.FocusModeEnum.None&&c.Name!="ActiveScreenProxy").ToArray();
  foreach(var c in focus){var center=c.GetGlobalRect().GetCenter();
   Control? Find(Func<Vector2,bool> allowed,Func<Vector2,float> score)=>focus.Where(n=>n!=c).Select(n=>(Node:n,Delta:n.GetGlobalRect().GetCenter()-center)).Where(p=>allowed(p.Delta)).OrderBy(p=>score(p.Delta)).Select(p=>p.Node).FirstOrDefault();
   var l=Find(d=>d.X<-.5f&&Math.Abs(d.Y)<40,d=>-d.X+Math.Abs(d.Y));var r=Find(d=>d.X>.5f&&Math.Abs(d.Y)<40,d=>d.X+Math.Abs(d.Y));
   var up=Find(d=>d.Y< -40,d=>-d.Y*3+Math.Abs(d.X));var down=Find(d=>d.Y>40,d=>d.Y*3+Math.Abs(d.X));
   c.FocusNeighborLeft=(l??c).GetPath();c.FocusNeighborRight=(r??c).GetPath();c.FocusNeighborTop=(up??c).GetPath();if(down!=null)c.FocusNeighborBottom=down.GetPath();
  }
 }
 static IEnumerable<Control> Descendants(Node n){foreach(var c in n.GetChildren()){if(c is Control control)yield return control;foreach(var child in Descendants(c))yield return child;}}
 void Restore(){
  active=false;foreach(var (c,pose) in originals)if(GodotObject.IsInstanceValid(c))pose.Restore(c);
  foreach(var (copy,_) in potionBackgrounds)if(GodotObject.IsInstanceValid(copy))copy.Visible=false;
  if(expandedBackground!=null)expandedBackground.Visible=false;
  RestorePotionSources();ShiftBelow(0);
  foreach(var container in watched)if(GodotObject.IsInstanceValid(container))container.QueueSort();oldHeight=80;bar.SetMeta("adaptive_topbar_rows",1);bar.SetMeta("adaptive_topbar_bottom",100);
 }
 sealed class Pose {
  public Vector2 Size,Scale;public Color SelfModulate;public float Bottom;
  readonly float left,top,right,al,at,ar,ab;readonly Control.MouseFilterEnum mouseFilter;readonly bool clip;
  public Pose(Control c){Size=c.Size;Scale=c.Scale;SelfModulate=c.SelfModulate;left=c.OffsetLeft;top=c.OffsetTop;right=c.OffsetRight;Bottom=c.OffsetBottom;al=c.AnchorLeft;at=c.AnchorTop;ar=c.AnchorRight;ab=c.AnchorBottom;mouseFilter=c.MouseFilter;clip=c.ClipContents;}
  public void Restore(Control c){c.AnchorLeft=al;c.AnchorTop=at;c.AnchorRight=ar;c.AnchorBottom=ab;c.OffsetLeft=left;c.OffsetTop=top;c.OffsetRight=right;c.OffsetBottom=Bottom;c.Scale=Scale;c.SelfModulate=SelfModulate;c.MouseFilter=mouseFilter;c.ClipContents=clip;}
 }
}
