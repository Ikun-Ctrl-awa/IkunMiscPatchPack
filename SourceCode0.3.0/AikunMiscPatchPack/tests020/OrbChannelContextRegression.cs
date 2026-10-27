using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static async Task OrbChannelContextChecks(){
  int first=passes.Count;var evidence=new List<object>();
  if(Minimal){Check("orb context missing Anthony safely skipped",Pack.Settings.Status(Pack.Options.All.Single(o=>o.Key=="orb_channel_context")).Contains("未装载相关模组"));return;}
  Check("orb context patch installed",Pack.RuntimeLoader.States.GetValueOrDefault("orb_channel_context")=="已应用。");
  var context=Find("AutoAnthony.ChaosOrbChannelContext");var push=AccessTools.Method(context,"Push");var capture=AccessTools.Method(context,"Capture");
  var pop=AccessTools.Method(Find("AutoAnthony.Patches.ChaosOrbChannelContextPatch"),"Postfix");
  Pack.Settings.Startup["orb_channel_context"]=false;
  bool broken=false;try{pop.Invoke(null,[null]);}catch(System.Reflection.TargetInvocationException e){broken=e.InnerException is NullReferenceException;}
  Check("orb context disabled null state reproduces failure",broken);
  Pack.Settings.Startup["orb_channel_context"]=true;
  pop.Invoke(null,[null]);Check("orb context null state safely ignored",capture.Invoke(null,null)==null);
  var parent=push.Invoke(null,[true]);var child=push.Invoke(null,[false]);pop.Invoke(null,[null]);
  Check("orb context null cleanup preserves active parent",ReferenceEquals(capture.Invoke(null,null),child));
  pop.Invoke(null,[child]);Check("orb context real child pops to parent",ReferenceEquals(capture.Invoke(null,null),parent));
  pop.Invoke(null,[parent]);Check("orb context real parent cleanup retained",capture.Invoke(null,null)==null);
  foreach(bool enabled in new[]{false,true}){
   bool oldMode=TestMode.IsOn;ulong? oldLocal=LocalContext.NetId;var listeners=AccessTools.Field(typeof(CombatStateTracker),"CombatStateChanged");var frontend=listeners.GetValue(CombatManager.Instance.StateTracker);
   try{
    listeners.SetValue(CombatManager.Instance.StateTracker,null);TestMode.IsOn=true;Pack.Settings.Startup["orb_channel_context"]=enabled;
    var p=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var state=RunState.CreateForTest([p],seed:"ORB-CONTEXT");RunManager.Instance.SetUpTest(state,new NetSingleplayerGameService());
    var combat=new CombatState(runState:state);combat.AddPlayer(p);var enemy=combat.CreateCreature(ModelDb.Monster<CalcifiedCultist>().ToMutable(),CombatSide.Enemy,"orb-target");combat.AddCreature(enemy);enemy.SetMaxHpInternal(10000);enemy.SetCurrentHpInternal(10000);
    CombatManager.Instance.SetUpCombat(combat);var turn=AccessTools.Field(typeof(CombatManager),"_turnState").GetValue(CombatManager.Instance)!;AccessTools.Property(turn.GetType(),"IsInProgress").SetValue(turn,true);
    var power=ModelDb.GetById<PowerModel>(ModelDb.GetId(Find("FlawTheFunction.Core.Models.Powers.FlawElectrifyPower"))).ToMutable();power.ApplyInternal(p.Creature,1);
    Exception? error=null;try{await power.AfterPlayerTurnStart(new ThrowingPlayerChoiceContext(),p).WaitAsync(TimeSpan.FromSeconds(5));}catch(Exception e){error=e;}
    await Wait(.3);evidence.Add(new{Enabled=enabled,Error=error?.ToString(),Orbs=p.PlayerCombatState!.OrbQueue.Orbs.Count,Capacity=p.PlayerCombatState.OrbQueue.Capacity,Hp=enemy.CurrentHp});
    GD.Print("[Aikun020Probe] orb context evidence "+JsonSerializer.Serialize(evidence.Last()));
    if(!enabled){Check("orb context actual Electrify reproduces exception",error is NullReferenceException);continue;}
    if(error!=null)throw error;
    Check("orb context actual Electrify completes",p.PlayerCombatState.OrbQueue.Orbs.Count==1);
    Check("orb context movement passive still deals three damage",enemy.CurrentHp==9997);
    for(int i=0;i<3;i++)await power.AfterPlayerTurnStart(new ThrowingPlayerChoiceContext(),p).WaitAsync(TimeSpan.FromSeconds(5));
    Check("orb context repeat turns complete without duplicates",p.PlayerCombatState.OrbQueue.Orbs.Count==1&&p.PlayerCombatState.OrbQueue.Capacity==1);
    Check("orb context repeated evoke and movement damage retained",enemy.CurrentHp==9979);
    Check("orb context history records exactly four channels",CombatManager.Instance.History.Entries.OfType<OrbChanneledEntry>().Count()==4);
    Check("orb context no leaked Anthony state",capture.Invoke(null,null)==null);
    // 原生出牌包装器要求本地玩家身份；测试夹具显式设置并在退出时恢复。
    LocalContext.NetId=p.NetId;
    var armaments=combat.CreateCard(ModelDb.Card<Armaments>(),p);armaments.UpgradeInternal();armaments.FinalizeUpgradeInternal();var strike=combat.CreateCard(ModelDb.Card<StrikeIronclad>(),p);
    await CardPileCmd.Add(armaments,PileType.Hand,skipVisuals:true);await CardPileCmd.Add(strike,PileType.Hand,skipVisuals:true);
    await armaments.OnPlayWrapper(new ThrowingPlayerChoiceContext(),null,false,default,skipCardPileVisuals:true).WaitAsync(TimeSpan.FromSeconds(5));
    Check("orb context Armaments plus resolves and upgrades hand",strike.IsUpgraded&&p.Creature.Block>0);
    int before=enemy.CurrentHp;await strike.OnPlayWrapper(new ThrowingPlayerChoiceContext(),enemy,false,default,skipCardPileVisuals:true).WaitAsync(TimeSpan.FromSeconds(5));
    Check("orb context subsequent attack resolves",enemy.CurrentHp<before);
    p.PlayerCombatState.OrbQueue.Clear();p.PlayerCombatState.OrbQueue.AddCapacity(3);
    foreach(bool active in new[]{false,true}){
     Pack.Settings.Startup["orb_channel_context"]=active;p.PlayerCombatState.OrbQueue.Clear();p.PlayerCombatState.OrbQueue.AddCapacity(3);
     await OrbCmd.Channel<LightningOrb>(new ThrowingPlayerChoiceContext(),p);
     Check("orb context normal native channel still works enabled="+active,p.PlayerCombatState.OrbQueue.Orbs.Single() is LightningOrb&&capture.Invoke(null,null)==null);
    }
   }finally{
    Pack.Settings.Startup["orb_channel_context"]=true;
    try{if(RunManager.Instance.DebugOnlyGetState()!=null){CombatManager.Instance.Reset(false);RunManager.Instance.CleanUp();}}
    finally{LocalContext.NetId=oldLocal;TestMode.IsOn=oldMode;listeners.SetValue(CombatManager.Instance.StateTracker,frontend);}
   }
  }
  File.WriteAllText(Output+"/orb-channel-context-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Failed=0,Checks=passes.Skip(first),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
 }
}
