using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Scaffolding.Content;

namespace AikunMiscPatchPack;

// 不改 CardReward.CanSkip、替代选项或选牌画面；只在存在未领取卡牌时拦截继续和整组放弃。
public static class DongniRewardGate {
 static readonly ConditionalWeakTable<RewardsSetSynchronizer,List<RewardsSet>> activeSets=new();
 sealed class ClaimState {public bool TookCard;}
 static readonly ConditionalWeakTable<CardReward,ClaimState> claims=new();
 static readonly System.Reflection.FieldInfo SetField=AccessTools.Field(AccessTools.Inner(typeof(RewardsSetSynchronizer),"RewardsSetState"),"set");
 public static bool HasBlessing(Player player)=>player.Relics.Any(r=>r is DongniBlessing);
 static bool PendingCard(Reward reward){
  if(reward.SuccessfullySelected)return false;
  if(reward is LinkedRewardSet linked)return linked.Rewards.Any(PendingCard);
  // BeginRewardsSet 在原生填充之后调用；空候选没有可领取的卡，不应造成无法离开的死锁。
  if(reward is CardReward card)return !(claims.TryGetValue(card,out var claim)&&claim.TookCard)&&card.Cards.Any();
  return reward is SpecialCardReward;
 }
 public static bool NeedsChoice(RewardsSet? set)=>set!=null&&HasBlessing(set.Player)&&set.Rewards.Any(PendingCard);
 public static bool HasPending(Player player)=>HasPending(RunManager.Instance.RewardsSetSynchronizer,player);
 public static bool HasPending(RewardsSetSynchronizer sync,Player player)=>activeSets.TryGetValue(sync,out var sets)&&sets.Any(s=>s.Player==player&&NeedsChoice(s));
 public static void Install(Harmony h){
  h.Patch(AccessTools.Method(typeof(RewardsSetSynchronizer),nameof(RewardsSetSynchronizer.BeginRewardsSet)),prefix:new HarmonyMethod(typeof(DongniRewardGate),nameof(Begin)));
  h.Patch(AccessTools.Method(typeof(RewardsSetSynchronizer),"CompleteRewardsSet"),postfix:new HarmonyMethod(typeof(DongniRewardGate),nameof(Complete)));
  h.Patch(AccessTools.Method(typeof(RewardsSetSynchronizer),nameof(RewardsSetSynchronizer.SkipLocalRewardsSet)),prefix:new HarmonyMethod(typeof(DongniRewardGate),nameof(SkipTop)));
  h.Patch(AccessTools.Method(typeof(RewardsSetSynchronizer),"SkipRewardsSet"),prefix:new HarmonyMethod(typeof(DongniRewardGate),nameof(SkipSet)));
  h.Patch(AccessTools.Method(typeof(RewardsSetSynchronizer),nameof(RewardsSetSynchronizer.BeforeLeavingRoom)),prefix:new HarmonyMethod(typeof(DongniRewardGate),nameof(LeaveRoom)));
  h.Patch(AccessTools.Method(typeof(NRewardsScreen),"TryEnableProceedButton"),prefix:new HarmonyMethod(typeof(DongniRewardGate),nameof(TryProceed)));
  h.Patch(AccessTools.Method(typeof(NRewardsScreen),"OnProceedButtonPressed"),prefix:new HarmonyMethod(typeof(DongniRewardGate),nameof(ClickProceed)));
  h.Patch(AccessTools.Method(typeof(NRewardsScreen),nameof(NRewardsScreen.RewardSkippedFrom)),postfix:new HarmonyMethod(typeof(DongniRewardGate),nameof(RefreshProceed)));
  h.Patch(AccessTools.Method(typeof(RunManager),nameof(RunManager.ProceedFromTerminalRewardsScreen)),prefix:new HarmonyMethod(typeof(DongniRewardGate),nameof(ProceedFromCombat)));
  h.Patch(AccessTools.Method(typeof(CardReward),"OnSelect"),prefix:new HarmonyMethod(typeof(DongniRewardGate),nameof(BeforeCardSelection)),postfix:new HarmonyMethod(typeof(DongniRewardGate),nameof(AfterCardSelection)));
  // 使用 RitsuLib 自己的关联奖励同步入口，在生成本地或远端选择之前检查，而非抢占其选择流程。
  var linkedRuntime=typeof(ModEventTemplate).Assembly.GetType("STS2RitsuLib.Combat.Rewards.LinkedRewardSetRuntime",true)!;
  h.Patch(AccessTools.Method(linkedRuntime,"TryPrepareSelection"),prefix:new HarmonyMethod(typeof(DongniRewardGate),nameof(PrepareLinkedSelection)));
 }
 static void Begin(RewardsSetSynchronizer __instance,RewardsSet set){var sets=activeSets.GetOrCreateValue(__instance);if(!sets.Contains(set))sets.Add(set);}
 static void Complete(RewardsSetSynchronizer __instance,object setState){if(activeSets.TryGetValue(__instance,out var sets))sets.Remove((RewardsSet)SetField.GetValue(setState)!);}
 static bool SkipTop(RewardsSetSynchronizer __instance,ulong ____localPlayerId)=>!activeSets.TryGetValue(__instance,out var sets)||!NeedsChoice(sets.LastOrDefault(s=>s.Player.NetId==____localPlayerId));
 static bool SkipSet(object setState)=>!NeedsChoice((RewardsSet)SetField.GetValue(setState)!);
 static bool LeaveRoom(RewardsSetSynchronizer __instance,ulong ____localPlayerId)=>!activeSets.TryGetValue(__instance,out var sets)||!sets.Any(s=>s.Player.NetId==____localPlayerId&&NeedsChoice(s));
 static bool TryProceed(RewardsSet ____rewardsSet,NProceedButton ____proceedButton){if(!NeedsChoice(____rewardsSet))return true;____proceedButton.Disable();return false;}
 static bool ClickProceed(RewardsSet ____rewardsSet)=>!NeedsChoice(____rewardsSet);
 static void RefreshProceed(NRewardsScreen __instance)=>AccessTools.Method(typeof(NRewardsScreen),"TryEnableProceedButton").Invoke(__instance,null);
 static int PickedCount(CardReward reward)=>reward.Player.RunState.CurrentMapPointHistoryEntry?.GetEntry(reward.Player.NetId).CardChoices.Count(c=>c.wasPicked)??0;
 static void BeforeCardSelection(CardReward __instance,out int __state)=>__state=HasBlessing(__instance.Player)?PickedCount(__instance):-1;
 static void AfterCardSelection(CardReward __instance,int __state,ref Task<bool> __result){if(__state>=0&&HasBlessing(__instance.Player))__result=ObserveSelection(__result,__instance,__state);}
 static async Task<bool> ObserveSelection(Task<bool> operation,CardReward reward,int before){
  bool result=await operation;
  var claim=claims.GetOrCreateValue(reward);claim.TookCard|=PickedCount(reward)>before;
  // 可选多领奖励领取过一张再返回时也满足要求；非卡牌替代收益不充当领取卡牌。
  return result&&claim.TookCard;
 }
 static bool PrepareLinkedSelection(LinkedRewardSet linkedRewardSet,Reward selectedReward,ref bool __result){
  if(!HasBlessing(selectedReward.Player)||!PendingCard(linkedRewardSet)||selectedReward is CardReward or SpecialCardReward)return true;
  // 关联奖励领取任一项会放弃其他项；卡牌尚未领取时不能用非卡牌分支绕过。
  __result=false;return false;
 }
 static bool ProceedFromCombat(RunManager __instance,ref Task __result){
  var state=__instance.DebugOnlyGetState();
  if(state==null||!state.Players.Any(p=>LocalContext.IsMe(p)&&HasPending(p)))return true;
  __result=Task.CompletedTask;return false;
 }
}
