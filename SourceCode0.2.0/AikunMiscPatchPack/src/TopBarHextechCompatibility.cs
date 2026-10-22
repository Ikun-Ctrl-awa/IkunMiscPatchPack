using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;

namespace AdaptiveTopBar;

// 海克斯选择仍使用原父节点和输入控件，只在多行顶栏下方安排可用显示区域。
public static class HextechCompatibility {
 static readonly ConditionalWeakTable<Control,SelectionLayout> layouts=new();
 static readonly List<WeakReference<Control>> screens=new();
 static bool installed;
 static ulong nextAttempt;
 public static void TryInstall(){
  if(installed||Time.GetTicksMsec()<nextAttempt)return;nextAttempt=Time.GetTicksMsec()+1000;
  var type=FusionQuestBlessingFix.Entry.Find("HextechRunes.HextechRuneSelectionScreen");if(type==null)return;
  installed=true;
  try{
   var h=new Harmony("Aikun.AdaptiveTopBar.Hextech");
   foreach(var name in new[]{"BuildUi","_Ready","AfterOverlayOpened","AfterOverlayShown"}){
    var method=AccessTools.Method(type,name);if(method!=null)h.Patch(method,postfix:new HarmonyMethod(typeof(HextechCompatibility),nameof(Track)){priority=Priority.Last});
   }
   GD.Print("[AdaptiveTopBar] Hextech selection safe area enabled");
  }catch(Exception e){GD.Print("[AdaptiveTopBar] Hextech compatibility skipped: "+e.Message);}
 }
 static void Track(Control __instance){
  if(layouts.TryGetValue(__instance,out _))return;
  var panel=__instance.GetNodeOrNull<Control>("ScreenCenter/ContentPanel");
  if(panel==null)return; // 外部呈现器有自己的布局，不接管。
  layouts.Add(__instance,new(__instance,panel));screens.Add(new(__instance));
 }
 public static void Paint(IEnumerable<Control> topBars){
  if(screens.Count==0)return;var bars=topBars.ToArray();
  for(int i=screens.Count-1;i>=0;i--){
   if(!screens[i].TryGetTarget(out var screen)||!GodotObject.IsInstanceValid(screen)){screens.RemoveAt(i);continue;}
   layouts.GetValue(screen,_=>throw new InvalidOperationException()).Apply(bars);
  }
 }
 sealed class SelectionLayout {
  readonly Control screen,panel;readonly Vector2 originalScale,originalPivot;
  bool adjusted;
  public SelectionLayout(Control screen,Control panel){this.screen=screen;this.panel=panel;originalScale=panel.Scale;originalPivot=panel.PivotOffset;}
  void Restore(){
   if(!adjusted||!GodotObject.IsInstanceValid(panel))return;
   panel.Scale=originalScale;panel.PivotOffset=originalPivot;
   if(panel.GetParent() is Container container)container.QueueSort();adjusted=false;
  }
  public void Apply(Control[] bars){
   if(!GodotObject.IsInstanceValid(panel)||screen.IsQueuedForDeletion())return;
   if(!AikunMiscPatchPack.Settings.Active("better_topbar")||!screen.IsInsideTree()||!screen.IsVisibleInTree()){Restore();return;}
   var inverse=screen.GetGlobalTransformWithCanvas().AffineInverse();float top=0;
   foreach(var bar in bars){
    if(!bar.IsVisibleInTree()||bar.GetViewport()!=screen.GetViewport())continue;
    var transform=bar.GetGlobalTransformWithCanvas();float bottom=(float)bar.GetMeta("adaptive_topbar_bottom",100f);
    top=Math.Max(top,Math.Max((inverse*(transform*new Vector2(0,bottom))).Y,(inverse*(transform*new Vector2(bar.Size.X,bottom))).Y));
   }
   if(top<=0){Restore();return;}
   const float margin=16;
   var area=new Rect2(margin,top+margin,Math.Max(1,screen.Size.X-margin*2),Math.Max(1,screen.Size.Y-top-margin*2));
   var size=panel.GetCombinedMinimumSize();if(size.X<=0||size.Y<=0)return;
   float scale=Math.Min(1,Math.Min(area.Size.X/size.X,area.Size.Y/size.Y));
   var local=area.Position+(area.Size-size*scale)*.5f;
   var parent=(Control)panel.GetParent();
   // 不改变显示层级，顶栏的地图／设置仍可使用；Godot 自动按缩放后的几何处理鼠标命中。
   panel.PivotOffset=Vector2.Zero;panel.Size=size;panel.Scale=new(scale,scale);
   panel.Position=parent.GetGlobalTransformWithCanvas().AffineInverse()*(screen.GetGlobalTransformWithCanvas()*local);
   adjusted=true;
  }
 }
}
