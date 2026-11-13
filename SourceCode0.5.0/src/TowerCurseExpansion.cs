using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Random;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Scaffolding.Content;

namespace IkunMiscPatchPack;

public sealed class LagCurse : ModCardTemplate {
 public LagCurse():base(3,CardType.Curse,CardRarity.Curse,TargetType.Self){}
 public override CardPoolModel Pool=>ModelDb.CardPool<CurseCardPool>();
 public override IEnumerable<CardKeyword> CanonicalKeywords=>[CardKeyword.Retain,CardKeyword.Sly,CardKeyword.Exhaust];
 public override bool CanBeGeneratedInCombat=>Settings.Active(TowerCurseContent.Key);
 public override bool CanBeGeneratedByModifiers=>CanBeGeneratedInCombat;
 public override string PortraitPath=>LagCurseArt.PortraitPath;
 public override string BetaPortraitPath=>PortraitPath;
 protected override string PortraitPngPath=>PortraitPath;
 public override string? CustomPortraitPath=>PortraitPath;
 public override string? CustomBetaPortraitPath=>PortraitPath;
 public override IEnumerable<string> AllPortraitPaths=>[PortraitPath];
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new StringVar("UpgradeText","\n可以被[gold]升级[/gold]。")];
 protected override void OnUpgrade(){RemoveKeyword(CardKeyword.Exhaust);((StringVar)DynamicVars["UpgradeText"]).StringValue="";}
 protected override Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay)=>Task.CompletedTask;
}

public static class LagCurseArt {
 static readonly object artLock=new();
 static string? artFile;
 public static string PortraitPath {
  get{lock(artLock){
   if(artFile==null||!File.Exists(artFile)){
    using var stream=typeof(LagCurse).Assembly.GetManifestResourceStream("cardart.lag_curse.png")??throw new FileNotFoundException("卡了的卡图缺失");
    using var bytes=new MemoryStream();stream.CopyTo(bytes);using var image=new Image();
    if(image.LoadPngFromBuffer(bytes.ToArray())!=Error.Ok)throw new InvalidDataException("卡了的卡图PNG无效");
    artFile=EnchantmentIconTexture.Cache(image,"card-portraits");
   }
   return EnchantmentIconTexture.ResourcePath(artFile);
  }}
 }
}

public static class TowerCurseContent {
 public const string Key="tower_curse_expansion";
 sealed record TransformedCard(CardModel Original,Player Owner,ICombatState Combat);
 public static void Initialize(){
  // 三张旧牌沿用原注册入口和ID，只迁移获取开关，保持现有存档与融合素材可读。
  STS2RitsuLib.RitsuLibFramework.CreateContentPack(Settings.Id+".TowerCurses").Card<CurseCardPool,LagCurse>().Apply();
  var h=new Harmony(Settings.Id+".TowerCurses");
  // 原生测试图的私有路径也使用同一缓存资源，避免预览回退到原图。
  h.Patch(AccessTools.PropertyGetter(typeof(CardModel),"BetaPortraitPngPath"),prefix:new HarmonyMethod(typeof(TowerCurseContent),nameof(BetaPath)));
  h.Patch(AccessTools.Method(typeof(CardPoolModel),nameof(CardPoolModel.GetUnlockedCards)),postfix:new HarmonyMethod(typeof(TowerCurseContent),nameof(Filter)));
  h.Patch(AccessTools.PropertyGetter(typeof(CardPoolModel),nameof(CardPoolModel.AllCards)),postfix:new HarmonyMethod(typeof(TowerCurseContent),nameof(Filter)));
  h.Patch(AccessTools.Method(typeof(CardCmd),nameof(CardCmd.Transform),[typeof(IEnumerable<CardTransformation>),typeof(Rng),typeof(CardPreviewStyle)]),prefix:new HarmonyMethod(typeof(TowerCurseContent),nameof(BeforeTransform)),postfix:new HarmonyMethod(typeof(TowerCurseContent),nameof(AfterTransform)));
  h.Patch(AccessTools.Method(typeof(Hook),nameof(Hook.AfterCardExhausted)),postfix:new HarmonyMethod(typeof(TowerCurseContent),nameof(AfterExhausted)));
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.Initialize)),postfix:new HarmonyMethod(typeof(TowerCurseContent),nameof(Localize)));
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.SetLanguage)),postfix:new HarmonyMethod(typeof(TowerCurseContent),nameof(Localize)));
  Localize();RuntimeLoader.States[Key]="已应用。";
 }
 static bool BetaPath(CardModel __instance,ref string __result){if(__instance is not LagCurse)return true;__result=__instance.PortraitPath;return false;}
 public static bool IsOwn(CardModel card)=>card is QuCurse or DongniCurse or DongniDisgust or LagCurse;
 static bool CopiesOnDeparture(CardModel card)=>FusionQuestBlessingFix.Entry.IsOrContains<LagCurse>(card);
 static void AfterExhausted(ICombatState combatState,CardModel card,ref Task __result){if(CopiesOnDeparture(card))__result=FinishExhausted(__result,card,combatState);}
 static async Task FinishExhausted(Task original,CardModel card,ICombatState combat){
  // 融合会将通知转发成子牌自身；在原生外层通知结束后统一复制真正消耗的父牌，避免裸子牌和重复触发。
  await original;await GenerateCopy(card,card.Owner,combat);
 }
 static void Filter(ref IEnumerable<CardModel> __result)=>__result=__result.Where(c=>!IsOwn(c)||Settings.Active(Key));
 static void BeforeTransform(ref IEnumerable<CardTransformation> transformations,out TransformedCard[] __state){
  var array=transformations.ToArray();transformations=array;
  __state=array.Select(t=>t.Original).Where(CopiesOnDeparture).Distinct()
   .Where(c=>!c.HasBeenRemovedFromState&&c.Owner?.Creature.CombatState!=null)
   .Select(c=>new TransformedCard(c,c.Owner,c.Owner.Creature.CombatState!)).ToArray();
 }
 static void AfterTransform(TransformedCard[] __state,ref Task<IEnumerable<CardPileAddResult>> __result){if(__state.Length>0)__result=FinishTransform(__result,__state);}
 static async Task<IEnumerable<CardPileAddResult>> FinishTransform(Task<IEnumerable<CardPileAddResult>> original,TransformedCard[] records){
  var results=await original;
  // 等待原生变化完成，只对真正移除原牌的成功变化触发；单张/批量重载共用此处，不重复生成。
  foreach(var record in records)if(record.Original.HasBeenRemovedFromState&&record.Original.Pile==null)
   await GenerateCopy(record.Original,record.Owner,record.Combat);
  return results;
 }
 internal static async Task GenerateCopy(CardModel source,Player owner,ICombatState? combat){
  // 战斗外没有手牌，按用户选择只在仍进行中的同一场战斗内生成，不回填永久牌组。
  if(combat==null||owner.Creature.CombatState!=combat||!owner.Creature.IsAlive||CombatManager.Instance.IsOverOrEnding)return;
  var copy=source.CreateClone();
  await CardPileCmd.AddGeneratedCardsToCombat([copy],PileType.Hand,owner);
 }
 static void Localize(){
  if(LocManager.Instance==null)return;string id=ModelDb.GetId<LagCurse>().Entry;
  LocManager.Instance.GetTable("cards").MergeWith(new(){[id+".title"]="卡了",[id+".description"]="当这张牌被[gold]变化[/gold]或[gold]消耗[/gold]时，将1张本卡牌的复制品生成到手牌中。{UpgradeText}"});
 }
}
