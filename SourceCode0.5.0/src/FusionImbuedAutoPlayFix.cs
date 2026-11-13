using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;

namespace IkunMiscPatchPack;

public static class FusionImbuedAutoPlayFix {
 sealed class Played { public ICombatState? Combat; }
 static readonly ConditionalWeakTable<CardModel,Played> played=new();
 public static void Install(){
  var h=new Harmony("IkunMiscPatchPack.FusionImbuedAutoPlay");
  h.Patch(AccessTools.Method(typeof(Imbued),nameof(Imbued.AfterAutoPrePlayPhaseEntered)),prefix:new HarmonyMethod(typeof(FusionImbuedAutoPlayFix),nameof(NativeImbued)));
  h.Patch(AccessTools.Method(typeof(Hook),nameof(Hook.AfterAutoPrePlayPhaseEntered)),prefix:new HarmonyMethod(typeof(FusionImbuedAutoPlayFix),nameof(Capture)),postfix:new HarmonyMethod(typeof(FusionImbuedAutoPlayFix),nameof(Complete)));
 }
 static bool Active(Player player)=>Settings.Active(PlagueImbuedAutoPlayFix.Key)&&LocalContext.NetId.HasValue&&player.PlayerCombatState?.TurnNumber<=1;
 static bool Fused(CardModel card)=>card.GetType().FullName=="CardFusion.FusedCard";
 static CardModel Root(CardModel card,Player player)=>player.PlayerCombatState?.AllCards.FirstOrDefault(c=>Fused(c)&&FusionQuestBlessingFix.Entry.Descendants(c).Any(d=>ReferenceEquals(d,card)))??card;
 static bool Claim(CardModel card){
  var combat=card.CombatState;if(combat==null)return false;
  var state=played.GetOrCreateValue(card);if(ReferenceEquals(state.Combat,combat))return false;
  state.Combat=combat;return true;
 }
 static bool NativeImbued(Imbued __instance,PlayerChoiceContext choiceContext,Player player,ref Task __result){
  if(!Active(player)||!__instance.HasCard||__instance.Card.Owner!=player)return true;
  var root=Root(__instance.Card,player);if(!Fused(root))return true;
  // 本体、复合槽及上游转发共享一次触发；子牌注能必须打出实际融合实体。
  __result=Claim(root)?CardCmd.AutoPlay(choiceContext,root,null):Task.CompletedTask;return false;
 }
 static void Capture(Player player,out CardModel[] __state){
  __state=Active(player)?player.PlayerCombatState!.AllCards.Where(c=>Fused(c)&&c.ShouldReceiveCombatHooks&&HasImbued(c)).ToArray():[];
 }
 static bool HasImbued(CardModel card)=>new[]{card}.Concat(FusionQuestBlessingFix.Entry.Descendants(card))
  .Any(c=>EnchantmentView.Read(c).OfType<Imbued>().Any());
 static void Complete(HookPlayerChoiceContext playerChoiceContext,ICombatState combatState,Player player,CardModel[] __state,ref Task __result)
  =>__result=Finish(__result,playerChoiceContext,combatState,player,__state);
 static async Task Finish(Task original,PlayerChoiceContext context,ICombatState combat,Player player,CardModel[] candidates){
  await original;
  foreach(var card in candidates){
   if(!Active(player)||card.Owner!=player||!ReferenceEquals(card.CombatState,combat)||!card.ShouldReceiveCombatHooks||!HasImbued(card)||!Claim(card))continue;
   // 补发遗漏入口，实际目标选择、免费出牌及弃牌/消耗仍由原生 AutoPlay 负责。
   context.PushModel(card);
   try{await CardCmd.AutoPlay(context,card,null);card.InvokeExecutionFinished();}
   finally{context.PopModel(card);}
  }
 }
}
