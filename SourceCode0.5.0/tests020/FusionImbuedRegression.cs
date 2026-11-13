using HarmonyLib;
using System.Text.Json;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.TestSupport;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static CardModel ImbuedFusion(string slot,int level=0,bool reverse=false){
  var one=Card<DefendIronclad>();one.EnchantInternal(ModelDb.Enchantment<Sown>().ToMutable(),1);
  var two=Card<Apotheosis>(level);
  if(slot=="plague")ImbuedComposite(two);
  else if(slot=="extra"){two.EnchantInternal(ModelDb.Enchantment<Sharp>().ToMutable(),1);Extra(two,ModelDb.Enchantment<Imbued>().ToMutable());}
  else two.EnchantInternal(ModelDb.Enchantment<Imbued>().ToMutable(),1);
  return reverse?Fuse(two,one):Fuse(one,two);
 }
 static async Task<CardModel> ImbuedBattleCopy(BloodFixture f,CardModel deck,Player? owner=null){
  var p=owner??f.P;f.Run.AddCard(deck,p);await CardPileCmd.Add(deck,PileType.Deck,skipVisuals:true);
  var card=f.Combat.CloneCard(deck);await CardPileCmd.Add(card,PileType.Draw,skipVisuals:true);return card;
 }
 static async Task FusionImbuedChecks(){
  int start=passes.Count;var evidence=new List<object>();
  if(Minimal){Check("fusion imbued safely absent",FusionQuestBlessingFix.Entry.Find("CardFusion.FusedCard")==null);return;}
  string key=Pack.PlagueImbuedAutoPlayFix.Key;bool active=Pack.Settings.Startup[key];
  var synth=AccessTools.Field(Find("CardFusion.CardFusionSettings"),"_synthesizeMode");var oldSynth=synth.GetValue(null);
  var observer=new Harmony("Aikun020Probe.FusionImbuedObservation");
  observer.Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.OnPlayWrapper)),prefix:new HarmonyMethod(typeof(Entry),nameof(ObserveImbuedPlay)));
  try{
   foreach(bool enabled in new[]{false,true}){
    Pack.Settings.Startup[key]=enabled;synth.SetValue(null,false);
    using var f=new BloodFixture();await ImbuedFill(f);var deck=ImbuedFusion("primary");
    var card=await ImbuedBattleCopy(f,deck);var strike=PileType.Draw.GetPile(f.P).Cards.OfType<StrikeIronclad>().First();
    Check("fusion imbued reproduction has actual imbued material enabled="+enabled,Materials(card).Any(c=>Pack.EnchantmentView.Read(c).OfType<Imbued>().Any()));
    await ImbuedPhase(f,setup:true);
    Check("fusion imbued baseline versus fix root plays enabled="+enabled,ImbuedPlayCount(card)==(enabled?1:0));
    Check("fusion imbued baseline versus fix effects enabled="+enabled,strike.CurrentUpgradeLevel==(enabled?1:0));
    evidence.Add(new{Enabled=enabled,Plays=ImbuedPlayCount(card),Pile=card.Pile?.Type.ToString(),Energy=f.P.PlayerCombatState!.Energy});
   }
   Pack.Settings.Startup[key]=true;
   foreach(bool mode in new[]{false,true})foreach(string slot in new[]{"primary","plague","extra"})foreach(bool reverse in new[]{false,true})foreach(int level in new[]{0,1}){
    synth.SetValue(null,mode);using var f=new BloodFixture();await ImbuedFill(f);
    var deck=ImbuedFusion(slot,level,reverse);var card=await ImbuedBattleCopy(f,deck);
    var before=MaterialSnapshot(deck);var strike=PileType.Draw.GetPile(f.P).Cards.OfType<StrikeIronclad>().First();
    string tag=$"fusion imbued mode={mode} slot={slot} reverse={reverse} level={level}";
    await ImbuedPhase(f,setup:true);
    Check(tag+" root exactly one automatic play",ImbuedPlayCount(card)==1&&imbuedPlays[card].SequenceEqual(new[]{true}));
    Check(tag+" actual apotheosis effect and native exhaust",strike.CurrentUpgradeLevel==1&&card.Pile?.Type==PileType.Exhaust);
    Check(tag+" combat copy independent of permanent sources",MaterialSnapshot(deck)==before&&Materials(deck).Zip(Materials(card)).All(p=>!ReferenceEquals(p.First,p.Second)));
    await ImbuedPhase(f);Check(tag+" repeated phase does not duplicate",ImbuedPlayCount(card)==1);
    f.P.PlayerCombatState!.IncrementTurnNumber();await CardPileCmd.Add(card,PileType.Draw,skipVisuals:true);await ImbuedPhase(f);
    Check(tag+" second turn does not autoplay",ImbuedPlayCount(card)==1&&card.Pile?.Type==PileType.Draw);
    evidence.Add(new{Mode=mode,Slot=slot,Reverse=reverse,Level=level,Plays=ImbuedPlayCount(card)});
   }
   synth.SetValue(null,false);
   using(var f=new BloodFixture()){
    var first=Card<DefendIronclad>();first.EnchantInternal(ModelDb.Enchantment<Sharp>().ToMutable(),1);
    var second=Card<DefendIronclad>();second.EnchantInternal(ModelDb.Enchantment<Imbued>().ToMutable(),1);
    var card=await ImbuedBattleCopy(f,Fuse(first,second));f.P.PlayerCombatState!.LoseEnergy(999);
    await ImbuedPhase(f);
    Check("fusion imbued zero energy still auto plays and remains zero",ImbuedPlayCount(card)==1&&f.P.PlayerCombatState.Energy==0&&card.Pile?.Type==PileType.Discard&&f.P.Creature.Block>=10);
   }
   using(var f=new BloodFixture()){
    Pack.Settings.Startup[key]=false;var card=await ImbuedBattleCopy(f,ImbuedFusion("primary",reverse:true));await ImbuedPhase(f);
    Check("fusion imbued switch off preserves inherited native primary",ImbuedPlayCount(card)==1);
    Pack.Settings.Startup[key]=true;
   }
   using(var f=new BloodFixture()){
    var card=await ImbuedBattleCopy(f,ImbuedFusion("primary"));var local=LocalContext.NetId;
    try{LocalContext.NetId=null;await ImbuedPhase(f);Check("fusion imbued native missing local context guard preserved",ImbuedPlayCount(card)==0);}
    finally{LocalContext.NetId=local;}
   }
   foreach(string slot in new[]{"primary","plague","extra"}){
    var saved=ImbuedFusion(slot);
    for(int i=0;i<3;i++){
     saved=Restore(saved);using var f=new BloodFixture();await ImbuedFill(f);
     var card=await ImbuedBattleCopy(f,saved);await ImbuedPhase(f,setup:true);
     Check($"fusion imbued saved slot={slot} cycle={i} plays root once",ImbuedPlayCount(card)==1&&card.Pile?.Type==PileType.Exhaust);
    }
   }
   using(var f=new BloodFixture(multiplayer:true)){
    var one=await ImbuedBattleCopy(f,ImbuedFusion("plague"));var two=await ImbuedBattleCopy(f,ImbuedFusion("primary"),f.Other);
    await ImbuedPhase(f);Check("fusion imbued player isolation",ImbuedPlayCount(one)==1&&ImbuedPlayCount(two)==0);
    await ImbuedPhase(f,f.Other);Check("fusion imbued second player own phase",ImbuedPlayCount(one)==1&&ImbuedPlayCount(two)==1);
   }
   using(var f=new BloodFixture()){
    var source=ImbuedFusion("primary");var second=Card<DefendIronclad>();second.EnchantInternal(ModelDb.Enchantment<Imbued>().ToMutable(),1);
    var card=await ImbuedBattleCopy(f,Fuse(source,second));await ImbuedPhase(f);
    Check("fusion imbued refusion multiple imbued only plays root once",Materials(card).Length==3&&ImbuedPlayCount(card)==1);
   }
   using(var f=new BloodFixture()){
    var plain=await ImbuedBattleCopy(f,Fuse(Card<DefendIronclad>(),Card<DefendIronclad>()));await ImbuedPhase(f);
    Check("fusion imbued ordinary fusion unchanged",ImbuedPlayCount(plain)==0&&plain.Pile?.Type==PileType.Draw);
   }
  }finally{
   Pack.Settings.Startup[key]=active;synth.SetValue(null,oldSynth);observer.UnpatchAll(observer.Id);imbuedPlays.Clear();
   File.WriteAllText(Output+"/fusion-imbued-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Checks=passes.Skip(start).ToArray(),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
  }
 }
}
