using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;

namespace AikunMiscPatchPack;

public static class FusionEmberCostFix {
 public const string Key="fusion_ember_cost";
 static Type fused=null!;
 static FieldInfo canonical=null!;
 public static void Install(){
  fused=FusionQuestBlessingFix.Entry.Find("CardFusion.FusedCard")??throw new TypeLoadException("未找到融合卡牌。");
  canonical=AccessTools.Field(typeof(MegaCrit.Sts2.Core.Entities.Cards.CardEnergyCost),"<Canonical>k__BackingField")??throw new MissingFieldException("卡牌基础费用接口已改变。");
  var getter=AccessTools.DeclaredPropertyGetter(fused,"CanonicalEnergyCost")??throw new MissingMemberException("融合默认费用接口已改变。");
  var refresh=AccessTools.DeclaredMethod(fused,"FixEnergyCostFields")??throw new MissingMethodException("融合费用同步接口已改变。");
  var upgrade=AccessTools.DeclaredMethod(fused,"OnUpgrade")??throw new MissingMethodException("融合升级接口已改变。");
  EnchantmentView.Discover();var h=new Harmony("AikunMiscPatchPack.FusionEmberCost");
  try{
   h.Patch(getter,postfix:new HarmonyMethod(typeof(FusionEmberCostFix),nameof(DefaultCost)){priority=Priority.Last});
   // 创建、读档与进战斗克隆均调用此费用同步；升级另有一条独立重算路径。
   h.Patch(refresh,postfix:new HarmonyMethod(typeof(FusionEmberCostFix),nameof(Refresh)){priority=Priority.Last});
   h.Patch(upgrade,postfix:new HarmonyMethod(typeof(FusionEmberCostFix),nameof(Refresh)){priority=Priority.Last});
  }catch{h.UnpatchAll(h.Id);throw;}
 }
 static bool HasEmber(CardModel card)=>EnchantmentView.Read(card).Any(e=>e is TezcatarasEmber)
  ||FusionQuestBlessingFix.Entry.Descendants(card).Any(c=>EnchantmentView.Read(c).Any(e=>e is TezcatarasEmber));
 static void DefaultCost(CardModel __instance,ref int __result){
  // 保留 X 费及不可打出的原有类型规则，只校正有限能量费用。
  if(Settings.Active(Key)&&__instance.IsMutable&&__result>=0&&HasEmber(__instance))__result=0;
 }
 static void Refresh(CardModel __instance){
  if(!Settings.Active(Key)||!__instance.IsMutable||__instance.EnergyCost.CostsX||!HasEmber(__instance))return;
  if((int)AccessTools.PropertyGetter(fused,"CanonicalEnergyCost").Invoke(__instance,null)!<0)return;
  // 修改默认费用，不清除局内临时加减费、绝对费用修正或星星费用。
  canonical.SetValue(__instance.EnergyCost,0);
  __instance.EnergyCost.SetCustomBaseCost(0);
 }
}
