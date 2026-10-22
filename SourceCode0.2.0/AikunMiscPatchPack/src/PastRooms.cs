using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace AikunMiscPatchPack;

public static class PastRooms {
 static readonly Harmony H=new("Aikun.MiscPatchPack.PastRooms");
 static readonly FieldInfo Rooms=AccessTools.Field(typeof(ActModel),"_rooms");
 public static bool Legacy(AbstractModel model)=>model.GetType().Assembly.GetName().Name=="ActsFromThePast";
 public static int Chance(string key)=>Settings.StartupChances.GetValueOrDefault(key,30);
 public static bool Roll(int percent,Rng rng)=>percent>=100||(percent>0&&rng.NextInt(100)<percent);
 static Rng RoomRng(RunState state,ActModel act,string type,int index)=>new(state.Rng.Seed,$"Aikun.past.{act.Index}.{type}.{index}");
 static ActModel? LegacyAct(ActModel act)=>ModelDb.Acts.FirstOrDefault(a=>a.Index==act.Index&&Legacy(a));
 static ActModel? NativeAct(ActModel act)=>!Legacy(act)?act:ModelDb.Acts.FirstOrDefault(a=>a.Index==act.Index&&a.GetType().Assembly==typeof(ActModel).Assembly&&a.IsDefault);
 static T[] Stable<T>(IEnumerable<T> models) where T:AbstractModel=>models.DistinctBy(m=>m.Id).OrderBy(m=>m.Id.ToString(),StringComparer.Ordinal).ToArray();
 static bool ActAllows(EventModel model,int index){
  var restriction=model.GetType().GetInterfaces().FirstOrDefault(t=>t.Name=="IActRestricted");
  return restriction?.GetProperty("AllowedActIndices")?.GetValue(model) is not IEnumerable<int> allowed||allowed.Contains(index+1);
 }
 public static void Install(){
  if(FusionQuestBlessingFix.Entry.Find("ActsFromThePast.Acts.ExordiumAct")==null)throw new MissingMemberException("往昔之章章节接口已改变");
  H.Patch(AccessTools.PropertyGetter(typeof(ModelDb),nameof(ModelDb.ActsByIndex)),postfix:new HarmonyMethod(typeof(PastRooms),nameof(FilterActs)){priority=Priority.Last});
  H.Patch(AccessTools.Method(typeof(ActModel),nameof(ActModel.PullNextEncounter)),postfix:new HarmonyMethod(typeof(PastRooms),nameof(NextEncounter)){priority=Priority.Last});
  H.Patch(AccessTools.Method(typeof(ActModel),nameof(ActModel.PullNextEvent)),prefix:new HarmonyMethod(typeof(PastRooms),nameof(NextEvent)){priority=Priority.First});
  H.Patch(AccessTools.Method(typeof(ActModel),nameof(ActModel.GenerateRooms)),postfix:new HarmonyMethod(typeof(PastRooms),nameof(Bosses)){priority=Priority.Last});
  H.Patch(AccessTools.Method(typeof(ActModel),nameof(ActModel.ApplyDiscoveryOrderModifications)),postfix:new HarmonyMethod(typeof(PastRooms),nameof(Bosses)){priority=Priority.Last});
  H.Patch(AccessTools.Method(typeof(ActModel),nameof(ActModel.SetSecondBossEncounter)),postfix:new HarmonyMethod(typeof(PastRooms),nameof(Bosses)){priority=Priority.Last});
 }
 public static void FilterActs(ref IReadOnlyList<IReadOnlyList<ActModel>> __result){
  // 只过滤新开局的章节候选；保留总目录，原模组资源和旧局的模型 ID 仍可读取。
  __result=__result.Select(slot=>(IReadOnlyList<ActModel>)slot.Where(a=>!Legacy(a)).ToArray()).ToArray();
 }
 public static EncounterModel SelectEncounter(ActModel act,RoomType type,EncounterModel original,RunState state){
  var past=LegacyAct(act);var native=NativeAct(act);if(past==null||native==null||act.Index is <0 or >2)return original;
  var rooms=(RoomSet)Rooms.GetValue(act)!;string key=type==RoomType.Elite?"elite":"monster";
  int index=type==RoomType.Elite?rooms.eliteEncountersVisited:rooms.normalEncountersVisited;
  var rng=RoomRng(state,act,key,index);
  var candidates=Stable((type==RoomType.Elite?past.AllEliteEncounters:original.IsWeak?past.AllWeakEncounters:past.AllRegularEncounters).Where(Legacy));
  bool choosePast=Roll(Chance(key),rng)&&candidates.Length>0;
  EncounterModel selected;
  if(choosePast)selected=rng.NextItem(candidates)!;
  else if(!Legacy(original))selected=original;
  else{
   var fallback=Stable(type==RoomType.Elite?native.AllEliteEncounters:original.IsWeak?native.AllWeakEncounters:native.AllRegularEncounters);
   selected=fallback.Length>0?rng.NextItem(fallback)!:original;
  }
  var list=type==RoomType.Elite?rooms.eliteEncounters:rooms.normalEncounters;
  if(list.Count>0){
   var previous=index>0?list[(index-1)%list.Count]:null;
   if(choosePast&&candidates.Length>1&&previous!=null&&previous.Id==selected.Id)selected=rng.NextItem(candidates.Where(c=>c.Id!=previous.Id))!;
   list[index%list.Count]=selected;
  }
  return selected;
 }
 public static void NextEncounter(ActModel __instance,RoomType roomType,ref EncounterModel __result){
  if(roomType is not (RoomType.Monster or RoomType.Elite))return;
  var state=RunManager.Instance.DebugOnlyGetState();if(state!=null)__result=SelectEncounter(__instance,roomType,__result,state);
 }
 public static bool NextEvent(ActModel __instance,RunState runState,ref EventModel __result){
  var past=LegacyAct(__instance);var native=NativeAct(__instance);if(past==null||native==null||__instance.Index is <0 or >2)return true;
  var rng=RoomRng(runState,__instance,"event",runState.TotalFloor);
  bool Allowed(EventModel e)=>e.IsAllowed(runState)&&!runState.VisitedEventIds.Contains(e.Id);
  var old=Stable(past.AllEvents.Concat(ModelDb.AllSharedEvents.Where(Legacy)).Where(e=>ActAllows(e,__instance.Index)&&Allowed(e)));
  // 沿用原版当前章节的事件与其他模组事件，不把一代共享事件混入原版分支。
  var rooms=(RoomSet)Rooms.GetValue(__instance)!;
  var normal=rooms.events.Where(e=>!Legacy(e)).ToArray();
  if(normal.Length==0)normal=Stable(native.AllEvents.Concat(ModelDb.AllSharedEvents).Where(e=>!Legacy(e)));
  EventModel? selected=null;
  if(Roll(Chance("event"),rng)&&old.Length>0)selected=rng.NextItem(old);
  else if(normal.Length>0){
   for(int n=0;n<normal.Length;n++){var candidate=normal[(rooms.eventsVisited+n)%normal.Length];if(Allowed(candidate)){selected=candidate;break;}}
   selected??=normal[rooms.eventsVisited%normal.Length];
  }
  if(selected==null&&old.Length>0)selected=rng.NextItem(old);
  if(selected==null)return true;
  __result=Hook.ModifyNextEvent(runState,selected);runState.AddVisitedEvent(__result);return false;
 }
 public static void Bosses(ActModel __instance){
  var state=RunManager.Instance.DebugOnlyGetState();if(state==null)return;
  ApplyBosses(__instance,state);
 }
 public static void ApplyBosses(ActModel act,RunState state){
  var past=LegacyAct(act);var native=NativeAct(act);if(past==null||native==null||act.Index is <0 or >2)return;
  var rooms=(RoomSet)Rooms.GetValue(act)!;
  var old=Stable(past.AllBossEncounters.Where(Legacy));var current=Stable(native.AllBossEncounters.Where(e=>!Legacy(e)));
  for(int slot=0;slot<(rooms.HasSecondBoss?2:1);slot++){
   var rng=RoomRng(state,act,"boss",slot);var pool=Roll(Chance("boss"),rng)&&old.Length>0?old:current;
   if(slot>0)pool=pool.Where(e=>e.Id!=rooms.Boss.Id).ToArray();if(pool.Length==0)continue;
   if(slot==0){if(pool==old||Legacy(rooms.Boss))rooms.Boss=rng.NextItem(pool)!;}
   else if(pool.Any(Legacy)||rooms.SecondBoss!=null&&Legacy(rooms.SecondBoss))rooms.SecondBoss=rng.NextItem(pool);
  }
 }
}
