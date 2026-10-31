using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;

namespace AikunMiscPatchPack;
public static class IgniteEnchantmentFix {
 const string GeneratedCardKey="YuiExtra.Enhancement.IgniteEnchantment.GeneratedCard";
 static Dictionary<string,Type>? generatedCards;
 public static void Install(){
  var type=FusionQuestBlessingFix.Entry.Find("YuiExtra.Enhancement.IgniteEnchantment")??throw new TypeLoadException("未找到点火附魔");
  var h=new Harmony("AikunMiscPatchPack.IgniteEnchantment");
  var registry=type.Assembly.GetType("YuiExtra.Enhancement.GeneratedCardEnchantmentRegistry")??throw new TypeLoadException("未找到生成牌注册表");
  generatedCards=(Dictionary<string,Type>)AccessTools.Field(registry,"CardTypes").GetValue(null)!;
  h.Patch(AccessTools.DeclaredMethod(type,nameof(AbstractModel.AfterCombatEnd)),prefix:new HarmonyMethod(typeof(IgniteEnchantmentFix),nameof(KeepPermanent)));
  h.Patch(AccessTools.DeclaredMethod(registry,"CreateCardHoverTip"),prefix:new HarmonyMethod(typeof(IgniteEnchantmentFix),nameof(GeneratedPreview)));
 }
 static bool GeneratedPreview(string key,ref IHoverTip? __result){
  if(!Settings.Active("yui_ignite")||key!=GeneratedCardKey||generatedCards==null||!generatedCards.TryGetValue(key,out var type))return true;
  // 当前 FromCard<T> 带可选 bool 参数，旧注册表寻找零参数方法会静默失败。
  __result=HoverTipFactory.FromCard(ModelDb.GetById<CardModel>(ModelDb.GetId(type)));return false;
 }
 static bool KeepPermanent(EnchantmentModel __instance,ref Task __result){
  if(!Settings.Active("yui_ignite")||!__instance.HasCard)return true;
  // 原生战后钩子也遍历永久牌组。只保护牌组实体，战斗临时副本仍由原模组清理。
  var card=__instance.Card;
  if(card.Pile?.Type!=PileType.Deck)return true;
  __instance.Status=EnchantmentStatus.Normal;__result=Task.CompletedTask;return false;
 }
}
