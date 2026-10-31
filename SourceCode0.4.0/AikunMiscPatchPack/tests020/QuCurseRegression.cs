using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.addons.mega_text;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static string QuText(string value)=>System.Text.RegularExpressions.Regex.Replace(value,@"\[[^\]]*\]","");
 static async Task QuEnd(BloodFixture f,Player? player=null){
  var turn=AccessTools.Field(typeof(CombatManager),"_turnState").GetValue(CombatManager.Instance)!;
  await ((Task)AccessTools.Method(typeof(CombatManager),"DoTurnEnd").Invoke(CombatManager.Instance,[turn,player??f.P,f.Choice])!).WaitAsync(TimeSpan.FromSeconds(8));
 }
 static async Task QuCurseChecks(){
  int start=passes.Count;var evidence=new List<object>();var canonical=ModelDb.Card<Pack.QuCurse>();
  Check("qu independent default-on miscellaneous without extra mod dependency",Pack.Options.All.Single(o=>o.Key==Pack.QuCurseContent.Key) is {Misc:true,Required.Length:0}&&new Pack.PackConfig().Get(Pack.QuCurseContent.Key));
  Check("qu independent miscellaneous with unique option keys",Pack.Options.All.Single(o=>o.Key==Pack.QuCurseContent.Key).Misc&&Pack.Options.All.Select(o=>o.Key).Distinct().Count()==Pack.Options.All.Length);
  Check("qu curse pool rarity type and unplayable keyword",canonical.Pool is CurseCardPool&&canonical.Rarity==CardRarity.Curse&&canonical.Type==CardType.Curse&&canonical.Keywords.Contains(CardKeyword.Unplayable)&&canonical.EnergyCost.Canonical==-1);
  Check("qu native once-upgradable curse and in-hand end effect",canonical.MaxUpgradeLevel==1&&canonical.IsUpgradable&&canonical.HasTurnEndInHandEffect);
  Check("qu in ordinary random curse catalog",canonical.Pool.AllCards.Any(c=>c is Pack.QuCurse)&&canonical.Pool.GetUnlockedCards(UnlockState.all,CardMultiplayerConstraint.None).Any(c=>c is Pack.QuCurse)&&canonical.CanBeGeneratedInCombat&&canonical.CanBeGeneratedByModifiers);
  Check("qu not added to ordinary colorless or mod-form card rewards",!ModelDb.CardPool<ColorlessCardPool>().AllCards.Any(c=>c is Pack.QuCurse)&&!Pack.ModFormContent.IsCandidate(canonical)&&!Pack.ModFormContent.Groups.SelectMany(g=>g.Cards).Any(c=>c is Pack.QuCurse));
  foreach(int level in new[]{0,1}){
   int expected=level==0?3:1;var card=Up(canonical.ToMutable(),level);string tag="qu level="+level;
   Check(tag+" exact damage and upgrade eligibility",card.DynamicVars.Damage.IntValue==expected&&card.IsUpgradable==(level==0)&&card.Keywords.Contains(CardKeyword.Unplayable));
   Check(tag+" native formatted Chinese description",card.Title==(level==0?"区":"区+")&&QuText(card.GetDescriptionForPile(PileType.None)).Contains(expected+"点伤害")&&QuText(card.GetDescriptionForPile(PileType.None)).Contains(QuText(new LocString("card_keywords","UNPLAYABLE.title").GetFormattedText()))&&QuText(card.GetDescriptionForPile(PileType.None)).Contains("升级"));
   for(int i=0;i<3;i++){card=Restore(card);Check(tag+" repeated card save roundtrip "+i,card.CurrentUpgradeLevel==level&&card.DynamicVars.Damage.IntValue==expected&&card.Type==CardType.Curse&&card.HasTurnEndInHandEffect&&card.Keywords.Contains(CardKeyword.Unplayable));}
   var clone=(CardModel)card.ClonePreservingMutability();Check(tag+" clone independent variables exact amount",clone.DynamicVars.Damage.IntValue==expected&&!ReferenceEquals(clone.DynamicVars,card.DynamicVars));
   foreach(var pile in new[]{PileType.Hand,PileType.Draw,PileType.Discard,PileType.Exhaust}){
    using var f=new BloodFixture();var own=Up(f.Card<Pack.QuCurse>(),level);await CardPileCmd.Add(own,pile,skipVisuals:true);
    if(pile==PileType.Hand)Check(tag+" actual native play rejected",!own.CanPlay());
    int hp=f.P.Creature.CurrentHp;await QuEnd(f);
    Check(tag+" native end-hand selection pile="+pile,hp-f.P.Creature.CurrentHp==(pile==PileType.Hand?expected:0));
    Check(tag+" native end resolution puts hand curse in discard pile="+pile,own.Pile?.Type==(pile==PileType.Hand?PileType.Discard:pile));
    await QuEnd(f);Check(tag+" no duplicate trigger once in discard pile="+pile,hp-f.P.Creature.CurrentHp==(pile==PileType.Hand?expected:0));
    evidence.Add(new{Level=level,Pile=pile.ToString(),ActualDamage=hp-f.P.Creature.CurrentHp});
   }
   foreach(int block in new[]{1,5}){
    using var f=new BloodFixture();var own=Up(f.Card<Pack.QuCurse>(),level);await CardPileCmd.Add(own,PileType.Hand,skipVisuals:true);f.P.Creature.GainBlockInternal(block);int hp=f.P.Creature.CurrentHp;await QuEnd(f);
    Check(tag+" actual damage respects block="+block,hp-f.P.Creature.CurrentHp==Math.Max(0,expected-block));
   }
   using(var f=new BloodFixture()){
    var own=Up(f.Card<Pack.QuCurse>(),level);await CardPileCmd.Add(own,PileType.Hand,skipVisuals:true);await PowerCmd.Apply<IntangiblePower>(f.Choice,f.P.Creature,1,f.P.Creature,null);int hp=f.P.Creature.CurrentHp;await QuEnd(f);
    Check(tag+" actual damage respects native intangible",hp-f.P.Creature.CurrentHp==1);
   }
  }
  using(var f=new BloodFixture(multiplayer:true)){
   var first=f.Card<Pack.QuCurse>();var upgraded=Up(f.Card<Pack.QuCurse>());var other=f.Card<Pack.QuCurse>(f.Other);
   foreach(var card in new CardModel[]{first,upgraded,other})await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);
   int hp=f.P.Creature.CurrentHp,otherHp=f.Other.Creature.CurrentHp;await QuEnd(f);
   Check("qu two copies resolve each exactly once",hp-f.P.Creature.CurrentHp==4&&first.Pile?.Type==PileType.Discard&&upgraded.Pile?.Type==PileType.Discard);
   Check("qu another player's hand does not trigger on owner end",f.Other.Creature.CurrentHp==otherHp&&other.Pile?.Type==PileType.Hand);
   await QuEnd(f,f.Other);Check("qu another player's own end resolves only own curse",f.Other.Creature.CurrentHp==otherHp-3&&f.P.Creature.CurrentHp==hp-4);
   await CardPileCmd.Add(first,PileType.Hand,skipVisuals:true);await QuEnd(f);Check("qu returned to hand can trigger next turn",f.P.Creature.CurrentHp==hp-7);
  }
  bool active=Pack.Settings.Startup[Pack.QuCurseContent.Key];
  try{
   Pack.Settings.Startup[Pack.QuCurseContent.Key]=false;
   Check("qu disabled only stops subsequent random acquisition",!canonical.CanBeGeneratedInCombat&&!canonical.CanBeGeneratedByModifiers&&!canonical.Pool.AllCards.Any(c=>c is Pack.QuCurse)&&!canonical.Pool.GetUnlockedCards(UnlockState.all,CardMultiplayerConstraint.None).Any(c=>c is Pack.QuCurse));
   using var f=new BloodFixture();var saved=Restore(Up(canonical.ToMutable()));f.Combat.AddCard(saved,f.P);await CardPileCmd.Add(saved,PileType.Hand,skipVisuals:true);int hp=f.P.Creature.CurrentHp;await QuEnd(f);
   Check("qu saved upgraded curse keeps working while acquisition disabled",hp-f.P.Creature.CurrentHp==1&&saved.CurrentUpgradeLevel==1);
  }finally{Pack.Settings.Startup[Pack.QuCurseContent.Key]=active;}
  if(!Minimal){
   using var f=new BloodFixture();var fused=Fuse(Up(canonical.ToMutable()),ModelDb.Card<DefendIronclad>().ToMutable());f.Combat.AddCard(fused,f.P);await CardPileCmd.Add(fused,PileType.Hand,skipVisuals:true);int hp=f.P.Creature.CurrentHp;await QuEnd(f);
   Check("qu upgraded fusion child resolves native end-hand damage",hp-f.P.Creature.CurrentHp==1&&fused.Pile?.Type==PileType.Discard);
  }
  await QuScreenshot();File.WriteAllText(Output+"/qu-curse-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Checks=passes.Skip(start).ToArray(),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
 }
 static async Task QuScreenshot(){
  using var f=new BloodFixture();var viewport=new SubViewport{Size=new(1300,1000),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("14101e")});bool test=TestMode.IsOn;
  try{
   for(int level=0;level<=1;level++){
    var card=Up(f.Card<Pack.QuCurse>(),level);TestMode.IsOn=false;var node=NCard.Create(card)!;host.AddChild(node);node.Position=new(360+580*level,450);node.Scale=Vector2.One*.95f;node.UpdateVisuals(PileType.None,CardPreviewMode.Normal);TestMode.IsOn=test;
    string text=QuText(node.GetNode<MegaRichTextLabel>("%DescriptionLabel").Text);
    Check("qu rendered native unplayable upgradable curse L"+level,text.Contains(QuText(new LocString("card_keywords","UNPLAYABLE.title").GetFormattedText()))&&text.Contains("升级")&&text.Contains(level==0?"3":"1"));
    Check("qu embedded portrait normal beta and reloadable resource L"+level,card.PortraitPath.StartsWith("user://AikunMiscPatchPack/card-portraits/")&&ResourceLoader.Exists(card.PortraitPath)&&card.Portrait.GetSize()==new Vector2(1536,1024)&&card.BetaPortraitPath==card.PortraitPath);
   }
   var source=f.Card<Pack.QuCurse>();TestMode.IsOn=false;var holder=NGridCardHolder.Create(NCard.Create(source)!)!;host.AddChild(holder);holder.Position=new(-300,-300);holder.SetIsPreviewingUpgrade(true);
   Check("qu actual native upgrade preview shows one damage",holder.CardNode!.Model!.CurrentUpgradeLevel==1&&holder.CardNode.Model.DynamicVars.Damage.IntValue==1&&source.CurrentUpgradeLevel==0&&source.DynamicVars.Damage.IntValue==3);holder.QueueFree();TestMode.IsOn=test;
   await Wait(.2);await AfterDraw();Check("qu normal and upgraded native screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/qu-curse-native.png")==Error.Ok);
  }finally{TestMode.IsOn=test;viewport.QueueFree();await Wait(.05);}
 }
}
