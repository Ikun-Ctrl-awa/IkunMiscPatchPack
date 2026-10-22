using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static TaskCompletionSource? curseRemovalGate;
 static async Task WaitForCurseRemoval(Task original){await original;if(curseRemovalGate!=null)await curseRemovalGate.Task;}
 static void DelayCurseRemoval(ref Task __result){if(curseRemovalGate!=null)__result=WaitForCurseRemoval(__result);}
 static RelicModel Scroll(Player p){var r=ModelDb.GetById<RelicModel>(ModelDb.GetId(Find("YuWanCard.Relics.ThousandCurseScroll"))).ToMutable();p.AddRelicInternal(r,silent:true);return r;}
 static (RunState State,Player Owner,CombatRoom Room) ScrollRoom(string seed,int ascension=20,int act=0,bool scroll=true,bool boss=true,Player? other=null){
  var p=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);
  var s=RunState.CreateForTest(other==null?[p]:[p,other],ascensionLevel:ascension,seed:seed);
  AccessTools.Property(typeof(RunState),nameof(RunState.CurrentActIndex)).SetValue(s,act);
  RunManager.Instance.SetUpTest(s,new NetSingleplayerGameService());
  if(scroll)Scroll(p);
  var encounter=(boss?s.Act.AllBossEncounters:s.Act.AllRegularEncounters).First(e=>e.GetType().Assembly==typeof(CardModel).Assembly).ToMutable();
  var room=new CombatRoom(encounter,s);s.PushRoom(room);return(s,p,room);
 }
 static void CleanupScrollRoom(){if(RunManager.Instance.DebugOnlyGetState()!=null)RunManager.Instance.CleanUp();}
 static async Task CurseScrollChecks(){
  if(Minimal){Check("curse scroll safely skips absent optional mods",Pack.Settings.Status(Pack.Options.All.Single(o=>o.Key=="curse_scroll")).Contains("未装载相关模组"));return;}
  var start=passes.Count;var oldTest=TestMode.IsOn;var h=new Harmony("Aikun020Probe.CurseScroll.Delay");
  var listeners=AccessTools.Field(typeof(CombatStateTracker),"CombatStateChanged");var frontend=listeners.GetValue(CombatManager.Instance.StateTracker);
  try{
   listeners.SetValue(CombatManager.Instance.StateTracker,null);TestMode.IsOn=true;
   h.Patch(AccessTools.Method(typeof(Hook),nameof(Hook.BeforeCardRemoved)),postfix:new HarmonyMethod(typeof(Entry),nameof(DelayCurseRemoval)));
   var type=Find("BetterAscension.AscensionCombatStart");var pool=(Type[])AccessTools.Field(type,"CursePool").GetValue(null)!;
   var pick=AccessTools.Method(Find("BetterAscension.DeterministicPick"),"Index");
   var seedMethod=AccessTools.Method(type,"RunSeed");
   var seeds=new Dictionary<int,string>();
   for(int i=0;i<1000&&seeds.Count<pool.Length;i++){
    string seed="SCROLL-"+i;var dummy=RunState.CreateForTest(seed:seed);
    int hash=(int)seedMethod.Invoke(null,[dummy])!;
    int index=(int)pick.Invoke(null,[pool.Length,new[]{hash,0,-1,1,1}])!;seeds.TryAdd(index,seed);
   }
   Check("curse scroll finds deterministic seeds for complete A20 pool",seeds.Count==17);
   foreach(var pair in seeds.OrderBy(x=>x.Key)){
    var baseline=ScrollRoom(pair.Value);Pack.Settings.Startup["curse_scroll"]=false;
    int before=baseline.Owner.Deck.Cards.Count;
    await Hook.BeforeRoomEntered(baseline.State,baseline.Room);
    var curse=baseline.Owner.Deck.Cards.Last();
    Check("curse scroll disabled reproduces A20 curse "+pool[pair.Key].Name,baseline.Owner.Deck.Cards.Count==before+1&&curse.GetType()==pool[pair.Key]);
    var rng=JsonSerializer.Serialize(baseline.State.Rng.ToSerializable(),new JsonSerializerOptions{IncludeFields=true});
    CleanupScrollRoom();
    Pack.Settings.Startup["curse_scroll"]=true;var fixedRoom=ScrollRoom(pair.Value);
    before=fixedRoom.Owner.Deck.Cards.Count;
    var old=fixedRoom.State.CreateCard<Injury>(fixedRoom.Owner);old.FloorAddedToDeck=-99;fixedRoom.Owner.Deck.AddInternal(old,silent:true);
    var pending=Hook.BeforeRoomEntered(fixedRoom.State,fixedRoom.Room);
    await pending;
    Check("curse scroll removes new A20 curse before entry "+pool[pair.Key].Name,fixedRoom.Owner.Deck.Cards.Count==before+1&&fixedRoom.Owner.Deck.Cards.Last()==old);
    Check("curse scroll retains old curse "+pool[pair.Key].Name,old.Pile==fixedRoom.Owner.Deck);
    Check("curse scroll preserves original RNG "+pool[pair.Key].Name,rng==JsonSerializer.Serialize(fixedRoom.State.Rng.ToSerializable(),new JsonSerializerOptions{IncludeFields=true}));
    await Hook.BeforeRoomEntered(fixedRoom.State,fixedRoom.Room);
    Check("curse scroll repeated room notification adds no curse "+pool[pair.Key].Name,fixedRoom.Owner.Deck.Cards.Count==before+1);
    CleanupScrollRoom();
   }
   for(int act=1;act<3;act++){
    var x=ScrollRoom("SCROLL-ACT-"+act,act:act);int before=x.Owner.Deck.Cards.Count;
    await Hook.BeforeRoomEntered(x.State,x.Room);
    Check("curse scroll works in act "+(act+1),x.Owner.Deck.Cards.Count==before);CleanupScrollRoom();
   }
   foreach(var variant in new[]{"no relic","ascension 19","non-boss","removed relic"}){
    var x=ScrollRoom("SCROLL-NEG-"+variant,ascension:variant=="ascension 19"?19:20,scroll:variant!="no relic",boss:variant!="non-boss");
    if(variant=="removed relic")x.Owner.RemoveRelicInternal(x.Owner.Relics.Last(),silent:true);
    int before=x.Owner.Deck.Cards.Count;await Hook.BeforeRoomEntered(x.State,x.Room);
    int expected=variant is "no relic" or "removed relic"?1:0;
    Check("curse scroll negative case "+variant,x.Owner.Deck.Cards.Count==before+expected);CleanupScrollRoom();
   }
   var second=Player.CreateForNewRun<Defect>(UnlockState.all,2);var multi=ScrollRoom("SCROLL-TWO-PLAYERS",other:second);
   int ownerCount=multi.Owner.Deck.Cards.Count,otherCount=second.Deck.Cards.Count;
   await Hook.BeforeRoomEntered(multi.State,multi.Room);
   Check("curse scroll only consumes holder curse",multi.Owner.Deck.Cards.Count==ownerCount&&second.Deck.Cards.Count==otherCount+1);CleanupScrollRoom();
   var delayed=ScrollRoom("SCROLL-DELAY");int delayedCount=delayed.Owner.Deck.Cards.Count;
   curseRemovalGate=new();var entry=Hook.BeforeRoomEntered(delayed.State,delayed.Room);
   Check("curse scroll room entry awaits asynchronous native removal",!entry.IsCompleted&&delayed.Owner.Deck.Cards.Count==delayedCount+1);
   curseRemovalGate.SetResult();await entry;curseRemovalGate=null;
   Check("curse scroll asynchronous removal finishes before entry completes",delayed.Owner.Deck.Cards.Count==delayedCount);
   var ordinary=delayed.State.CreateCard<Injury>(delayed.Owner);
   await CardPileCmd.Add(ordinary,PileType.Deck,skipVisuals:true);
   Check("curse scroll ordinary gain still removed by original relic",!delayed.Owner.Deck.Cards.Contains(ordinary)&&delayed.Owner.Deck.Cards.Count==delayedCount);
   var direct=delayed.State.CreateCard<Injury>(delayed.Owner);delayed.Owner.Deck.AddInternal(direct,silent:true);
   Check("curse scroll unrelated internal insertion is unchanged",delayed.Owner.Deck.Cards.Contains(direct));
   var saved=delayed.Owner.ToSerializable();var restored=Player.FromSerializable(saved);
   Check("curse scroll existing player serialization preserved",restored.Deck.Cards.Select(c=>c.Id).SequenceEqual(delayed.Owner.Deck.Cards.Select(c=>c.Id))&&restored.Relics.Any(r=>r.GetType().Name=="ThousandCurseScroll"));
   CleanupScrollRoom();
  }catch(Exception e){GD.Print("[Aikun020Probe] curse scroll original failure "+e);throw;}finally{
   curseRemovalGate?.TrySetResult();curseRemovalGate=null;
   try{CleanupScrollRoom();}finally{h.UnpatchAll(h.Id);TestMode.IsOn=oldTest;Pack.Settings.Startup["curse_scroll"]=true;listeners.SetValue(CombatManager.Instance.StateTracker,frontend);}
   File.WriteAllText(Output+"/curse-scroll-checks.json",JsonSerializer.Serialize(new{Checks=passes.Skip(start).ToArray(),Passed=passes.Count-start},new JsonSerializerOptions{WriteIndented=true}));
  }
 }
}
