using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace AikunMiscPatchPack;
public static class EnchantRestSiteSaveFix {
 public const string Key="enchant_rest_site_sl";
 static readonly MethodInfo Clear=typeof(ICollection<RestSiteOption>).GetMethod(nameof(ICollection<RestSiteOption>.Clear))!;
 static readonly string[] OwnOptionTypes=["MoreEnchantmentsMod.EnchantCardRestSiteOption","MoreEnchantmentsMod.RemoveEnchantRestSiteOption","MoreEnchantmentsMod.SynthesizeEnchantRestSiteOption"];
 public static void Install(){
  var save=FusionQuestBlessingFix.Entry.Find("MoreEnchantmentsMod.MoreEnchantSave")??throw new TypeLoadException("未找到更多附魔存档入口");
  var table=save.Assembly.GetType("MoreEnchantmentsMod.EnchantersTable")??throw new TypeLoadException("未找到附魔台");
  var canSave=AccessTools.DeclaredMethod(save,"CanSaveRunNow",[typeof(IRunState)])??throw new MissingMethodException("附魔存档边界已改变");
  var modify=AccessTools.DeclaredMethod(table,"TryModifyRestSiteOptions")??throw new MissingMethodException("附魔台火堆选项接口已改变");
  if(canSave.ReturnType!=typeof(bool)||!modify.GetParameters().Select(p=>p.ParameterType).SequenceEqual(new[]{typeof(MegaCrit.Sts2.Core.Entities.Players.Player),typeof(ICollection<RestSiteOption>)}))throw new InvalidOperationException("附魔台接口形状未适配");
  var harmony=new Harmony("AikunMiscPatchPack.EnchantRestSiteSave");
  try{
   harmony.Patch(canSave,postfix:new HarmonyMethod(typeof(EnchantRestSiteSaveFix),nameof(CheckSaveBoundary)));
   harmony.Patch(modify,transpiler:new HarmonyMethod(typeof(EnchantRestSiteSaveFix),nameof(KeepOtherOptions)));
  }catch{harmony.UnpatchAll(harmony.Id);throw;}
 }
 static void CheckSaveBoundary(IRunState? runState,ref bool __result){
  // 只约束更多附魔自己的排队与执行检查；原生进房存档及其他房间不受影响。
  if(Settings.Active(Key)&&runState?.CurrentRoom is RestSiteRoom)__result=false;
 }
 static void CompleteOwnOptions(ICollection<RestSiteOption> options){
  if(!Settings.Active(Key)){options.Clear();return;}
  // 不删除完成标记；只移除附魔自身入口，保留迷你帐篷、原版和其他模组的选项。
  foreach(var option in options.Where(o=>OwnOptionTypes.Contains(o.GetType().FullName)).ToArray())options.Remove(option);
 }
 static IEnumerable<CodeInstruction> KeepOtherOptions(IEnumerable<CodeInstruction> source){
  var instructions=source.ToList();
  if(instructions.Count(i=>i.Calls(Clear))!=1)throw new InvalidOperationException("附魔台清空选项路径已改变，未强行修补");
  foreach(var i in instructions)if(i.Calls(Clear)){i.opcode=OpCodes.Call;i.operand=AccessTools.Method(typeof(EnchantRestSiteSaveFix),nameof(CompleteOwnOptions));}
  return instructions;
 }
}
