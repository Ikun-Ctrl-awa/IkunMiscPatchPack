using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using Pack=AikunMiscPatchPack;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace Aikun020Probe;
public static partial class Entry {
 static async Task BloodBlockSourceChecks(){
  int start=passes.Count;var evidence=new List<object>();
  Check("blood block correction installed",Pack.RuntimeLoader.States.GetValueOrDefault("blood_sect")=="已应用。");
  foreach(int level in new[]{0,1,2,5}){
   using var f=new BloodFixture(multiplayer:true);var card=f.Card<Pack.BloodSect>();
   if(level<=1||!Minimal){if(level>=2)Scorch(card);Up(card,level);}
   await f.Play(card);var blood=f.P.Creature.GetPower<Pack.BloodSectPower>()!;
   if(Minimal&&level>=2){blood.SetAmount(100+20*Math.Max(0,level-1));blood.RegisterUpgrade(level);}
   string label="blood block all levels n="+level;
   string description=((StringVar)blood.DynamicVars["EffectText"]).StringValue;
   Check(label+" description allows potions and powers",description.Contains("药水与状态效果提供的格挡不受影响")&&!description.Contains("你无法获得"));
   if(!Minimal||level<=1)Check(label+" card description agrees with state",card.GetDescriptionForPile(PileType.None).Contains("药水与状态效果提供的格挡不受影响"));
   await f.Play(f.Card<DefendIronclad>());Check(label+" own card direct block denied",f.P.Creature.Block==0);
   var relic=(Anchor)ModelDb.Relic<Anchor>().ToMutable();f.P.AddRelicInternal(relic,silent:true);await relic.BeforeCombatStart();Check(label+" own relic direct block denied",f.P.Creature.Block==0);
   var ally=f.Card<DefendIronclad>(f.Other);await CreatureCmd.GainBlock(f.P.Creature,7,ValueProp.Move,BloodPlay(ally,f.P.Creature));Check(label+" ally direct block allowed",f.P.Creature.Block==7);f.P.Creature.LoseBlockInternal(9999);
   var potion=ModelDb.Potion<BlockPotion>().ToMutable();await (Task)AccessTools.Method(typeof(BlockPotion),"OnUse").Invoke(potion,[f.Choice,f.P.Creature])!;Check(label+" block potion gives twelve",f.P.Creature.Block==12);f.P.Creature.LoseBlockInternal(9999);
   // 自己的牌实际附加覆甲后，覆甲自己结算格挡；不能追溯回该牌再拦截。
   await f.Play(f.Card<EternalArmor>());var ownPlating=f.P.Creature.GetPower<PlatingPower>()!;int plated=ownPlating.Amount;
   await ownPlating.BeforeSideTurnEndEarly(f.Choice,CombatSide.Player,[f.P.Creature]);Check(label+" own card granted plating gives its full amount",plated>0&&f.P.Creature.Block==plated);f.P.Creature.LoseBlockInternal(9999);
   // 同一覆甲上叠加药水，所有层数均按状态正常结算。
   var iron=ModelDb.Potion<HeartOfIron>().ToMutable();iron.Owner=f.P;await (Task)AccessTools.Method(typeof(HeartOfIron),"OnUse").Invoke(iron,[f.Choice,f.P.Creature])!;
   var combined=f.P.Creature.GetPower<PlatingPower>()!;await combined.BeforeSideTurnEndEarly(f.Choice,CombatSide.Player,[f.P.Creature]);Check(label+" mixed card and potion plating fully allowed",combined.Amount==plated+7&&f.P.Creature.Block==combined.Amount);f.P.Creature.LoseBlockInternal(9999);await PowerCmd.Remove(combined);
   foreach(string name in new[]{"ActsFromThePast.Powers.MetallicizePower","ActsFromThePast.Powers.PlatedArmorPower"}){
    var type=FusionQuestBlessingFix.Entry.Find(name);
    if(type==null){Check(label+" optional armor effect safely absent "+name,Minimal);continue;}
    var power=ModelDb.GetById<PowerModel>(ModelDb.GetId(type)).ToMutable();await PowerCmd.Apply(f.Choice,power,f.P.Creature,4,f.P.Creature,card);
    await power.BeforeSideTurnEndEarly(f.Choice,CombatSide.Player,[f.P.Creature]);Check(label+" optional armor effect gives four "+name,f.P.Creature.Block==4);f.P.Creature.LoseBlockInternal(9999);await PowerCmd.Remove(power);
   }
   // 退出状态及药水回调后，自己的直接格挡仍禁止，来源作用域不能泄漏。
   await f.Play(f.Card<DefendIronclad>());Check(label+" own direct block still denied after power callbacks",f.P.Creature.Block==0);
   await CreatureCmd.GainBlock(f.P.Creature,6,ValueProp.Unpowered,null);Check(label+" unowned block allowed",f.P.Creature.Block==6);f.P.Creature.LoseBlockInternal(9999);
   f.P.Creature.SetCurrentHpInternal(95);await f.Damage(f.Enemy,10);int pct=100+20*Math.Max(0,level-1);Check(label+" leech upgrade and overflow unchanged",f.P.Creature.CurrentHp==100&&f.Pool==(level>=1?10*pct/100-5:0));
   evidence.Add(new{Level=level,LeechPercent=blood.LeechPercent,PlatingFromCard=plated,PlatingWithPotion=plated+7,Description=description});
  }
  // 格挡来源修正后，继续检查本轮相关的临时血池与怪物交互。
  await BloodMonsterChecks(evidence);
  await BloodArtChecks();
  File.WriteAllText(Output+"/blood-block-sources-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Failed=0,Checks=passes.Skip(start),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
 }
}
