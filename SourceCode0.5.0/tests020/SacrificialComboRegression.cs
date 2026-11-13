using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static readonly List<AttackCommand> comboAttacks=new();
 static void ObserveComboAttack(AttackCommand __instance,ref Task<AttackCommand> __result){
  if(__instance.ModelSource is Pack.SacrificialCombo)__result=FinishComboAttack(__result);
 }
 static async Task<AttackCommand> FinishComboAttack(Task<AttackCommand> original){var attack=await original;comboAttacks.Add(attack);return attack;}
 static async Task SacrificialComboChecks(){
  var h=new Harmony("Aikun020Probe.SacrificialCombo");h.Patch(AccessTools.Method(typeof(AttackCommand),nameof(AttackCommand.Execute)),postfix:new HarmonyMethod(typeof(Entry),nameof(ObserveComboAttack)));
  try{
   foreach(int level in new[]{0,1})foreach(int x in new[]{0,1,3})foreach(bool chemical in new[]{false,true})foreach(int take in new[]{0,1,-1}){
    using var f=XFixture(typeof(Pack.SacrificialCombo),true);var card=XCard(f,typeof(Pack.SacrificialCombo),level);
    if(chemical)f.P.AddRelicInternal(ModelDb.Relic<ChemicalX>().ToMutable(),silent:true);
    var eligible=new List<CardModel>();var attacks=new List<CardModel>();
    foreach(var pile in new[]{PileType.Hand,PileType.Draw,PileType.Discard}){
     CardModel c=pile==PileType.Hand?f.Card<DefendIronclad>():pile==PileType.Draw?f.Card<Inflame>():f.Card<Injury>();
     eligible.Add(c);await CardPileCmd.Add(c,pile,skipVisuals:true);
     var second=f.Card<DefendIronclad>();eligible.Add(second);await CardPileCmd.Add(second,pile,skipVisuals:true);
     var attack=f.Card<StrikeIronclad>();attacks.Add(attack);await CardPileCmd.Add(attack,pile,skipVisuals:true);
    }
    var foreign=f.Card<DefendIronclad>(f.Other);await CardPileCmd.Add(foreign,PileType.Hand,skipVisuals:true);
    var already=f.Card<DefendIronclad>();await CardPileCmd.Add(already,PileType.Exhaust,skipVisuals:true);
    var originalPiles=eligible.ToDictionary(c=>c,c=>c.Pile!.Type);var deck=f.P.Deck.Cards.ToArray();
    await PowerCmd.Apply<StrengthPower>(f.Choice,f.P.Creature,2,f.P.Creature,null);
    await PowerCmd.Apply<FlexPotionPower>(f.Choice,f.P.Creature,3,f.P.Creature,null);
    var selector=new DisgustSelector(take);int hits=x+(chemical?2:0)+level,max=Math.Min(hits,eligible.Count),selected=take<0?max:Math.Min(take,max);
    f.P.PlayerCombatState!.Energy=x;comboAttacks.Clear();int hp=f.Enemy.CurrentHp;
    using(CardSelectCmd.UseSelector(selector))await DongniManualPlay(f,card,f.Enemy);
    string tag=$"X combo level={level} x={x} chemical={chemical} choose={take}";
    Check(tag+" native payment and discard",card.EnergyCost.CapturedXValue==x&&f.P.PlayerCombatState.Energy==0&&card.Pile?.Type==PileType.Discard);
    Check(tag+" optional filtered three-pile selection",selector.Calls==(max==0?0:1)&&(max==0||(selector.Min==0&&selector.Max==max&&selector.Seen.ToHashSet().SetEquals(eligible))));
    Check(tag+" exhaust actual selected only",eligible.Count(c=>c.Pile?.Type==PileType.Exhaust)==selected&&eligible.Where(c=>!selector.Chosen.Contains(c)).All(c=>c.Pile?.Type==originalPiles[c])&&attacks.All(c=>c.Pile?.Type!=PileType.Exhaust)&&foreign.Pile?.Type==PileType.Hand&&already.Pile?.Type==PileType.Exhaust);
    Check(tag+" strength before exact native hits",f.P.Creature.GetPowerAmount<StrengthPower>()==5+selected&&f.P.Creature.GetPowerAmount<Pack.SacrificialComboStrengthPower>()==selected&&hp-f.Enemy.CurrentHp==(6+selected)*hits&&comboAttacks.Count==1&&comboAttacks[0].Results.Count()==hits);
    Check(tag+" permanent deck unchanged",f.P.Deck.Cards.SequenceEqual(deck));
    if(selected>0){
     var power=f.P.Creature.GetPower<Pack.SacrificialComboStrengthPower>()!;
     await power.AfterSideTurnEnd(f.Choice,f.Other.Creature.Side,[f.Other.Creature]);
     Check(tag+" other turn leaves temporary strength",f.P.Creature.GetPowerAmount<StrengthPower>()==5+selected);
     await power.AfterSideTurnEnd(f.Choice,f.P.Creature.Side,[f.P.Creature]);
     Check(tag+" own turn removes only combo strength",f.P.Creature.GetPowerAmount<StrengthPower>()==5&&!f.P.Creature.HasPower<Pack.SacrificialComboStrengthPower>());
    }
    await f.P.Creature.GetPower<FlexPotionPower>()!.AfterSideTurnEnd(f.Choice,f.P.Creature.Side,[f.P.Creature]);
    Check(tag+" independent native temporary strength cleanup",f.P.Creature.GetPowerAmount<StrengthPower>()==2);
   }
   // 没有可消耗牌时不弹窗，仍按X次数攻击；消耗数量不控制攻击次数。
   using(var f=XFixture(typeof(Pack.SacrificialCombo))){
    var only=f.Card<StrikeIronclad>();await CardPileCmd.Add(only,PileType.Hand,skipVisuals:true);var selector=new DisgustSelector(-1);f.P.PlayerCombatState!.Energy=3;int hp=f.Enemy.CurrentHp;
    using(CardSelectCmd.UseSelector(selector))await DongniManualPlay(f,XCard(f,typeof(Pack.SacrificialCombo)),f.Enemy);
    Check("X combo no eligible cards still attacks X times",selector.Calls==0&&hp-f.Enemy.CurrentHp==3&&!f.P.Creature.HasPower<Pack.SacrificialComboStrengthPower>()&&only.Pile?.Type==PileType.Hand);
   }
   foreach(int level in new[]{0,1}){
    using var f=XFixture(typeof(Pack.SacrificialCombo));var eligible=f.Card<DefendIronclad>();await CardPileCmd.Add(eligible,PileType.Draw,skipVisuals:true);
    var selector=new DisgustSelector(-1);var card=XCard(f,typeof(Pack.SacrificialCombo),level);f.P.PlayerCombatState!.Energy=5;int hp=f.Enemy.CurrentHp;
    using(CardSelectCmd.UseSelector(selector))await DongniManualPlay(f,card,f.Enemy);
    Check("X combo insufficient candidates level="+level,selector.Min==0&&selector.Max==1&&eligible.Pile?.Type==PileType.Exhaust&&hp-f.Enemy.CurrentHp==2*(5+level));
   }
   // 到期时只执行原先捕获的X，选择的是兑现当时的三个战斗牌堆。
   using(var f=XFixture(typeof(Pack.SacrificialCombo))){
    await DongniCurseInHand(f);var card=XCard(f,typeof(Pack.SacrificialCombo),1);f.P.PlayerCombatState!.Energy=3;
    var selector=new DisgustSelector(0);int hp=f.Enemy.CurrentHp;
    using(CardSelectCmd.UseSelector(selector))await DongniManualPlay(f,card,f.Enemy);
    Check("X combo curse initially defers without selection",selector.Calls==0&&Pack.DongniCurseRules.PendingCount(f.P)==1&&hp==f.Enemy.CurrentHp&&card.Pile?.Type==PileType.Discard);
    // 兑现前移走诅咒，加入一张当时存在的非攻击牌。
    foreach(var c in f.P.PlayerCombatState.Hand.Cards.ToArray())await CardPileCmd.Add(c,PileType.Discard,skipVisuals:true);
    var fresh=f.Card<DefendIronclad>();await CardPileCmd.Add(fresh,PileType.Draw,skipVisuals:true);selector=new DisgustSelector(-1);f.P.PlayerCombatState.Energy=1;
    using(CardSelectCmd.UseSelector(selector))await DongniNext(f);
    Check("X combo delayed original X plus upgrade",selector.Max==2&&selector.Chosen.Length==2&&fresh.Pile?.Type==PileType.Exhaust&&hp-f.Enemy.CurrentHp==12&&f.P.PlayerCombatState.Energy==1&&Pack.DongniCurseRules.PendingCount(f.P)==0);
   }
   using(var f=XFixture(typeof(Pack.SacrificialCombo))){
    var c=f.Card<DefendIronclad>();await CardPileCmd.Add(c,PileType.Hand,skipVisuals:true);var selector=new DisgustSelector(-1);f.P.PlayerCombatState!.Energy=2;int hp=f.Enemy.CurrentHp;
    using(CardSelectCmd.UseSelector(selector))await CardCmd.AutoPlay(f.Choice,XCard(f,typeof(Pack.SacrificialCombo),1),f.Enemy,skipCardPileVisuals:true);
    Check("X combo native free autoplay captures X",f.P.PlayerCombatState.Energy==2&&selector.Max==1&&hp-f.Enemy.CurrentHp==6);
   }
  }finally{h.UnpatchAll(h.Id);comboAttacks.Clear();}
 }
}
