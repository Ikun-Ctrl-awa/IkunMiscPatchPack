using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.addons.mega_text;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static Pack.LagCurse[] LagCards(BloodFixture f)=>f.P.PlayerCombatState!.AllCards.OfType<Pack.LagCurse>().ToArray();
 static async Task TowerCurseChecks(){
  int start=passes.Count;var canonical=ModelDb.Card<Pack.LagCurse>();string key=Pack.TowerCurseContent.Key;
  Check("tower independent default on miscellaneous",Pack.Options.All.Single(o=>o.Key==key) is {Misc:true,Required.Length:0}&&new Pack.PackConfig().Get(key));
  Check("tower replaces old qu option with same total count",Pack.Options.All.Length==51&&Pack.Options.All.Count(o=>o.Misc)==10&&!Pack.Options.All.Any(o=>o.Key=="qu_curse")&&Pack.Options.All.Select(o=>o.Key).Distinct().Count()==51);
  var curses=new CardModel[]{ModelDb.Card<Pack.QuCurse>(),ModelDb.Card<Pack.DongniCurse>(),ModelDb.Card<Pack.DongniDisgust>(),canonical};
  bool tower=Pack.Settings.Startup[key],dongni=Pack.Settings.Startup[Pack.DongniContent.Key];
  try{
   foreach(bool a in new[]{false,true})foreach(bool b in new[]{false,true}){
    Pack.Settings.Startup[key]=a;Pack.Settings.Startup[Pack.DongniContent.Key]=b;
    var all=ModelDb.CardPool<CurseCardPool>().AllCards.ToArray();var unlocked=ModelDb.CardPool<CurseCardPool>().GetUnlockedCards(UnlockState.all,CardMultiplayerConstraint.None).ToArray();
    foreach(var c in curses)Check($"tower four curse acquisition independent {a}/{b}/"+c.Id,all.Contains(c)==a&&unlocked.Contains(c)==a&&c.CanBeGeneratedInCombat==a&&c.CanBeGeneratedByModifiers==a);
    foreach(var t in DongniTypes){var c=ModelDb.GetById<CardModel>(ModelDb.GetId(t));Check($"tower five noncurse dongni independent {a}/{b}/"+c.Id,c.Pool.AllCards.Contains(c)==b&&c.CanBeGeneratedInCombat==(b&&c.Rarity!=CardRarity.Token));}
   }
  }finally{Pack.Settings.Startup[key]=tower;Pack.Settings.Startup[Pack.DongniContent.Key]=dongni;}
  foreach(var c in curses)foreach(int level in new[]{0,1}){
   var copy=Up(c.ToMutable(),level);for(int i=0;i<3;i++)copy=Restore(copy);
   Check("tower moved and new card three DTO readbacks "+c.Id+"/"+level,copy.Id==c.Id&&copy.GetType()==c.GetType()&&copy.CurrentUpgradeLevel==level);
  }
  foreach(int level in new[]{0,1}){
   var c=Up(canonical.ToMutable(),level);
   Check("lag native keywords and cost level="+level,c.Type==CardType.Curse&&c.Rarity==CardRarity.Curse&&c.Pool is CurseCardPool&&c.EnergyCost.Canonical==3&&c.MaxUpgradeLevel==1&&c.IsUpgradable==(level==0)&&c.Keywords.SetEquals(level==0?new[]{CardKeyword.Retain,CardKeyword.Sly,CardKeyword.Exhaust}:new[]{CardKeyword.Retain,CardKeyword.Sly}));
   using(var f=NewDongniFixture()){
    var card=Up(f.Card<Pack.LagCurse>(),level);f.P.PlayerCombatState!.Energy=3;int deck=f.P.Deck.Cards.Count;
    await DongniManualPlay(f,card);var copies=LagCards(f).Where(x=>x!=card).ToArray();
    Check("lag manual three energy normal exhaust upgraded discard level="+level,f.P.PlayerCombatState.Energy==0&&card.Pile?.Type==(level==0?PileType.Exhaust:PileType.Discard));
    Check("lag manual exactly one copy only normal level="+level,copies.Length==(level==0?1:0)&&copies.All(x=>x.Pile?.Type==PileType.Hand&&x.CurrentUpgradeLevel==level&&!x.IsDupe)&&f.P.Deck.Cards.Count==deck);
   }
   using(var f=NewDongniFixture()){
    var card=Up(f.Card<Pack.LagCurse>(),level);await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);f.P.PlayerCombatState!.Energy=0;
    await CardCmd.Discard(f.Choice,card);var copies=LagCards(f).Where(x=>x!=card).ToArray();
    Check("lag native sly free discard auto play level="+level,f.P.PlayerCombatState.Energy==0&&card.Pile?.Type==(level==0?PileType.Exhaust:PileType.Discard)&&copies.Length==(level==0?1:0)&&copies.All(x=>x.Pile?.Type==PileType.Hand));
   }
   foreach(var pile in new[]{PileType.Hand,PileType.Draw,PileType.Discard}){
    using var f=NewDongniFixture(true);var card=Up(f.Card<Pack.LagCurse>(),level);await CardPileCmd.Add(card,pile,skipVisuals:true);int deck=f.P.Deck.Cards.Count;
    await CardCmd.Exhaust(f.Choice,card,false);var copy=LagCards(f).Single(x=>x!=card);
    Check($"lag actual exhaust one independent copy {level}/{pile}",card.Pile?.Type==PileType.Exhaust&&copy.Pile?.Type==PileType.Hand&&copy.CurrentUpgradeLevel==level&&copy.Keywords.SetEquals(card.Keywords)&&copy.Owner==f.P&&!copy.HasBeenRemovedFromState&&!copy.IsDupe&&f.P.Deck.Cards.Count==deck&&!f.Other.PlayerCombatState!.AllCards.OfType<Pack.LagCurse>().Any());
   }
   foreach(var pile in new[]{PileType.Hand,PileType.Draw,PileType.Discard,PileType.Exhaust}){
    using var f=NewDongniFixture();var card=Up(f.Card<Pack.LagCurse>(),level);await CardPileCmd.Add(card,pile,skipVisuals:true);var replacement=f.Card<DefendIronclad>();
    await CardCmd.Transform(card,replacement,CardPreviewStyle.None);var copy=LagCards(f).Single();
    Check($"lag single native transform exactly one copy {level}/{pile}",card.HasBeenRemovedFromState&&card.Pile==null&&replacement.Pile?.Type==pile&&copy.Pile?.Type==PileType.Hand&&copy.CurrentUpgradeLevel==level&&copy.Keywords.SetEquals(card.Keywords)&&!copy.HasBeenRemovedFromState);
   }
   using(var f=NewDongniFixture()){
    var card=Up(f.Card<Pack.LagCurse>(),level);await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);await QuEnd(f);
    Check("lag native retain end turn no phantom copies level="+level,card.Pile?.Type==PileType.Hand&&LagCards(f).Length==1);
   }
   using(var f=NewDongniFixture()){
    for(int i=0;i<CardPile.MaxCardsInHand;i++)await DongniPut(f,PileType.Hand);
    var card=Up(f.Card<Pack.LagCurse>(),level);await CardPileCmd.Add(card,PileType.Draw,skipVisuals:true);await CardCmd.Exhaust(f.Choice,card,false);var copy=LagCards(f).Single(x=>x!=card);
    Check("lag native full hand overflow no sly recursion level="+level,copy.Pile?.Type==PileType.Discard&&copy.CurrentUpgradeLevel==level&&f.P.PlayerCombatState!.Hand.Cards.Count==CardPile.MaxCardsInHand&&LagCards(f).Length==2);
   }
  }
  using(var f=NewDongniFixture()){
   var card=f.Card<Pack.LagCurse>();await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);
   for(int i=0;i<3;i++){await CardCmd.Exhaust(f.Choice,card,false);card=LagCards(f).Single(c=>c.Pile?.Type==PileType.Hand);Check("lag repeated exhaust grows one without infinite self loop cycle="+i,LagCards(f).Length==i+2&&f.P.PlayerCombatState!.ExhaustPile.Cards.Count==i+1);}
  }
  using(var f=NewDongniFixture()){
   var a=f.Card<Pack.LagCurse>();var b=Up(f.Card<Pack.LagCurse>());await CardPileCmd.Add(a,PileType.Draw,skipVisuals:true);await CardPileCmd.Add(b,PileType.Discard,skipVisuals:true);
   var x=f.Card<StrikeIronclad>();var y=f.Card<DefendIronclad>();await CardCmd.Transform([new CardTransformation(a,x),new CardTransformation(b,y)],null,CardPreviewStyle.None);
   var copies=LagCards(f);Check("lag bulk transform one copy each preserves mixed upgrades",copies.Length==2&&copies.All(c=>c.Pile?.Type==PileType.Hand)&&copies.Select(c=>c.CurrentUpgradeLevel).Order().SequenceEqual(new[]{0,1})&&x.Pile?.Type==PileType.Draw&&y.Pile?.Type==PileType.Discard);
   await CardCmd.Transform(Array.Empty<CardTransformation>(),null,CardPreviewStyle.None);Check("lag empty transform produces no copy",LagCards(f).Length==2);
   var other=f.Card<StrikeIronclad>();await CardPileCmd.Add(other,PileType.Draw,skipVisuals:true);await CardCmd.Transform(other,f.Card<DefendIronclad>(),CardPreviewStyle.None);Check("lag unrelated card transform produces no copy",LagCards(f).Length==2);
  }
  foreach(int level in new[]{0,1}){
   using var f=NewDongniFixture();var card=Up(f.Run.CreateCard<Pack.LagCurse>(f.P),level);await CardPileCmd.Add(card,PileType.Deck);
   CombatManager.Instance.Reset(false);f.Combat.RemoveCreature(f.P.Creature);
   Check("lag outside battle fixture has no combat level="+level,f.P.Creature.CombatState==null);
   int deck=f.P.Deck.Cards.Count;var replacement=f.Run.CreateCard<DefendIronclad>(f.P);await CardCmd.Transform(card,replacement,CardPreviewStyle.None);
   Check("lag outside combat transform no hand or deck duplicate level="+level,f.P.Deck.Cards.Count==deck&&!f.P.Deck.Cards.OfType<Pack.LagCurse>().Any()&&f.P.Deck.Cards.Contains(replacement)&&!LagCards(f).Any());
  }
  bool enabled=Pack.Settings.Startup[key];try{
   Pack.Settings.Startup[key]=false;using var f=NewDongniFixture();var card=f.Card<Pack.LagCurse>();await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);await CardCmd.Exhaust(f.Choice,card,false);
   Check("lag existing card still copies with acquisition disabled",LagCards(f).Length==2&&LagCards(f).Count(c=>c.Pile?.Type==PileType.Hand)==1);
  }finally{Pack.Settings.Startup[key]=enabled;}
  await LagScreenshot();
  File.WriteAllText(Output+"/tower-curses-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Checks=passes.Skip(start).ToArray(),Ids=curses.Select(c=>new{Type=c.GetType().FullName,Id=c.Id.ToString()})},new JsonSerializerOptions{WriteIndented=true}));
 }
 static async Task LagScreenshot(){
  using var f=NewDongniFixture();var viewport=new SubViewport{Size=new(1400,1000),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("15121c")});bool old=TestMode.IsOn;
  try{
   for(int level=0;level<=1;level++){
    var card=Up(f.Card<Pack.LagCurse>(),level);TestMode.IsOn=false;var node=NCard.Create(card)!;host.AddChild(node);node.Position=new(370+630*level,450);node.Scale=Vector2.One;node.UpdateVisuals(PileType.None,CardPreviewMode.Normal);TestMode.IsOn=old;
    string text=QuText(node.GetNode<MegaRichTextLabel>("%DescriptionLabel").Text);
    Check("lag rendered native retain sly correct exhaust level="+level,text.Contains("保留")&&text.Contains("奇巧")&&text.Contains("变化")&&text.Contains("复制品")&&text.Contains("消耗。")==(level==0)&&!text.Contains("不能被打出")&&ResourceLoader.Exists(card.PortraitPath));
   }
   await Wait(.2);await AfterDraw();Check("lag native two level screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/lag-curse-native.png")==Error.Ok);
  }finally{TestMode.IsOn=old;viewport.QueueFree();await Wait(.05);}
 }
}
