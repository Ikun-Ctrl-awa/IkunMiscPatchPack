using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Powers;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static Soul[] RitualSouls(BloodFixture f)=>f.P.PlayerCombatState!.AllCards.OfType<Soul>().ToArray();
 static void ClearRitualSouls(BloodFixture f){foreach(var soul in RitualSouls(f))soul.RemoveFromCurrentPile();}
 static async Task SoulRitualChecks(){
  foreach(int level in new[]{0,1})foreach(int x in new[]{0,1,3})foreach(bool chemical in new[]{false,true}){
   using var f=XFixture(typeof(Pack.SoulRitual),true);var card=XCard(f,typeof(Pack.SoulRitual),level);if(chemical)f.P.AddRelicInternal(ModelDb.Relic<ChemicalX>().ToMutable(),silent:true);
   int count=x+(chemical?2:0);f.P.PlayerCombatState!.Energy=x;await DongniManualPlay(f,card);
   string tag=$"X ritual level={level} x={x} chemical={chemical}";
   var souls=RitualSouls(f);
   Check(tag+" native captured payment immediate refund and power pile",card.EnergyCost.CapturedXValue==x&&f.P.PlayerCombatState.Energy==1+level&&card.Pile==null);
   Check(tag+" initial upgraded ethereal soul count",souls.Length==count&&souls.All(c=>c.CurrentUpgradeLevel==level&&c.Keywords.Contains(CardKeyword.Ethereal)&&c.Keywords.Contains(CardKeyword.Exhaust)&&c.Pile?.Type==PileType.Hand&&c.Owner==f.P));
   Check(tag+" locked power count and quality",f.P.Creature.GetPowerAmount<Pack.SoulRitualPower>()==(level==0?count:0)&&f.P.Creature.GetPowerAmount<Pack.SoulRitualPlusPower>()==(level==1?count:0));
   foreach(var power in f.P.Creature.Powers.OfType<Pack.SoulRitualPowerBase>())Check(tag+" power local title and icons",power.Title.GetFormattedText().Contains("灵魂祭仪")&&power.GetDumbHoverTip().Description.Contains(count+"张")&&power.Icon!=null&&power.BigIcon!=null);
   await QuEnd(f);Check(tag+" native unplayed ethereal souls exhaust at turn end",souls.All(c=>c.Pile?.Type==PileType.Exhaust));ClearRitualSouls(f);
   await Hook.BeforeHandDraw(f.Combat,f.Other,f.Choice);Check(tag+" no generation on other player turn",RitualSouls(f).Length==0&&!f.Other.PlayerCombatState!.AllCards.OfType<Soul>().Any());
   foreach(int laterEnergy in new[]{0,2,7}){
    f.P.PlayerCombatState.Energy=laterEnergy;await Hook.BeforeHandDraw(f.Combat,f.P,f.Choice);souls=RitualSouls(f);
    Check(tag+" own later turn fixed original X energy="+laterEnergy,souls.Length==count&&souls.All(c=>c.CurrentUpgradeLevel==level&&c.Keywords.Contains(CardKeyword.Ethereal))&&f.P.PlayerCombatState.Energy==laterEnergy);
    ClearRitualSouls(f);
   }
  }
  // 普通和升级祭仪分别叠加数量，不能互相改变灵魂的升级状态。
  using(var f=XFixture(typeof(Pack.SoulRitual))){
   foreach(var (level,x) in new[]{(0,2),(1,3),(0,1)}){f.P.PlayerCombatState!.Energy=x;await DongniManualPlay(f,XCard(f,typeof(Pack.SoulRitual),level));ClearRitualSouls(f);}
   int energy=f.P.PlayerCombatState!.Energy;await Hook.BeforeHandDraw(f.Combat,f.P,f.Choice);var souls=RitualSouls(f);
   Check("X ritual mixed casts stack preserving both qualities",souls.Length==6&&souls.Count(c=>c.CurrentUpgradeLevel==0)==3&&souls.Count(c=>c.CurrentUpgradeLevel==1)==3&&souls.All(c=>c.Keywords.Contains(CardKeyword.Ethereal))&&f.P.Creature.GetPowerAmount<Pack.SoulRitualPower>()==3&&f.P.Creature.GetPowerAmount<Pack.SoulRitualPlusPower>()==3);
   Check("X ritual later turns do not repeat immediate energy",f.P.PlayerCombatState.Energy==energy);
  }
  foreach(int level in new[]{0,1}){
   using var f=XFixture(typeof(Pack.SoulRitual));f.P.PlayerCombatState!.Energy=2;await DongniManualPlay(f,XCard(f,typeof(Pack.SoulRitual),level));
   for(int i=0;i<5;i++)await CardPileCmd.Add(f.Card<StrikeNecrobinder>(),PileType.Draw,skipVisuals:true);
   var souls=RitualSouls(f);int hand=f.P.PlayerCombatState.Hand.Cards.Count;await DongniManualPlay(f,souls[0]);
   Check("X ritual generated native soul draws and exhausts level="+level,souls[0].Pile?.Type==PileType.Exhaust&&f.P.PlayerCombatState.Hand.Cards.Count==hand-1+2+level&&f.P.PlayerCombatState.DrawPile.Cards.Count==3-level);
   await QuEnd(f);Check("X ritual remaining soul expires level="+level,souls[1].Pile?.Type==PileType.Exhaust);
   foreach(var c in f.P.PlayerCombatState.AllCards.ToArray())c.RemoveFromCurrentPile();
   f.P.PlayerCombatState.IncrementTurnNumber();await ImbuedPhase(f,setup:true);
   Check("X ritual actual native turn setup generates once level="+level,RitualSouls(f).Length==2&&RitualSouls(f).All(c=>c.CurrentUpgradeLevel==level&&c.Keywords.Contains(CardKeyword.Ethereal)));
  }
  using(var f=XFixture(typeof(Pack.SoulRitual))){
   for(int i=0;i<CardPile.MaxCardsInHand;i++)await CardPileCmd.Add(f.Card<StrikeNecrobinder>(),PileType.Hand,skipVisuals:true);
   f.P.PlayerCombatState!.Energy=3;await CardCmd.AutoPlay(f.Choice,XCard(f,typeof(Pack.SoulRitual),1),null,skipCardPileVisuals:true);var souls=RitualSouls(f);
   Check("X ritual full hand native overflow preserves upgrades and ethereal",f.P.PlayerCombatState.Hand.Cards.Count==CardPile.MaxCardsInHand&&souls.Length==3&&souls.All(c=>c.Pile?.Type==PileType.Discard&&c.CurrentUpgradeLevel==1&&c.Keywords.Contains(CardKeyword.Ethereal))&&f.P.PlayerCombatState.Energy==5);
  }
  using(var f=XFixture(typeof(Pack.SoulRitual))){
   await DongniCurseInHand(f);var card=XCard(f,typeof(Pack.SoulRitual));f.P.PlayerCombatState!.Energy=3;await DongniManualPlay(f,card);
   Check("X ritual curse defers all immediate effects",RitualSouls(f).Length==0&&f.P.PlayerCombatState.Energy==0&&card.Pile==null&&Pack.DongniCurseRules.PendingCount(f.P)==1);
   f.P.PlayerCombatState.Energy=1;await DongniNext(f);
   Check("X ritual curse resolves saved X and one refund",RitualSouls(f).Length==3&&f.P.Creature.GetPowerAmount<Pack.SoulRitualPower>()==3&&f.P.PlayerCombatState.Energy==2&&Pack.DongniCurseRules.PendingCount(f.P)==0);
  }
  using(var f=XFixture(typeof(Pack.SoulRitual))){
   await DongniCurseInHand(f,1);f.P.PlayerCombatState!.Energy=2;await DongniManualPlay(f,XCard(f,typeof(Pack.SoulRitual),1));
   Check("X ritual upgraded curse delays energy only",RitualSouls(f).Length==2&&RitualSouls(f).All(c=>c.CurrentUpgradeLevel==1)&&f.P.PlayerCombatState.Energy==0&&f.P.Creature.GetPowerAmount<Pack.SoulRitualPlusPower>()==2);
   f.P.PlayerCombatState.Energy=1;await Hook.AfterEnergyReset(f.Combat,f.P);await DongniNext(f);
   Check("X ritual upgraded curse energy delivered once",f.P.PlayerCombatState.Energy==3&&!f.P.Creature.HasPower<EnergyNextTurnPower>());
  }
 }
}
