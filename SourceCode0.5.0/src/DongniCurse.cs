using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using STS2RitsuLib.Scaffolding.Content;

namespace IkunMiscPatchPack;

public sealed class DongniCurse : ModCardTemplate {
 public DongniCurse():base(3,CardType.Curse,CardRarity.Curse,TargetType.Self){}
 public override CardPoolModel Pool=>ModelDb.CardPool<CurseCardPool>();
 public override int MaxUpgradeLevel=>1;
 public override IEnumerable<CardKeyword> CanonicalKeywords=>[CardKeyword.Exhaust];
 public override bool CanBeGeneratedInCombat=>Settings.Active(TowerCurseContent.Key);
 public override bool CanBeGeneratedByModifiers=>CanBeGeneratedInCombat;
 public override string PortraitPath=>DongniCurseArt.PortraitPath;
 public override string BetaPortraitPath=>PortraitPath;
 protected override string PortraitPngPath=>PortraitPath;
 public override string? CustomPortraitPath=>PortraitPath;
 public override string? CustomBetaPortraitPath=>PortraitPath;
 public override IEnumerable<string> AllPortraitPaths=>[PortraitPath];
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new StringVar("EffectText",DongniCurseRules.NormalText)];
 protected override void OnUpgrade()=>((StringVar)DynamicVars["EffectText"]).StringValue=DongniCurseRules.UpgradedText;
 protected override Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay)=>Task.CompletedTask;
}

public static class DongniCurseArt {
 static readonly object artLock=new();
 static string? artFile;
 public static string PortraitPath {
  get{lock(artLock){
   if(artFile==null||!File.Exists(artFile)){
    using var stream=typeof(DongniCurse).Assembly.GetManifestResourceStream("cardart.dongni_curse.png")??throw new FileNotFoundException("东尼的诅咒卡图缺失");
    using var bytes=new MemoryStream();stream.CopyTo(bytes);using var image=new Image();
    if(image.LoadPngFromBuffer(bytes.ToArray())!=Error.Ok)throw new InvalidDataException("东尼的诅咒卡图PNG无效");
    artFile=EnchantmentIconTexture.Cache(image,"card-portraits");
   }
   return EnchantmentIconTexture.ResourcePath(artFile);
  }}
 }
}

public static class DongniCurseRules {
 public const string NormalText="这张牌在你的手牌中时，你打出的卡牌效果改为下回合开始时按出牌顺序生效。\n可以被[gold]升级[/gold]。";
 public const string UpgradedText="这张牌在你的手牌中时，你的获得[gold]能量[/gold]和[gold]抽牌[/gold]效果改为下回合生效。";
 sealed record Pending(CardModel Card,Creature? Target,ResourceInfo Resources,int Turn);
 sealed class CombatQueue {public readonly Dictionary<Player,List<Pending>> Players=new();}
 sealed record Delivery(ResourceInfo Resources);
 sealed class DrawCapture(Player player){public readonly Player Player=player;public readonly List<CardModel> Cards=new();}
 static readonly ConditionalWeakTable<ICombatState,CombatQueue> queues=new();
 static readonly ConditionalWeakTable<CardModel,object> suppressed=new();
 static readonly ConditionalWeakTable<CardModel,Delivery> deliveries=new();
 static readonly Dictionary<Player,int> resourceDelivery=new();
 static readonly AsyncLocal<DrawCapture?> drawCapture=new();
 static readonly AsyncLocal<Player?> replayingPlayer=new();
 static readonly object marker=new();
 static readonly PropertyInfo Dupe=AccessTools.Property(typeof(CardModel),nameof(CardModel.IsDupe));
 static readonly MethodInfo Generate=AccessTools.Method(typeof(CardModel),"GeneratePlayCount");
 static bool InHand(Player player,bool upgraded)=>player.PlayerCombatState?.Hand.Cards.Any(c=>
  (c is DongniCurse&&c.IsUpgraded==upgraded)||FusionQuestBlessingFix.Entry.Descendants(c).Any(d=>d is DongniCurse&&d.IsUpgraded==upgraded))==true;
 static bool Bypassed(Player player)=>resourceDelivery.GetValueOrDefault(player)>0;
 static void Enter(Player player)=>resourceDelivery[player]=resourceDelivery.GetValueOrDefault(player)+1;
 static void Leave(Player player){int depth=resourceDelivery.GetValueOrDefault(player)-1;if(depth<=0)resourceDelivery.Remove(player);else resourceDelivery[player]=depth;}
 public static int PendingCount(Player player)=>player.Creature.CombatState is {} combat&&queues.TryGetValue(combat,out var q)&&q.Players.TryGetValue(player,out var list)?list.Count:0;
 public static void Install(Harmony h){
  h.Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.OnPlayWrapper)),prefix:new HarmonyMethod(typeof(DongniCurseRules),nameof(Play)){priority=Priority.Last});
  h.Patch(Generate,prefix:new HarmonyMethod(typeof(DongniCurseRules),nameof(SkipEffects)));
  h.Patch(AccessTools.Method(typeof(Hook),nameof(Hook.ModifyCardPlayResultLocation)),postfix:new HarmonyMethod(typeof(DongniCurseRules),nameof(ReplayResult)){priority=Priority.Last});
  h.Patch(AccessTools.Method(typeof(Hook),nameof(Hook.AfterAutoPrePlayPhaseEntered)),postfix:new HarmonyMethod(typeof(DongniCurseRules),nameof(AfterAutoPlayPhase)));
  h.Patch(AccessTools.Method(typeof(PlayerCmd),nameof(PlayerCmd.GainEnergy)),prefix:new HarmonyMethod(typeof(DongniCurseRules),nameof(Energy)){priority=Priority.Last});
  h.Patch(AccessTools.Method(typeof(CardPileCmd),nameof(CardPileCmd.Draw),[typeof(PlayerChoiceContext),typeof(decimal),typeof(Player),typeof(bool)]),prefix:new HarmonyMethod(typeof(DongniCurseRules),nameof(Draw)){priority=Priority.Last},postfix:new HarmonyMethod(typeof(DongniCurseRules),nameof(CaptureDraw)));
  h.Patch(AccessTools.Method(typeof(Hook),nameof(Hook.AfterPowerAmountChanged)),prefix:new HarmonyMethod(typeof(DongniCurseRules),nameof(DelayedPowerChanging)),postfix:new HarmonyMethod(typeof(DongniCurseRules),nameof(DelayedPowerChanged)));
  h.Patch(AccessTools.Method(typeof(EnergyNextTurnPower),nameof(EnergyNextTurnPower.AfterEnergyReset)),prefix:new HarmonyMethod(typeof(DongniCurseRules),nameof(FutureEnergyStarting)),postfix:new HarmonyMethod(typeof(DongniCurseRules),nameof(DelayedPowerChanged)));
  h.Patch(AccessTools.Method(typeof(Hook),nameof(Hook.AfterCombatEnd)),postfix:new HarmonyMethod(typeof(DongniCurseRules),nameof(CombatEnded)));
  h.Patch(AccessTools.PropertyGetter(typeof(CardModel),"BetaPortraitPngPath"),prefix:new HarmonyMethod(typeof(DongniCurseRules),nameof(BetaPath)));
 }
 static bool BetaPath(CardModel __instance,ref string __result){if(__instance is not DongniCurse)return true;__result=__instance.PortraitPath;return false;}
 static bool Play(CardModel __instance,PlayerChoiceContext choiceContext,Creature? target,bool isAutoPlay,ref ResourceInfo resources,bool skipCardPileVisuals,ref Task __result){
  if(deliveries.TryGetValue(__instance,out var delivery)){resources=delivery.Resources;return true;}
  if(suppressed.TryGetValue(__instance,out _)||__instance is DongniCurse||(__instance.Owner is {} replayOwner&&ReferenceEquals(replayingPlayer.Value,replayOwner))||__instance.Keywords.Contains(CardKeyword.Unplayable)||__instance.Owner?.Creature.CombatState is not {} combat||!InHand(__instance.Owner,false))return true;
  // 保存独立战斗快照；原牌仍完整执行原生牌堆去向、消耗及费用清理。
  var snapshot=(CardModel)__instance.ClonePreservingMutability();snapshot.DeckVersion=__instance.DeckVersion;
  var q=queues.GetOrCreateValue(combat);if(!q.Players.TryGetValue(__instance.Owner,out var list))q.Players[__instance.Owner]=list=new();
  list.Add(new(snapshot,target,resources,__instance.Owner.PlayerCombatState!.TurnNumber));
  suppressed.Add(__instance,marker);
  __result=FinishOriginal(__instance,choiceContext,target,isAutoPlay,resources,skipCardPileVisuals);return false;
 }
 static async Task FinishOriginal(CardModel card,PlayerChoiceContext context,Creature? target,bool auto,ResourceInfo resources,bool skip){
  try{await card.OnPlayWrapper(context,target,auto,resources,skip);}finally{suppressed.Remove(card);}
 }
 static bool SkipEffects(CardModel __instance,ref Task<int> __result){
  if(!suppressed.TryGetValue(__instance,out _))return true;
  __result=Task.FromResult(0);return false;
 }
 static void ReplayResult(CardModel __1,ref CardLocation __result){if(deliveries.TryGetValue(__1,out _))__result=new CardLocation(__1.Owner,PileType.None,CardPilePosition.Bottom);}
 static void AfterAutoPlayPhase(HookPlayerChoiceContext playerChoiceContext,ICombatState combatState,Player player,ref Task __result)=>__result=AfterPhase(__result,playerChoiceContext,combatState,player);
 static async Task AfterPhase(Task original,PlayerChoiceContext context,ICombatState combat,Player player){
  await original;if(!queues.TryGetValue(combat,out var q)||!q.Players.TryGetValue(player,out var list)||player.PlayerCombatState is not {} state)return;
  var due=list.Where(p=>p.Turn<state.TurnNumber).ToArray();list.RemoveAll(p=>p.Turn<state.TurnNumber);
  foreach(var p in due){
   if(player.Creature.IsDead||CombatManager.Instance.IsOverOrEnding||player.Creature.CombatState!=combat)break;
   var card=p.Card;Dupe.SetValue(card,true);card.Owner=null!;combat.AddCard(card,player);deliveries.Add(card,new(p.Resources));Enter(player);
   // 已到期整张牌及其为同一玩家自动打出的子牌属于同一次兑现，不再排到更晚的回合。
   var previousReplay=replayingPlayer.Value;replayingPlayer.Value=player;
   try{
    Creature? target=p.Target;if(target!=null&&(!target.IsAlive||!combat.Creatures.Contains(target)))target=null;
    await CardCmd.AutoPlay(context,card,target,skipXCapture:true);
   }finally{
    replayingPlayer.Value=previousReplay;Leave(player);deliveries.Remove(card);
    if(card.Pile is {IsCombatPile:true})await CardPileCmd.RemoveFromCombat(card);
    if(combat.ContainsCard(card))combat.RemoveCard(card);
   }
  }
 }
 static bool Energy(decimal amount,Player player,ref Task __result){
  if(amount<=0||Bypassed(player)||player.PlayerCombatState==null||player.Creature.CombatState is not {} combat||!InHand(player,true)||CombatManager.Instance.IsOverOrEnding)return true;
  if(Hook.ModifyEnergyGain(combat,player,amount,out _)<=0)return true;
  __result=DelayEnergy(player,amount);return false;
 }
 static async Task DelayEnergy(Player player,decimal amount){
  var context=new HookPlayerChoiceContext(player,LocalContext.NetId??player.NetId,GameActionType.Combat);
  await PowerCmd.Apply<EnergyNextTurnPower>(context,player.Creature,amount,player.Creature,null);
 }
 static bool Draw(PlayerChoiceContext choiceContext,decimal count,Player player,bool fromHandDraw,ref Task<IEnumerable<CardModel>> __result){
  // 回合开始的基本抽牌和已到期的抽牌不再延后，避免保留诅咒造成永久断抽。
  if(count<=0||fromHandDraw||Bypassed(player)||player.PlayerCombatState==null||player.Creature.CombatState is not {} combat||!InHand(player,true)||CombatManager.Instance.IsOverOrEnding)return true;
  if(!Hook.ShouldDraw(combat,player,fromHandDraw,out _))return true;
  __result=DelayDraw(choiceContext,player,count);return false;
 }
 static async Task<IEnumerable<CardModel>> DelayDraw(PlayerChoiceContext context,Player player,decimal count){
  var previous=drawCapture.Value;var capture=new DrawCapture(player);drawCapture.Value=capture;
  try{await PowerCmd.Apply<DrawCardsNextTurnPower>(context,player.Creature,Math.Ceiling(count),player.Creature,null);return capture.Cards.Distinct().ToArray();}
  finally{drawCapture.Value=previous;}
 }
 static void CaptureDraw(Player player,ref Task<IEnumerable<CardModel>> __result){if(drawCapture.Value is {} capture&&capture.Player==player&&Bypassed(player))__result=CollectDraw(__result,capture);}
 static async Task<IEnumerable<CardModel>> CollectDraw(Task<IEnumerable<CardModel>> task,DrawCapture capture){var cards=(await task).ToArray();capture.Cards.AddRange(cards);return cards;}
 static void DelayedPowerChanging(PowerModel power,out Player? __state){
  __state=power is EnergyNextTurnPower or DrawCardsNextTurnPower?power.Owner?.Player:null;
  if(__state!=null)Enter(__state);
 }
 static void FutureEnergyStarting(EnergyNextTurnPower __instance,out Player? __state){__state=__instance.Owner?.Player;if(__state!=null)Enter(__state);}
 static void DelayedPowerChanged(Player? __state,ref Task __result){if(__state!=null)__result=EndDelivery(__result,__state);}
 static async Task EndDelivery(Task task,Player player){try{await task;}finally{Leave(player);}}
 static void CombatEnded(ICombatState combatState){queues.Remove(combatState);foreach(var p in combatState.Players)resourceDelivery.Remove(p);}
 public static void Localize(){
  if(LocManager.Instance==null)return;string id=ModelDb.GetId<DongniCurse>().Entry;
  LocManager.Instance.GetTable("cards").MergeWith(new(){[id+".title"]="东尼的诅咒",[id+".description"]="{EffectText}"});
 }
}
