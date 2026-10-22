using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace AdaptiveTopBar;

// 模组可能在原生 SetAlignment 校正之后再次定位提示，因此在最终绘制前检查实际范围。
public static class TopBarHoverTips {
 static readonly FieldInfo Active=AccessTools.Field(typeof(NHoverTipSet),"_activeHoverTips");
 static readonly ConditionalWeakTable<NHoverTipSet,Adjustment> adjusted=new();
 sealed class Adjustment {
  public Vector2 Before,After;
  public bool Applied;
  public void Restore(Control tip){
   if(!Applied)return;
   // 跟随图标或外部模组已经重新定位时，以新位置为准，不减去上一帧的偏移。
   if(tip.Position.DistanceTo(After)<.01f)tip.Position=Before;
   Applied=false;
  }
 }
 public static void Paint(IReadOnlyList<Control> bars){
  if(Active.GetValue(null) is not Dictionary<Control,NHoverTipSet> tips||tips.Count==0)return;
  foreach(var (owner,tip) in tips.ToArray()){
   if(!GodotObject.IsInstanceValid(tip)||!tip.IsInsideTree()||tip.IsQueuedForDeletion())continue;
   var state=adjusted.GetOrCreateValue(tip);state.Restore(tip);
   if(!AikunMiscPatchPack.Settings.Active("better_topbar")||!GodotObject.IsInstanceValid(owner)||!tip.IsVisibleInTree())continue;
   var bar=bars.FirstOrDefault(b=>b.IsVisibleInTree()&&b.GetViewport()==owner.GetViewport()&&(b==owner||b.IsAncestorOf(owner)));
   if(bar==null||!TryBounds(tip,out var bounds))continue;
   const float margin=16,gap=16;
   var viewport=tip.GetViewportRect();var safe=viewport.Grow(-margin);
   Vector2 delta=Vector2.Zero;
   if(bounds.End.X>safe.End.X){
    var mouse=tip.GetViewport().GetMousePosition();
    var ownerBounds=CanvasRect(owner);
    // 键盘聚焦时鼠标可能在别处；鼠标位于该图标时按鼠标，否则按聚焦图标放到左边。
    float anchor=ownerBounds.Grow(16).HasPoint(mouse)?mouse.X:ownerBounds.Position.X;
    delta.X=anchor-gap-bounds.End.X;
   }
   var moved=bounds.Position+delta;
   moved.X=Math.Clamp(moved.X,safe.Position.X,Math.Max(safe.Position.X,safe.End.X-bounds.Size.X));
   moved.Y=Math.Clamp(moved.Y,safe.Position.Y,Math.Max(safe.Position.Y,safe.End.Y-bounds.Size.Y));
   delta=moved-bounds.Position;if(delta.LengthSquared()<.0001f)continue;
   state.Before=tip.Position;
   // 使用画布变换处理窗口缩放及 CanvasLayer，整体平移文本、附加说明和卡牌预览。
   var transform=tip.GetGlobalTransformWithCanvas();
   var parentTransform=transform*tip.GetTransform().AffineInverse();
   var inverse=parentTransform.AffineInverse();
   tip.Position+=inverse*delta-inverse*Vector2.Zero;
   state.After=tip.Position;state.Applied=true;
  }
 }
 static Rect2 CanvasRect(Control control){
  var t=control.GetGlobalTransformWithCanvas();var size=control.Size;
  var a=t*Vector2.Zero;var b=t*new Vector2(size.X,0);var c=t*size;var d=t*new Vector2(0,size.Y);
  var min=a.Min(b).Min(c).Min(d);var max=a.Max(b).Max(c).Max(d);return new(min,max-min);
 }
 public static bool TryBounds(NHoverTipSet tip,out Rect2 bounds){
  bounds=default;bool any=false;
  foreach(var container in tip.GetChildren().OfType<Control>()){
   if(!container.IsVisibleInTree()||container.GetChildCount()==0)continue;
   foreach(var control in container.GetChildren().OfType<Control>().Prepend(container)){
    if(!control.IsVisibleInTree()||control.Size.X<=0||control.Size.Y<=0)continue;
    var rect=CanvasRect(control);bounds=any?bounds.Merge(rect):rect;any=true;
   }
  }
  return any;
 }
}
