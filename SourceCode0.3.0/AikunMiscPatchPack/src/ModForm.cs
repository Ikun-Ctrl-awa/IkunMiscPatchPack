using System.Reflection;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2RitsuLib;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Models.Capabilities;
using STS2RitsuLib.Scaffolding.Content;

namespace AikunMiscPatchPack;

public sealed class ModForm : ModCardTemplate {
 public ModForm():base(3,CardType.Power,CardRarity.Rare,TargetType.Self){}
 // 卡面、百科与详情均读取同一张嵌入图，避免只替换纹理 getter 而遗漏路径读取方。
 public override string PortraitPath=>ContentArt.PortraitResourcePath;
 public override string BetaPortraitPath=>PortraitPath;
 protected override string PortraitPngPath=>PortraitPath;
 public override string? CustomPortraitPath=>PortraitPath;
 public override string? CustomBetaPortraitPath=>PortraitPath;
 public override IEnumerable<string> AllPortraitPaths=>[PortraitPath];
 public override CardPoolModel Pool=>ModelDb.CardPool<ColorlessCardPool>();
 public override bool CanBeGeneratedInCombat=>Settings.Active("mod_form")&&(Settings.StartupModFormIncludeSelf||ModFormContent.Groups.Length>0);
 public override bool CanBeGeneratedByModifiers=>CanBeGeneratedInCombat;
 // 提供实际数值增量，让叠影等比较升级前后数值的附魔正确识别本牌。
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new StringVar("UpgradeText",""),new DynamicVar("GenerationUpgradeLevel",0)];
 protected override void OnUpgrade(){
  DynamicVars["GenerationUpgradeLevel"].UpgradeValueBy(1);
  ((StringVar)DynamicVars["UpgradeText"]).StringValue=CurrentUpgradeLevel==1?"升级过的":"升级 +"+CurrentUpgradeLevel+" 的";
 }
 protected override async Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay){
  // 每张形态各自记录升级次数；多张形态不会把等级混在一起。
  var power=await PowerCmd.Apply<ModFormPower>(choiceContext,Owner.Creature,1,Owner.Creature,this);
  if(power!=null)power.GenerationUpgradeLevel=CurrentUpgradeLevel;
  await ModFormContent.Generate(Owner,CurrentUpgradeLevel);
 }
}

public sealed class ModFormPower : ModPowerTemplate {
 public override PowerType Type=>PowerType.Buff;
 public override PowerStackType StackType=>PowerStackType.Counter;
 public override PowerInstanceType InstanceType=>PowerInstanceType.Instanced;
 int generationUpgradeLevel;
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new StringVar("UpgradeText","")];
 [SavedProperty] public int GenerationUpgradeLevel{get=>generationUpgradeLevel;set{generationUpgradeLevel=Math.Max(0,value);((StringVar)DynamicVars["UpgradeText"]).StringValue=value==0?"":value==1?"升级过的":"升级 +"+value+" 的";}}
 public override async Task AfterSideTurnStart(CombatSide side,IReadOnlyList<Creature> participants,ICombatState combatState){
  if(!Owner.IsAlive||Owner.Player==null||!participants.Contains(Owner))return;
  Flash();await ModFormContent.Generate(Owner.Player,GenerationUpgradeLevel);
 }
}

// 仅生成牌携带此能力。RitsuLib 负责克隆、保存和恢复，避免污染其规范卡牌。
public sealed class GeneratedCardUpgrade : ModelCapability {
 int upgradeLimit;
 public int UpgradeLimit{get=>upgradeLimit;set{upgradeLimit=Math.Max(0,value);if(Owner!=null)MarkDirty();}}
 protected override JsonNode? SaveAdditionalState()=>JsonValue.Create(UpgradeLimit);
 protected override void LoadAdditionalState(JsonNode? state,int schemaVersion)=>UpgradeLimit=Math.Max(0,state?.GetValue<int>()??0);
 protected override void OnAttach(AbstractModel owner){
  if(owner is CardModel card&&card.IsMutable){card.AddKeyword(CardKeyword.Ethereal);card.AddKeyword(CardKeyword.Exhaust);}
 }
 protected override void OnLoadedFromSave(){if(Owner!=null)OnAttach(Owner);}
}

public sealed record ModCardGroup(string ModId,CardModel[] Cards);
public static class ModFormContent {
 static readonly Harmony H=new("Aikun.MiscPatchPack.ModForm");
 public static ModCardGroup[] Groups{get;private set;}=[];
 [ThreadStatic] static Stack<int>? restoring;
 public static void Initialize(){
  // 始终登记类型以便关闭开关后仍能读取既有牌组；开关控制获取和战斗生成。
  RitsuLibFramework.CreateContentPack(Settings.Id).Card<ColorlessCardPool,ModForm>().Power<ModFormPower>()
   .Custom(ctx=>ctx.Content.RegisterModelCapability<GeneratedCardUpgrade>()).Apply();
  H.Patch(AccessTools.Method(typeof(CardPoolModel),nameof(CardPoolModel.GetUnlockedCards)),postfix:new HarmonyMethod(typeof(ModFormContent),nameof(FilterPool)));
  H.Patch(AccessTools.PropertyGetter(typeof(CardPoolModel),nameof(CardPoolModel.AllCards)),postfix:new HarmonyMethod(typeof(ModFormContent),nameof(FilterAllCards)){priority=Priority.Last});
  H.Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.FromSerializable)),prefix:new HarmonyMethod(typeof(ModFormContent),nameof(BeforeRestore)),finalizer:new HarmonyMethod(typeof(ModFormContent),nameof(AfterRestore)));
  ContentArt.Install(H);
  H.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.Initialize)),postfix:new HarmonyMethod(typeof(ModFormContent),nameof(InstallLocalization)));
  H.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.SetLanguage)),postfix:new HarmonyMethod(typeof(ModFormContent),nameof(InstallLocalization)));
  if(LocManager.Instance!=null)InstallLocalization();
 }
 public static void AfterMods(){
  EnchantmentView.Discover();
  ModFormRepeatUpgrades.Install(H);
  // 完整卡池可能因空候选池而暂时隐藏形态；显式加入规范牌再由自身开关筛选。
  Groups=GroupCards(ModelDb.AllCards.Append(ModelDb.Card<ModForm>()));
  AccessTools.Field(typeof(CardPoolModel),"_allCardIds").SetValue(ModelDb.CardPool<ColorlessCardPool>(),null);
  AccessTools.Field(typeof(ModelDb),"_allCards").SetValue(null,null);
  var getters=ModelDb.AllAbstractModelSubtypes.Where(t=>typeof(CardModel).IsAssignableFrom(t))
   .Select(t=>AccessTools.DeclaredPropertyGetter(t,nameof(CardModel.MaxUpgradeLevel))).Append(AccessTools.PropertyGetter(typeof(CardModel),nameof(CardModel.MaxUpgradeLevel))).Where(m=>m?.GetMethodBody()!=null).Distinct();
  foreach(var getter in getters)H.Patch(getter,postfix:new HarmonyMethod(typeof(ModFormContent),nameof(UpgradeCap)){priority=Priority.Last});
  GD.Print($"[AikunMiscPatchPack] mod form candidates: mods={Groups.Length}, cards={Groups.Sum(g=>g.Cards.Length)}");
 }
 public static bool IsCandidate(CardModel card){
  // 独立判定自身，避免“候选池为空→不可生成→无法加入候选池”的循环依赖。
  if(card is ModForm)return Settings.Active("mod_form")&&Settings.StartupModFormIncludeSelf;
  if(card.GetType().Assembly==typeof(CardModel).Assembly||card.Type is not (CardType.Attack or CardType.Skill or CardType.Power)
   ||!card.CanBeGeneratedInCombat||card.Keywords.Contains(CardKeyword.Unplayable))return false;
  try{return card.Pool is not (QuestCardPool or CurseCardPool or StatusCardPool or DeprecatedCardPool);}
  catch(InvalidProgramException){return false;}
 }
 public static ModCardGroup[] GroupCards(IEnumerable<CardModel> cards){
  var owners=ModManager.Mods.Where(m=>m.state==ModLoadState.Loaded&&m.manifest!=null)
   .SelectMany(m=>m.assemblies.Select(a=>(Assembly:a,Id:m.manifest!.id))).GroupBy(p=>p.Assembly).ToDictionary(g=>g.Key,g=>g.First().Id);
  return cards.Where(IsCandidate).DistinctBy(c=>c.Id).GroupBy(c=>owners.GetValueOrDefault(c.GetType().Assembly,c.GetType().Assembly.GetName().Name!))
   .OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new ModCardGroup(g.Key!,g.OrderBy(c=>c.Id.ToString(),StringComparer.Ordinal).ToArray())).ToArray();
 }
 public static CardModel? Choose(IReadOnlyList<ModCardGroup> groups,Rng rng)=>groups.Count==0?null:rng.NextItem(rng.NextItem(groups)!.Cards);
 public static ModCardGroup[] ForPlayerCount(IEnumerable<ModCardGroup> groups,CardMultiplayerConstraint constraint)=>groups
  .Select(g=>g with{Cards=g.Cards.Where(c=>constraint==CardMultiplayerConstraint.SingleplayerOnly?c.MultiplayerConstraint!=CardMultiplayerConstraint.MultiplayerOnly:constraint!=CardMultiplayerConstraint.MultiplayerOnly||c.MultiplayerConstraint!=CardMultiplayerConstraint.SingleplayerOnly).ToArray()})
  .Where(g=>g.Cards.Length>0).ToArray();
 public static void FilterPool(CardPoolModel __instance,CardMultiplayerConstraint multiplayerConstraint,ref IEnumerable<CardModel> __result){
  if(__instance is ColorlessCardPool&&(!Settings.Active("mod_form")||ForPlayerCount(Groups,multiplayerConstraint).Length==0))__result=__result.Where(c=>c is not ModForm).ToArray();
 }
 public static void FilterAllCards(CardPoolModel __instance,ref IEnumerable<CardModel> __result){
  if(__instance is ColorlessCardPool&&(!Settings.Active("mod_form")||Groups.Length==0))__result=__result.Where(c=>c is not ModForm);
 }
 public static CardModel PrepareGenerated(CardModel card,int upgrades){
  var marker=card.GetOrCreateCapability<GeneratedCardUpgrade>();marker.UpgradeLimit=Math.Max(0,upgrades);
  for(int i=card.CurrentUpgradeLevel;i<upgrades;i++){card.UpgradeInternal();card.FinalizeUpgradeInternal();}
  card.AddKeyword(CardKeyword.Ethereal);card.AddKeyword(CardKeyword.Exhaust);card.SetToFreeThisTurn();return card;
 }
 public static async Task Generate(Player player,int upgrades){
  if(!Settings.Active("mod_form")||!CombatManager.Instance.IsInProgress||player.Creature.CombatState==null)return;
  // 同一个已保存的战斗随机流，依次均匀选择模组、再均匀选择该模组的卡牌。
  var canonical=Choose(ForPlayerCount(Groups,player.RunState.CardMultiplayerConstraint),player.RunState.Rng.CombatCardGeneration);if(canonical==null)return;
  // 已按类型和可生成标志筛选。通用工厂会再剔除基础／事件稀有度，导致随机命中后漏发牌。
  var card=player.Creature.CombatState.CreateCard(canonical,player);
  await CardPileCmd.AddGeneratedCardToCombat(PrepareGenerated(card,upgrades),PileType.Hand,player);
 }
 public static void UpgradeCap(CardModel __instance,ref int __result){
  int limit=__instance.IsMutable?__instance.Capability<GeneratedCardUpgrade>()?.UpgradeLimit??0:0;
  // 恢复阶段临时抬高所有牌的上限，不能把这个值误缓存为附魔规则。
  if(__instance is ModForm or BloodSect&&__instance.IsMutable&&restoring is not {Count:>0})limit=Math.Max(limit,ModFormRepeatUpgrades.Limit(__instance));
  if(restoring is {Count:>0})limit=Math.Max(limit,restoring.Peek());
  __result=Math.Max(__result,limit);
 }
 static void BeforeRestore(SerializableCard save)=>(restoring??=new()).Push(save.CurrentUpgradeLevel);
 static void AfterRestore(){restoring!.Pop();}
 public static void InstallLocalization(){
  if(LocManager.Instance==null)return;
  string id=ModelDb.GetId<ModForm>().Entry,power=ModelDb.GetId<ModFormPower>().Entry;
  LocManager.Instance.GetTable("cards").MergeWith(new(){[id+".title"]="mod形态",[id+".description"]="打出时及你的回合开始时，生成一张来自随机模组的{UpgradeText}随机卡牌到你的手牌。\n该牌本回合免费，并获得[gold]虚无[/gold]和[gold]消耗[/gold]。"});
  const string description="你的回合开始时，生成一张来自随机模组的{UpgradeText}随机攻击、技能或能力牌。本回合免费，获得虚无和消耗。";
  LocManager.Instance.GetTable("powers").MergeWith(new(){[power+".title"]="mod形态",[power+".description"]=description,[power+".smartDescription"]=description});
 }
}

public static class ContentArt {
 public const string PortraitResourcePath="res://AikunMiscPatchPack/images/card_portraits/mod_form.png";
 static Texture2D? portrait,icon;
 static Texture2D Svg(string source){var image=new Image();var error=image.LoadSvgFromString(source);if(error!=Error.Ok)throw new InvalidDataException("SVG "+error);return ImageTexture.CreateFromImage(image);}
 public static Texture2D Portrait=>portrait??=LoadPortrait();
 static Texture2D LoadPortrait(){
  using var stream=typeof(ContentArt).Assembly.GetManifestResourceStream("cardart.mod_form.png")??throw new FileNotFoundException("cardart.mod_form.png");
  using var bytes=new MemoryStream();stream.CopyTo(bytes);using var image=new Image();
  if(image.LoadPngFromBuffer(bytes.ToArray())!=Error.Ok)throw new InvalidDataException("mod形态卡图 PNG 无效");
  var texture=ImageTexture.CreateFromImage(image);
  // 向 Godot 缓存登记真实纹理路径，使 ResourceLoader 与预加载均能读取，无须外置 PCK。
  texture.TakeOverPath(PortraitResourcePath);return texture;
 }
 public static Texture2D Icon=>icon??=Svg("""
 <svg xmlns="http://www.w3.org/2000/svg" width="96" height="96"><path d="M48 4 88 26v44L48 92 8 70V26Z" fill="#20283e" stroke="#91d4e4" stroke-width="4"/><path d="m48 18 19 28-19 33-19-33Z" fill="#b795e9"/><path d="M15 34 32 44M81 34 64 44M48 79v9" stroke="#cce5ed" stroke-width="3"/></svg>
 """);
 public static void Install(Harmony h){
  _=Portrait;
  // 测试图的大图路径是原生私有属性，无法在 ModForm 内覆写。
  h.Patch(AccessTools.PropertyGetter(typeof(CardModel),"BetaPortraitPngPath"),prefix:new HarmonyMethod(typeof(ContentArt),nameof(BetaPortraitPath)));
  h.Patch(AccessTools.PropertyGetter(typeof(PowerModel),nameof(PowerModel.Icon)),prefix:new HarmonyMethod(typeof(ContentArt),nameof(PowerIcon)));
  h.Patch(AccessTools.PropertyGetter(typeof(PowerModel),nameof(PowerModel.BigIcon)),prefix:new HarmonyMethod(typeof(ContentArt),nameof(PowerIcon)));
  h.Patch(AccessTools.PropertyGetter(typeof(PowerModel),nameof(PowerModel.PackedIconPath)),postfix:new HarmonyMethod(typeof(ContentArt),nameof(PowerPath)));
 }
 static bool BetaPortraitPath(CardModel __instance,ref string __result){if(__instance is not ModForm)return true;__result=PortraitResourcePath;return false;}
 static bool PowerIcon(PowerModel __instance,ref Texture2D __result){if(__instance is not ModFormPower)return true;__result=Icon;return false;}
 static void PowerPath(PowerModel __instance,ref string __result){if(__instance is ModFormPower)__result=ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.DemonFormPower>().PackedIconPath;}
}
