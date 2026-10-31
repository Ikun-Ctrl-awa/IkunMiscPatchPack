using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace AikunMiscPatchPack;

public static class RingBossHpFix {
 static readonly Harmony H=new("Aikun.MiscPatchPack.RingBossHp");
 static MethodInfo primary=null!,initialize=null!;
 static PropertyInfo baseHp=null!,guardActive=null!;
 static object guard=null!;
 static readonly MethodInfo Lose=AccessTools.Method(typeof(CreatureCmd),nameof(CreatureCmd.LoseMaxHp));
 const string HexPrefix="HextechRunes.HextechCombatHooks+LoseMaxHpPatch";
 public static void Install(){
  var scaling=FusionQuestBlessingFix.Entry.Find("HextechRunes.HextechMaxHpScaling")??throw new MissingMemberException("海克斯生命换算接口不存在");
  var holder=FusionQuestBlessingFix.Entry.Find("HextechRunes.IHextechMaxHpBaseHolder")??throw new MissingMemberException("海克斯基础生命接口不存在");
  var hooks=FusionQuestBlessingFix.Entry.Find("HextechRunes.HextechCombatHooks")!;
  primary=AccessTools.Method(scaling,"GetPrimary")??throw new MissingMethodException("GetPrimary");
  initialize=AccessTools.Method(scaling,"EnsureBaseInitialized")??throw new MissingMethodException("EnsureBaseInitialized");
  baseHp=AccessTools.Property(holder,"BaseMaxHp")??throw new MissingMemberException("BaseMaxHp");
  guard=AccessTools.Field(hooks,"GoliathMaxHpGuard")?.GetValue(null)??throw new MissingMemberException("海克斯换算作用域不存在");
  guardActive=AccessTools.Property(guard.GetType(),"IsActive")??throw new MissingMemberException("IsActive");
  var ring=FusionQuestBlessingFix.Entry.Find("YuWanCard.Relics.RingOfSevenCurses")!;
  var victory=AccessTools.Method(ring,nameof(RelicModel.AfterCombatVictory));
  var state= victory?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType??throw new MissingMemberException("七咒之戒战后接口已改变");
  H.Patch(AccessTools.Method(state,"MoveNext"),transpiler:new HarmonyMethod(typeof(RingBossHpFix),nameof(LossBase)));
 }
 static int MaxHpForPenalty(Creature creature){
  // 戒指原先按面板生命算出绝对扣除量，海克斯却将这个量当成基础生命再乘一次倍率。
  // 仅在戒指的 Boss 惩罚中改用基础生命，保留原 25%、取整、LoseMaxHp 和全部伤害钩子。
  if(!Settings.Active("ring_boss_hp")||creature.Player is not {} player||(bool)guardActive.GetValue(guard)!)return creature.MaxHp;
  if(Harmony.GetPatchInfo(Lose)?.Prefixes.Any(p=>p.PatchMethod.DeclaringType?.FullName==HexPrefix)!=true)return creature.MaxHp;
  var holder=primary.Invoke(null,[player]);if(holder==null)return creature.MaxHp;
  initialize.Invoke(null,[player,holder,true]);return (int)baseHp.GetValue(holder)!;
 }
 static IEnumerable<CodeInstruction> LossBase(IEnumerable<CodeInstruction> source){
  var getter=AccessTools.PropertyGetter(typeof(Creature),nameof(Creature.MaxHp));int count=0;
  foreach(var instruction in source){
   if(instruction.Calls(getter)){instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(RingBossHpFix),nameof(MaxHpForPenalty));count++;}
   yield return instruction;
  }
  if(count!=1)throw new InvalidOperationException("七咒之戒生命计算路径已改变："+count);
 }
}
