using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace AikunMiscPatchPack;
public static class FakeDustyTomeFix {
 public const string Key="fake_dusty_tome_upgrade";
 static readonly MethodInfo Create=AccessTools.Method(typeof(ICardScope),nameof(ICardScope.CreateCard),[typeof(CardModel),typeof(Player)]);
 static readonly MethodInfo Preview=AccessTools.Method(typeof(HoverTipFactory),nameof(HoverTipFactory.FromCard),[typeof(CardModel),typeof(bool)]);
 public static void Install(){
  var type=FusionQuestBlessingFix.Entry.Find("FakeMerchantExpansion.FakeDustyTome")??throw new TypeLoadException("未找到尘封魔典？？？");
  var obtained=AccessTools.DeclaredMethod(type,nameof(RelicModel.AfterObtained));
  var state=obtained?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType??throw new MissingMethodException("尘封魔典拾取接口已改变");
  var setter=AccessTools.PropertySetter(type,"AncientCard")??throw new MissingMemberException("尘封魔典选牌接口已改变");
  var harmony=new Harmony("AikunMiscPatchPack.FakeDustyTome");
  try{
   harmony.Patch(AccessTools.Method(state,"MoveNext"),transpiler:new HarmonyMethod(typeof(FakeDustyTomeFix),nameof(UpgradeReward)));
   harmony.Patch(setter,transpiler:new HarmonyMethod(typeof(FakeDustyTomeFix),nameof(UpgradePreview)));
  }catch{harmony.UnpatchAll(harmony.Id);throw;}
 }
 static CardModel PrepareReward(CardModel card){
  // 与原版尘封魔典相同，在加入牌组及显示获得预览之前执行一次原生升级。
  if(Settings.Active(Key)&&card.CurrentUpgradeLevel==0&&card.IsUpgradable)CardCmd.Upgrade(card);
  return card;
 }
 static IHoverTip PreparePreview(CardModel card,bool upgrade)=>HoverTipFactory.FromCard(card,upgrade||Settings.Active(Key));
 static IEnumerable<CodeInstruction> UpgradeReward(IEnumerable<CodeInstruction> source){
  var instructions=source.ToList();
  // 作者更新后若已直接调用原生升级，不再额外注入一次。
  if(instructions.Any(i=>i.operand is MethodInfo m&&m.DeclaringType==typeof(CardCmd)&&m.Name==nameof(CardCmd.Upgrade)))return instructions;
  if(instructions.Count(i=>i.Calls(Create))!=1)throw new InvalidOperationException("尘封魔典发牌路径已改变，未强行修补。");
  var result=new List<CodeInstruction>();
  foreach(var instruction in instructions){result.Add(instruction);if(instruction.Calls(Create))result.Add(new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(FakeDustyTomeFix),nameof(PrepareReward))));}
  return result;
 }
 static IEnumerable<CodeInstruction> UpgradePreview(IEnumerable<CodeInstruction> source){
  var instructions=source.ToList();
  if(instructions.Count(i=>i.Calls(Preview))!=1)throw new InvalidOperationException("尘封魔典预览路径已改变，未强行修补。");
  foreach(var instruction in instructions)if(instruction.Calls(Preview)){instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(FakeDustyTomeFix),nameof(PreparePreview));}
  return instructions;
 }
}
