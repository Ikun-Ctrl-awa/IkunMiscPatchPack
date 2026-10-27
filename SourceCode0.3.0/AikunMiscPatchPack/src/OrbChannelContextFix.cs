using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace AikunMiscPatchPack;

// 其他模组接管 Channel 并返回 false 时，Harmony 会跳过东尼的有状态前缀，
// 但仍执行后缀。未 Push 的空状态没有需要 Pop 的上下文。
public static class OrbChannelContextFix {
 const string Key="orb_channel_context";
 public static void Install(){
  var type=FusionQuestBlessingFix.Entry.Find("AutoAnthony.Patches.ChaosOrbChannelContextPatch")
   ??throw new TypeLoadException("未找到东尼充能球上下文补丁");
  var postfix=AccessTools.DeclaredMethod(type,"Postfix")??throw new MissingMethodException(type.FullName,"Postfix");
  var args=postfix.GetParameters();
  if(postfix.ReturnType!=typeof(void)||args.Length!=1||args[0].ParameterType.FullName!="AutoAnthony.ChaosOrbChannelContext+Frame")
   throw new NotSupportedException("东尼充能球上下文签名未适配");
  // 只守护这一处后缀；保留接管者返回的原始 Task、充能球与所有正常上下文。
  var h=new Harmony("AikunMiscPatchPack.OrbChannelContext");
  try{
   h.Patch(postfix,prefix:new HarmonyMethod(typeof(OrbChannelContextFix),nameof(HasState)));
   // 此前创建的 Channel 包装器可能已经内联旧后缀。重新生成调用者，
   // 让真实充能链也调用受保护的后缀，而不是仅反射调用时生效。
   h.Patch(AccessTools.Method(typeof(OrbCmd),"Channel",[typeof(PlayerChoiceContext),typeof(OrbModel),typeof(Player)]),
    transpiler:new HarmonyMethod(typeof(OrbChannelContextFix),nameof(RefreshCaller)));
  }catch{h.UnpatchAll(h.Id);throw;}
 }
 static bool HasState(object? __0)=>!Settings.Active(Key)||__0!=null;
 static IEnumerable<CodeInstruction> RefreshCaller(IEnumerable<CodeInstruction> instructions)=>instructions;
}
