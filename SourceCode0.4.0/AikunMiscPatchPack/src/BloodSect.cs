using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Scaffolding.Content;

namespace AikunMiscPatchPack;
public sealed class BloodSect : ModCardTemplate {
 public BloodSect():base(4,CardType.Power,CardRarity.Uncommon,TargetType.Self){}
 public override CardPoolModel Pool=>ModelDb.CardPool<ColorlessCardPool>();
 public override bool CanBeGeneratedInCombat=>Settings.Active(BloodSectContent.Key);
 public override bool CanBeGeneratedByModifiers=>CanBeGeneratedInCombat;
 public override string PortraitPath=>BloodSectArt.Path;
 public override string BetaPortraitPath=>PortraitPath;
 protected override string PortraitPngPath=>PortraitPath;
 public override string? CustomPortraitPath=>PortraitPath;
 public override string? CustomBetaPortraitPath=>PortraitPath;
 public override IEnumerable<string> AllPortraitPaths=>[PortraitPath];
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new DynamicVar("LeechPercent",100),new StringVar("EffectText",BloodSectContent.Text(0))];
 protected override void OnUpgrade(){
  if(CurrentUpgradeLevel==1){EnergyCost.UpgradeBy(-1);AddKeyword(CardKeyword.Retain);}
  if(CurrentUpgradeLevel>=2)DynamicVars["LeechPercent"].UpgradeValueBy(20);
  ((StringVar)DynamicVars["EffectText"]).StringValue=BloodSectContent.Text(CurrentUpgradeLevel);
 }
 internal void SyncConsoleUpgrade(int level){
  // 控制台的突破等级可能高于原生字段；首次升级和后续升级不能按同一个增量外推。
  DynamicVars["LeechPercent"].BaseValue=100+20*Math.Max(0,level-1);
  ((StringVar)DynamicVars["EffectText"]).StringValue=BloodSectContent.Text(level);
  if(level>=1)AddKeyword(CardKeyword.Retain);
 }
 protected override async Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay){
  var power=Owner.Creature.GetPower<BloodSectPower>()??(BloodSectPower)ModelDb.Power<BloodSectPower>().ToMutable();
  // 溢出许可独立锁存；后续普通牌只增加比例，不会关闭已经开启的临时血池。
  power.RegisterUpgrade(BloodSectConsoleCompat.Level(this));
  await PowerCmd.Apply(choiceContext,power,Owner.Creature,DynamicVars["LeechPercent"].IntValue,Owner.Creature,this);
 }
}
public sealed class BloodSectPower : ModPowerTemplate {
 public override PowerType Type=>PowerType.Buff;
 public override PowerStackType StackType=>PowerStackType.Counter;
 int level;
 public int UpgradeLevel=>level;
 public bool OverflowEnabled{get;private set;}
 public int LeechPercent=>Amount;
 public void RegisterUpgrade(int upgradeLevel){
  level=Math.Max(level,Math.Max(0,upgradeLevel));OverflowEnabled|=upgradeLevel>=1;RefreshDescription();
 }
 protected override void AfterCloned(){base.AfterCloned();DisplayAmountChanged+=RefreshDescription;}
 void RefreshDescription()=>((StringVar)DynamicVars["EffectText"]).StringValue=BloodSectContent.EffectText(LeechPercent,OverflowEnabled);
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new StringVar("EffectText",BloodSectContent.Text(0))];
 public override decimal ModifyBlockMultiplicative(Creature target,decimal block,ValueProp props,CardModel? cardSource,CardPlay? cardPlay)=>
  target==Owner&&BloodSectRuntime.OwnSource(Owner,cardSource)?0:1;
 public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext,Creature? dealer,DamageResult result,ValueProp props,Creature target,CardModel? cardSource){
  // 只按自己的卡牌本体造成的实际生命损失吸血，排除能力、遗物、状态与附魔追加伤害。
  if(!BloodSectRuntime.IsCardDamageFor(Owner,result)||target.Side==Owner.Side||!Owner.IsAlive||result.UnblockedDamage<=0)return;
  int healing=(int)Math.Min(999999999m,decimal.Floor(result.UnblockedDamage*(decimal)LeechPercent/100));
  int overflow=OverflowEnabled?Math.Max(0,healing-Math.Max(0,Owner.MaxHp-Owner.CurrentHp)):0;
  if(healing>0)await CreatureCmd.Heal(Owner,healing);
  if(overflow>0&&Owner.IsAlive)await PowerCmd.Apply<TemporaryBloodPower>(choiceContext,Owner,overflow,Owner,cardSource);
 }
}
public sealed class TemporaryBloodPower : ModPowerTemplate {
 public override PowerType Type=>PowerType.Buff;
 public override PowerStackType StackType=>PowerStackType.Counter;
 protected override bool IsVisibleInternal=>Amount>0;
 public override Task AfterCombatEnd(CombatRoom room){SetAmount(0);return Task.CompletedTask;}
}
public static class BloodSectContent {
 public const string Key="blood_sect";
 public static string Text(int level)=>EffectText(100+20*Math.Max(0,level-1),level>=1);
 public static string EffectText(int percent,bool overflow)=>
  "你无法从自己的卡牌或遗物中直接获得[gold]格挡[/gold]。药水与状态效果提供的格挡不受影响。"+
  "\n你的卡牌直接对敌人造成伤害时，回复实际生命伤害值的[green]"+percent+"%[/green]生命（不含能力、遗物与效果的追加伤害）。"+
  (overflow?"\n超出最大生命值的回复转化为本场战斗的[red]临时血量[/red]。":"");
 public static void Initialize(){
  var h=new Harmony("AikunMiscPatchPack.BloodSect.Content");
  RitsuLibFramework.CreateContentPack(Settings.Id+".BloodSect").Card<ColorlessCardPool,BloodSect>().Card<ColorlessCardPool,BloodShield>()
   .Power<BloodSectPower>().Power<TemporaryBloodPower>().SharedEvent<BloodSectInitiation>()
   .Relic<EventRelicPool,BloodSectToken>().Relic<EventRelicPool,ThunderSectToken>().Relic<EventRelicPool,VampiricBlade>()
   .Enchantment<BloodScorching>().Enchantment<WeepingBlood>().Custom(ctx=>ctx.Content.RegisterModelCapability<BloodEnchantments>()).Apply();
  BloodInitiationArt.Initialize(h);BloodEnchantmentRules.Install(h);
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.Initialize)),postfix:new HarmonyMethod(typeof(BloodSectContent),nameof(Localize)));
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.SetLanguage)),postfix:new HarmonyMethod(typeof(BloodSectContent),nameof(Localize)));
  h.Patch(AccessTools.Method(typeof(CardPoolModel),nameof(CardPoolModel.GetUnlockedCards)),postfix:new HarmonyMethod(typeof(BloodSectContent),nameof(FilterUnlocked)));
  h.Patch(AccessTools.PropertyGetter(typeof(CardPoolModel),nameof(CardPoolModel.AllCards)),postfix:new HarmonyMethod(typeof(BloodSectContent),nameof(FilterAll)));
  BloodSectArt.Install(h);if(LocManager.Instance!=null)Localize();
 }
 public static void FilterUnlocked(ref IEnumerable<CardModel> __result)=>__result=__result.Where(c=>c is not BloodSect&&c is not BloodShield||Settings.Active(Key));
 public static void FilterAll(ref IEnumerable<CardModel> __result)=>FilterUnlocked(ref __result);
 public static void Localize(){
  if(LocManager.Instance==null)return;
  BloodInitiationContent.Localize();
  string card=ModelDb.GetId<BloodSect>().Entry,power=ModelDb.GetId<BloodSectPower>().Entry,temp=ModelDb.GetId<TemporaryBloodPower>().Entry;
  LocManager.Instance.GetTable("cards").MergeWith(new(){[card+".title"]="血魔宗",[card+".description"]="{EffectText}"});
  LocManager.Instance.GetTable("powers").MergeWith(new(){[power+".title"]="血魔宗",[power+".description"]="{EffectText}",[power+".smartDescription"]="{EffectText}",
   [temp+".title"]="临时血量",[temp+".description"]="额外血池：[red]{Amount}[/red]。受伤时优先消耗，本场战斗结束清除，不改变永久最大生命。完全吸收一次攻击时不触发纸伤难愈与吮吸，并触发失衡眩晕。"});
 }
}
