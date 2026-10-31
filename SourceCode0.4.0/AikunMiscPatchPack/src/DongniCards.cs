using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Scaffolding.Content;

namespace AikunMiscPatchPack;

// 规范类型始终注册，关闭获取开关后仍可读取已有牌和嵌入卡图。
public abstract class DongniCard : ModCardTemplate {
 protected DongniCard(CardType type,CardRarity rarity,TargetType target):base(0,type,rarity,target){}
 public override CardPoolModel Pool=>ModelDb.CardPool<ColorlessCardPool>();
 public override IEnumerable<CardKeyword> CanonicalKeywords=>[CardKeyword.Exhaust];
 public override bool CanBeGeneratedInCombat=>Rarity!=CardRarity.Token&&Settings.Active(DongniContent.Key);
 public override bool CanBeGeneratedByModifiers=>CanBeGeneratedInCombat;
 public override string PortraitPath=>DongniArt.Path(this);
 public override string BetaPortraitPath=>PortraitPath;
 protected override string PortraitPngPath=>PortraitPath;
 public override string? CustomPortraitPath=>PortraitPath;
 public override string? CustomBetaPortraitPath=>PortraitPath;
 public override IEnumerable<string> AllPortraitPaths=>[PortraitPath];
}

public abstract class DongniMeaningCard : DongniCard {
 protected DongniMeaningCard():base(CardType.Skill,CardRarity.Uncommon,TargetType.Self){}
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new StringVar("EffectText",DongniContent.Text(GetType(),false))];
 protected override void OnUpgrade()=>((StringVar)DynamicVars["EffectText"]).StringValue=DongniContent.Text(GetType(),true);
 protected override IEnumerable<IHoverTip> AdditionalHoverTips {
  get{
   var strike=ModelDb.Card<DongniStrike>().ToMutable();var defend=ModelDb.Card<DongniDefend>().ToMutable();
   if(this is DongniMeaning&&IsUpgraded){CardCmd.Upgrade(strike);CardCmd.Upgrade(defend);}
   return [new CardHoverTip(strike),new CardHoverTip(defend)];
  }
 }
}

public sealed class DongniMeaning : DongniMeaningCard {
 protected override Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay)=>DongniContent.Generate(Owner,IsUpgraded);
}

public sealed class NotDongniMeaning : DongniMeaningCard {
 protected override async Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay){
  var cards=DongniContent.Candidates(this,false);
  foreach(var card in await DongniContent.Select(choiceContext,this,cards,false))await CardCmd.Exhaust(choiceContext,card);
  // 不足两张时处理实际存在的牌，随后依然生成一组，避免空手牌卡死选牌。
  await DongniContent.Generate(Owner,false);
 }
}

public sealed class WhoDongniMeaning : DongniMeaningCard {
 protected override async Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay){
  var cards=DongniContent.Candidates(this,true);
  var selected=(await DongniContent.Select(choiceContext,this,cards,true)).ToArray();
  if(selected.Length==0)return;
  var combat=CombatState??throw new InvalidOperationException("东尼变化必须在战斗中结算。");
  bool firstStrike=selected.Length>=2||Owner.RunState.Rng.CombatCardGeneration.NextInt(2)==0;
  var replacements=new List<CardTransformation>();
  for(int i=0;i<selected.Length;i++){
   CardModel replacement=(i==0?firstStrike:!firstStrike)?combat.CreateCard<DongniStrike>(Owner):combat.CreateCard<DongniDefend>(Owner);
   replacements.Add(new CardTransformation(selected[i],replacement));
  }
  // 原生批量变化保留每张牌所在的战斗牌堆和位置，不移除永久牌组里的原牌。
  await CardCmd.Transform(replacements,null);
 }
}

public sealed class DongniStrike : DongniCard {
 public DongniStrike():base(CardType.Attack,CardRarity.Token,TargetType.AnyEnemy){}
 protected override IEnumerable<DynamicVar> CanonicalVars=>[
  new CalculationBaseVar(1),new ExtraDamageVar(1),
  new CalculatedDamageVar(ValueProp.Move).WithMultiplier((card,_)=>DongniContent.CountDamageCards(card)),
  new StringVar("PileText","抽牌堆、弃牌堆和手牌")];
 protected override void OnUpgrade()=>((StringVar)DynamicVars["PileText"]).StringValue="抽牌堆、弃牌堆、手牌和消耗堆";
 protected override async Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay){
  ArgumentNullException.ThrowIfNull(cardPlay.Target);
  await DamageCmd.Attack(DynamicVars.CalculatedDamage).FromCard(this,cardPlay).Targeting(cardPlay.Target).Execute(choiceContext);
 }
}

public sealed class DongniDefend : DongniCard {
 public DongniDefend():base(CardType.Skill,CardRarity.Token,TargetType.Self){}
 public override bool GainsBlock=>true;
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new BlockVar(15,ValueProp.Move),new StringVar("RestrictionText","\n你本回合不能获得[gold]格挡[/gold]。")];
 protected override void OnUpgrade()=>((StringVar)DynamicVars["RestrictionText"]).StringValue="";
 protected override async Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay){
  await CreatureCmd.LoseBlock(choiceContext,Owner.Creature,Owner.Creature.Block,Owner.Creature);
  await CreatureCmd.GainBlock(Owner.Creature,DynamicVars.Block,cardPlay);
  if(!IsUpgraded)await PowerCmd.Apply<DongniNoBlockPower>(choiceContext,Owner.Creature,1,Owner.Creature,this);
 }
}

public sealed class DongniNoBlockPower : ModPowerTemplate {
 public override PowerType Type=>PowerType.Debuff;
 public override PowerStackType StackType=>PowerStackType.Single;
 public override string? CustomIconPath=>ModelDb.Power<NoBlockPower>().PackedIconPath;
 public override string? CustomBigIconPath=>CustomIconPath;
 public override decimal ModifyBlockMultiplicative(Creature target,decimal block,ValueProp props,CardModel? cardSource,CardPlay? cardPlay)=>target==Owner?0:1;
 public override Task AfterSideTurnEnd(PlayerChoiceContext choiceContext,CombatSide side,IEnumerable<Creature> participants)=>
  side==Owner.Side&&participants.Contains(Owner)?PowerCmd.Remove(this):Task.CompletedTask;
}

public static class DongniContent {
 public const string Key="dongni_content";
 public static void Initialize(){
  RitsuLibFramework.CreateContentPack(Settings.Id+".Dongni")
   .Card<ColorlessCardPool,DongniMeaning>().Card<ColorlessCardPool,NotDongniMeaning>().Card<ColorlessCardPool,WhoDongniMeaning>()
   .Card<ColorlessCardPool,DongniStrike>().Card<ColorlessCardPool,DongniDefend>().Power<DongniNoBlockPower>()
   .Relic<EventRelicPool,DongniBlessing>().Relic<EventRelicPool,DongniThinking>().SharedEvent<DongniOffice>().Apply();
  var h=new Harmony(Settings.Id+".Dongni");
  DongniArt.Install(h);
  DongniOfficeArt.Initialize(h);DongniRewardGate.Install(h);
  h.Patch(AccessTools.Method(typeof(CardPoolModel),nameof(CardPoolModel.GetUnlockedCards)),postfix:new HarmonyMethod(typeof(DongniContent),nameof(FilterPool)));
  h.Patch(AccessTools.PropertyGetter(typeof(CardPoolModel),nameof(CardPoolModel.AllCards)),postfix:new HarmonyMethod(typeof(DongniContent),nameof(FilterPool)));
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.Initialize)),postfix:new HarmonyMethod(typeof(DongniContent),nameof(Localize)));
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.SetLanguage)),postfix:new HarmonyMethod(typeof(DongniContent),nameof(Localize)));
  if(LocManager.Instance!=null)Localize();
  RuntimeLoader.States[Key]="已应用。";
 }
 public static void FilterPool(ref IEnumerable<CardModel> __result)=>__result=__result.Where(c=>c is not DongniCard||Settings.Active(Key));
 public static int CountDamageCards(CardModel card){
  if(!card.IsMutable||card.Owner?.PlayerCombatState is not {} state)return 0;
  // 打出时本牌已在出牌区，不能把它计入手牌；预览也排除本牌，使卡面与结算一致。
  return state.Hand.Cards.Count(c=>c!=card)+state.DrawPile.Cards.Count(c=>c!=card)+state.DiscardPile.Cards.Count(c=>c!=card)+(card.IsUpgraded?state.ExhaustPile.Cards.Count(c=>c!=card):0);
 }
 public static IReadOnlyList<CardModel> Candidates(CardModel source,bool transform){
  var state=source.Owner.PlayerCombatState??throw new InvalidOperationException("东尼选牌必须在战斗中结算。");
  IEnumerable<CardModel> cards=state.Hand.Cards;
  if(source.IsUpgraded)cards=cards.Concat(state.DrawPile.Cards).Concat(state.DiscardPile.Cards);
  return cards.Where(c=>c!=source&&c.Owner==source.Owner&&(!transform||c.IsTransformable)).ToArray();
 }
 public static async Task<IEnumerable<CardModel>> Select(PlayerChoiceContext context,CardModel source,IReadOnlyList<CardModel> candidates,bool transform){
  int count=Math.Min(2,candidates.Count);
  if(count==0)return [];
  var prefs=new CardSelectorPrefs(transform?CardSelectorPrefs.TransformSelectionPrompt:CardSelectorPrefs.ExhaustSelectionPrompt,count);
  // 三牌堆合并使用原生网格选牌，仍由原生同步器处理联机选择。
  return await CardSelectCmd.FromSimpleGrid(context,candidates,source.Owner,prefs);
 }
 public static async Task Generate(Player player,bool upgraded){
  var state=player.Creature.CombatState??throw new InvalidOperationException("东尼衍生牌必须在战斗中生成。");
  var strike=state.CreateCard<DongniStrike>(player);var defend=state.CreateCard<DongniDefend>(player);
  if(upgraded){CardCmd.Upgrade(strike);CardCmd.Upgrade(defend);}
  await CardPileCmd.AddGeneratedCardsToCombat([strike,defend],PileType.Hand,player);
 }
 public static string Text(Type type,bool upgraded){
  if(type==typeof(DongniMeaning))return "向手牌中添加1张[gold]东尼打击"+(upgraded?"+":"")+"[/gold]和1张[gold]东尼防御"+(upgraded?"+":"")+"[/gold]。";
  string piles=upgraded?"手牌、抽牌堆和弃牌堆":"手牌";
  return type==typeof(NotDongniMeaning)?"消耗"+piles+"中的2张牌，向手牌中添加1张[gold]东尼打击[/gold]和1张[gold]东尼防御[/gold]。":
   "选择"+piles+"中的2张牌，分别变化为[gold]东尼打击[/gold]和[gold]东尼防御[/gold]。\n只有1张可变化的牌时，随机变化为其中之一。";
 }
 public static void Localize(){
  if(LocManager.Instance==null)return;
  DongniOfficeContent.Localize();
  var cards=new Dictionary<string,string>();
  foreach(var (type,title,description) in new (Type,string,string)[]{
   (typeof(DongniMeaning),"东尼意思","{EffectText}"),(typeof(NotDongniMeaning),"不东尼意思","{EffectText}"),(typeof(WhoDongniMeaning),"谁东尼意思","{EffectText}"),
   (typeof(DongniStrike),"东尼打击","造成{CalculatedDamage:diff()}点伤害。\n基础伤害为1，额外造成你的{PileText}总卡牌数的伤害。"),
   (typeof(DongniDefend),"东尼防御","移除你的[gold]格挡[/gold]。\n获得{Block:diff()}点[gold]格挡[/gold]。{RestrictionText}")}){
   string id=ModelDb.GetId(type).Entry;cards[id+".title"]=title;cards[id+".description"]=description;
  }
  LocManager.Instance.GetTable("cards").MergeWith(cards);
  string power=ModelDb.GetId<DongniNoBlockPower>().Entry;
  LocManager.Instance.GetTable("powers").MergeWith(new(){[power+".title"]="东尼防御",[power+".description"]="你本回合不能获得[gold]格挡[/gold]。你的回合结束时移除。",[power+".smartDescription"]="你本回合不能获得[gold]格挡[/gold]。你的回合结束时移除。"});
 }
}
