using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace AikunMiscPatchPack;
public static class BloodSectFusionPlayFix {
 static MethodInfo mergedMode=null!;
 public static bool Available{get;private set;}
 public static void Install(){
  var fused=FusionQuestBlessingFix.Entry.Find("CardFusion.FusedCard");if(fused==null)return;
  mergedMode=AccessTools.PropertyGetter(FusionQuestBlessingFix.Entry.Find("CardFusion.CardFusionSettings"),"SynthesizeMode")??throw new MissingMethodException("融合模式接口已改变");
  var play=AccessTools.Method(fused,"OnPlay");var machine=play.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType??throw new MissingMemberException("融合出牌异步接口已改变");
  var h=new Harmony("AikunMiscPatchPack.BloodSect.FusionPlay");
  try{
   h.Patch(AccessTools.Method(machine,"MoveNext"),transpiler:new HarmonyMethod(typeof(BloodSectFusionPlayFix),nameof(OrderedBlood)));
   h.Patch(AccessTools.Method(fused,"AddExtraArgsToDescription"),transpiler:new HarmonyMethod(typeof(BloodSectFusionPlayFix),nameof(OrderedBlood)));
   Available=true;
  }catch{h.UnpatchAll(h.Id);throw;}
 }
 static bool UseMergedMode(CardModel card)=>(bool)mergedMode.Invoke(null,null)!&&!FusionQuestBlessingFix.Entry.IsOrContains<BloodSect>(card)
  &&!FusionQuestBlessingFix.Entry.Descendants(card).Any(c=>EnchantmentView.Read(c).Any(e=>e is WeepingBlood or ThunderArc));
 static IEnumerable<CodeInstruction> OrderedBlood(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod){
  var code=instructions.ToList();int count=0;var owner=AccessTools.Field(__originalMethod.DeclaringType,"<>4__this");
  foreach(var instruction in code){
   if(!instruction.Calls(mergedMode)){yield return instruction;continue;}
   // 合并器不能表达“能力生效前后”的时间关系；仅此牌走原融合的有序子卡流程，不改全局设置。
   var load=new CodeInstruction(OpCodes.Ldarg_0);load.labels.AddRange(instruction.labels);instruction.labels.Clear();load.blocks.AddRange(instruction.blocks);instruction.blocks.Clear();yield return load;
   if(owner!=null)yield return new CodeInstruction(OpCodes.Ldfld,owner);
   yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(BloodSectFusionPlayFix),nameof(UseMergedMode)));count++;
  }
  if(count!=1)throw new InvalidOperationException("融合模式判断形状已改变，拒绝改写血魔宗顺序。");
 }
}
