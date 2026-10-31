using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace AikunMiscPatchPack;

public static class DoubleVisionRelicChoiceFix {
 static readonly Harmony H=new("Aikun.MiscPatchPack.DoubleVisionRelicChoice");
 static MethodInfo capture=null!,begin=null!,end=null!,complete=null!,save=null!;
 static readonly FieldInfo Claimed=AccessTools.Field(typeof(RelicReward),"<ClaimedRelic>k__BackingField");
 static readonly MethodInfo ObtainMethod=AccessTools.Method(typeof(RelicCmd),nameof(RelicCmd.Obtain),[typeof(RelicModel),typeof(Player),typeof(int)]);
 public static void Install(){
  var rune=FusionQuestBlessingFix.Entry.Find("HextechRunes.DoubleVisionRune")??throw new MissingMemberException("未找到海克斯复视");
  MethodInfo Require(string name,Type[] args)=>AccessTools.Method(rune,name,args)??throw new MissingMethodException(rune.FullName,name);
  capture=Require("CaptureRewardDuplicationState",[typeof(Player)]);
  begin=Require("BeginRewardCommandSuppression",[]);end=Require("CompleteRewardCommandSuppression",[typeof(object)]);
  complete=Require("CompleteRelicRewardAsync",[typeof(RelicReward),typeof(Task<bool>),typeof(object)]);
  var choice=FusionQuestBlessingFix.Entry.Find("RelicRewardChoices.Rewards.RelicRewardChoiceReward")??throw new MissingMemberException("未找到遗物三选一");
  var method=AccessTools.Method(choice,"FinalizeSelectedAsync");
  var state=method?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType??throw new MissingMemberException("遗物三选一领取接口已改变");
  var saveHelper=FusionQuestBlessingFix.Entry.Find("BaseLib.Patches.Saves.ExtendedSavePatches+PrepExtendedRewardData")??throw new MissingMemberException("未找到奖励扩展存档入口");
  save=AccessTools.Method(saveHelper,"ExtendedDataForReward",[typeof(Reward),typeof(SerializableReward)])??throw new MissingMethodException("ExtendedDataForReward");
  try{
   H.Patch(AccessTools.Method(state,"MoveNext"),transpiler:new HarmonyMethod(typeof(DoubleVisionRelicChoiceFix),nameof(ReplaceObtain)));
   H.Patch(AccessTools.DeclaredMethod(choice,nameof(Reward.ToSerializable)),postfix:new HarmonyMethod(typeof(DoubleVisionRelicChoiceFix),nameof(SaveChoice)){priority=Priority.Last});
  }catch{H.UnpatchAll(H.Id);throw;}
 }
 static void SaveChoice(Reward __instance,SerializableReward __result){
  // 三选一覆盖了 ToSerializable 却没有调用基类，漏过 BaseLib 原有的扩展状态收集。
  // 重用已注册的候选、选中项与完成标记，不引入另一套存档或复制进度。
  if(Settings.Active("double_vision_relic_choice"))save.Invoke(null,[__instance,__result]);
 }
 static IEnumerable<CodeInstruction> ReplaceObtain(IEnumerable<CodeInstruction> source){
  int count=0;
  foreach(var instruction in source){
   if(instruction.Calls(ObtainMethod)){instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(DoubleVisionRelicChoiceFix),nameof(Obtain));count++;}
   yield return instruction;
  }
  if(count!=1)throw new InvalidOperationException("遗物三选一领取路径已改变："+count);
 }
 static async Task<RelicModel> Obtain(RelicModel relic,Player player,int index){
  if(!Settings.Active("double_vision_relic_choice"))return await RelicCmd.Obtain(relic,player,index);
  // 在获取前捕获复视，避免本次选择复视本身时凭空多触发一次。
  var scope=capture.Invoke(null,[player]);
  if(scope==null)return await RelicCmd.Obtain(relic,player,index);
  var suppression=begin.Invoke(null,null);Task<RelicModel> original;
  // AsyncLocal 只进入原始获取的执行上下文，不将抑制状态泄漏给同时选择奖励的其他玩家。
  try{original=RelicCmd.Obtain(relic,player,index);}
  finally{end.Invoke(null,[suppression]);}
  var obtained=await original;
  // 仅传递已经取得的遗物；不生成、领取或向奖励集合登记新的奖励。
  var bridge=new RelicReward(obtained,player);Claimed.SetValue(bridge,obtained);
  await (Task<bool>)complete.Invoke(null,[bridge,Task.FromResult(true),scope])!;
  return obtained;
 }
}
