using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static readonly Dictionary<CardModel,List<bool>> imbuedPlays=new();
 static void ObserveImbuedPlay(CardModel __instance,bool isAutoPlay){
  if(!imbuedPlays.TryGetValue(__instance,out var list))imbuedPlays[__instance]=list=new();list.Add(isAutoPlay);
 }
 static int ImbuedPlayCount(CardModel card)=>imbuedPlays.GetValueOrDefault(card)?.Count??0;
 static CardModel ImbuedComposite(CardModel card,bool hasImbued=true){
  card.EnchantInternal(ModelDb.Enchantment<Sown>().ToMutable(),1);
  var wrapper=(EnchantmentModel)AccessTools.Method(Find("Plague.PlagueCompositeEnchantment"),"ConvertFrom").Invoke(null,[card,card.Enchantment])!;
  if(hasImbued)AccessTools.Method(wrapper.GetType(),"ImportExistingEnchantment").Invoke(wrapper,[Enchant(typeof(Imbued),1)]);
  return card;
 }
 static async Task ImbuedPhase(BloodFixture f,Player? owner=null,bool setup=false){
  var p=owner??f.P;var turn=AccessTools.Field(typeof(CombatManager),"_turnState").GetValue(CombatManager.Instance)!;
  var context=new HookPlayerChoiceContext(p,f.P.NetId,GameActionType.Combat);
  Task start=setup?(Task)AccessTools.Method(typeof(CombatManager),"SetupPlayerTurn").Invoke(CombatManager.Instance,[turn,p,context])!:Task.CompletedTask;
  await ((Task)AccessTools.Method(typeof(CombatManager),"RunAutoPrePlayPhase").Invoke(CombatManager.Instance,[turn,context,start,p])!).WaitAsync(TimeSpan.FromSeconds(8));
 }
 static async Task ImbuedFill(BloodFixture f){
  for(int i=0;i<12;i++)await CardPileCmd.Add(f.Card<StrikeIronclad>(),PileType.Draw,skipVisuals:true);
 }
 static async Task ImbuedApotheosisChecks(){
  int first=passes.Count;var evidence=new List<object>();string key=Pack.PlagueImbuedAutoPlayFix.Key;bool active=Pack.Settings.Startup[key];
  var observer=new Harmony("Aikun020Probe.ImbuedObservation");
  observer.Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.OnPlayWrapper)),prefix:new HarmonyMethod(typeof(Entry),nameof(ObserveImbuedPlay)));
  try{
   Check("imbued independent default-on Plague compatibility option",Pack.Options.All.Single(o=>o.Key==key) is {Misc:false,Required:["plague"]}&&new Pack.PackConfig().Get(key));
   Check("imbued optional dependency safely absent or applied",Minimal?!Pack.Options.Eligible(key)&&!Pack.RuntimeLoader.States.ContainsKey(key):Pack.RuntimeLoader.States.GetValueOrDefault(key)=="已应用。");
   foreach(bool enabled in new[]{false,true}){
    Pack.Settings.Startup[key]=enabled;using var f=new BloodFixture();await ImbuedFill(f);
    var card=f.Card<Apotheosis>();card.EnchantInternal(ModelDb.Enchantment<Imbued>().ToMutable(),1);await CardPileCmd.Add(card,PileType.Draw,skipVisuals:true);
    var strike=PileType.Draw.GetPile(f.P).Cards.OfType<StrikeIronclad>().First();await ImbuedPhase(f,setup:true);
    Check("imbued primary native enchant unchanged switch="+enabled,ImbuedPlayCount(card)==1&&imbuedPlays[card].All(x=>x)&&card.Pile?.Type==PileType.Exhaust&&strike.CurrentUpgradeLevel==1);
    Check("imbued primary native free autoplay switch="+enabled,f.P.PlayerCombatState!.Energy==3&&f.P.PlayerCombatState.Phase==PlayerTurnPhase.Play);
   }
   if(Minimal)return;
   var wrapperType=Find("Plague.PlagueCompositeEnchantment");
   Check("imbued installed upstream lacks new auto-pre-play override",AccessTools.Method(wrapperType,nameof(AbstractModel.AfterAutoPrePlayPhaseEntered)).DeclaringType==typeof(AbstractModel));
   foreach(bool enabled in new[]{false,true})foreach(int level in new[]{0,1}){
    Pack.Settings.Startup[key]=enabled;using var f=new BloodFixture();await ImbuedFill(f);
    var card=ImbuedComposite(Up(f.Card<Apotheosis>(),level));await CardPileCmd.Add(card,PileType.Draw,skipVisuals:true);
    var strike=PileType.Draw.GetPile(f.P).Cards.OfType<StrikeIronclad>().First();await ImbuedPhase(f,setup:true);
    string tag="imbued composite enabled="+enabled+" level="+level;
    Check(tag+" real first-turn native phase reproduces or fixes missing autoplay",ImbuedPlayCount(card)==(enabled?1:0)&&card.Pile?.Type==(enabled?PileType.Exhaust:PileType.Draw)&&strike.CurrentUpgradeLevel==(enabled?1:0));
    Check(tag+" free autoplay and sown energy preserved",f.P.PlayerCombatState!.Energy==(enabled?4:3)&&f.P.PlayerCombatState.Phase==PlayerTurnPhase.Play);
    Check(tag+" both original enchantments remain bound",Pack.EnchantmentView.Read(card).Count(e=>e is Imbued or Sown)==2&&Pack.EnchantmentView.Read(card).All(e=>ReferenceEquals(e.Card,card)));
    if(enabled){
     Check(tag+" auto-play flagged exactly once",imbuedPlays[card].SequenceEqual(new[]{true}));
     Check(tag+" sown grants its once-only energy and self upgrade unchanged",card.CurrentUpgradeLevel==level&&Pack.EnchantmentView.Read(card).OfType<Sown>().Single().Status==EnchantmentStatus.Disabled);
     await CardPileCmd.Add(card,PileType.Draw,skipVisuals:true);f.P.PlayerCombatState.IncrementTurnNumber();await ImbuedPhase(f);
     Check(tag+" no autoplay on second or extra turn",ImbuedPlayCount(card)==1&&card.Pile?.Type==PileType.Draw);
    }
    evidence.Add(new{Enabled=enabled,Level=level,Plays=ImbuedPlayCount(card),Energy=f.P.PlayerCombatState.Energy,StrikeLevel=strike.CurrentUpgradeLevel});
   }
   Pack.Settings.Startup[key]=true;
   foreach(int cycle in new[]{0,1,2}){
    using var f=new BloodFixture();var card=ImbuedComposite(ModelDb.Card<Apotheosis>().ToMutable());
    for(int i=0;i<=cycle;i++)card=Restore(card);
    f.Combat.AddCard(card,f.P);await CardPileCmd.Add(card,PileType.Draw,skipVisuals:true);var strike=f.Card<StrikeIronclad>();await CardPileCmd.Add(strike,PileType.Hand,skipVisuals:true);
    var clone=(CardModel)card.ClonePreservingMutability();Check("imbued clone inner binding independent cycle="+cycle,Pack.EnchantmentView.Read(clone).All(e=>ReferenceEquals(e.Card,clone))&&!ReferenceEquals(Pack.EnchantmentView.Read(clone).OfType<Imbued>().Single(),Pack.EnchantmentView.Read(card).OfType<Imbued>().Single()));
    await ImbuedPhase(f);Check("imbued repeated JSON restoration auto-plays and upgrades actual hand cycle="+cycle,ImbuedPlayCount(card)==1&&card.Pile?.Type==PileType.Exhaust&&strike.CurrentUpgradeLevel==1);
   }
   using(var f=new BloodFixture(multiplayer:true)){
    var own=ImbuedComposite(f.Card<Apotheosis>());var other=ImbuedComposite(f.Card<Apotheosis>(f.Other));
    foreach(var card in new[]{own,other})await CardPileCmd.Add(card,PileType.Draw,skipVisuals:true);
    var ownStrike=f.Card<StrikeIronclad>();var otherStrike=f.Card<StrikeIronclad>(f.Other);
    await CardPileCmd.Add(ownStrike,PileType.Hand,skipVisuals:true);await CardPileCmd.Add(otherStrike,PileType.Hand,skipVisuals:true);
    await ImbuedPhase(f);
    Check("imbued player phase only plays own composite",ImbuedPlayCount(own)==1&&ImbuedPlayCount(other)==0&&ownStrike.CurrentUpgradeLevel==1&&otherStrike.CurrentUpgradeLevel==0);
    await ImbuedPhase(f,f.Other);
    Check("imbued other player's own phase plays once",ImbuedPlayCount(own)==1&&ImbuedPlayCount(other)==1&&otherStrike.CurrentUpgradeLevel==1);
   }
   using(var f=new BloodFixture()){
    var one=ImbuedComposite(f.Card<Apotheosis>());var two=ImbuedComposite(f.Card<Apotheosis>());var ordinary=ImbuedComposite(f.Card<Apotheosis>(),false);
    foreach(var card in new[]{one,two,ordinary})await CardPileCmd.Add(card,PileType.Draw,skipVisuals:true);
    await ImbuedPhase(f);
    Check("imbued multiple copies each resolve once and no-imbued composite not played",ImbuedPlayCount(one)==1&&ImbuedPlayCount(two)==1&&ImbuedPlayCount(ordinary)==0);
   }
   using(var f=new BloodFixture()){
    var card=ImbuedComposite(f.Card<DefendIronclad>());await CardPileCmd.Add(card,PileType.Draw,skipVisuals:true);await ImbuedPhase(f);
    Check("imbued other composite skills follow native autoplay",ImbuedPlayCount(card)==1&&card.Pile?.Type==PileType.Discard&&f.P.Creature.Block>=5);
   }
   // 读取只读副本中真实神化 DTO，验证原保存数据无需迁移即可生效。
   if(System.Environment.GetEnvironmentVariable("AIKUN_IMBUED_INCIDENT_SAVE") is {} incidentSave&&File.Exists(incidentSave)){
   using(var f=new BloodFixture()){
    using var save=JsonDocument.Parse(File.ReadAllText(incidentSave));
    JsonElement? FindCard(JsonElement value){
     if(value.ValueKind==JsonValueKind.Object){
      if(value.TryGetProperty("id",out var id)&&id.ValueKind==JsonValueKind.String&&id.GetString()=="CARD.APOTHEOSIS"&&value.TryGetProperty("enchantment",out var enchantment)&&enchantment.GetRawText().Contains("IMBUED"))return value;
      foreach(var p in value.EnumerateObject())if(FindCard(p.Value) is {} found)return found;
     }else if(value.ValueKind==JsonValueKind.Array)foreach(var child in value.EnumerateArray())if(FindCard(child) is {} found)return found;
     return null;
    }
    var raw=FindCard(save.RootElement)??throw new Exception("真实副本中未找到注能神化。");
    var card=CardModel.FromSerializable(JsonSerializer.Deserialize<SerializableCard>(raw.GetRawText(),JsonSerializationUtility.Options)!);f.Combat.AddCard(card,f.P);
    await CardPileCmd.Add(card,PileType.Draw,skipVisuals:true);await ImbuedPhase(f);
    Check("imbued exact reported card DTO restored without migration plays once",card is Apotheosis&&ImbuedPlayCount(card)==1&&card.Pile?.Type==PileType.Exhaust&&Pack.EnchantmentView.Read(card).OfType<Imbued>().Count()==1);
   }
   }
  }finally{
   Pack.Settings.Startup[key]=active;observer.UnpatchAll(observer.Id);
   File.WriteAllText(Output+"/imbued-apotheosis-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Checks=passes.Skip(first).ToArray(),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));imbuedPlays.Clear();
  }
 }
}
