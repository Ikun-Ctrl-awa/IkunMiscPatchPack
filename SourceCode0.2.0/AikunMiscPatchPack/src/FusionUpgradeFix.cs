using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace AikunMiscPatchPack;

public static class FusionUpgradeFix {
 static readonly Harmony H=new("Aikun.MiscPatchPack.FusionUpgrades");
 static readonly FieldInfo NativeLevel=AccessTools.Field(typeof(CardModel),"_currentUpgradeLevel");
 static readonly FieldInfo EnchantmentField=AccessTools.Field(typeof(CardModel),"<Enchantment>k__BackingField");
 static readonly ConditionalWeakTable<EnchantmentModel,RepeatInfo> RepeatCache=new();
 static Type fused=null!;
 static FieldInfo sources=null!;
 static MethodInfo? readExternalLevel,trackExternalLevel;
 [ThreadStatic] static HashSet<CardModel>? syncing,upgrading;
 [ThreadStatic] static Grant? grant;
 sealed record Grant(CardModel Parent,CardModel Child,int Limit);
 sealed class RepeatInfo {public RepeatInfo(){} public int Amount=int.MinValue;public bool Repeats;}
 sealed record UpgradeState(CardModel Card,int Native,int Effective,bool Parent);
 static bool IsFused(CardModel card)=>fused.IsInstanceOfType(card);
 static bool Active=>Settings.Active("fusion_upgrades");
 static IReadOnlyList<CardModel> Children(CardModel card)=>(IReadOnlyList<CardModel>)sources.GetValue(card)!;

 public static void Install(){
  fused=FusionQuestBlessingFix.Entry.Find("CardFusion.FusedCard")??throw new InvalidOperationException("未找到融合卡类型。");
  sources=AccessTools.Field(fused,"_sourceCards")??throw new MissingFieldException(fused.FullName,"_sourceCards");
  var store=FusionQuestBlessingFix.Entry.Find("FreeLoadout.OverrideStore");
  if(store!=null){readExternalLevel=AccessTools.Method(store,"GetCardUpgradeLevel",[typeof(CardModel)]);trackExternalLevel=AccessTools.Method(store,"TrackUpgradeLevel",[typeof(CardModel),typeof(int)]);}
  EnchantmentView.Discover();
  Patch(AccessTools.PropertyGetter(typeof(CardModel),nameof(CardModel.CurrentUpgradeLevel)),postfix:nameof(CurrentLevel));
  Patch(AccessTools.PropertyGetter(typeof(CardModel),nameof(CardModel.IsUpgradable)),postfix:nameof(Upgradable));
  var getters=ModelDb.AllAbstractModelSubtypes.Where(t=>typeof(CardModel).IsAssignableFrom(t)).Select(t=>AccessTools.DeclaredPropertyGetter(t,nameof(CardModel.MaxUpgradeLevel)))
   .Append(AccessTools.PropertyGetter(typeof(CardModel),nameof(CardModel.MaxUpgradeLevel))).Where(m=>m?.GetMethodBody()!=null).Distinct();
  foreach(var getter in getters)Patch(getter!,postfix:nameof(MaximumLevel));
  Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.UpgradeInternal)),prefix:nameof(BeforeUpgrade),finalizer:nameof(AfterUpgrade),first:true);
  Patch(AccessTools.Method(fused,"CreateFromList"),postfix:nameof(Created));
  Patch(AccessTools.Method(fused,"DeepCloneFields"),prefix:nameof(BeforeClone),postfix:nameof(AfterClone));
  Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.FromSerializable)),postfix:nameof(Restored));
  Patch(AccessTools.Method(fused,"OnUpgrade"),prefix:nameof(EnterGrant),transpiler:nameof(SelectMaterial),finalizer:nameof(LeaveGrant));
 }
 static void Patch(MethodInfo method,string? prefix=null,string? postfix=null,string? transpiler=null,string? finalizer=null,bool first=false){
  var owners=Harmony.GetPatchInfo(method)?.Owners.ToArray()??[];
  HarmonyMethod? Hook(string? name)=>name==null?null:new(typeof(FusionUpgradeFix),name){priority=first?Priority.First:Priority.Last,before=first?owners:[],after=first?[]:owners};
  H.Patch(method,Hook(prefix),Hook(postfix),Hook(transpiler),Hook(finalizer));
 }
 static int Level(CardModel card)=>IsFused(card)?Sync(card):readExternalLevel!=null&&trackExternalLevel!=null?(int)readExternalLevel.Invoke(null,[card])!:card.CurrentUpgradeLevel;
 static int Total(CardModel parent)=>(int)Math.Min(int.MaxValue,Children(parent).Sum(c=>(long)Math.Max(0,Level(c))));
 static int Sync(CardModel card){
  if(!card.IsMutable||!IsFused(card)||Children(card).Count==0)return (int)NativeLevel.GetValue(card)!;
  var busy=syncing??=new(ReferenceEqualityComparer.Instance);
  if(!busy.Add(card))return (int)NativeLevel.GetValue(card)!;
  try{
   int level=Total(card);NativeLevel.SetValue(card,level);
   // 仅校正实例的弱引用等级记录，不写自由配装的配置或保存文件。
   trackExternalLevel?.Invoke(null,[card,level]);return level;
  }finally{busy.Remove(card);}
 }
 static bool Repeats(EnchantmentModel enchantment){
  if(enchantment.GetType().FullName=="MoreEnchant.Enchantments.ScorchingEnchantment")return true;
  var cached=RepeatCache.GetOrCreateValue(enchantment);if(cached.Amount==enchantment.Amount)return cached.Repeats;
  // 只探测附魔对升级上限的读取，不执行 ModifyCard、升级、伤害或随机效果。
  var probe=ModelDb.Card<Bash>().ToMutable();int baseline=probe.MaxUpgradeLevel;
  var copy=(EnchantmentModel)enchantment.ClonePreservingMutability();copy.ApplyInternal(probe,copy.Amount);EnchantmentField.SetValue(probe,copy);
  cached.Amount=enchantment.Amount;return cached.Repeats=probe.MaxUpgradeLevel>baseline;
 }
 static bool HasRepeat(CardModel card)=>EnchantmentView.Read(card).Any(Repeats);
 static int Limit(CardModel parent,CardModel child){
  if(IsFused(child))return child.MaxUpgradeLevel;
  // 不能因父牌附魔或第三方全局突破开关，把本身不可升级的诅咒、状态等变成可升级素材。
  if(ModelDb.GetById<CardModel>(child.Id).MaxUpgradeLevel<=0)return 0;
  int limit=child.MaxUpgradeLevel;
  if(HasRepeat(parent)||HasRepeat(child))limit=Math.Max(limit,(int)Math.Min(int.MaxValue,Math.Max(999L,(long)Level(child)+1)));
  return limit;
 }
 static bool CanUpgrade(CardModel parent,CardModel child)=>Level(child)<Limit(parent,child)
  &&(child.IsUpgradable||HasRepeat(parent)||HasRepeat(child));
 static CardModel? Next(CardModel parent)=>Children(parent).FirstOrDefault(c=>CanUpgrade(parent,c));
 static void CurrentLevel(CardModel __instance,ref int __result){
  if(Active&&IsFused(__instance)&&upgrading?.Contains(__instance)!=true&&syncing?.Contains(__instance)!=true)__result=Sync(__instance);
 }
 static void MaximumLevel(CardModel __instance,ref int __result){
  if(!Active)return;
  if(grant is { } g&&ReferenceEquals(g.Child,__instance))__result=Math.Max(__result,g.Limit);
  if(!IsFused(__instance))return;
  long total=Total(__instance);
  foreach(var child in Children(__instance))if(CanUpgrade(__instance,child))total+=Math.Max(0L,(long)Limit(__instance,child)-Level(child));
  __result=(int)Math.Min(int.MaxValue,total);
 }
 static void Upgradable(CardModel __instance,ref bool __result){
  if(Active&&IsFused(__instance))__result=Sync(__instance)<int.MaxValue&&Next(__instance)!=null;
 }
 static bool BeforeUpgrade(CardModel __instance,out UpgradeState? __state){
  __state=null;if(!Active)return true;
  bool parent=IsFused(__instance);
  if(parent){Sync(__instance);if(!__instance.IsUpgradable)return false;(upgrading??=new(ReferenceEqualityComparer.Instance)).Add(__instance);}
  if(parent||ReferenceEquals(grant?.Child,__instance))__state=new(__instance,(int)NativeLevel.GetValue(__instance)!,Level(__instance),parent);
  return true;
 }
 static void AfterUpgrade(UpgradeState? __state){
  if(__state==null)return;
  if(__state.Parent){upgrading!.Remove(__state.Card);Sync(__state.Card);}
  else if(__state.Effective>__state.Native){
   int difference=(int)NativeLevel.GetValue(__state.Card)!-__state.Native;
   if(difference>0)trackExternalLevel?.Invoke(null,[__state.Card,__state.Effective+difference]);
  }
 }
 static void Created(CardModel __result){if(Active)Sync(__result);}
 static void Restored(CardModel __result){if(Active&&__result!=null&&IsFused(__result))Sync(__result);}
 static void BeforeClone(CardModel __instance,out CardModel[] __state)=>__state=Active?Children(__instance).ToArray():[];
 static void AfterClone(CardModel __instance,CardModel[] __state){
  if(!Active)return;
  var children=Children(__instance);
  if(trackExternalLevel!=null)for(int i=0;i<Math.Min(children.Count,__state.Length);i++)trackExternalLevel.Invoke(null,[children[i],Level(__state[i])]);
  Sync(__instance);
 }
 static void EnterGrant(CardModel __instance,out Grant? __state){
  __state=grant;if(!Active)return;
  var child=Next(__instance);grant=child==null?null:new(__instance,child,Limit(__instance,child));
 }
 static void LeaveGrant(Grant? __state)=>grant=__state;
 static IReadOnlyList<CardModel> SelectedChildren(CardModel parent)=>!Active?Children(parent):grant is { } g&&ReferenceEquals(g.Parent,parent)?[g.Child]:[];
 static IEnumerable<CodeInstruction> SelectMaterial(IEnumerable<CodeInstruction> instructions){
  var getter=AccessTools.PropertyGetter(fused,"AllCards");
  foreach(var instruction in instructions){
   // 原模组仍负责升级素材和刷新描述、费用、附魔；只将枚举范围限定为首个合格素材。
   if(instruction.Calls(getter)){instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(FusionUpgradeFix),nameof(SelectedChildren));}
   yield return instruction;
  }
 }
}
