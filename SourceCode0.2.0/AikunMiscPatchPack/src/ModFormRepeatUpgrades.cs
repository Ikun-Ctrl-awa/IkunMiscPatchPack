using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace AikunMiscPatchPack;

// 附魔可通过上限或 IsUpgradable 两种接口允许重复升级；不依赖名字或本地化文案。
public static class ModFormRepeatUpgrades {
 static readonly FieldInfo Level=AccessTools.Field(typeof(CardModel),"_currentUpgradeLevel");
 static readonly FieldInfo Enchantment=AccessTools.Field(typeof(CardModel),"<Enchantment>k__BackingField");
 static readonly ConditionalWeakTable<EnchantmentModel,Rule> Rules=new();
 static MethodInfo? cloneExtras;
 sealed class Rule {public Rule(){} public int Amount=int.MinValue,Limit;public bool Unlimited;}
 public static void Install(Harmony harmony){
  cloneExtras=FusionQuestBlessingFix.Entry.Find("MoreEnchantmentsMod.MoreEnchantStack")?.GetMethod("CloneExtras",BindingFlags.Static|BindingFlags.NonPublic);
  if(cloneExtras==null)return;
  var method=AccessTools.Method(typeof(AbstractModel),nameof(AbstractModel.MutableClone));
  harmony.Patch(method,postfix:new HarmonyMethod(typeof(ModFormRepeatUpgrades),nameof(CopyExtras)){priority=Priority.Last,after=Harmony.GetPatchInfo(method)?.Owners.ToArray()??[]});
 }
 static void CopyExtras(AbstractModel __instance,AbstractModel __result){
  // 额外槽模组只拦截 ClonePreservingMutability；原生预览等直接克隆也必须保留槽位。
  if(__instance is ModForm source&&__result is ModForm target&&!ReferenceEquals(source,target))cloneExtras?.Invoke(null,[source,target,null]);
 }
 public static int Limit(CardModel card){
  int limit=0;
  foreach(var enchantment in EnchantmentView.Read(card)){
   if(enchantment.Status==EnchantmentStatus.Disabled)continue;
   var rule=Rules.GetOrCreateValue(enchantment);
   if(rule.Amount!=enchantment.Amount){
    // 只在独立探针上读取附魔的升级资格，不执行 ModifyCard、战斗或随机效果。
    var probe=ModelDb.Card<Bash>().ToMutable();int baseline=probe.MaxUpgradeLevel;
    Level.SetValue(probe,baseline);
    var copy=(EnchantmentModel)enchantment.ClonePreservingMutability();
    copy.ApplyInternal(probe,copy.Amount);Enchantment.SetValue(probe,copy);
    int cap=probe.MaxUpgradeLevel;
    rule.Limit=cap>baseline?cap:0;
    Level.SetValue(probe,Math.Max(baseline,cap));
    rule.Unlimited=probe.MaxUpgradeLevel>cap||probe.IsUpgradable;
    rule.Amount=enchantment.Amount;
   }
   // 有限附魔保留自己的上限；叠影的升级转为正常累加本牌等级，避免标题与效果分离。
   limit=Math.Max(limit,rule.Unlimited?Math.Max(rule.Limit,NextLimit(card.CurrentUpgradeLevel)):rule.Limit);
  }
  return limit;
 }
 static int NextLimit(int current)=>Math.Max(999,current<int.MaxValue?current+1:int.MaxValue);
}
