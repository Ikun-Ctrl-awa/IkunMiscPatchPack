using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace AikunMiscPatchPack;

public static class BloodSectConsoleCompat {
 static MethodInfo? readLevel,readBreak,mirrorLink,trackLevel;
 [ThreadStatic] static int restoring;
 public static bool Available=>readLevel!=null;
 public static int Level(CardModel card)=>readLevel==null?card.CurrentUpgradeLevel:(int)readLevel.Invoke(null,[card])!;
 public static bool LimitBreak(CardModel card)=>readBreak!=null&&(bool)readBreak.Invoke(null,[card])!;
 public static void Install(){
  var store=FusionQuestBlessingFix.Entry.Find("FreeLoadout.OverrideStore");if(store==null)return;
  var h=new Harmony("AikunMiscPatchPack.BloodSect.Console");
  try{
   var getter=AccessTools.Method(store,"GetCardUpgradeLevel",[typeof(CardModel)])??throw new MissingMethodException("控制台缺少实际升级次数接口");
   var repeated=AccessTools.Method(store,"ApplyLimitBreakUpgrade",[typeof(CardModel)])??throw new MissingMethodException("控制台缺少突破升级接口");
   var restored=AccessTools.Method(store,"ApplyUpgradeLevel")??throw new MissingMethodException("控制台缺少等级恢复接口");
   readBreak=AccessTools.Method(store,"GetCardLimitBreak",[typeof(CardModel)])??throw new MissingMethodException("控制台缺少突破资格接口");
   mirrorLink=AccessTools.Method(store,"MirrorEditLink",[typeof(CardModel),typeof(CardModel)])??throw new MissingMethodException("控制台缺少克隆等级接口");
   trackLevel=AccessTools.Method(store,"TrackUpgradeLevel",[typeof(CardModel),typeof(int)])??throw new MissingMethodException("控制台缺少等级记录接口");
   readLevel=getter;
   // 保留控制台原有次数、保存、编辑与费用流程，只同步血魔宗的分段升级效果。
   foreach(var method in new[]{repeated,restored})h.Patch(method,prefix:new HarmonyMethod(typeof(BloodSectConsoleCompat),nameof(BeforeApply)),postfix:new HarmonyMethod(typeof(BloodSectConsoleCompat),nameof(Refresh)){priority=Priority.Last});
   h.Patch(AccessTools.Method(store,"SetCardLimitBreak",[typeof(CardModel),typeof(bool)]),prefix:new HarmonyMethod(typeof(BloodSectConsoleCompat),nameof(BeforeToggle)),postfix:new HarmonyMethod(typeof(BloodSectConsoleCompat),nameof(Refresh)){priority=Priority.Last});
   var cloning=AccessTools.Method(typeof(AbstractModel),nameof(AbstractModel.MutableClone));
   h.Patch(cloning,postfix:new HarmonyMethod(typeof(BloodSectConsoleCompat),nameof(Clone)){priority=Priority.Last,after=Harmony.GetPatchInfo(cloning)?.Owners.ToArray()??[]});
   foreach(var name in new[]{nameof(CardModel.ToSerializable),nameof(CardModel.FromSerializable)}){
    var method=AccessTools.Method(typeof(CardModel),name);
    h.Patch(method,postfix:new HarmonyMethod(typeof(BloodSectConsoleCompat),name==nameof(CardModel.ToSerializable)?nameof(Saving):nameof(Restored)){priority=Priority.Last,after=Harmony.GetPatchInfo(method)?.Owners.ToArray()??[]});
   }
   h.Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.FromSerializable)),prefix:new HarmonyMethod(typeof(BloodSectConsoleCompat),nameof(EnterRestore)){priority=Priority.First},finalizer:new HarmonyMethod(typeof(BloodSectConsoleCompat),nameof(LeaveRestore)){priority=Priority.Last});
  }catch(Exception e){h.UnpatchAll(h.Id);readLevel=readBreak=null;GD.PrintErr("[AikunMiscPatchPack] 血魔宗控制台兼容未启用："+e.Message);}
 }
 static void EnterRestore(out int __state){__state=restoring;restoring++;}
 static void LeaveRestore(int __state)=>restoring=__state;
 static void BeforeApply(CardModel card,out CardModel[]? __state){
  // 读档时融合数据已经恢复出各素材；控制台旧外层等级不能再次降级并重放这些素材。
  __state=restoring>0&&card is not BloodSect&&FusionQuestBlessingFix.Entry.IsOrContains<BloodSect>(card)
   ?FusionQuestBlessingFix.Entry.Children(card).Select(c=>(CardModel)c.ClonePreservingMutability()).ToArray():null;
 }
 static void BeforeToggle(CardModel card,out CardModel[]? __state){
  // 切换突破资格只改资格，不能因控制台重放旧外层等级而重置现有融合素材。
  __state=card is not BloodSect&&FusionQuestBlessingFix.Entry.IsOrContains<BloodSect>(card)
   ?FusionQuestBlessingFix.Entry.Children(card).Select(c=>(CardModel)c.ClonePreservingMutability()).ToArray():null;
 }
 static void Saving(CardModel __instance,SerializableCard __result){
  // 融合子卡不在独立牌堆中，控制台不会为它落盘最新次数；由该子卡自己的保存数据携带实际次数。
  if(__instance is BloodSect)__result.CurrentUpgradeLevel=Level(__instance);
 }
 static void Restored(SerializableCard save,CardModel __result){
  if(__result is not BloodSect blood)return;
  // 保留旧控制台存档的外部次数，也接回比外部记录更新的融合子卡次数；不写控制台配置。
  if(save.CurrentUpgradeLevel>Level(blood)){
   // 旧编辑记录可能把读回的原生等级重置为0；恢复一次原生首升以同步费用和保留。
   if(blood.CurrentUpgradeLevel==0)blood.UpgradeInternal();
   trackLevel!.Invoke(null,[blood,save.CurrentUpgradeLevel]);
  }
  blood.SyncConsoleUpgrade(Level(blood));
 }
 static void Clone(AbstractModel __instance,AbstractModel __result){
  // 原生复制不会复制控制台弱引用表；沿用控制台自己的无落盘镜像接口，保留等级与突破资格。
  if(__instance is CardModel source&&__result is CardModel target&&!ReferenceEquals(source,target)&&
   FusionQuestBlessingFix.Entry.IsOrContains<BloodSect>(source))mirrorLink?.Invoke(null,[source,target]);
 }
 static void Refresh(CardModel card,CardModel[]? __state){
  if(__state is {Length:>0})FusionUpgradeFix.RestoreBloodChildren(card,__state);
  if(card is not BloodSect blood){
   if(FusionQuestBlessingFix.Entry.IsOrContains<BloodSect>(card))FusionUpgradeFix.RefreshBloodFusion(card);
   return;
  }
  blood.SyncConsoleUpgrade(Level(card));
  FusionUpgradeFix.RefreshBloodParent(blood);
  // 控制台在原方法内已发过一次刷新；同步后再通知，避免原生卡面滞留在旧吸血数值。
  card.Pile?.InvokeContentsChanged();
 }
}
