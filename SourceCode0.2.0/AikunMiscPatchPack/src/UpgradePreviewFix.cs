using System.Reflection.Emit;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using Godot;

namespace AikunMiscPatchPack;

public static class UpgradePreviewFix {
 static readonly Harmony H=new("Aikun.MiscPatchPack.UpgradePreview");
 static MethodInfo? effectiveLevel,trackLevel;
 [ThreadStatic] static int previewDepth;
 public static void Install(){
  // 自由配装的突破等级保存在弱引用表中，并非 CurrentUpgradeLevel；只向预览副本复制记录。
  var store=FusionQuestBlessingFix.Entry.Find("FreeLoadout.OverrideStore");
  if(store!=null){effectiveLevel=AccessTools.Method(store,"GetCardUpgradeLevel",[typeof(CardModel)]);trackLevel=AccessTools.Method(store,"TrackUpgradeLevel",[typeof(CardModel),typeof(int)]);}
  foreach(var method in new[]{AccessTools.Method(typeof(NUpgradePreview),"Reload"),AccessTools.Method(typeof(NGridCardHolder),"UpdateCardModel"),AccessTools.Method(typeof(NGridCardHolder),nameof(NGridCardHolder.SetIsPreviewingUpgrade)),AccessTools.Method(typeof(NInspectCardScreen),"UpdateCardDisplay"),AccessTools.Method(typeof(NDeckUpgradeSelectScreen),"OnCardClicked")})
   H.Patch(method,prefix:new HarmonyMethod(typeof(UpgradePreviewFix),nameof(Enter)){priority=Priority.First},finalizer:new HarmonyMethod(typeof(UpgradePreviewFix),nameof(Leave)));
  var cloning=AccessTools.Method(typeof(AbstractModel),nameof(AbstractModel.MutableClone));
  H.Patch(cloning,postfix:new HarmonyMethod(typeof(UpgradePreviewFix),nameof(CopyLevel)){priority=Priority.Last,after=Harmony.GetPatchInfo(cloning)?.Owners.ToArray()??[]});
  H.Patch(AccessTools.Method(typeof(NGridCardHolder),nameof(NGridCardHolder.SetIsPreviewingUpgrade)),prefix:new HarmonyMethod(typeof(UpgradePreviewFix),nameof(RefreshGrid)){priority=Priority.Last});
  H.Patch(AccessTools.Method(typeof(NInspectCardScreen),"UpdateCardDisplay"),prefix:new HarmonyMethod(typeof(UpgradePreviewFix),nameof(Inspect)){priority=Priority.Last});
  H.Patch(AccessTools.Method(typeof(NInspectCardScreen),"SetCard"),transpiler:new HarmonyMethod(typeof(UpgradePreviewFix),nameof(DefaultView)));
 }
 static int Level(CardModel card)=>effectiveLevel!=null&&trackLevel!=null?(int)effectiveLevel.Invoke(null,[card])!:card.CurrentUpgradeLevel;
 static bool Enabled(CardModel card)=>Settings.Active("upgrade_preview")&&(card.MaxUpgradeLevel>1||Level(card)>1);
 static void Enter(out int __state){__state=previewDepth;if(Settings.Active("upgrade_preview"))previewDepth++;}
 static void Leave(int __state){previewDepth=__state;}
 static void CopyLevel(AbstractModel __instance,AbstractModel __result){
  if(previewDepth>0&&__instance is CardModel source&&__result is CardModel clone&&!ReferenceEquals(source,clone)&&trackLevel!=null)
   trackLevel.Invoke(null,[clone,Level(source)]);
 }
 static CardModel Clone(CardModel source){
  var clone=(CardModel)(source.IsMutable?source.ClonePreservingMutability():source.MutableClone());
  if(trackLevel!=null)trackLevel.Invoke(null,[clone,Level(source)]);return clone;
 }
 static void RefreshGrid(bool showUpgradePreview,CardModel ____baseCard,ref CardModel ____upgradedCard,ref bool ____isPreviewingUpgrade){
  if(!showUpgradePreview||!Enabled(____baseCard)||!____baseCard.IsUpgradable)return;
  // 缓存可能建立于早先的等级；每次查看都从当前原牌生成，绝不升级原牌。
  var preview=Clone(____baseCard);preview.UpgradePreviewType=CardUpgradePreviewType.Deck;preview.UpgradeInternal();
  ____upgradedCard=preview;____isPreviewingUpgrade=false;
 }
 static bool Inspect(NInspectCardScreen __instance,List<CardModel> ____cards,int ____index,NCard ____card,NTickbox ____upgradeTickbox,Control ____hoverTipRect){
  var source=____cards[____index];if(!Enabled(source))return true;
  var preview=Clone(source);
  if(____upgradeTickbox.IsTicked&&source.IsUpgradable){
   preview.UpgradePreviewType=CardUpgradePreviewType.Deck;preview.UpgradeInternal();
   ____card.Model=preview;____card.ShowUpgradePreview();
  }else{
   // 取消勾选显示当前等级；达到上限时也保留当前效果，不能降回基础牌。
   ____card.Model=preview;____card.UpdateVisuals(PileType.None,CardPreviewMode.Normal);
  }
  NHoverTipSet.Clear();NHoverTipSet.CreateAndShow(__instance,preview.HoverTips)?.SetAlignment(____hoverTipRect,HoverTip.GetHoverTipAlignment(__instance));
  return false;
 }
 static bool InitiallyUpgraded(CardModel card)=>Enabled(card)?false:card.IsUpgraded;
 static IEnumerable<CodeInstruction> DefaultView(IEnumerable<CodeInstruction> instructions){
  var getter=AccessTools.PropertyGetter(typeof(CardModel),nameof(CardModel.IsUpgraded));
  foreach(var instruction in instructions){
   // 详情页先显示当前牌；只有明确勾选时，多次升级牌才展示下一级。
   if(instruction.Calls(getter)){instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(UpgradePreviewFix),nameof(InitiallyUpgraded));}
   yield return instruction;
  }
 }
}
