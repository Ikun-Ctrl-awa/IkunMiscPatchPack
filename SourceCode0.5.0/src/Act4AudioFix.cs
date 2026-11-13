using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace IkunMiscPatchPack;

public static class Act4AudioFix {
 public const string Key="act4_audio_crash";
 public static void Install(){
  var h=new Harmony("IkunMiscPatchPack.Act4Audio");
  try{
   h.Patch(AccessTools.Method(typeof(NRunMusicController),nameof(NRunMusicController.StopMusic)),postfix:new HarmonyMethod(typeof(Act4AudioFix),nameof(Stopped)));
   h.Patch(AccessTools.Method(typeof(NRunMusicController),nameof(NRunMusicController.UpdateTrack),Type.EmptyTypes),prefix:new HarmonyMethod(typeof(Act4AudioFix),nameof(Track)){priority=Priority.First});
   h.Patch(AccessTools.Method(typeof(NRunMusicController),nameof(NRunMusicController.TriggerCampfireGoingOut)),prefix:new HarmonyMethod(typeof(Act4AudioFix),nameof(GoingOut)){priority=Priority.First});
  }catch{h.UnpatchAll(h.Id);throw;}
 }
 static void Stopped(ref string? ____currentAmbience){
  // 原生 stop_ambience 已释放事件；同步清除控制器缓存，允许相同章节环境音重新创建。
  if(Settings.Active(Key))____currentAmbience=null;
 }
 static bool IsEndingRest(IRunState state)=>Settings.Active(Key)&&!NonInteractiveMode.IsActive
  &&state.Act.GetType().Assembly.GetName().Name=="Act4Heart"&&state.CurrentRoom is RestSiteRoom;
 static bool? NativeEventReady(Node proxy){
  // 只识别已审核的原生代理；其他模组替换代理脚本时保留其接口和行为。
  if(!proxy.GetPropertyList().Any(p=>p["name"].AsString()=="_ambienceEv"))return null;
  using var value=proxy.Get("_ambienceEv");
  return value.VariantType==Variant.Type.Object&&GodotObject.IsInstanceValid(value.AsGodotObject());
 }
 static bool Prepare(NRunMusicController controller,Node proxy,ref string? currentAmbience){
  if(!GodotObject.IsInstanceValid(proxy))return false;
  if(NativeEventReady(proxy)!=false)return true;
  // 兼容已处于旧缓存状态的局内对象及直接进入火堆的路径，先恢复真正的环境音事件。
  currentAmbience=null;controller.UpdateAmbience();
  return NativeEventReady(proxy)!=false;
 }
 static bool Track(NRunMusicController __instance,IRunState ____runState,Node ____proxy,ref string? ____currentAmbience){
  if(!IsEndingRest(____runState)||Prepare(__instance,____proxy,ref ____currentAmbience))return true;
  // 环境音无法创建时只略过失效事件的营火参数；正常地图进度与第四幕 OGG 音乐保留。
  if(GodotObject.IsInstanceValid(____proxy))____proxy.Call("update_global_parameter","Progress",3f);
  return false;
 }
 static bool GoingOut(NRunMusicController __instance,IRunState ____runState,Node ____proxy,ref string? ____currentAmbience)
  =>!IsEndingRest(____runState)||Prepare(__instance,____proxy,ref ____currentAmbience);
}
