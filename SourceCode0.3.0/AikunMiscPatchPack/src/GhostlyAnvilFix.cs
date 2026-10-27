using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;

namespace AikunMiscPatchPack;

public static class GhostlyAnvilFix {
 public const string Key="ghostly_anvil_remove";
 static Type anvil=null!;
 static readonly ConditionalWeakTable<CardModel,Settlement> Settlements=new();
 sealed class Settlement {public Task Task=Task.CompletedTask;public bool Running;}
 [ThreadStatic] static int depth;
 [ThreadStatic] static HashSet<CardModel>? queued;
 sealed class Scope {public required CardModel[] Cards;}
 public static Task Pending(CardModel root)=>Settlements.TryGetValue(root,out var state)?state.Task:Task.CompletedTask;
 public static void Install(){
  anvil=FusionQuestBlessingFix.Entry.Find("YUIExtra.CurseExtra.GhostlyAnvil")??throw new TypeLoadException("未找到闹鬼的铁砧");
  var old=AccessTools.DeclaredMethod(anvil,"RemoveCompletedFromOwnersDecks",[typeof(IEnumerable<CardModel>)])??throw new MissingMethodException("铁砧升级结算接口已改变");
  var h=new Harmony("AikunMiscPatchPack.GhostlyAnvil");
  try{
   h.Patch(old,prefix:new HarmonyMethod(typeof(GhostlyAnvilFix),nameof(ReplaceOld)));
   h.Patch(AccessTools.Method(typeof(CardCmd),nameof(CardCmd.Upgrade),[typeof(IEnumerable<CardModel>),typeof(CardPreviewStyle)]),
    prefix:new HarmonyMethod(typeof(GhostlyAnvilFix),nameof(EnterCommand)){priority=Priority.First},finalizer:new HarmonyMethod(typeof(GhostlyAnvilFix),nameof(LeaveCommand)){priority=Priority.Last});
   h.Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.UpgradeInternal)),
    prefix:new HarmonyMethod(typeof(GhostlyAnvilFix),nameof(EnterUpgrade)){priority=Priority.First},finalizer:new HarmonyMethod(typeof(GhostlyAnvilFix),nameof(LeaveUpgrade)){priority=Priority.Last});
  }catch{h.UnpatchAll(h.Id);throw;}
 }
 static bool ReplaceOld(ref Task __result){if(!Settings.Active(Key))return true;__result=Task.CompletedTask;return false;}
 static IEnumerable<CardModel> Cards(CardModel root)=>FusionQuestBlessingFix.Entry.Descendants(root).Prepend(root);
 static bool Completed(CardModel root)=>Cards(root).Any(c=>anvil.IsInstanceOfType(c)&&c.CurrentUpgradeLevel>=3);
 static CardModel? Root(CardModel changed){
  if(!changed.IsMutable)return null;
  // 只按实际引用识别永久实体；预览和战斗副本的 DeckVersion 不能反向触发永久删牌。
  var run=RunManager.Instance.DebugOnlyGetState();if(run==null)return null;
  return run.Players.SelectMany(p=>p.Deck.Cards).FirstOrDefault(root=>Cards(root).Any(c=>ReferenceEquals(c,changed)));
 }
 static void Queue(CardModel root){if(root.Pile?.Type==PileType.Deck&&Completed(root))(queued??=new(ReferenceEqualityComparer.Instance)).Add(root);}
 static void EnterCommand(ref IEnumerable<CardModel> cards,out Scope? __state){
  __state=null;if(!Settings.Active(Key))return;
  var snapshot=cards.ToArray();cards=snapshot;__state=new(){Cards=snapshot};depth++;
 }
 static Exception? LeaveCommand(Exception? __exception,Scope? __state){
  if(__state==null)return __exception;
  try{if(__exception==null)foreach(var owner in __state.Cards.Where(c=>c.IsMutable).Select(c=>c.Owner).Where(p=>p!=null).Distinct())foreach(var root in owner!.Deck.Cards.ToArray())Queue(root);}
  finally{Leave();}return __exception;
 }
 static void EnterUpgrade(out bool __state){__state=Settings.Active(Key);if(__state)depth++;}
 static Exception? LeaveUpgrade(CardModel __instance,Exception? __exception,bool __state){
  if(!__state)return __exception;
  try{if(__exception==null&&__instance.IsMutable&&Cards(__instance).Any(anvil.IsInstanceOfType)&&Root(__instance) is {} root)Queue(root);}
  finally{Leave();}return __exception;
 }
 static void Leave(){
  if(--depth!=0)return;
  var roots=queued?.ToArray()??[];queued?.Clear();if(!Settings.Active(Key))return;
  // 等最外层升级枚举及融合素材重算结束，再移除父卡，避免中途修改正在遍历的牌组。
  foreach(var root in roots)Start(root);
 }
 static void Start(CardModel root){
  if(root.Pile?.Type!=PileType.Deck||!Completed(root))return;
  var state=Settlements.GetOrCreateValue(root);if(state.Running)return;
  var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);state.Running=true;state.Task=gate.Task;
  _=Complete();
  async Task Complete(){
   try{
    // 该流程直接移除永久融合父卡，子卡是否拥有主人及旧融合任务开关均不影响定位。
    foreach(var card in Cards(root))if(card.IsMutable&&anvil.IsInstanceOfType(card)&&card.Keywords.Contains(CardKeyword.Eternal))CardCmd.RemoveKeyword(card,CardKeyword.Eternal);
    if(root.Keywords.Contains(CardKeyword.Eternal))CardCmd.RemoveKeyword(root,CardKeyword.Eternal);
    await CardPileCmd.RemoveFromDeck(root);gate.SetResult();
   }catch(Exception e){GD.PrintErr("[AikunMiscPatchPack] 闹鬼的铁砧移除失败："+e);gate.SetException(e);_ = gate.Task.Exception;}
   finally{state.Running=false;}
  }
 }
}
