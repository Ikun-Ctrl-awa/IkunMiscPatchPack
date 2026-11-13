using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static bool HasLag(CardModel c)=>FusionQuestBlessingFix.Entry.IsOrContains<Pack.LagCurse>(c);
 static CardModel[] LagRoots(Player p)=>p.PlayerCombatState!.AllCards.Where(HasLag).ToArray();
 static string LagFusionState(CardModel c)=>c.GetType().FullName+":"+c.CurrentUpgradeLevel+":"+string.Join("|",Materials(c).Select(x=>x.Id+"/"+x.CurrentUpgradeLevel+"/"+string.Join(",",x.Keywords.Order())+"/"+x.EnergyCost.Canonical));
 static void AssertLagClone(CardModel source,CardModel copy,Player p,string tag){
  Check(tag+" complete fused copy not standalone",copy.GetType()==source.GetType()&&Materials(copy).Length==Materials(source).Length&&copy is not Pack.LagCurse);
  Check(tag+" root and each material upgrades preserved",LagFusionState(copy)==LagFusionState(source));
  Check(tag+" independent root and materials",!ReferenceEquals(source,copy)&&Materials(source).Zip(Materials(copy)).All(pair=>!ReferenceEquals(pair.First,pair.Second)));
  Check(tag+" copy owned by same player and native hand",copy.Owner==p&&copy.Pile?.Type==PileType.Hand&&!copy.HasBeenRemovedFromState&&!copy.IsDupe);
  var restored=copy;for(int i=0;i<3;i++){restored=Restore(restored);Check(tag+" copy three DTO readbacks "+i,LagFusionState(restored)==LagFusionState(copy));}
 }
 static async Task LagFusionRepro(){
  if(Minimal){Check("lag fusion optional dependency absent",FusionQuestBlessingFix.Entry.Find("CardFusion.FusedCard")==null);return;}
  var errors=new List<string>();
  foreach(bool transform in new[]{false,true}){
   using var f=NewDongniFixture();var root=Fuse(Up(f.Card<Pack.LagCurse>()),Up(f.Card<Bash>()));f.Combat.AddCard(root,f.P);await CardPileCmd.Add(root,PileType.Hand,skipVisuals:true);
   if(transform)await CardCmd.Transform(root,f.Card<DefendIronclad>(),CardPreviewStyle.None);else await CardCmd.Exhaust(f.Choice,root,false);
   var generated=LagRoots(f.P).Where(c=>c!=root).ToArray();
   var entry=new{Transform=transform,Root=root.GetType().FullName,RootLevel=root.CurrentUpgradeLevel,Generated=generated.Select(c=>new{Type=c.GetType().FullName,Level=c.CurrentUpgradeLevel}).ToArray()};
   File.AppendAllText(Output+"/lag-fusion-repro.jsonl",JsonSerializer.Serialize(entry)+"\n");
   if(generated.Length!=1||generated[0].GetType()!=root.GetType())errors.Add((transform?"transform":"exhaust")+": "+JsonSerializer.Serialize(entry));
  }
  if(errors.Count>0)throw new Exception(string.Join("\n",errors));
  Check("lag fusion both baseline reproduction routes fixed",true);
 }
 static async Task LagFusionChecks(){
  int start=passes.Count;var errors=new List<string>();
  if(Minimal){Check("lag fusion optional dependency absent",FusionQuestBlessingFix.Entry.Find("CardFusion.FusedCard")==null);return;}
  async Task Case(string label,Func<Task> test){try{await test();}catch(Exception e){errors.Add(label+": "+e);Godot.GD.Print("[LagFusion] FAIL "+label+" "+e);}}
  var settings=Find("CardFusion.CardFusionSettings");_ = AccessTools.Property(settings,"SumCostMode").GetValue(null);var modeField=AccessTools.Field(settings,"_synthesizeMode");
  bool previous=(bool)modeField.GetValue(null)!;
  try{
   foreach(bool synth in new[]{false,true})foreach(int level in new[]{0,1})foreach(bool transform in new[]{false,true})foreach(var pile in new[]{PileType.Hand,PileType.Draw,PileType.Discard}){
    modeField.SetValue(null,synth);string tag=$"lag fused {synth}/{level}/{transform}/{pile}";
    await Case(tag,async()=>{
     using var f=NewDongniFixture(true);var root=Fuse(Up(f.Card<Pack.LagCurse>(),level),Up(f.Card<Bash>()));f.Combat.AddCard(root,f.P);await CardPileCmd.Add(root,pile,skipVisuals:true);
     string state=LagFusionState(root);int deck=f.P.Deck.Cards.Count;
     if(transform)await CardCmd.Transform(root,f.Card<DefendIronclad>(),CardPreviewStyle.None);else await CardCmd.Exhaust(f.Choice,root,false);
     var generated=LagRoots(f.P).Where(c=>c!=root).ToArray();Check(tag+" exactly one generated",generated.Length==1);AssertLagClone(root,generated.Single(),f.P,tag);
     Check(tag+" original state and permanent deck untouched",LagFusionState(root)==state&&f.P.Deck.Cards.Count==deck&&LagRoots(f.Other).Length==0&&(transform?root.HasBeenRemovedFromState&&root.Pile==null:root.Pile?.Type==PileType.Exhaust));
     var copy=generated.Single();var copiedPart=Materials(copy).OfType<Pack.LagCurse>().Single();if(level==0){Up(copiedPart);Check(tag+" upgrading copied child cannot mutate original",Materials(root).OfType<Pack.LagCurse>().Single().CurrentUpgradeLevel==0);}
    });
   }
   modeField.SetValue(null,false);
   foreach(bool transform in new[]{false,true})foreach(bool other in new[]{false,true})await Case($"native rest fusion {transform}/{other}",async()=>{
    using var f=NewDongniFixture(true);var p=other?f.Other:f.P;var source=Up(f.Run.CreateCard<Pack.LagCurse>(p));var bash=Up(f.Run.CreateCard<Bash>(p));await CardPileCmd.Add(source,PileType.Deck);await CardPileCmd.Add(bash,PileType.Deck);var selector=new DisgustFlowSelector();
    Check($"lag real rest fusion succeeds {transform}/{other}",await DisgustNativeFuse(p,selector,source,bash));var deckRoot=p.Deck.Cards.Single(HasLag);var extra=Up(f.Run.CreateCard<DefendIronclad>(p));await CardPileCmd.Add(extra,PileType.Deck);
    Check($"lag real rest refusion succeeds {transform}/{other}",await DisgustNativeFuse(p,selector,deckRoot,extra));deckRoot=p.Deck.Cards.Single(HasLag);string permanent=LagFusionState(deckRoot);
    var root=Restore(deckRoot);for(int cycle=0;cycle<2;cycle++)root=Restore(root);f.Combat.AddCard(root,p);await CardPileCmd.Add(root,PileType.Hand,skipVisuals:true);
    if(transform)await CardCmd.Transform(root,f.Card<StrikeIronclad>(p),CardPreviewStyle.None);else await CardCmd.Exhaust(f.Choice,root,false);
    var copy=LagRoots(p).Single(c=>c!=root);AssertLagClone(root,copy,p,$"lag real rest refusion {transform}/{other}");
    Check($"lag refusion preserves all three upgraded materials {transform}/{other}",Materials(copy).Length==3&&Materials(copy).All(c=>c.CurrentUpgradeLevel==1)&&LagFusionState(deckRoot)==permanent);
   });
   await Case("batch transform",async()=>{
    using var f=NewDongniFixture();var a=Fuse(f.Card<Pack.LagCurse>(),Up(f.Card<Bash>()));var b=Fuse(Up(f.Card<Pack.LagCurse>()),f.Card<DefendIronclad>());foreach(var c in new[]{a,b}){f.Combat.AddCard(c,f.P);await CardPileCmd.Add(c,PileType.Draw,skipVisuals:true);}
    await CardCmd.Transform([new CardTransformation(a,f.Card<StrikeIronclad>()),new CardTransformation(b,f.Card<DefendIronclad>())],null,CardPreviewStyle.None);var copies=LagRoots(f.P);Check("lag fused batch transform one whole copy per original",copies.Length==2&&copies.Any(c=>LagFusionState(c)==LagFusionState(a))&&copies.Any(c=>LagFusionState(c)==LagFusionState(b)));
   });
   await Case("full hand and chain",async()=>{
    using var f=NewDongniFixture();for(int i=0;i<CardPile.MaxCardsInHand;i++)await DongniPut(f,PileType.Hand);var root=Fuse(Up(f.Card<Pack.LagCurse>()),Up(f.Card<Bash>()));f.Combat.AddCard(root,f.P);await CardPileCmd.Add(root,PileType.Draw,skipVisuals:true);await CardCmd.Exhaust(f.Choice,root,false);
    var copy=LagRoots(f.P).Single(c=>c!=root);Check("lag fused full hand overflows whole upgraded copy without sly recursion",copy.Pile?.Type==PileType.Discard&&LagFusionState(root)==LagFusionState(copy)&&LagRoots(f.P).Length==2);
    foreach(var c in f.P.PlayerCombatState!.Hand.Cards.ToArray())c.RemoveFromCurrentPile();for(int i=0;i<3;i++){await CardCmd.Exhaust(f.Choice,copy,false);copy=LagRoots(f.P).Single(c=>c.Pile?.Type==PileType.Hand);Check("lag fused chained exhaust grows once preserves upgrades cycle="+i,LagRoots(f.P).Length==i+3&&LagFusionState(copy)==LagFusionState(root));}
   });
   await Case("native manual play",async()=>{
    using var f=NewDongniFixture();var root=Fuse(f.Card<Pack.LagCurse>(),Up(f.Card<Bash>()));f.Combat.AddCard(root,f.P);f.P.PlayerCombatState!.Energy=20;await DongniManualPlay(f,root,f.Enemy);var copies=LagRoots(f.P).Where(c=>c!=root).ToArray();Check("lag fused native play exhaust makes whole copy",root.Pile?.Type==PileType.Exhaust&&copies.Length==1&&LagFusionState(copies[0])==LagFusionState(root));
   });
   foreach(int steps in new[]{1,2})foreach(bool transform in new[]{false,true})await Case($"upgrade parent {steps}/{transform}",async()=>{
    using var f=NewDongniFixture();var root=Fuse(f.Card<Pack.LagCurse>(),f.Card<Bash>());Up(root,steps);root=Restore(root);f.Combat.AddCard(root,f.P);await CardPileCmd.Add(root,PileType.Hand,skipVisuals:true);string state=LagFusionState(root);
    if(transform)await CardCmd.Transform(root,f.Card<DefendIronclad>(),CardPreviewStyle.None);else await CardCmd.Exhaust(f.Choice,root,false);
    var copy=LagRoots(f.P).Single(c=>c!=root);AssertLagClone(root,copy,f.P,$"lag upgraded parent {steps}/{transform}");Check($"lag parent upgraded before readback retains grade {steps}/{transform}",copy.CurrentUpgradeLevel==steps&&LagFusionState(copy)==state);
   });
   foreach(int level in new[]{0,1})await Case("native sly "+level,async()=>{
    using var f=NewDongniFixture();var root=Fuse(Up(f.Card<Pack.LagCurse>(),level),Up(f.Card<Bash>()));f.Combat.AddCard(root,f.P);await CardPileCmd.Add(root,PileType.Hand,skipVisuals:true);f.P.PlayerCombatState!.Energy=0;
    await CardCmd.Discard(f.Choice,root);var copies=LagRoots(f.P).Where(c=>c!=root).ToArray();Check("lag fused native sly creates whole copy only if exhausted level="+level,copies.Length==(root.Pile?.Type==PileType.Exhaust?1:0)&&copies.All(c=>LagFusionState(c)==LagFusionState(root))&&f.P.PlayerCombatState.Energy==0);
   });
   await Case("multiple lag materials",async()=>{
    using var f=NewDongniFixture();var root=Fuse(f.Card<Pack.LagCurse>(),Up(f.Card<Pack.LagCurse>()));f.Combat.AddCard(root,f.P);await CardPileCmd.Add(root,PileType.Hand,skipVisuals:true);await CardCmd.Exhaust(f.Choice,root,false);var copies=LagRoots(f.P).Where(c=>c!=root).ToArray();Check("lag fused repeated material forwards cannot duplicate root event",copies.Length==1&&LagFusionState(copies[0])==LagFusionState(root));
   });
   await Case("unrelated fusion",async()=>{
    using var f=NewDongniFixture();var root=Fuse(Up(f.Card<Bash>()),Up(f.Card<DefendIronclad>()));f.Combat.AddCard(root,f.P);await CardPileCmd.Add(root,PileType.Hand,skipVisuals:true);await CardCmd.Exhaust(f.Choice,root,false);Check("lag fused unrelated exhaust no copies",LagRoots(f.P).Length==0);
   });
   await Case("outside battle",async()=>{
    using var f=NewDongniFixture();var root=Fuse(Up(f.Run.CreateCard<Pack.LagCurse>(f.P)),Up(f.Run.CreateCard<Bash>(f.P)));f.Run.AddCard(root,f.P);await CardPileCmd.Add(root,PileType.Deck);CombatManager.Instance.Reset(false);f.Combat.RemoveCreature(f.P.Creature);int count=f.P.Deck.Cards.Count;await CardCmd.Transform(root,f.Run.CreateCard<DefendIronclad>(f.P),CardPreviewStyle.None);Check("lag fused outside battle transform no duplicate",f.P.Deck.Cards.Count==count&&!f.P.Deck.Cards.Any(HasLag)&&!LagRoots(f.P).Any());
   });
  }finally{modeField.SetValue(null,previous);}
  File.WriteAllText(Output+"/lag-fusion-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Errors=errors,Checks=passes.Skip(start)},new JsonSerializerOptions{WriteIndented=true}));
  if(errors.Count>0)throw new Exception(string.Join("\n",errors));
 }
}
