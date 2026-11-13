using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Scaffolding.Content.Patches;

namespace IkunMiscPatchPack;

public abstract class XExpansionCard : ModCardTemplate {
 protected XExpansionCard(CardType type=CardType.Skill,TargetType target=TargetType.Self,CardRarity rarity=CardRarity.Rare):base(0,type,rarity,target){}
 protected override bool HasEnergyCostX=>true;
 public override bool CanBeGeneratedInCombat=>Settings.Active(XCardExpansion.Key);
 public override bool CanBeGeneratedByModifiers=>CanBeGeneratedInCombat;
 public override string PortraitPath=>XExpansionArt.PortraitPath(this);
 public override string BetaPortraitPath=>PortraitPath;
 protected override string PortraitPngPath=>PortraitPath;
 public override string? CustomPortraitPath=>PortraitPath;
 public override string? CustomBetaPortraitPath=>PortraitPath;
 public override IEnumerable<string> AllPortraitPaths=>[PortraitPath];
}

public sealed class KnifeRain : XExpansionCard {
 public override CardPoolModel Pool=>ModelDb.CardPool<SilentCardPool>();
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new StringVar("ShivUpgrade","")];
 protected override IEnumerable<IHoverTip> AdditionalHoverTips=>[HoverTipFactory.FromCard<Shiv>(IsUpgraded)];
 protected override void OnUpgrade()=>((StringVar)DynamicVars["ShivUpgrade"]).StringValue="+";
 protected override async Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay){
  int count=checked(2*ResolveEnergyXValue());
  for(int i=0;i<count;i++){
   if(CombatManager.Instance.IsOverOrEnding||!Owner.Creature.IsAlive||CombatState?.HittableEnemies.Any()!=true)break;
   var shiv=await Shiv.CreateInHand(Owner,CombatState);
   if(shiv==null)break;
   if(IsUpgraded)CardCmd.Upgrade(shiv);
   // 每张小刀分别传入空目标，由原生 AutoPlay 使用战斗目标随机流重新选取当前可攻击敌人。
   await CardCmd.AutoPlay(choiceContext,shiv,null);
  }
 }
}

public sealed class HundredForge : XExpansionCard {
 public override CardPoolModel Pool=>ModelDb.CardPool<RegentCardPool>();
 public override IEnumerable<CardKeyword> CanonicalKeywords=>[CardKeyword.Exhaust];
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new ForgeVar(15)];
 protected override IEnumerable<IHoverTip> AdditionalHoverTips=>[HoverTipFactory.FromCard<SovereignBlade>()];
 protected override void OnUpgrade()=>DynamicVars["Forge"].UpgradeValueBy(5);
 protected override async Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay){
  int x=ResolveEnergyXValue();
  await ForgeCmd.Forge(DynamicVars["Forge"].BaseValue*x,Owner,this);
 }
}

public sealed class GlassFactory : XExpansionCard {
 public override CardPoolModel Pool=>ModelDb.CardPool<DefectCardPool>();
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new PowerVar<FocusPower>(1),new DynamicVar("ExtraOrbs",0),new StringVar("OrbBonusText","")];
 protected override IEnumerable<IHoverTip> AdditionalHoverTips=>[HoverTipFactory.FromPower<FocusPower>(),HoverTipFactory.Static(StaticHoverTip.Channeling),HoverTipFactory.FromOrb<GlassOrb>()];
 protected override void OnUpgrade(){DynamicVars["ExtraOrbs"].UpgradeValueBy(1);((StringVar)DynamicVars["OrbBonusText"]).StringValue="+"+DynamicVars["ExtraOrbs"].IntValue;}
 protected override async Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay){
  int count=ResolveEnergyXValue()+DynamicVars["ExtraOrbs"].IntValue;
  await PowerCmd.Apply<GlassFactoryFocusPower>(choiceContext,Owner.Creature,DynamicVars["FocusPower"].BaseValue,Owner.Creature,this);
  for(int i=0;i<count;i++)await OrbCmd.Channel<GlassOrb>(choiceContext,Owner);
 }
}

public sealed class SacrificialCombo : XExpansionCard {
 public SacrificialCombo():base(CardType.Attack,TargetType.AnyEnemy){}
 public override CardPoolModel Pool=>ModelDb.CardPool<IroncladCardPool>();
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new DamageVar(1,ValueProp.Move),new DynamicVar("ExtraHits",0),new StringVar("HitBonusText","")];
 protected override IEnumerable<IHoverTip> AdditionalHoverTips=>[HoverTipFactory.FromKeyword(CardKeyword.Exhaust),HoverTipFactory.FromPower<StrengthPower>()];
 protected override void OnUpgrade(){DynamicVars["ExtraHits"].UpgradeValueBy(1);((StringVar)DynamicVars["HitBonusText"]).StringValue="+"+DynamicVars["ExtraHits"].IntValue;}
 protected override async Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay){
  ArgumentNullException.ThrowIfNull(cardPlay.Target);
  // 先锁定本次X，选牌及消耗钩子改变能量时不会改变选择上限或攻击次数。
  int hits=ResolveEnergyXValue()+DynamicVars["ExtraHits"].IntValue;
  var state=Owner.PlayerCombatState!;
  var candidates=state.Hand.Cards.Concat(state.DrawPile.Cards).Concat(state.DiscardPile.Cards)
   .Where(c=>c!=this&&c.Owner==Owner&&c.Type!=CardType.Attack).ToArray();
  int exhausted=0,max=Math.Min(hits,candidates.Length);
  if(max>0){
   var prefs=new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt,0,max){Cancelable=true};
   foreach(var card in await CardSelectCmd.FromSimpleGrid(choiceContext,candidates,Owner,prefs))
    if(await CardCmd.Exhaust(choiceContext,card)!=null)exhausted++;
  }
  if(exhausted>0)await PowerCmd.Apply<SacrificialComboStrengthPower>(choiceContext,Owner.Creature,exhausted,Owner.Creature,this);
  await DamageCmd.Attack(DynamicVars.Damage.BaseValue).WithHitCount(hits).FromCard(this,cardPlay).Targeting(cardPlay.Target).Execute(choiceContext);
 }
}

public sealed class SacrificialComboStrengthPower : TemporaryStrengthPower,IModPowerAssetOverrides {
 public override AbstractModel OriginModel=>ModelDb.Card<SacrificialCombo>();
 public PowerAssetProfile AssetProfile=>PowerAssetProfile.Empty;
 public string? CustomIconPath=>ModelDb.Power<FlexPotionPower>().PackedIconPath;
 public string? CustomBigIconPath=>ModelDb.Power<FlexPotionPower>().ResolvedBigIconPath;
}

public sealed class SoulRitual : XExpansionCard {
 public SoulRitual():base(CardType.Power,TargetType.Self,CardRarity.Uncommon){}
 public override CardPoolModel Pool=>ModelDb.CardPool<NecrobinderCardPool>();
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new EnergyVar(1),new StringVar("SoulUpgrade","")];
 protected override IEnumerable<IHoverTip> AdditionalHoverTips=>[HoverTipFactory.FromCard<Soul>(IsUpgraded),HoverTipFactory.FromKeyword(CardKeyword.Ethereal),EnergyHoverTip];
 protected override void OnUpgrade(){DynamicVars.Energy.UpgradeValueBy(1);((StringVar)DynamicVars["SoulUpgrade"]).StringValue="+";}
 protected override async Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay){
  int x=ResolveEnergyXValue();
  await Generate(Owner,x,IsUpgraded,CombatState!);
  // 两种品质分别叠层，避免普通/升级祭仪混用时把普通灵魂升级或把升级灵魂降级。
  if(IsUpgraded)await PowerCmd.Apply<SoulRitualPlusPower>(choiceContext,Owner.Creature,x,Owner.Creature,this);
  else await PowerCmd.Apply<SoulRitualPower>(choiceContext,Owner.Creature,x,Owner.Creature,this);
  await PlayerCmd.GainEnergy(DynamicVars.Energy.IntValue,Owner);
 }
 internal static async Task Generate(Player owner,int count,bool upgraded,ICombatState combat){
  var souls=Soul.Create(owner,count,combat).ToArray();
  foreach(var soul in souls){if(upgraded)CardCmd.Upgrade(soul);soul.AddKeyword(CardKeyword.Ethereal);}
  // 在进入原生生成流程前设好升级与虚无，满手牌及生成钩子沿用原版行为。
  await CardPileCmd.AddGeneratedCardsToCombat(souls,PileType.Hand,owner);
 }
}

public abstract class SoulRitualPowerBase : ModPowerTemplate {
 protected abstract bool UpgradedSouls{get;}
 public override PowerType Type=>PowerType.Buff;
 public override PowerStackType StackType=>PowerStackType.Counter;
 public override string? CustomIconPath=>ModelDb.Power<SoulboundPower>().PackedIconPath;
 public override string? CustomBigIconPath=>ModelDb.Power<SoulboundPower>().ResolvedBigIconPath;
 protected override IEnumerable<IHoverTip> AdditionalHoverTips=>[HoverTipFactory.FromCard<Soul>(UpgradedSouls),HoverTipFactory.FromKeyword(CardKeyword.Ethereal)];
 public override async Task BeforeHandDraw(Player player,PlayerChoiceContext choiceContext,ICombatState combatState){
  if(player!=Owner.Player||!Owner.IsAlive||Amount<=0)return;
  Flash();await SoulRitual.Generate(player,Amount,UpgradedSouls,combatState);
 }
}
public sealed class SoulRitualPower : SoulRitualPowerBase {protected override bool UpgradedSouls=>false;}
public sealed class SoulRitualPlusPower : SoulRitualPowerBase {protected override bool UpgradedSouls=>true;}

// 继承原生临时集中：叠层与本方回合末撤销均由原版处理，并保留永久集中及其他来源。
public sealed class GlassFactoryFocusPower : TemporaryFocusPower,IModPowerAssetOverrides {
 public override AbstractModel OriginModel=>ModelDb.Card<GlassFactory>();
 public PowerAssetProfile AssetProfile=>PowerAssetProfile.Empty;
 public string? CustomIconPath=>ModelDb.Power<HotfixPower>().PackedIconPath;
 public string? CustomBigIconPath=>ModelDb.Power<HotfixPower>().ResolvedBigIconPath;
}

public static class XCardExpansion {
 public const string Key="x_card_expansion";
 public static void Initialize(){
  // 始终登记模型；开关仅控制获取，关闭后旧牌的结算与读档继续保留。
  RitsuLibFramework.CreateContentPack(Settings.Id+".XCards").Card<SilentCardPool,KnifeRain>().Card<RegentCardPool,HundredForge>()
   .Card<DefectCardPool,GlassFactory>().Card<IroncladCardPool,SacrificialCombo>()
   .Card<NecrobinderCardPool,SoulRitual>().Power<SoulRitualPower>().Power<SoulRitualPlusPower>()
   .Power<GlassFactoryFocusPower>().Power<SacrificialComboStrengthPower>().Apply();
  var h=new Harmony(Settings.Id+".XCards");
  h.Patch(AccessTools.PropertyGetter(typeof(CardModel),"BetaPortraitPngPath"),prefix:new HarmonyMethod(typeof(XCardExpansion),nameof(BetaPath)));
  h.Patch(AccessTools.Method(typeof(CardPoolModel),nameof(CardPoolModel.GetUnlockedCards)),postfix:new HarmonyMethod(typeof(XCardExpansion),nameof(Filter)));
  h.Patch(AccessTools.PropertyGetter(typeof(CardPoolModel),nameof(CardPoolModel.AllCards)),postfix:new HarmonyMethod(typeof(XCardExpansion),nameof(Filter)));
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.Initialize)),postfix:new HarmonyMethod(typeof(XCardExpansion),nameof(Localize)));
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.SetLanguage)),postfix:new HarmonyMethod(typeof(XCardExpansion),nameof(Localize)));
  if(LocManager.Instance!=null)Localize();RuntimeLoader.States[Key]="已应用。";
 }
 static bool BetaPath(CardModel __instance,ref string __result){if(__instance is not XExpansionCard)return true;__result=__instance.PortraitPath;return false;}
 static void Filter(ref IEnumerable<CardModel> __result)=>__result=__result.Where(c=>c is not XExpansionCard||Settings.Active(Key));
 static void Localize(){
  if(LocManager.Instance==null)return;var strings=new Dictionary<string,string>();
  foreach(var (type,title,description) in new (Type,string,string)[]{
   (typeof(KnifeRain),"小刀雨","生成并向随机目标打出2X张[gold]小刀{ShivUpgrade}[/gold]。"),
   (typeof(HundredForge),"百冶","[gold]铸造[/gold]{Forge:diff()}X。"),
   (typeof(GlassFactory),"玻璃厂","在本回合获得{FocusPower:diff()}点[gold]集中[/gold]。\n生成X{OrbBonusText}个[gold]玻璃充能球[/gold]。"),
   (typeof(SacrificialCombo),"舍身连击","从手牌、抽牌堆、弃牌堆中选择至多X{HitBonusText}张非攻击牌[gold]消耗[/gold]。\n获得本次消耗卡牌数量的[gold]临时力量[/gold]。\n造成{Damage:diff()}点伤害X{HitBonusText}次。"),
   (typeof(SoulRitual),"灵魂祭仪","生成X张带有[gold]虚无[/gold]的[gold]灵魂{SoulUpgrade}[/gold]到手牌。\n在你的回合开始时，在手牌中生成X张带有[gold]虚无[/gold]的[gold]灵魂{SoulUpgrade}[/gold]。\n获得{Energy:energyIcons()}。")}){
   string id=ModelDb.GetId(type).Entry;strings[id+".title"]=title;strings[id+".description"]=description;
  }
  LocManager.Instance.GetTable("cards").MergeWith(strings);
  var powers=new Dictionary<string,string>();
  foreach(var (type,suffix) in new (Type,string)[]{(typeof(SoulRitualPower),""),(typeof(SoulRitualPlusPower),"+")}){
   string id=ModelDb.GetId(type).Entry;powers[id+".title"]="灵魂祭仪"+suffix;
   powers[id+".description"]="在你的回合开始时，在手牌中生成{Amount}张带有[gold]虚无[/gold]的[gold]灵魂"+suffix+"[/gold]。";
  }
  LocManager.Instance.GetTable("powers").MergeWith(powers);
 }
}
