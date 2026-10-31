using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;

namespace AikunMiscPatchPack;

public static class PlagueImbuedAutoPlayFix {
 public const string Key="plague_imbued_autoplay";
 static Type? composite;
 static PropertyInfo? inner;
 public static void Install(){
  composite=FusionQuestBlessingFix.Entry.Find("Plague.PlagueCompositeEnchantment")??throw new TypeLoadException("未找到 Plague 复合附魔。");
  inner=AccessTools.DeclaredProperty(composite,"InnerEnchantments");
  if(inner==null||!typeof(IEnumerable<EnchantmentModel>).IsAssignableFrom(inner.PropertyType))throw new InvalidOperationException("Plague 复合附魔接口已改变。");
  var method=AccessTools.Method(composite,nameof(AbstractModel.AfterAutoPrePlayPhaseEntered),[typeof(PlayerChoiceContext),typeof(Player)]);
  // 上游若已实现新版触发入口，保留其实现，不再补发注能。
  if(new[]{"AfterAutoPrePlayPhaseEnteredEarly","AfterAutoPrePlayPhaseEntered","AfterAutoPrePlayPhaseEnteredLate"}.Any(name=>AccessTools.Method(composite,name,[typeof(PlayerChoiceContext),typeof(Player)])?.DeclaringType!=typeof(AbstractModel)))return;
  var declared=AccessTools.DeclaredMethod(typeof(AbstractModel),method!.Name,[typeof(PlayerChoiceContext),typeof(Player)])!;
  new Harmony("AikunMiscPatchPack.PlagueImbuedAutoPlay").Patch(declared,postfix:new HarmonyMethod(typeof(PlagueImbuedAutoPlayFix),nameof(Forward)));
 }
 static void Forward(AbstractModel __instance,PlayerChoiceContext choiceContext,Player player,ref Task __result){
  if(!Settings.Active(Key)||__instance.GetType()!=composite||player.PlayerCombatState?.TurnNumber>1)return;
  __result=ForwardImbued(__result,(EnchantmentModel)__instance,choiceContext,player);
 }
 static async Task ForwardImbued(Task original,EnchantmentModel wrapper,PlayerChoiceContext context,Player player){
  await original;
  if(!wrapper.HasCard||wrapper.Card.Owner!=player||!wrapper.Card.ShouldReceiveCombatHooks)return;
  // 仅补复合槽漏掉的原生注能，继续使用本体自动出牌、目标、消耗和能量规则。
  var imbued=((IEnumerable<EnchantmentModel>)inner!.GetValue(wrapper)!).OfType<Imbued>().FirstOrDefault();
  if(imbued==null)return;
  context.PushModel(imbued);
  try{await imbued.AfterAutoPrePlayPhaseEntered(context,player);imbued.InvokeExecutionFinished();}
  finally{context.PopModel(imbued);}
 }
}
