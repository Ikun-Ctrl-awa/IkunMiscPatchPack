using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace AdaptiveTopBar;

public static class MonsterIntents {
 static readonly ConditionalWeakTable<NCreature,Adjustment> states=new();
 static readonly List<WeakReference<NCreature>> creatures=new();
 static readonly FieldInfo Frames=AccessTools.Field(typeof(NIntent),"_animationFrames");
 static readonly FieldInfo Animation=AccessTools.Field(typeof(NIntent),"_animationName");
 static readonly ConditionalWeakTable<NIntent,AnimationBounds> animationBounds=new();
 sealed class AnimationBounds {
  public string? Name;public int Count=-1;public bool Centered;public Vector2 Offset;public Rect2 Rect;
 }
 sealed class Adjustment {
  public Vector2 Before,After;public bool Applied;
  public void Restore(Control group){
   // 原生体型变化或其他模组重新定位后，以新位置为准，不再扣除旧偏移。
   if(Applied&&group.Position.DistanceTo(After)<.01f)group.Position=Before;
   Applied=false;
  }
 }
 public static void Install(Harmony h)=>h.Patch(AccessTools.DeclaredMethod(typeof(NCreature),nameof(NCreature._Ready)),postfix:new HarmonyMethod(typeof(MonsterIntents),nameof(Track)));
 static void Track(NCreature __instance){
  if(__instance.Entity.Monster==null||__instance.Entity.Side!=CombatSide.Enemy||states.TryGetValue(__instance,out _))return;
  states.Add(__instance,new());creatures.Add(new(__instance));
 }
 static Rect2 Rect(Transform2D t,Rect2 r){
  var a=t*r.Position;var b=t*new Vector2(r.End.X,r.Position.Y);var c=t*r.End;var d=t*new Vector2(r.Position.X,r.End.Y);
  var min=a.Min(b).Min(c).Min(d);return new(min,a.Max(b).Max(c).Max(d)-min);
 }
 static Vector2 Vector(Transform2D t,Vector2 v)=>t*v-t*Vector2.Zero;
 static Rect2 SpriteBounds(NIntent intent,Sprite2D sprite){
  var cache=animationBounds.GetOrCreateValue(intent);var name=(string?)Animation.GetValue(intent);var frames=(List<Texture2D>)Frames.GetValue(intent)!;
  if(cache.Count==frames.Count&&cache.Name==name&&cache.Offset==sprite.Offset&&cache.Centered==sprite.Centered)return cache.Rect;
  var rect=sprite.GetRect();foreach(var texture in frames){var size=texture.GetSize();rect=rect.Merge(new(sprite.Offset-(sprite.Centered?size*.5f:Vector2.Zero),size));}
  cache.Name=name;cache.Count=frames.Count;cache.Offset=sprite.Offset;cache.Centered=sprite.Centered;return cache.Rect=rect;
 }
 // 使用完整浮动范围而非某一帧图标的位置，让意图保留原生上下浮动且不挤压、抖动。
 static bool Bounds(Control group,out Rect2 result){
  result=default;bool found=false;
  foreach(var intent in group.GetChildren().OfType<NIntent>().Where(n=>n.IsVisibleInTree()&&!n.IsQueuedForDeletion())){
   var rect=Rect(intent.GetGlobalTransformWithCanvas(),new(Vector2.Zero,intent.Size));
   var holder=intent.GetNode<Control>("%IntentHolder");var sprite=intent.GetNode<Sprite2D>("%Intent");var label=intent.GetNode<Control>("%Value");
   var parentTransform=holder.GetGlobalTransformWithCanvas()*holder.GetTransform().AffineInverse();
   var bob=Vector(parentTransform,holder.Position);
   var spriteRect=SpriteBounds(intent,sprite);
   var visual=Rect(sprite.GetGlobalTransformWithCanvas(),spriteRect).Merge(Rect(label.GetGlobalTransformWithCanvas(),new(Vector2.Zero,label.Size)));
   visual.Position-=bob;
   var upper=visual;upper.Position+=Vector(parentTransform,new(0,-18));
   var lower=visual;lower.Position+=Vector(parentTransform,new(0,2));
   rect=rect.Merge(upper).Merge(lower);
   result=found?result.Merge(rect):rect;found=true;
  }
  return found;
 }
 public static void Paint(IReadOnlyList<Control> bars){
  for(int i=creatures.Count-1;i>=0;i--){
   if(!creatures[i].TryGetTarget(out var creature)||!GodotObject.IsInstanceValid(creature)){creatures.RemoveAt(i);continue;}
   var group=creature.IntentContainer;if(!GodotObject.IsInstanceValid(group))continue;
   var state=states.GetValue(creature,_=>throw new InvalidOperationException());state.Restore(group);
   if(!AikunMiscPatchPack.Settings.Active("better_topbar")||!creature.IsInsideTree()||!creature.IsVisibleInTree()||creature.IsQueuedForDeletion()||!Bounds(group,out var bounds))continue;
   float dy=0;
   foreach(var bar in bars){
    if(!bar.IsVisibleInTree()||bar.Modulate.A<=.001f||bar.GetViewport()!=creature.GetViewport())continue;
    var occupied=Rect(bar.GetGlobalTransformWithCanvas(),new(Vector2.Zero,new(bar.Size.X,(float)bar.GetMeta("adaptive_topbar_bottom",100f))));
    if(occupied.End.Y<=0||bounds.End.X<=occupied.Position.X||bounds.Position.X>=occupied.End.X)continue;
    dy=Math.Max(dy,occupied.End.Y+12-bounds.Position.Y);
   }
   if(dy<=.01f)continue;
   state.Before=group.Position;
   var parent=group.GetGlobalTransformWithCanvas()*group.GetTransform().AffineInverse();
   group.Position+=Vector(parent.AffineInverse(),new(0,dy));state.After=group.Position;state.Applied=true;
  }
 }
}
