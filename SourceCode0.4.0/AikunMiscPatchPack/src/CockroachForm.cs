using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2RitsuLib;
using STS2RitsuLib.Scaffolding.Content;

namespace AikunMiscPatchPack;

public sealed class CockroachForm : ModCardTemplate {
 public CockroachForm():base(3,CardType.Power,CardRarity.Rare,TargetType.Self){}
 public override CardPoolModel Pool=>ModelDb.CardPool<ColorlessCardPool>();
 public override bool CanBeGeneratedInCombat=>Settings.Active(CockroachFormContent.Key);
 public override bool CanBeGeneratedByModifiers=>CanBeGeneratedInCombat;
 public override string PortraitPath=>CockroachFormContent.PortraitPath;
 public override string BetaPortraitPath=>PortraitPath;
 protected override string PortraitPngPath=>PortraitPath;
 public override string? CustomPortraitPath=>PortraitPath;
 public override string? CustomBetaPortraitPath=>PortraitPath;
 public override IEnumerable<string> AllPortraitPaths=>[PortraitPath];
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new PowerVar<HardToKillPower>(5),new DynamicVar("PerTurn",3)];
 protected override IEnumerable<IHoverTip> AdditionalHoverTips=>[HoverTipFactory.FromPower<HardToKillPower>()];
 protected override void OnUpgrade(){
  // 难以杀灭是每次伤害的上限；按设计从5降到1，重复强化不再继续扣成负数。
  if(CurrentUpgradeLevel==1){DynamicVars["HardToKillPower"].UpgradeValueBy(-4);AddKeyword(CardKeyword.Retain);}
 }
 protected override async Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay){
  await PowerCmd.Apply<HardToKillPower>(choiceContext,Owner.Creature,DynamicVars["HardToKillPower"].BaseValue,Owner.Creature,this);
  await PowerCmd.Apply<CockroachFormPower>(choiceContext,Owner.Creature,DynamicVars["PerTurn"].BaseValue,Owner.Creature,this);
 }
}

public sealed class CockroachFormPower : ModPowerTemplate {
 public override PowerType Type=>PowerType.Buff;
 public override PowerStackType StackType=>PowerStackType.Counter;
 public override string? CustomIconPath=>ModelDb.Power<HardToKillPower>().PackedIconPath;
 public override string? CustomBigIconPath=>CustomIconPath;
 protected override IEnumerable<IHoverTip> AdditionalHoverTips=>[HoverTipFactory.FromPower<HardToKillPower>()];
 public override async Task AfterSideTurnStart(CombatSide side,IReadOnlyList<Creature> participants,ICombatState combatState){
  if(!Owner.IsAlive||Owner.Player==null||side!=Owner.Side||!participants.Contains(Owner))return;
  Flash();
  await PowerCmd.Apply<HardToKillPower>(new ThrowingPlayerChoiceContext(),Owner,Amount,Owner,null);
 }
}

public static class CockroachFormContent {
 public const string Key="cockroach_form";
 static readonly object artLock=new();
 static string? artFile;
 public static string PortraitPath {
  get{
   lock(artLock){
    if(artFile==null||!File.Exists(artFile)){
     using var stream=typeof(CockroachForm).Assembly.GetManifestResourceStream("cardart.cockroach_form.png")??throw new FileNotFoundException("蟑螂形态插画缺失");
     using var bytes=new MemoryStream();stream.CopyTo(bytes);using var image=new Image();
     if(image.LoadPngFromBuffer(bytes.ToArray())!=Error.Ok)throw new InvalidDataException("蟑螂形态插画PNG无效");
     artFile=EnchantmentIconTexture.Cache(image,"card-portraits");
    }
    return EnchantmentIconTexture.ResourcePath(artFile);
   }
  }
 }
 public static void Initialize(){
  // 始终登记以支持既有牌组；开关只控制后续获取，不关闭已打出状态的效果。
  RitsuLibFramework.CreateContentPack(Settings.Id+".CockroachForm").Card<ColorlessCardPool,CockroachForm>().Power<CockroachFormPower>().Apply();
  var h=new Harmony(Settings.Id+".CockroachForm");
  h.Patch(AccessTools.Method(typeof(CardPoolModel),nameof(CardPoolModel.GetUnlockedCards)),postfix:new HarmonyMethod(typeof(CockroachFormContent),nameof(Filter)));
  h.Patch(AccessTools.PropertyGetter(typeof(CardPoolModel),nameof(CardPoolModel.AllCards)),postfix:new HarmonyMethod(typeof(CockroachFormContent),nameof(Filter)));
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.Initialize)),postfix:new HarmonyMethod(typeof(CockroachFormContent),nameof(Localize)));
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.SetLanguage)),postfix:new HarmonyMethod(typeof(CockroachFormContent),nameof(Localize)));
  h.Patch(AccessTools.PropertyGetter(typeof(CardModel),"BetaPortraitPngPath"),prefix:new HarmonyMethod(typeof(CockroachFormContent),nameof(BetaPath)));
  _=PortraitPath;if(LocManager.Instance!=null)Localize();
  RuntimeLoader.States[Key]="已应用。";
 }
 static bool BetaPath(CardModel __instance,ref string __result){if(__instance is not CockroachForm)return true;__result=PortraitPath;return false;}
 public static void Filter(ref IEnumerable<CardModel> __result)=>__result=__result.Where(c=>c is not CockroachForm||Settings.Active(Key));
 public static void Localize(){
  if(LocManager.Instance==null)return;
  string id=ModelDb.GetId<CockroachForm>().Entry,power=ModelDb.GetId<CockroachFormPower>().Entry;
  LocManager.Instance.GetTable("cards").MergeWith(new(){[id+".title"]="蟑螂形态",[id+".description"]="获得{HardToKillPower:diff()}层[gold]难以杀灭[/gold]。\n在你的回合开始时，获得{PerTurn:diff()}层[gold]难以杀灭[/gold]。"});
  const string text="在你的回合开始时，获得{Amount}层[gold]难以杀灭[/gold]。";
  LocManager.Instance.GetTable("powers").MergeWith(new(){[power+".title"]="蟑螂形态",[power+".description"]=text,[power+".smartDescription"]=text});
 }
}
