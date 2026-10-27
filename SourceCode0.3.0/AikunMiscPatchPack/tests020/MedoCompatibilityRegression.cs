using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using Pack=AikunMiscPatchPack;
namespace Aikun020Probe;
public static partial class Entry {
 static CardModel? medoRemovalSelection;
 static bool MedoSelect(ref Task<IEnumerable<CardModel>> __result){__result=Task.FromResult<IEnumerable<CardModel>>(medoRemovalSelection==null?[]:[medoRemovalSelection]);return false;}
 static async Task MedoCompatibilityChecks(){
  if(Minimal){Check("Medo audit absent optional mod safely skipped",Pack.Settings.Status(Pack.Options.All.Single(o=>o.Key=="medo_events_api")).Contains("未装载相关模组"));return;}
  int first=passes.Count;var evidence=new List<object>();bool oldMode=TestMode.IsOn;ulong? oldLocal=LocalContext.NetId;
  var listeners=AccessTools.Field(typeof(CombatStateTracker),"CombatStateChanged");var frontend=listeners.GetValue(CombatManager.Instance.StateTracker);
  var h=new Harmony("Aikun020Probe.MedoAudit");
  try{
   listeners.SetValue(CombatManager.Instance.StateTracker,null);TestMode.IsOn=true;
   Check("Medo event API loaded from verified patch cache",Pack.RuntimeLoader.States.GetValueOrDefault("medo_events_api")=="已应用。");
   h.Patch(AccessTools.Method(typeof(CardSelectCmd),nameof(CardSelectCmd.FromDeckForRemoval)),prefix:new HarmonyMethod(typeof(Entry),nameof(MedoSelect)));
   async Task<EventModel> Event(string name,Player p,RunState state){var canonical=ModelDb.GetById<EventModel>(ModelDb.GetId(Find("Medo.Scripts.Events."+name)));state.PushRoom(new EventRoom(canonical));var e=canonical.ToMutable();await e.BeginEvent(p,null,false);return e;}
   async Task Choice(EventModel e,string method)=>await ((Task)AccessTools.Method(e.GetType(),method).Invoke(e,null)!).WaitAsync(TimeSpan.FromSeconds(5));
   foreach(string name in new[]{"HeroicSagaOfFlameChase","ElysianRealm"}){
    var p=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var s=RunState.CreateForTest([p],seed:"MEDO-EVENT-"+name);RunManager.Instance.SetUpTest(s,new NetSingleplayerGameService());LocalContext.NetId=p.NetId;s.AppendToMapPointHistory(MapPointType.Unknown,RoomType.Event,null);
    var e=await Event(name,p,s);int hp=p.Creature.CurrentHp,gold=p.Gold,deck=p.Deck.Cards.Count;medoRemovalSelection=p.Deck.Cards.First(c=>c.Type==CardType.Attack);
    await Choice(e,name=="ElysianRealm"?"GainedWeight":"Ignore");
    Check("Medo event finishes "+name,e.IsFinished);
    Check("Medo event native health cost "+name,p.Creature.CurrentHp==hp-(name=="ElysianRealm"?5:13));
    Check("Medo event native reward "+name,name=="ElysianRealm"?p.Deck.Cards.Count==deck-1&&!p.Deck.Cards.Contains(medoRemovalSelection):p.Gold==gold+250);
    evidence.Add(new{Event=name,HpBefore=hp,HpAfter=p.Creature.CurrentHp,GoldChange=p.Gold-gold,DeckChange=p.Deck.Cards.Count-deck});RunManager.Instance.CleanUp();
   }
   var owner=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var other=Player.CreateForNewRun<Defect>(UnlockState.all,2);var state=RunState.CreateForTest([owner,other],seed:"MEDO-RELICS");RunManager.Instance.SetUpTest(state,new NetSingleplayerGameService());LocalContext.NetId=owner.NetId;
   var combat=new CombatState(runState:state);combat.AddPlayer(owner);combat.AddPlayer(other);var enemy=combat.CreateCreature(ModelDb.Monster<CalcifiedCultist>().ToMutable(),CombatSide.Enemy,"medo-relic-target");combat.AddCreature(enemy);CombatManager.Instance.SetUpCombat(combat);
   var turn=AccessTools.Field(typeof(CombatManager),"_turnState").GetValue(CombatManager.Instance)!;AccessTools.Property(turn.GetType(),"IsInProgress").SetValue(turn,true);
   RelicModel Relic(string n){var r=ModelDb.GetById<RelicModel>(ModelDb.GetId(Find("Medo.Scripts.Relics."+n))).ToMutable();owner.AddRelicInternal(r,silent:true);return r;}
   var because=Relic("BecauseOfYou");var demi=Relic("Demiurge");var written=Relic("AsIHaveWritten");
   Check("Medo BecauseOfYou native draw hook adds one",Hook.ModifyHandDraw(combat,owner,5,out _)==6);
   Check("Medo BecauseOfYou other player draw unchanged",Hook.ModifyHandDraw(combat,other,5,out _)==5);
   Check("Medo Demiurge native energy hook adds one",Hook.ModifyMaxEnergy(combat,owner,3)==4);
   Check("Medo Demiurge other player energy unchanged",Hook.ModifyMaxEnergy(combat,other,3)==3);
   var room=new CombatRoom(state.Act.AllRegularEncounters.First().ToMutable(),state);state.PushRoom(room);await Hook.AfterRoomEntered(state,room);
   Check("Medo Demiurge native room hook grants two strength",owner.Creature.GetPower<StrengthPower>()?.Amount==2);
   await written.BeforeCombatStart();owner.Creature.SetCurrentHpInternal(50);var choice=new ThrowingPlayerChoiceContext();await CreatureCmd.Damage(choice,owner.Creature,15,ValueProp.Unpowered,enemy,null,null);
   Check("Medo AsIHaveWritten actual damage restores triggering hit",owner.Creature.CurrentHp==50);
   await CreatureCmd.Damage(choice,owner.Creature,15,ValueProp.Unpowered,enemy,null,null);Check("Medo AsIHaveWritten triggers once per combat",owner.Creature.CurrentHp==35);
   await written.BeforeCombatStart();await CreatureCmd.Damage(choice,owner.Creature,1,ValueProp.Unpowered,enemy,null,null);Check("Medo AsIHaveWritten new combat resets allowance",owner.Creature.CurrentHp==35);
   var restored=Player.FromSerializable(owner.ToSerializable());Check("Medo all three relics survive save roundtrip",restored.Relics.Count(r=>r.GetType().Assembly.GetName().Name=="Medo")==3);
   var restoredWritten=restored.Relics.Single(r=>r.GetType().Name=="AsIHaveWritten");Check("Medo relic trigger flag survives save roundtrip",(bool)AccessTools.Field(restoredWritten.GetType(),"_triggeredThisCombat").GetValue(restoredWritten)!);
   Check("Medo relic max HP snapshot survives save roundtrip",(int)AccessTools.Field(restoredWritten.GetType(),"_maxHp").GetValue(restoredWritten)! == owner.Creature.MaxHp);
   CombatManager.Instance.Reset(false);RunManager.Instance.CleanUp();
   var questOwner=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var quests=RunState.CreateForTest([questOwner],seed:"MEDO-QUESTS");RunManager.Instance.SetUpTest(quests,new NetSingleplayerGameService());LocalContext.NetId=questOwner.NetId;quests.AppendToMapPointHistory(MapPointType.Unknown,RoomType.Event,null);
   CardModel Quest(string n){var c=ModelDb.GetById<CardModel>(ModelDb.GetId(Find("Medo.Scripts.Cards."+n))).ToMutable();c.Owner=questOwner;return c;}
   var mem=Quest("Mem");var cyrene=Quest("Cyrene");
   Check("Medo quest cards remain unplayable and not upgradable",!mem.IsUpgradable&&!cyrene.IsUpgradable&&mem.Type==CardType.Quest&&cyrene.Type==CardType.Quest);
   AccessTools.Property(typeof(RunState),nameof(RunState.CurrentActIndex)).SetValue(quests,1);
   Check("Medo Mem room hook remains event only",mem.ModifyUnknownMapPointRoomTypes(new HashSet<RoomType>{RoomType.Monster,RoomType.Event}).SetEquals(new HashSet<RoomType>{RoomType.Event}));
   Check("Medo Mem native event replacement",mem.ModifyNextEvent(ModelDb.Event<WarHistorianRepy>()).GetType().Name=="ForTheSunIsSetToDie");
   AccessTools.Property(typeof(RunState),nameof(RunState.CurrentActIndex)).SetValue(quests,2);
   var war=ModelDb.Event<WarHistorianRepy>();Check("Medo Cyrene keeps historian priority",ReferenceEquals(cyrene.ModifyNextEvent(war),war));
   cyrene=Restore(cyrene);cyrene.Owner=questOwner;Check("Medo Cyrene deferred flag survives save and resumes",cyrene.ModifyNextEvent(war).GetType().Name=="AsTomorrowBecameYesterday");
   cyrene=quests.LoadCard(cyrene.ToSerializable(),questOwner);await CardPileCmd.Add(cyrene,PileType.Deck,skipVisuals:true);var journey=await Event("AsTomorrowBecameYesterday",questOwner,quests);await Choice(journey,"Journey");
   Check("Medo final quest event finishes",journey.IsFinished);
   Check("Medo final quest removes Cyrene and grants upgraded power card",questOwner.Deck.Cards.All(c=>c.GetType().Name!="Cyrene")&&questOwner.Deck.Cards.Single(c=>c.GetType().Name=="FlyIntoAPinkTomorrow").CurrentUpgradeLevel==1);
   Check("Medo final quest grants Demiurge",questOwner.Relics.Any(r=>r.GetType().Name=="Demiurge"));
   File.WriteAllText(Output+"/medo-compat-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Failed=0,Checks=passes.Skip(first),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
  }finally{
   medoRemovalSelection=null;h.UnpatchAll(h.Id);
   try{if(RunManager.Instance.DebugOnlyGetState()!=null){CombatManager.Instance.Reset(false);RunManager.Instance.CleanUp();}}
   finally{LocalContext.NetId=oldLocal;TestMode.IsOn=oldMode;listeners.SetValue(CombatManager.Instance.StateTracker,frontend);}
  }
 }
}
