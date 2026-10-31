using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib;
using STS2RitsuLib.Scaffolding.Content;

namespace AikunMiscPatchPack;

public sealed class QuCurse : ModCardTemplate {
 public QuCurse():base(-1,CardType.Curse,CardRarity.Curse,TargetType.None){}
 public override CardPoolModel Pool=>ModelDb.CardPool<CurseCardPool>();
 public override int MaxUpgradeLevel=>1;
 public override IEnumerable<CardKeyword> CanonicalKeywords=>[CardKeyword.Unplayable];
 public override bool HasTurnEndInHandEffect=>true;
 public override bool CanBeGeneratedInCombat=>Settings.Active(QuCurseContent.Key);
 public override bool CanBeGeneratedByModifiers=>CanBeGeneratedInCombat;
 public override string PortraitPath=>QuCurseContent.PortraitPath;
 public override string BetaPortraitPath=>PortraitPath;
 protected override string PortraitPngPath=>PortraitPath;
 public override string? CustomPortraitPath=>PortraitPath;
 public override string? CustomBetaPortraitPath=>PortraitPath;
 public override IEnumerable<string> AllPortraitPaths=>[PortraitPath];
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new DamageVar(3,ValueProp.Unpowered|ValueProp.Move)];
 protected override void OnUpgrade(){if(CurrentUpgradeLevel==1)DynamicVars.Damage.UpgradeValueBy(-2);}
 protected override Task OnTurnEndInHand(PlayerChoiceContext choiceContext){
  // 原生回合末先从手牌选出目标，再临时移入出牌区执行；此处不能再次要求Pile==Hand。
  // 与腐朽使用同一伤害命令，保留格挡、无实体和原生伤害归属规则。
  return CreatureCmd.Damage(choiceContext,Owner.Creature,DynamicVars.Damage,this,null);
 }
}

public static class QuCurseContent {
 public const string Key="qu_curse";
 static readonly object artLock=new();
 static string? artFile;
 public static string PortraitPath {
  get{
   lock(artLock){
    if(artFile==null||!File.Exists(artFile)){
     using var stream=typeof(QuCurse).Assembly.GetManifestResourceStream("cardart.qu_curse.png")??throw new FileNotFoundException("区的卡图缺失");
     using var bytes=new MemoryStream();stream.CopyTo(bytes);using var image=new Image();
     if(image.LoadPngFromBuffer(bytes.ToArray())!=Error.Ok)throw new InvalidDataException("区的卡图PNG无效");
     artFile=EnchantmentIconTexture.Cache(image,"card-portraits");
    }
    return EnchantmentIconTexture.ResourcePath(artFile);
   }
  }
 }
 public static void Initialize(){
  // 关闭获取后仍登记规范模型，确保已经获得的诅咒可以继续升级、使用和读档。
  RitsuLibFramework.CreateContentPack(Settings.Id+".QuCurse").Card<CurseCardPool,QuCurse>().Apply();
  var h=new Harmony(Settings.Id+".QuCurse");
  h.Patch(AccessTools.Method(typeof(CardPoolModel),nameof(CardPoolModel.GetUnlockedCards)),postfix:new HarmonyMethod(typeof(QuCurseContent),nameof(Filter)));
  h.Patch(AccessTools.PropertyGetter(typeof(CardPoolModel),nameof(CardPoolModel.AllCards)),postfix:new HarmonyMethod(typeof(QuCurseContent),nameof(Filter)));
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.Initialize)),postfix:new HarmonyMethod(typeof(QuCurseContent),nameof(Localize)));
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.SetLanguage)),postfix:new HarmonyMethod(typeof(QuCurseContent),nameof(Localize)));
  h.Patch(AccessTools.PropertyGetter(typeof(CardModel),"BetaPortraitPngPath"),prefix:new HarmonyMethod(typeof(QuCurseContent),nameof(BetaPath)));
  _=PortraitPath;if(LocManager.Instance!=null)Localize();RuntimeLoader.States[Key]="已应用。";
 }
 static bool BetaPath(CardModel __instance,ref string __result){if(__instance is not QuCurse)return true;__result=PortraitPath;return false;}
 public static void Filter(ref IEnumerable<CardModel> __result)=>__result=__result.Where(c=>c is not QuCurse||Settings.Active(Key));
 public static void Localize(){
  if(LocManager.Instance==null)return;string id=ModelDb.GetId<QuCurse>().Entry;
  LocManager.Instance.GetTable("cards").MergeWith(new(){[id+".title"]="区",[id+".description"]="在你的回合结束时，若这张牌在你的手牌中，受到{Damage:diff()}点伤害。\n可以被[gold]升级[/gold]。"});
 }
}
