using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace AikunMiscPatchPack;
public static class YuiFusionBlessingRewardFix {
 // 保留原配置键，原来对妙计补丁的启用/关闭选择继续有效。
 public const string Key="yui_finesse_reward";
 static MethodInfo activeEffect=null!;
 static readonly AccessTools.FieldRef<CardReward,bool> Manual=AccessTools.FieldRefAccess<CardReward,bool>("_cardsWereManuallySet");
 static readonly AccessTools.FieldRef<CardReward,bool> Rerolled=AccessTools.FieldRefAccess<CardReward,bool>("_hasBeenRerolled");
 static readonly Func<CardReward,CardCreationOptions> Options=AccessTools.MethodDelegate<Func<CardReward,CardCreationOptions>>(AccessTools.DeclaredPropertyGetter(typeof(CardReward),"Options"));
 public static void Install(){
  var registry=FusionQuestBlessingFix.Entry.Find("YuiExtra.Relics.CardBlessingRegistry")??throw new TypeLoadException("未找到 YUI 赐福注册表");
  var effect=registry.Assembly.GetType("YuiExtra.Relics.ICardBlessingEffect")??throw new TypeLoadException("未找到 YUI 赐福接口");
  activeEffect=AccessTools.DeclaredMethod(registry,"TryGetActiveEffect",[typeof(CardModel),effect.MakeByRefType()])??throw new MissingMethodException("YUI 赐福判定接口已改变");
  var injector=FusionQuestBlessingFix.Entry.Find("FusionExpansionPack.MainFile")??throw new TypeLoadException("未找到融合扩展包");
  var method=AccessTools.DeclaredMethod(injector,"InjectModCards",[typeof(CardReward)])??throw new MissingMethodException("融合扩展包奖励注入接口已改变");
  if(!activeEffect.IsStatic||activeEffect.ReturnType!=typeof(bool)||!method.IsStatic||method.ReturnType!=typeof(void))throw new InvalidOperationException("赐福奖励兼容接口未适配");
  new Harmony("AikunMiscPatchPack.YuiFusionBlessingRewards").Patch(method,prefix:new HarmonyMethod(typeof(YuiFusionBlessingRewardFix),nameof(KeepSpecifiedBlessingCards)));
 }
 static bool KeepSpecifiedBlessingCards(CardReward __0){
  if(!Settings.Active(Key)||!Manual(__0)||Rerolled(__0))return true;
  var options=Options(__0);
  if(options.Source!=CardCreationSource.Other||!options.Flags.HasFlag(CardCreationFlags.NoCardPoolModifications))return true;
  var cards=__0.Cards.ToArray();
  // 由原作者判定卡牌与主人当前赐福是否匹配，兼容爪击替代牌；不按类名猜测。
  return cards.Length==0||!cards.All(c=>(bool)activeEffect.Invoke(null,[c,null])!);
 }
}
