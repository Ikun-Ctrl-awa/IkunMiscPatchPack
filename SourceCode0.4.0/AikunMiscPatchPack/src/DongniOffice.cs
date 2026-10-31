using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Scaffolding.Content;

namespace AikunMiscPatchPack;

public sealed class DongniOffice : ModEventTemplate {
 bool resolving;
 public override bool IsAllowed(IRunState runState)=>Settings.Active(DongniContent.Key);
 public override string? CustomInitialPortraitPath=>DongniOfficeArt.BackgroundPath;
 public override IEnumerable<string> GetAssetPaths(IRunState runState)=>["res://scenes/events/default_event_layout.tscn",CustomInitialPortraitPath!];
 protected override IReadOnlyList<EventOption> GenerateInitialOptions()=>[
  new EventOption(this,()=>Choose("AGREE"),InitialOptionKey("AGREE"),new CardHoverTip(ModelDb.Card<DongniMeaning>())),
  new EventOption(this,()=>Choose("DISCUSS"),InitialOptionKey("DISCUSS"),ModelDb.Relic<DongniBlessing>().HoverTips),
  new EventOption(this,()=>Choose("THINK"),InitialOptionKey("THINK"),ModelDb.Relic<DongniThinking>().HoverTips),
  new EventOption(this,()=>Choose("LEAVE"),InitialOptionKey("LEAVE"))
 ];
 async Task Choose(string choice){
  // 一个事件只结算一个分支；界面移除后的旧按钮也不能重复赠送奖励。
  if(IsFinished||resolving)return;
  resolving=true;
  try{
   var player=Owner??throw new InvalidOperationException("东尼的办公室尚未指定玩家。");
   if(choice=="AGREE")await CardPileCmd.Add(player.RunState.CreateCard<DongniMeaning>(player),PileType.Deck);
   else if(choice=="DISCUSS")await RelicCmd.Obtain<DongniBlessing>(player);
   else if(choice=="THINK")await RelicCmd.Obtain<DongniThinking>(player);
   SetEventFinished(PageDescription(choice));
  }finally{resolving=false;}
 }
}

public abstract class DongniEventRelic : ModRelicTemplate {
 public override RelicRarity Rarity=>RelicRarity.Event;
 public override bool IsAllowedInShops=>false;
 public override string? CustomIconPath=>DongniOfficeArt.IconPath(this is DongniBlessing?DongniOfficeArt.BlessingPath:DongniOfficeArt.ThinkingPath);
 public override string? CustomIconOutlinePath=>CustomIconPath;
 public override string? CustomBigIconPath=>CustomIconPath;
}

public sealed class DongniBlessing : DongniEventRelic {
 public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext,Player player){
  if(player!=Owner||!Owner.Creature.IsAlive||Owner.PlayerCombatState is not {} state||state.DrawPile.Cards.Count==0||state.Hand.Cards.Count>=CardPile.MaxCardsInHand)return;
  // 使用原生按玩家同步的牌堆选牌；0..1 允许放弃，且不会泄露抽牌堆顺序。
  var selected=await CardSelectCmd.FromCombatPile(choiceContext,state.DrawPile,Owner,new CardSelectorPrefs(SelectionScreenPrompt,0,1){Cancelable=true});
  foreach(var card in selected){
   if(card.Owner==Owner&&card.Pile==state.DrawPile&&state.Hand.Cards.Count<CardPile.MaxCardsInHand){Flash();await CardPileCmd.Add(card,PileType.Hand);}
  }
 }
 public override bool ShouldProceedToNextMapPoint()=>!LocalContext.IsMe(Owner)||!DongniRewardGate.HasPending(Owner);
}

public sealed class DongniThinking : DongniEventRelic {
 public override bool HasUponPickupEffect=>true;
 protected override IEnumerable<IHoverTip> AdditionalHoverTips=>[new CardHoverTip(ModelDb.Card<DongniMeaning>()),new CardHoverTip(ModelDb.Card<NotDongniMeaning>()),new CardHoverTip(ModelDb.Card<WhoDongniMeaning>())];
 public override async Task AfterObtained(){
  var run=Owner.RunState;
  await CardPileCmd.Add(new CardModel[]{run.CreateCard<DongniMeaning>(Owner),run.CreateCard<NotDongniMeaning>(Owner),run.CreateCard<WhoDongniMeaning>(Owner)},PileType.Deck);
 }
}

public static class DongniOfficeContent {
 public static void Localize(){
  if(LocManager.Instance==null)return;
  string office=ModelDb.GetId<DongniOffice>().Entry;
  var events=new Dictionary<string,string>{[office+".title"]="东尼的办公室",[office+".pages.INITIAL.description"]="你路过一间办公室，里面的人向你滔滔不绝地讲述他的设计理论。"};
  foreach(var (key,title,description,result) in new[]{
   ("AGREE","表示认同。","将[gold]东尼意思[/gold]添加到你的牌组。","懂你意思。"),
   ("DISCUSS","和他讨论。","获得遗物：[gold]东尼的祝福[/gold]。","你和办公室里的人讨论了半天，得到了一个结论：大牌组就是好。他祝福你使用大牌组顺利通关。"),
   ("THINK","陷入沉思。","获得遗物：[gold]东尼的思考[/gold]。","他说的对……他说的对吗？他说的不对吧。"),
   ("LEAVE","直接离开。","什么都不会发生。","不听不听，东尼念经。")}){
   events[office+".pages.INITIAL.options."+key+".title"]=title;
   events[office+".pages.INITIAL.options."+key+".description"]=description;
   events[office+".pages."+key+".description"]=result;
  }
  LocManager.Instance.GetTable("events").MergeWith(events);
  var relics=new Dictionary<string,string>();
  foreach(var (type,title,description) in new[]{
   (typeof(DongniBlessing),"东尼的祝福","在每回合开始时，你可以从抽牌堆选择1张卡牌，将其放入手牌。\n有未领取的卡牌奖励时，必须领取其中1张才能继续或进入新房间。你仍可以在选牌界面点击跳过，返回查看其他奖励。"),
   (typeof(DongniThinking),"东尼的思考","获得时，将[gold]东尼意思[/gold]、[gold]不东尼意思[/gold]、[gold]谁东尼意思[/gold]各1张加入你的牌组。")}){
   string id=ModelDb.GetId(type).Entry;relics[id+".title"]=title;relics[id+".description"]=description;relics[id+".flavor"]=title=="东尼的祝福"?"大牌组就是好。":"他说的对吗？";
   relics[id+".eventDescription"]=description;
   relics[id+".selectionScreenPrompt"]="你可以从抽牌堆选择1张卡牌放入手牌。";
  }
  LocManager.Instance.GetTable("relics").MergeWith(relics);
 }
}
