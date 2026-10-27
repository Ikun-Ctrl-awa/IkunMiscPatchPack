using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace AikunMiscPatchPack;

public static class ThousandCurseScrollFix {
 const string Key="curse_scroll";
 static readonly Harmony H=new("Aikun.MiscPatchPack.ThousandCurseScroll");
 static Type scrollType=null!;
 [ThreadStatic] static Batch? current;
 sealed class Batch {
  public readonly Batch? Previous=current;
  public readonly List<CardModel> Cards=[];
 }
 public static void Install(){
  scrollType=FusionQuestBlessingFix.Entry.Find("YuWanCard.Relics.ThousandCurseScroll")??throw new MissingMemberException("未找到千咒卷轴");
  var type=FusionQuestBlessingFix.Entry.Find("BetterAscension.AscensionCombatStart")??throw new MissingMemberException("未找到进阶 Boss 诅咒接口");
  try{
   H.Patch(AccessTools.Method(type,"AddCurse")??throw new MissingMethodException(type.FullName,"AddCurse"),transpiler:new HarmonyMethod(typeof(ThousandCurseScrollFix),nameof(CaptureAddition)));
   // 早于进阶模组的同步前缀建立作用域；返回的任务必须等待卷轴完成后再进入房间。
   H.Patch(AccessTools.Method(typeof(Hook),nameof(Hook.BeforeRoomEntered)),
    prefix:new HarmonyMethod(typeof(ThousandCurseScrollFix),nameof(Begin)){priority=Priority.First},
    postfix:new HarmonyMethod(typeof(ThousandCurseScrollFix),nameof(Finish)){priority=Priority.Last},
    finalizer:new HarmonyMethod(typeof(ThousandCurseScrollFix),nameof(Clear)));
  }catch{H.UnpatchAll(H.Id);throw;}
 }
 static void Begin(out Batch __state){__state=new();current=__state;}
 static void Finish(IRunState runState,Batch __state,ref Task __result){
  current=__state.Previous;
  if(__state.Cards.Count>0)__result=Consume(__result,runState,__state.Cards);
 }
 static void Clear(Batch? __state){if(__state!=null)current=__state.Previous;}
 static void Add(CardPile pile,CardModel card,int index,bool silent){
  pile.AddInternal(card,index,silent);
  // 仅记录进阶 AddCurse 本次新建的牌，不扫描或追溯处理旧诅咒。
  if(Settings.Active(Key)&&current!=null&&pile.Type==PileType.Deck&&card.Type==CardType.Curse)current.Cards.Add(card);
 }
 static async Task Consume(Task original,IRunState runState,List<CardModel> cards){
  await original;
  if(!Settings.Active(Key))return;
  foreach(var card in cards){
   if(card.Pile?.Type!=PileType.Deck)continue;
   var scroll=runState.IterateHookListeners(null).OfType<RelicModel>().FirstOrDefault(r=>r.GetType()==scrollType&&r.Owner==card.Owner);
   if(scroll==null)continue;
   // 调用卷轴原有处理：牌组诅咒移除、战斗内收益及每战上限均由原遗物负责。
   await scroll.AfterCardChangedPilesLate(card,PileType.None,null);
   scroll.InvokeExecutionFinished();
  }
 }
 static IEnumerable<CodeInstruction> CaptureAddition(IEnumerable<CodeInstruction> source){
  var add=AccessTools.Method(typeof(CardPile),nameof(CardPile.AddInternal));int count=0;
  foreach(var instruction in source){
   if(instruction.Calls(add)){instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(ThousandCurseScrollFix),nameof(Add));count++;}
   yield return instruction;
  }
  if(count!=1)throw new InvalidOperationException("进阶 Boss 诅咒加入路径已改变："+count);
 }
}
