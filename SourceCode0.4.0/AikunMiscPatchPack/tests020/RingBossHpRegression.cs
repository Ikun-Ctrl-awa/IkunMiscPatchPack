using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static object? HpPrimary(Player p)=>AccessTools.Method(Find("HextechRunes.HextechMaxHpScaling"),"GetPrimary").Invoke(null,[p]);
 static int HpBase(Player p)=>(int)AccessTools.Property(Find("HextechRunes.IHextechMaxHpBaseHolder"),"BaseMaxHp").GetValue(HpPrimary(p))!;
 static void HpBase(Player p,int value)=>AccessTools.Property(Find("HextechRunes.IHextechMaxHpBaseHolder"),"BaseMaxHp").SetValue(HpPrimary(p),value);
 static decimal HpScale(Player p)=>(decimal)AccessTools.Method(Find("HextechRunes.HextechMaxHpScaling"),"GetScale").Invoke(null,[p])!;
 static RelicModel AddRing(Player p){var r=ModelDb.GetById<RelicModel>(ModelDb.GetId(Find("YuWanCard.Relics.RingOfSevenCurses"))).ToMutable();p.AddRelicInternal(r,silent:true);return r;}
 static RelicModel AddHpForge(Player p,string name,int count){var r=ModelDb.GetById<RelicModel>(ModelDb.GetId(Find("HextechRunes."+name))).ToMutable();AccessTools.Property(r.GetType(),"SavedStackCount").SetValue(r,count);p.AddRelicInternal(r,silent:true);return r;}
 static CombatRoom HpRoom(Player p,string encounter){
  var state=RunState.CreateForTest([p],seed:"BOSS-HP-"+encounter);RunManager.Instance.SetUpTest(state,new NetSingleplayerGameService());
  var room=new CombatRoom(ModelDb.GetById<EncounterModel>(new("ENCOUNTER",encounter)).ToMutable(),state);state.PushRoom(room);room.CombatState.AddPlayer(p);
  CombatManager.Instance.SetUpCombat(room.CombatState);
  // 战后回调仍在真实战斗状态中运行；死亡可设置 PendingLoss，但不启动真实 UI／存档流程。
  var turn=AccessTools.Field(typeof(CombatManager),"_turnState").GetValue(CombatManager.Instance)!;AccessTools.Property(turn.GetType(),"IsInProgress").SetValue(turn,true);return room;
 }
 static bool HpPendingLoss(){var turn=AccessTools.Field(typeof(CombatManager),"_turnState").GetValue(CombatManager.Instance);return turn!=null&&AccessTools.Property(turn.GetType(),"PendingLoss").GetValue(turn)!=null;}
 static void CleanupHp(){if(RunManager.Instance.DebugOnlyGetState()!=null){CombatManager.Instance.Reset(false);RunManager.Instance.CleanUp();}}
 static async Task RingBossHpChecks(){
  if(Minimal){Check("ring boss HP safely skips absent optional mods",Pack.Settings.Status(Pack.Options.All.Single(o=>o.Key=="ring_boss_hp")).Contains("未装载相关模组"));return;}
  var first=passes.Count;var oldTest=TestMode.IsOn;var evidence=new List<object>();
  var listeners=AccessTools.Field(typeof(CombatStateTracker),"CombatStateChanged");var frontend=listeners.GetValue(CombatManager.Instance.StateTracker);
  try{
   TestMode.IsOn=true;listeners.SetValue(CombatManager.Instance.StateTracker,null);
   foreach(bool enabled in new[]{false,true})foreach(bool aeonglass in new[]{true,false}){
    Pack.Settings.Startup["ring_boss_hp"]=enabled;
    var p=Player.CreateForNewRun<Silent>(UnlockState.all,1);var ring=AddRing(p);
    var forge=AddHpForge(p,"SilverHpForge",13);AddHpForge(p,"PrismaticLifeForge",3);AddHpForge(p,"GoldHpForge",1);
    var tail=(LizardTail)ModelDb.Relic<LizardTail>().ToMutable();p.AddRelicInternal(tail,silent:true);AccessTools.Property(typeof(LizardTail),"WasUsed").SetValue(tail,!aeonglass);
    var room=HpRoom(p,aeonglass?"AEONGLASS_BOSS":"YUWANCARD-IGNIS_BOSS");
    int basis=aeonglass?416:108;HpBase(p,basis);int max=aeonglass?1258:326;p.Creature.SetMaxHpInternal(max);p.Creature.SetCurrentHpInternal(max);
    var scale=HpScale(p);string label=(aeonglass?"Aeonglass":"Ignis")+" enabled="+enabled;
    Check("ring boss HP incident forge multiplier "+label,scale==3.025m);
    Check("ring boss HP initial visible equals saved base scale "+label,(int)Math.Floor(basis*scale)==max);
    await ring.AfterCombatVictory(room);
    int hpAfterPenalty=p.Creature.CurrentHp;
    int expectedBase=enabled?basis-basis/4:Math.Max(1,basis-max/4);int expectedMax=(int)Math.Floor(expectedBase*scale);
    Check("ring boss HP exact native base "+label,HpBase(p)==expectedBase);
    Check("ring boss HP exact final visible maximum "+label,p.Creature.MaxHp==expectedMax);
    if(!enabled){
     Check("ring boss HP reproduces reported maximum "+label,p.Creature.MaxHp==(aeonglass?308:81));
     Check("ring boss HP reproduces tail use or post-boss death "+label,aeonglass?tail.WasUsed&&!p.Creature.IsDead:p.Creature.IsDead&&HpPendingLoss());
    }else{
     Check("ring boss HP penalty no longer kills or consumes tail "+label,!p.Creature.IsDead&&!HpPendingLoss()&&tail.WasUsed==!aeonglass);
     var saved=p.ToSerializable();var loaded=Player.FromSerializable(saved);
     Check("ring boss HP save keeps corrected base and visible maximum "+label,HpBase(loaded)==expectedBase&&loaded.Creature.MaxHp==expectedMax);
     var reapply=AccessTools.Method(Find("HextechRunes.HextechMaxHpScaling"),"ReapplyScale");
     await (Task)reapply.Invoke(null,reapply.GetParameters().Select((parameter,index)=>index==0?(object)p:parameter.DefaultValue).ToArray())!;
     Check("ring boss HP reapplying forge scale does not undo penalty "+label,p.Creature.MaxHp==expectedMax);
     await CreatureCmd.GainMaxHp(p.Creature,6);
     Check("ring boss HP ordinary base gain remains scaled "+label,HpBase(p)==expectedBase+6&&p.Creature.MaxHp==(int)Math.Floor((expectedBase+6)*scale));
     await CreatureCmd.LoseMaxHp(new ThrowingPlayerChoiceContext(),p.Creature,6,false);
     Check("ring boss HP ordinary base loss remains scaled "+label,HpBase(p)==expectedBase&&p.Creature.MaxHp==expectedMax);
    }
    evidence.Add(new{label,scale,basis,max,finalBase=HpBase(p),finalMax=p.Creature.MaxHp,currentHpImmediatelyAfterPenalty=hpAfterPenalty,currentHpAfterAdditionalDeltaChecks=p.Creature.CurrentHp,dead=p.Creature.IsDead,tailUsed=tail.WasUsed,pendingLoss=HpPendingLoss()});CleanupHp();
   }
   Pack.Settings.Startup["ring_boss_hp"]=true;
   foreach(int stacks in new[]{1,4,13})foreach(int basis in new[]{1,3,4,101,400}){
    var p=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var ring=AddRing(p);AddHpForge(p,"SilverHpForge",stacks);
    var room=HpRoom(p,"AEONGLASS_BOSS");HpBase(p,basis);var scale=HpScale(p);p.Creature.SetMaxHpInternal((int)Math.Floor(basis*scale));p.Creature.SetCurrentHpInternal(p.Creature.MaxHp);
    await ring.AfterCombatVictory(room);int expected=Math.Max(1,basis-basis/4);
    Check("ring boss HP scaled rounding and minimum stacks="+stacks+" base="+basis,HpBase(p)==expected&&p.Creature.MaxHp==(int)Math.Floor(expected*scale)&&!p.Creature.IsDead);CleanupHp();
   }
   var plain=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var ordinaryRing=AddRing(plain);var plainRoom=HpRoom(plain,"AEONGLASS_BOSS");plain.Creature.SetMaxHpInternal(1258);plain.Creature.SetCurrentHpInternal(1258);await ordinaryRing.AfterCombatVictory(plainRoom);
   Check("ring boss HP without forge retains original 25 percent formula",plain.Creature.MaxHp==944&&!plain.Creature.IsDead);CleanupHp();
   var normal=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var normalRing=AddRing(normal);AddHpForge(normal,"SilverHpForge",13);var normalRoom=HpRoom(normal,"AEONGLASS_BOSS");HpBase(normal,400);normal.Creature.SetMaxHpInternal(790);normal.Creature.SetCurrentHpInternal(790);
   var other=new CombatRoom(normal.RunState.Act.AllRegularEncounters.First().ToMutable(),normal.RunState);await normalRing.AfterCombatVictory(other);
   Check("ring boss HP non-boss victory unchanged",HpBase(normal)==400&&normal.Creature.MaxHp==790);CleanupHp();
   var source=System.Environment.GetEnvironmentVariable("AIKUN_BOSS_HP_INCIDENT_SAVE");
   if(!string.IsNullOrWhiteSpace(source)&&File.Exists(source)){
   using var incident=JsonDocument.Parse(File.ReadAllText(source));var relics=incident.RootElement.GetProperty("players")[0].GetProperty("relics");
   var copied=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);
   foreach(var id in new[]{"SILVER_HP_FORGE","PRISMATIC_LIFE_FORGE","GOLD_HP_FORGE"}){
    var json=relics.EnumerateArray().Single(r=>r.GetProperty("id").GetString()=="RELIC."+id).GetRawText();
    var saved=JsonSerializer.Deserialize<SerializableRelic>(json,JsonSerializationUtility.Options)!;copied.AddRelicInternal(RelicModel.FromSerializable(saved),silent:true);
   }
   Check("ring boss HP actual saved forges restore 3.025 scale and base 27",HpScale(copied)==3.025m&&HpBase(copied)==27);
   }
   File.WriteAllText(Output+"/ring-boss-hp-replay.json",JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
  }catch(Exception e){GD.Print("[Aikun020Probe] ring boss HP original failure "+e);throw;}finally{
   try{CleanupHp();}finally{TestMode.IsOn=oldTest;listeners.SetValue(CombatManager.Instance.StateTracker,frontend);Pack.Settings.Startup["ring_boss_hp"]=true;}
   File.WriteAllText(Output+"/ring-boss-hp-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Checks=passes.Skip(first).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
  }
 }
}
