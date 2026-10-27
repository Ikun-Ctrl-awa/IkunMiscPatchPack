using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace AikunMiscPatchPack;
public static class StoneSwordQuestFix {
 public const string Key="stone_sword_quest";
 static Type stone=null!;
 static PropertyInfo counter=null!;
 static readonly ConditionalWeakTable<CardModel,Ledger> Ledgers=new();
 sealed class Ledger {public readonly Dictionary<CombatRoom,Task> Rooms=new(ReferenceEqualityComparer.Instance);}
 public static void Install(){
  stone=FusionQuestBlessingFix.Entry.Find("YuWanCard.Cards.Quest.StoneSword")??throw new TypeLoadException("未找到石中剑任务卡");
  counter=AccessTools.Property(stone,"ElitesDefeated")??throw new MissingMemberException("石中剑进度接口已改变");
  var h=new Harmony("AikunMiscPatchPack.StoneSwordQuest");
  h.Patch(AccessTools.DeclaredMethod(stone,nameof(AbstractModel.AfterCombatVictory)),prefix:new HarmonyMethod(typeof(StoneSwordQuestFix),nameof(Standalone)));
  var fused=FusionQuestBlessingFix.Entry.Find("CardFusion.FusedCard");
  if(fused!=null)h.Patch(AccessTools.DeclaredMethod(fused,nameof(AbstractModel.AfterCombatVictory))??AccessTools.DeclaredMethod(typeof(AbstractModel),nameof(AbstractModel.AfterCombatVictory)),postfix:new HarmonyMethod(typeof(StoneSwordQuestFix),nameof(Fusion)));
 }
 static CardModel? Root(CardModel card){
  var permanent=card.DeckVersion??card;
  if(permanent.Owner==null)return null;
  if(permanent.Owner.Deck.Cards.Contains(permanent))return permanent;
  return permanent.Owner.Deck.Cards.FirstOrDefault(p=>FusionQuestBlessingFix.Entry.Descendants(p).Any(c=>ReferenceEquals(c,permanent)));
 }
 static bool Standalone(CardModel __instance,CombatRoom room,ref Task __result){
  if(!Settings.Active(Key))return true;
  var root=Root(__instance);__result=root==null?Task.CompletedTask:Settle(root,room);return false;
 }
 static void Fusion(AbstractModel __instance,CombatRoom room,ref Task __result){
  if(!Settings.Active(Key)||__instance is not CardModel card||!FusionQuestBlessingFix.Entry.Descendants(card).Any(stone.IsInstanceOfType))return;
  var root=Root(card);if(root!=null)__result=After(__result,root,room);
 }
 static async Task After(Task original,CardModel root,CombatRoom room){await original;await Settle(root,room);}
 static Task Settle(CardModel root,CombatRoom room){
  if(room.RoomType!=RoomType.Elite||root.Pile?.Type!=PileType.Deck)return Task.CompletedTask;
  var ledger=Ledgers.GetOrCreateValue(root);if(ledger.Rooms.TryGetValue(room,out var prior))return prior;
  var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);ledger.Rooms[room]=gate.Task;
  _=Run();return gate.Task;
  async Task Run(){try{await Complete(root,room);gate.SetResult();}catch(Exception e){ledger.Rooms.Remove(room);gate.SetException(e);}}
 }
 static async Task Complete(CardModel root,CombatRoom room){
  var quests=(stone.IsInstanceOfType(root)?new[]{root}:FusionQuestBlessingFix.Entry.Descendants(root).Where(stone.IsInstanceOfType)).ToArray();
  if(quests.Length==0)return;
  var owner=root.Owner;bool completed=false;
  foreach(var quest in quests){
   int old=(int)counter.GetValue(quest)!,next=Math.Min(old+1,quest.DynamicVars["Elites"].IntValue);counter.SetValue(quest,next);
   AccessTools.Field(root.GetType(),"_fusionDataStored")?.SetValue(root,null);
   if(next<quest.DynamicVars["Elites"].IntValue)continue;
   await RelicCmd.Obtain(ModelDb.Relic<SwordOfJade>().ToMutable(),owner);
   // 完成记录必须写任务子卡 ID，而不是融合牌 ID；记录时为子卡补齐主人。
   var ownerField=AccessTools.Field(typeof(CardModel),"_owner");var previous=quest.Owner;
   try{ownerField.SetValue(quest,owner);PlayerCmd.CompleteQuest(quest);}finally{ownerField.SetValue(quest,previous);}
   completed=true;
  }
  if(completed&&root.Pile?.Type==PileType.Deck)await CardPileCmd.RemoveFromDeck(root);
 }
}
