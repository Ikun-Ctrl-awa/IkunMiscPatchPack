using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
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
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static async Task PinkTomorrowChecks(){
  int first=passes.Count;var evidence=new List<object>();
  if(Minimal){Check("pink tomorrow missing Medo safely skipped",Pack.Settings.Status(Pack.Options.All.Single(o=>o.Key=="pink_tomorrow_damage")).Contains("未装载相关模组"));return;}
  Check("pink tomorrow damage bridge installed",Pack.RuntimeLoader.States.GetValueOrDefault("pink_tomorrow_damage")=="已应用。");
  var powerType=Find("Medo.Scripts.Powers.FlyIntoAPinkTomorrowPower");var cardType=Find("Medo.Scripts.Cards.FlyIntoAPinkTomorrow");
  foreach(bool enabled in new[]{false,true}){
   bool oldMode=TestMode.IsOn;ulong? oldLocal=LocalContext.NetId;
   var listeners=AccessTools.Field(typeof(CombatStateTracker),"CombatStateChanged");var frontend=listeners.GetValue(CombatManager.Instance.StateTracker);
   try{
    listeners.SetValue(CombatManager.Instance.StateTracker,null);TestMode.IsOn=true;Pack.Settings.Startup["pink_tomorrow_damage"]=enabled;
    var p=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var other=Player.CreateForNewRun<Defect>(UnlockState.all,2);var state=RunState.CreateForTest([p,other],seed:"PINK-TOMORROW-DAMAGE");
    RunManager.Instance.SetUpTest(state,new NetSingleplayerGameService());LocalContext.NetId=p.NetId;
    var combat=new CombatState(runState:state);combat.AddPlayer(p);combat.AddPlayer(other);var enemy=combat.CreateCreature(ModelDb.Monster<CalcifiedCultist>().ToMutable(),CombatSide.Enemy,"pink-target");combat.AddCreature(enemy);enemy.SetMaxHpInternal(10000);enemy.SetCurrentHpInternal(10000);
    CombatManager.Instance.SetUpCombat(combat);var turn=AccessTools.Field(typeof(CombatManager),"_turnState").GetValue(CombatManager.Instance)!;AccessTools.Property(turn.GetType(),"IsInProgress").SetValue(turn,true);
    var choice=new ThrowingPlayerChoiceContext();
    async Task<int> Play(CardModel card,Creature? target){await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);int hp=enemy.CurrentHp;await card.OnPlayWrapper(choice,target,false,default,skipCardPileVisuals:true).WaitAsync(TimeSpan.FromSeconds(5));return hp-enemy.CurrentHp;}
    CardModel Card<T>() where T:CardModel=>combat.CreateCard(ModelDb.Card<T>(),p);
    var form=combat.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(cardType)),p);
    var draw1=Card<DefendIronclad>();var draw2=Card<StrikeIronclad>();await CardPileCmd.Add(draw1,PileType.Draw,CardPilePosition.Top,skipVisuals:true);await CardPileCmd.Add(draw2,PileType.Draw,CardPilePosition.Top,skipVisuals:true);
    string description=form.GetDescriptionForPile(PileType.None);Check("pink tomorrow base card costs one enabled="+enabled,form.EnergyCost.Canonical==1);
    await Play(form,null);
    var power=p.Creature.Powers.Single(x=>x.GetType()==powerType);
    Check("pink tomorrow actual card draws two enabled="+enabled,p.PlayerCombatState!.Hand.Cards.Contains(draw1)&&p.PlayerCombatState.Hand.Cards.Contains(draw2));
    Check("pink tomorrow actual card applies two stacks enabled="+enabled,power.Amount==2);
    var strike=Card<StrikeIronclad>();var old=AccessTools.DeclaredMethod(powerType,nameof(AbstractModel.ModifyDamageMultiplicative));
    decimal original=(decimal)old.Invoke(power,[enemy,6m,ValueProp.Move,p.Creature,strike])!;
    decimal routed=power.ModifyDamageMultiplicative(enemy,6,ValueProp.Move,p.Creature,strike,null);
    Check("pink tomorrow old hook really provides double damage enabled="+enabled,original==2m);
    Check("pink tomorrow native dispatch enabled="+enabled,routed==(enabled?2m:1m));
    int[] hits=[await Play(strike,enemy),await Play(Card<StrikeIronclad>(),enemy),await Play(Card<StrikeIronclad>(),enemy)];
    evidence.Add(new{Enabled=enabled,Description=description,OriginalHook=original,NativeHook=routed,ActualStrikeDamage=hits});
    Check("pink tomorrow first two strikes and exhausted allowance enabled="+enabled,hits.SequenceEqual(enabled?new[]{12,12,6}:new[]{6,6,6}));
    if(!enabled)continue;
    Check("pink tomorrow exhausted preview no boost",power.ModifyDamageMultiplicative(enemy,6,ValueProp.Move,p.Creature,Card<StrikeIronclad>(),null)==1m);
    combat.RoundNumber++;
    Check("pink tomorrow next turn resets boost",power.ModifyDamageMultiplicative(enemy,6,ValueProp.Move,p.Creature,Card<StrikeIronclad>(),null)==2m);
    Check("pink tomorrow other player never gains boost",power.ModifyDamageMultiplicative(enemy,6,ValueProp.Move,other.Creature,strike,null)==1m);
    Check("pink tomorrow non attack damage never boosted",power.ModifyDamageMultiplicative(enemy,6,ValueProp.Unpowered,p.Creature,strike,null)==1m);
    Check("pink tomorrow no card source never boosted",power.ModifyDamageMultiplicative(enemy,6,ValueProp.Move,p.Creature,null,null)==1m);
    await Play(Card<DefendIronclad>(),null);
    Check("pink tomorrow skill does not spend allowance",power.ModifyDamageMultiplicative(enemy,6,ValueProp.Move,p.Creature,Card<StrikeIronclad>(),null)==2m);
    int twin=await Play(Card<TwinStrike>(),enemy);int following=await Play(Card<StrikeIronclad>(),enemy);int last=await Play(Card<StrikeIronclad>(),enemy);
    Check("pink tomorrow both hits doubled counts as one attack",twin==20&&following==12&&last==6);
    combat.RoundNumber++;
    var extra=combat.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(cardType)),p);extra.UpgradeInternal();extra.FinalizeUpgradeInternal();
    Check("pink tomorrow upgraded card costs zero",extra.EnergyCost.GetWithModifiers(CostModifiers.All)==0);
    await Play(extra,null);Check("pink tomorrow repeated card stacks allowance",power.Amount==4);
    var stacked=new List<int>();for(int i=0;i<5;i++)stacked.Add(await Play(Card<StrikeIronclad>(),enemy));
    Check("pink tomorrow four stacks affect four plays only",stacked.SequenceEqual(new[]{12,12,12,12,6}));
    combat.RoundNumber++;
    var otherStrike=combat.CreateCard(ModelDb.Card<StrikeIronclad>(),other);await CardPileCmd.Add(otherStrike,PileType.Hand,skipVisuals:true);
    int hp=enemy.CurrentHp;await otherStrike.OnPlayWrapper(choice,enemy,false,default,skipCardPileVisuals:true);
    Check("pink tomorrow other player real strike remains six",hp-enemy.CurrentHp==6);
    Check("pink tomorrow other player attack does not spend owner allowance",power.ModifyDamageMultiplicative(enemy,6,ValueProp.Move,p.Creature,Card<StrikeIronclad>(),null)==2m);
    evidence.Add(new{MultiHit=twin,Following=following,AfterAllowance=last,Stacked=stacked,OtherPlayerDamage=hp-enemy.CurrentHp});
   }finally{
    Pack.Settings.Startup["pink_tomorrow_damage"]=true;
    try{if(RunManager.Instance.DebugOnlyGetState()!=null){CombatManager.Instance.Reset(false);RunManager.Instance.CleanUp();}}
    finally{LocalContext.NetId=oldLocal;TestMode.IsOn=oldMode;listeners.SetValue(CombatManager.Instance.StateTracker,frontend);}
   }
  }
  File.WriteAllText(Output+"/pink-tomorrow-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Failed=0,Checks=passes.Skip(first),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
 }
}
