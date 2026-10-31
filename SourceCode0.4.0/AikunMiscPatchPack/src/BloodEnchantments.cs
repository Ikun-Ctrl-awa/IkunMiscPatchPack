using System.Text.Json;
using System.Text.Json.Nodes;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Models.Capabilities;
using STS2RitsuLib.Scaffolding.Content;

namespace AikunMiscPatchPack;

// 本包附加槽由 RitsuLib 随卡保存、克隆；不替换原生槽，也不依赖第三方多附魔模组。
public sealed class BloodEnchantments : CardCapability {
 static readonly JsonSerializerOptions SaveOptions=new(){IncludeFields=true};
 List<EnchantmentModel> entries=new();
 public IReadOnlyList<EnchantmentModel> Entries=>entries;
 protected override void DeepCloneFields(){base.DeepCloneFields();entries=entries.Select(e=>(EnchantmentModel)e.ClonePreservingMutability()).ToList();}
 public override bool ShouldReceiveOwnerHooks=>Owner?.ShouldReceiveCombatHooks==true;
 public void Add<T>() where T:EnchantmentModel {
  if(Owner==null)throw new InvalidOperationException("附魔必须先关联卡牌。");
  var enchantment=ModelDb.Enchantment<T>().ToMutable();enchantment.ApplyInternal(Owner,1);entries.Add(enchantment);MarkDirty();
 }
 public override IModelCapability CloneFor(AbstractModel clonedOwner){
  var clone=(BloodEnchantments)base.CloneFor(clonedOwner);
  clone.entries=entries.Select(e=>(EnchantmentModel)e.ClonePreservingMutability()).ToList();
  clone.Bind();return clone;
 }
 protected override JsonNode? SaveAdditionalState()=>JsonSerializer.SerializeToNode(entries.Select(e=>e.ToSerializable()).ToArray(),SaveOptions);
 protected override void LoadAdditionalState(JsonNode? state,int schemaVersion){
  entries=state?.Deserialize<SerializableEnchantment[]>(SaveOptions)?.Select(EnchantmentModel.FromSerializable).ToList()??[];Bind();
 }
 protected override void OnAttach(CardModel owner)=>Bind();
 protected override void OnLoadedFromSave(CardModel owner)=>Bind();
 void Bind(){if(Owner==null)return;foreach(var e in entries)if(!e.HasCard)e.ApplyInternal(Owner,e.Amount);}
 public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext,CardPlay cardPlay){
  if(cardPlay.Card!=Owner||!cardPlay.Player.Creature.IsAlive)return;
  foreach(var e in entries)await e.OnPlay(choiceContext,cardPlay);
 }
}

public sealed class BloodScorching : ModEnchantmentTemplate {
 public override bool HasExtraCardText=>true;
 public override bool ShowAmount=>false;
 public override string? CustomIconPath=>BloodInitiationArt.IconPath(BloodInitiationArt.ScorchingPath);
 public override bool CanEnchant(CardModel card)=>base.CanEnchant(card)&&card.MaxUpgradeLevel>0&&!BloodEnchantmentRules.HasScorching(card);
}

public sealed class WeepingBlood : ModEnchantmentTemplate {
 public override bool HasExtraCardText=>true;
 public override bool ShowAmount=>false;
 public override string? CustomIconPath=>BloodInitiationArt.IconPath(BloodInitiationArt.WeepingPath);
 public override bool CanEnchantCardType(CardType cardType)=>cardType==CardType.Attack;
 public override bool CanEnchant(CardModel card)=>base.CanEnchant(card)&&!EnchantmentView.Read(card).Any(e=>e is WeepingBlood);
 public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext,Creature? dealer,DamageResult result,ValueProp props,Creature target,CardModel? cardSource){
  var owner=Card.Owner.Creature;
  if(!owner.IsAlive||target.Side==owner.Side||!BloodSectRuntime.IsDamageFrom(Card,result)||result.UnblockedDamage<=0)return;
  int heal=(int)decimal.Floor(result.UnblockedDamage/4m);
  if(heal>0)await CreatureCmd.Heal(owner,heal);
 }
}

public static class BloodEnchantmentRules {
 static readonly Harmony Compat=new("AikunMiscPatchPack.BloodScorching.Compatibility");
 public static IEnumerable<EnchantmentModel> Extra(CardModel card)=>card.IsMutable?card.Capability<BloodEnchantments>()?.Entries??[]:[];
 // 识别已知灼热提供者的模型，不按中文名称猜测、不加载或调用它们的实现。
 public static bool IsScorching(EnchantmentModel e)=>e is BloodScorching||e.GetType().FullName is "MoreEnchant.Enchantments.ScorchingEnchantment" or "MoreEnchantmentsMod.Scorching" or "MoreEnchantmentsMod.ScorchingEnchantment";
 public static bool HasScorching(CardModel card)=>EnchantmentView.Read(card).Any(e=>e.Status!=EnchantmentStatus.Disabled&&IsScorching(e));
 public static void AddInitiation(CardModel card){
  bool sown=EnchantmentView.Read(card).Any(e=>e is Sown),scorch=HasScorching(card);
  var slots=card.GetOrCreateCapability<BloodEnchantments>();
  if(!sown)slots.Add<Sown>();if(!scorch)slots.Add<BloodScorching>();
 }
 public static void AddWeeping(CardModel card){if(!EnchantmentView.Read(card).Any(e=>e is WeepingBlood))card.GetOrCreateCapability<BloodEnchantments>().Add<WeepingBlood>();}
 public static int ScorchingLimit(CardModel card)=>HasScorching(card)?Math.Max(999,card.CurrentUpgradeLevel<int.MaxValue?card.CurrentUpgradeLevel+1:int.MaxValue):0;
 public static void AfterMods(){
  var fusion=FusionQuestBlessingFix.Entry.Find("CardFusion.MultiEnchantmentCompat");
  if(fusion!=null)Compat.Patch(AccessTools.Method(fusion,"RunEnchantmentsOnPlay"),postfix:new HarmonyMethod(typeof(BloodEnchantmentRules),nameof(FusionOnPlay)));
  foreach(var type in ModelDb.AllAbstractModelSubtypes.Where(t=>typeof(EnchantmentModel).IsAssignableFrom(t)&&t!=typeof(BloodScorching))){
   if(type.FullName is not ("MoreEnchant.Enchantments.ScorchingEnchantment" or "MoreEnchantmentsMod.Scorching" or "MoreEnchantmentsMod.ScorchingEnchantment"))continue;
   var method=AccessTools.DeclaredMethod(type,nameof(EnchantmentModel.CanEnchant));
   if(method!=null)Compat.Patch(method,postfix:new HarmonyMethod(typeof(BloodEnchantmentRules),nameof(CompatibleEligibility)));
  }
 }
 static void FusionOnPlay(CardModel card,PlayerChoiceContext choiceContext,CardPlay cardPlay,ref Task __result)=>__result=RunFusionExtras(__result,card,choiceContext,cardPlay);
 static async Task RunFusionExtras(Task original,CardModel sourceCard,PlayerChoiceContext context,CardPlay play){
  await original;if(!sourceCard.Owner.Creature.IsAlive)return;
  foreach(var e in Extra(sourceCard))await e.OnPlay(context,play);
 }
 static void CompatibleEligibility(CardModel card,ref bool __result){if(__result&&HasScorching(card))__result=false;}
 public static void Install(Harmony h){
  MultiEnchantIcons.InstallOwn(h);
  h.Patch(AccessTools.Method(typeof(Hook),nameof(Hook.AfterDamageGiven)),postfix:new HarmonyMethod(typeof(BloodEnchantmentRules),nameof(DamageExtras)));
  h.Patch(AccessTools.PropertyGetter(typeof(EnchantmentModel),nameof(EnchantmentModel.HoverTip)),prefix:new HarmonyMethod(typeof(BloodEnchantmentRules),nameof(EnchantHoverTip)));
  h.Patch(AccessTools.PropertyGetter(typeof(CardModel),nameof(CardModel.HoverTips)),postfix:new HarmonyMethod(typeof(BloodEnchantmentRules),nameof(HoverTips)));
  foreach(var method in typeof(CardModel).GetMethods(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Where(m=>m.Name=="GetDescriptionForPile"&&m.GetParameters().Length==3))
   h.Patch(method,postfix:new HarmonyMethod(typeof(BloodEnchantmentRules),nameof(Description)));
 }
 static void DamageExtras(PlayerChoiceContext choiceContext,DamageResult results,Creature? dealer,ValueProp props,Creature target,CardModel? cardSource,ref Task __result)=>__result=RunDamageExtras(__result,choiceContext,results,dealer,props,target,cardSource);
 static async Task RunDamageExtras(Task original,PlayerChoiceContext context,DamageResult result,Creature? dealer,ValueProp props,Creature target,CardModel? cardSource){
  await original;var source=BloodSectRuntime.DirectCard(result);if(source==null)return;
  // 融合素材不属于战斗牌堆的监听模型，显式结算其附加槽；永久牌组不会参与。
  var cards=new List<CardModel>{source};
  if(source.Owner.PlayerCombatState is {} state)cards.AddRange(state.AllCards.Where(c=>!ReferenceEquals(c,source)&&FusionQuestBlessingFix.Entry.Descendants(c).Any(child=>ReferenceEquals(child,source))));
  foreach(var card in cards.Distinct())foreach(var e in Extra(card))await e.AfterDamageGiven(context,dealer,result,props,target,cardSource);
 }
 static bool EnchantHoverTip(EnchantmentModel __instance,ref HoverTip __result){
  if(__instance is not (BloodScorching or WeepingBlood or ThunderArc))return true;
  __result=new HoverTip(__instance.Title,__instance.DynamicDescription,EnchantmentView.ResolveIcon(__instance));return false;
 }
 static void HoverTips(CardModel __instance,ref IEnumerable<IHoverTip> __result)=>__result=__result.Concat(Extra(__instance).SelectMany(e=>e.HoverTips)).ToArray();
 static void Description(CardModel __instance,ref string __result){
  // 长能力牌用一行名称提示，详细效果保留在原生悬停说明，避免附加文字挤出卡面。
  if(__instance is BloodSect){var names=Extra(__instance).Select(e=>e.Title.GetFormattedText()).ToArray();if(names.Length>0)__result="[purple]"+string.Join(" · ",names)+"[/purple]\n"+__result;return;}
  foreach(var e in Extra(__instance))if(e.DynamicExtraCardText is {} text)__result+="\n[purple]"+text.GetFormattedText()+"[/purple]";
 }
}
