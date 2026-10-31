using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace AikunMiscPatchPack;

public static class PinkTomorrowDamageFix {
 const string PowerName="Medo.Scripts.Powers.FlyIntoAPinkTomorrowPower";
 static MethodInfo? Legacy;
 public static void Install(){
  var type=FusionQuestBlessingFix.Entry.Find(PowerName)??throw new TypeLoadException("未找到飞向粉色的明天效果");
  Type[] oldArgs=[typeof(Creature),typeof(decimal),typeof(ValueProp),typeof(Creature),typeof(CardModel)];
  var current=AccessTools.Method(type,nameof(AbstractModel.ModifyDamageMultiplicative),[..oldArgs,typeof(CardPlay)]);
  if(current==null)throw new MissingMethodException("未找到当前伤害倍率接口");
  // 新版原模组若已实现当前接口，直接沿用，不叠加第二遍倍率。
  if(current.DeclaringType==type)return;
  Legacy=AccessTools.DeclaredMethod(type,nameof(AbstractModel.ModifyDamageMultiplicative),oldArgs);
  if(Legacy?.ReturnType!=typeof(decimal)||current.DeclaringType!=typeof(AbstractModel))
   throw new NotSupportedException("飞向粉色的明天伤害接口版本未适配");
  // Harmony 需要声明类上的 MethodInfo，不能传入通过派生类型反射得到的继承成员。
  var target=AccessTools.DeclaredMethod(typeof(AbstractModel),nameof(AbstractModel.ModifyDamageMultiplicative),[..oldArgs,typeof(CardPlay)]);
  new Harmony("AikunMiscPatchPack.PinkTomorrowDamage").Patch(target,
   prefix:new HarmonyMethod(typeof(PinkTomorrowDamageFix),nameof(Bridge)));
 }
 static bool Bridge(AbstractModel __instance,Creature? target,decimal amount,ValueProp props,
  Creature? dealer,CardModel? cardSource,ref decimal __result){
  if(!Settings.Active("pink_tomorrow_damage")||__instance.GetType().FullName!=PowerName)return true;
  // 调用原模组的整套规则：攻击性质、来源、每回合次数、宠物和卡牌所在堆判断。
  __result=(decimal)Legacy!.Invoke(__instance,[target,amount,props,dealer,cardSource])!;
  return false;
 }
}
