using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace AikunMiscPatchPack;

public static class FusionStrikeDisplayFix {
 public const string Key="fusion_strike_display";
 static Type target=null!;
 public static void Install(){
  target=FusionQuestBlessingFix.Entry.Find("FusionExpansionPack.FusionStrikeCard")??throw new TypeLoadException("未找到融合打击");
  var vars=ModelDb.GetById<CardModel>(ModelDb.GetId(target)).DynamicVars;
  if(!vars.TryGetValue("CalculatedDamage",out var damage)||damage is not CalculatedDamageVar||!vars.ContainsKey("ExtraDamage"))
   throw new MissingMemberException("融合打击的动态伤害接口已改变");
  new Harmony("AikunMiscPatchPack.FusionStrikeDisplay").Patch(AccessTools.PropertyGetter(typeof(CardModel),nameof(CardModel.Description)),
   postfix:new HarmonyMethod(typeof(FusionStrikeDisplayFix),nameof(Description)){priority=Priority.Last});
 }
 static void Description(CardModel __instance,LocString __result){
  if(!Settings.Active(Key)||!target.IsInstanceOfType(__instance))return;
  // 旧翻译的 Damage/MagicNumber 绑定到原生实际变量对象，保留目标预览、颜色与升级差值。
  // 仅给本次描述补别名，不新增/复制卡牌动态变量，不修改任何伤害或素材计数逻辑。
  var vars=__instance.DynamicVars;
  if(!vars.ContainsKey("Damage")&&!__result.Variables.ContainsKey("Damage"))__result.AddObj("Damage",vars.CalculatedDamage);
  if(!vars.ContainsKey("MagicNumber")&&!__result.Variables.ContainsKey("MagicNumber"))__result.AddObj("MagicNumber",vars.ExtraDamage);
 }
}
