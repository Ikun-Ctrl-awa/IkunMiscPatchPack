using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace AikunMiscPatchPack;
public static class BloodSectRuntime {
 static readonly AsyncLocal<AbstractModel?> Source=new();
 sealed record DamageAttribution(Player? Controller,CardModel? Card);
 sealed record CommandOrigin(AbstractModel Emitter);
 static readonly AsyncLocal<DamageAttribution?> CurrentDamage=new();
 static readonly ConditionalWeakTable<DamageResult,DamageAttribution> DamageOrigins=new();
 static readonly ConditionalWeakTable<AttackCommand,CommandOrigin> CommandOrigins=new();
 static readonly ConditionalWeakTable<DamageResult,object> FullyAbsorbed=new();
 static readonly Dictionary<MethodInfo,bool> BlockCalls=new();
 static readonly Dictionary<short,OpCode> OpCodesByValue=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>o.Value);
 public static void Install(){
  var h=new Harmony("AikunMiscPatchPack.BloodSect.Runtime");
  var damage=AccessTools.Method(typeof(CreatureCmd),nameof(CreatureCmd.Damage),[typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),typeof(IEnumerable<Creature>),typeof(decimal),typeof(ValueProp),typeof(Creature),typeof(CardModel),typeof(MegaCrit.Sts2.Core.Entities.Cards.CardPlay)]);
  var moveNext=AccessTools.Method(damage.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType,"MoveNext");
  h.Patch(moveNext,transpiler:new HarmonyMethod(typeof(BloodSectRuntime),nameof(RouteDamage)));
  h.Patch(damage,prefix:new HarmonyMethod(typeof(BloodSectRuntime),nameof(BeginDamage)),finalizer:new HarmonyMethod(typeof(BloodSectRuntime),nameof(EndDamage)));
  foreach(string name in new[]{nameof(AttackCommand.FromCard),nameof(AttackCommand.FromOsty)})h.Patch(AccessTools.Method(typeof(AttackCommand),name),postfix:new HarmonyMethod(typeof(BloodSectRuntime),nameof(RecordCommand)));
  h.Patch(AccessTools.Method(typeof(AttackCommand),nameof(AttackCommand.Execute)),prefix:new HarmonyMethod(typeof(BloodSectRuntime),nameof(BeginCommand)),finalizer:new HarmonyMethod(typeof(BloodSectRuntime),nameof(EndSource)));
  h.Patch(AccessTools.Method(typeof(Creature),nameof(Creature.GainBlockInternal)),prefix:new HarmonyMethod(typeof(BloodSectRuntime),nameof(InternalBlock)));
  h.Patch(AccessTools.DeclaredMethod(typeof(ImbalancedPower),nameof(AbstractModel.AfterDamageGiven)),prefix:new HarmonyMethod(typeof(BloodSectRuntime),nameof(Imbalance)));
  // 记录实际发放格挡或伤害的模型回调，含非虚 Trigger 与异步延续，不依赖模组名称。
  foreach(var type in ModelDb.AllAbstractModelSubtypes)
   foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly).Where(m=>!m.IsAbstract&&!m.ContainsGenericParameters&&typeof(Task).IsAssignableFrom(m.ReturnType)&&m.GetMethodBody()!=null))
    if(CallsBlock(method,new(),0)){
     try{h.Patch(method,prefix:new HarmonyMethod(typeof(BloodSectRuntime),nameof(BeginSource)),finalizer:new HarmonyMethod(typeof(BloodSectRuntime),nameof(EndSource)));}
     catch(Exception e) when(e.GetBaseException() is MissingMemberException or TypeLoadException or BadImageFormatException){Godot.GD.Print("[AikunMiscPatchPack] blood source scope skipped incompatible method: "+method.DeclaringType?.FullName+"."+method.Name+": "+e.GetBaseException().Message);}
    }
 }
 static bool CallsBlock(MethodInfo method,HashSet<MethodInfo> seen,int depth){
  if(BlockCalls.TryGetValue(method,out bool cached))return cached;
  if(depth>3||!seen.Add(method))return false;
  var body=method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType.GetMethod("MoveNext",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)??method;
  if(body.GetMethodBody()==null)return false;
  var calls=Calls(body).ToArray();
  bool found=calls.Any(m=>(m.DeclaringType==typeof(CreatureCmd)&&m.Name is nameof(CreatureCmd.GainBlock) or nameof(CreatureCmd.Damage))||(m.DeclaringType==typeof(DamageCmd)&&m.Name==nameof(DamageCmd.Attack))||(m.DeclaringType==typeof(AttackCommand)&&m.Name==nameof(AttackCommand.Execute)));
  if(!found)found=calls.Where(m=>!m.ContainsGenericParameters&&(m.DeclaringType==method.DeclaringType||(m.IsStatic&&m.DeclaringType?.Assembly==method.DeclaringType?.Assembly&&(m.DeclaringType?.Assembly!=typeof(AbstractModel).Assembly||m.DeclaringType?.Namespace==method.DeclaringType?.Namespace)))).Any(m=>CallsBlock(m,seen,depth+1));
  return BlockCalls[method]=found;
 }
 // 读取原始 IL 的调用令牌，不为泛型异步状态机生成动态方法或局部变量。
 static IEnumerable<MethodInfo> Calls(MethodInfo method){
  var bytes=method.GetMethodBody()?.GetILAsByteArray()??[];int p=0;
  while(p<bytes.Length){
   short value=bytes[p++];if(value==0xfe)value=(short)(0xfe00|bytes[p++]);var op=OpCodesByValue[value];
   int size=op.OperandType switch{OperandType.InlineNone=>0,OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar=>1,OperandType.InlineVar=>2,OperandType.InlineI8 or OperandType.InlineR=>8,OperandType.InlineSwitch=>4+4*BitConverter.ToInt32(bytes,p),_=>4};
   if(op.OperandType==OperandType.InlineMethod){
    MethodInfo? target=null;try{target=method.Module.ResolveMethod(BitConverter.ToInt32(bytes,p),method.DeclaringType?.GetGenericArguments(),method.IsGenericMethod?method.GetGenericArguments():null) as MethodInfo;}
    catch(Exception e) when(e is ArgumentException or MissingMemberException or TypeLoadException or BadImageFormatException or NotSupportedException){}
    if(target!=null)yield return target;
   }
   p+=size;
  }
 }
 static void BeginSource(AbstractModel __instance,out AbstractModel? __state){__state=Source.Value;Source.Value=__instance;}
 static Exception? EndSource(Exception? __exception,AbstractModel? __state){Source.Value=__state;return __exception;}
 static void RecordCommand(AttackCommand __instance,CardModel card){
  var emitter=Source.Value??card;CommandOrigins.GetValue(__instance,_=>new(emitter));
 }
 static void BeginCommand(AttackCommand __instance,out AbstractModel? __state){
  __state=Source.Value;if(CommandOrigins.TryGetValue(__instance,out var origin))Source.Value=origin.Emitter;
 }
 static void BeginDamage(Creature? dealer,CardModel? cardSource,CardPlay? cardPlay,out DamageAttribution? __state){
  __state=CurrentDamage.Value;var card=cardSource??cardPlay?.Card??Source.Value as CardModel;
  var controller=cardPlay?.Player??(card?.IsMutable==true?card.Owner:null);
  // 追加效果即使转传触发卡，也不能借此冒充卡牌伤害；卡牌本体保留技能、多段、群体及宠物攻击。
  bool direct=(Source.Value==null||Source.Value is CardModel)&&card!=null&&controller!=null&&(dealer==null||dealer==controller.Creature||dealer.PetOwner==controller);
  CurrentDamage.Value=new(direct?controller:null,direct?card:null);
 }
 static Exception? EndDamage(Exception? __exception,DamageAttribution? __state){CurrentDamage.Value=__state;return __exception;}
 public static bool IsCardDamageFor(Creature owner,DamageResult result)=>DamageOrigins.TryGetValue(result,out var origin)&&origin.Controller?.Creature==owner;
 public static bool OwnSource(Creature owner,CardModel? card){
  // 状态效果与药水始终允许；不沿状态的施加者追溯回卡牌或遗物。
  if(Source.Value is PowerModel or PotionModel)return false;
  return (card??Source.Value) switch{CardModel c=>c.Owner?.Creature==owner,RelicModel r=>r.Owner?.Creature==owner,_=>false};
 }
 static bool InternalBlock(Creature __instance){
  var power=__instance.Powers.OfType<BloodSectPower>().FirstOrDefault();
  return power==null||!OwnSource(__instance,null);
 }
 static IEnumerable<CodeInstruction> RouteDamage(IEnumerable<CodeInstruction> source){
  var code=source.ToList();int count=0;var original=AccessTools.Method(typeof(Creature),nameof(Creature.LoseHpInternal));
  foreach(var i in code)if(i.Calls(original)){i.opcode=OpCodes.Call;i.operand=AccessTools.Method(typeof(BloodSectRuntime),nameof(ApplyDamage));count++;}
  if(count!=2)throw new InvalidOperationException("伤害结算接口已改变，拒绝套用临时血量补丁。");
  return code;
 }
 // 仅接管伤害命令的扣血，不能拦截强制死亡等直接调用的内部接口。
 static DamageResult ApplyDamage(Creature target,decimal amount,ValueProp props){
  var pool=target.Powers.OfType<TemporaryBloodPower>().FirstOrDefault();int used=0,incoming=(int)Math.Clamp(amount,0,999999999m);
  if(pool!=null&&pool.Amount>0&&target.IsAlive){
   used=Math.Min(incoming,pool.Amount);pool.SetAmount(pool.Amount-used);amount-=used;
  }
  var result=target.LoseHpInternal(amount,props);
  if(CurrentDamage.Value is {} origin)DamageOrigins.Add(result,origin);
  if(used>0&&used==incoming&&result.UnblockedDamage==0)FullyAbsorbed.GetValue(result,_=>new object());
  return result;
 }
 static bool Imbalance(ImbalancedPower __instance,Creature? dealer,DamageResult result,ref Task __result){
  if(dealer!=__instance.Owner||result.WasFullyBlocked||!FullyAbsorbed.TryGetValue(result,out _))return true;
  AccessTools.Method(typeof(PowerModel),"Flash").Invoke(__instance,null);
  if(__instance.Owner.Monster is BowlbugRock rock){rock.IsOffBalance=true;__result=Task.CompletedTask;}
  else __result=CreatureCmd.Stun(__instance.Owner);
  return false;
 }
}
