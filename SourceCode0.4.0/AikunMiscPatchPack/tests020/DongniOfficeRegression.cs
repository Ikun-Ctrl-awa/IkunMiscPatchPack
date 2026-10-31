using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 sealed class OfficeSelector : ICardSelector {
  public CardModel? Wanted;public bool SkipReward,SkipAfterFirst;public int Calls,Min,Max,RewardCalls;public CardModel[] Seen=[];
  public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options,int minSelect,int maxSelect){
   Calls++;Min=minSelect;Max=maxSelect;Seen=options.ToArray();
   if(Wanted!=null&&!Seen.Contains(Wanted))throw new Exception("办公室测试选择了候选范围之外的牌。");
   return Task.FromResult<IEnumerable<CardModel>>(Wanted==null?[]:[Wanted]);
  }
  public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,IReadOnlyList<CardRewardAlternative> alternatives){bool skip=SkipReward||(SkipAfterFirst&&RewardCalls>0);RewardCalls++;return skip?new(){alternative=alternatives.Single(a=>a.OptionId=="Skip")}:new(){card=options.First().Card};}
 }
 static BloodFixture OfficeFixture(){var f=NewDongniFixture(true);f.Run.AppendToMapPointHistory(MapPointType.Unknown,RoomType.Event,null);return f;}
 static async Task DongniOfficeChecks(){
  var model=ModelDb.Event<Pack.DongniOffice>();
  Check("office registered as cross-act shared event",ModelDb.AllSharedEvents.Count(e=>e is Pack.DongniOffice)==1);
  Check("office and relic localization installed",model.Title.GetFormattedText()=="东尼的办公室"&&model.InitialDescription.GetFormattedText()=="你路过一间办公室，里面的人向你滔滔不绝地讲述他的设计理论。");
  using(var f=OfficeFixture()){
   foreach(var act in ModelDb.Acts.Where(a=>a.IsDefault||a.Id.Entry.Contains("ENDING")||a.GetType().FullName!.Contains("Act4Heart"))){
    var mutable=(ActModel)act.ToMutable();mutable.GenerateRooms(new Rng(123),UnlockState.all);
    var rooms=(RoomSet)AccessTools.Field(typeof(ActModel),"_rooms").GetValue(mutable)!;
    Check("office generated in chapter event pool "+act.Id,rooms.events.Any(e=>e is Pack.DongniOffice));
   }
   bool enabled=Pack.Settings.Startup[Pack.DongniContent.Key];
   try{Pack.Settings.Startup[Pack.DongniContent.Key]=false;Check("office disabled option prevents future encounters",!model.IsAllowed(f.Run));}
   finally{Pack.Settings.Startup[Pack.DongniContent.Key]=enabled;}
  }
  string[] titles=["表示认同。","和他讨论。","陷入沉思。","直接离开。"];
  string[] results=["懂你意思。","你和办公室里的人讨论了半天，得到了一个结论：大牌组就是好。他祝福你使用大牌组顺利通关。","他说的对……他说的对吗？他说的不对吧。","不听不听，东尼念经。"];
  for(int index=0;index<4;index++)foreach(bool other in new[]{false,true}){
   using var f=OfficeFixture();var owner=other?f.Other:f.P;var office=(Pack.DongniOffice)model.ToMutable();await office.BeginEvent(owner,null,false);
   Check($"office four options P{owner.NetId} branch {index}",office.CurrentOptions.Select(o=>o.Title.GetFormattedText()).SequenceEqual(titles));
   int cards=owner.Deck.Cards.Count,foreign=(other?f.P:f.Other).Deck.Cards.Count;var options=office.CurrentOptions.ToArray();
   await options[index].Chosen();int added=owner.Deck.Cards.Count-cards;
   Check($"office exact outcome text P{owner.NetId} branch {index}",office.IsFinished&&office.CurrentOptions.Count==0&&office.Description!.GetFormattedText()==results[index]);
   Check($"office branch rewards P{owner.NetId} branch {index}",added==(index==0?1:index==2?3:0)&&owner.Relics.Count(r=>r is Pack.DongniBlessing)==(index==1?1:0)&&owner.Relics.Count(r=>r is Pack.DongniThinking)==(index==2?1:0));
   var newCards=owner.Deck.Cards.OfType<Pack.DongniMeaningCard>().ToArray();
   Check($"office exact permanent card types P{owner.NetId} branch {index}",index==0?newCards.Length==1&&newCards[0] is Pack.DongniMeaning:index==2?newCards.Length==3&&newCards.Select(c=>c.GetType()).Distinct().Count()==3:newCards.Length==0);
   Check($"office other player isolated P{owner.NetId} branch {index}",(other?f.P:f.Other).Deck.Cards.Count==foreign&&!(other?f.P:f.Other).Relics.Any(r=>r is Pack.DongniEventRelic));
   await options[index].Chosen();await options[(index+1)%4].Chosen();
   Check($"office old and repeated choices cannot duplicate rewards P{owner.NetId} branch {index}",owner.Deck.Cards.Count==cards+added&&owner.Relics.Count(r=>r is Pack.DongniEventRelic)==(index is 1 or 2?1:0));
   if(index is 1 or 2){var relic=owner.Relics.Single(r=>r is Pack.DongniEventRelic);var restored=RelicModel.FromSerializable(JsonSerializer.Deserialize<SerializableRelic>(JsonSerializer.Serialize(relic.ToSerializable()))!);
    Check("office relic save restores model without pickup duplication "+index,restored.GetType()==relic.GetType()&&restored.Rarity==RelicRarity.Event&&restored.Icon!=null&&restored.BigIcon!=null&&owner.Deck.Cards.Count==cards+added);}
  }
  foreach(bool other in new[]{false,true}){
   using var f=OfficeFixture();var owner=other?f.Other:f.P;var blessing=(Pack.DongniBlessing)ModelDb.Relic<Pack.DongniBlessing>().ToMutable();owner.AddRelicInternal(blessing,silent:true);
   var first=await DongniPut(f,PileType.Draw,owner);var chosen=await DongniPut(f,PileType.Draw,owner);var discard=await DongniPut(f,PileType.Discard,owner);var foreign=await DongniPut(f,PileType.Draw,other?f.P:f.Other);
   var selector=new OfficeSelector{Wanted=chosen};using(CardSelectCmd.UseSelector(selector))await blessing.AfterPlayerTurnStart(f.Choice,owner);
   Check("blessing chooses own draw pile 0 or 1 P"+owner.NetId,selector.Calls==1&&selector.Min==0&&selector.Max==1&&selector.Seen.Length==2&&selector.Seen.Contains(chosen)&&!selector.Seen.Contains(discard)&&!selector.Seen.Contains(foreign)&&chosen.Pile?.Type==PileType.Hand&&first.Pile?.Type==PileType.Draw);
   selector=new OfficeSelector();using(CardSelectCmd.UseSelector(selector))await blessing.AfterPlayerTurnStart(f.Choice,owner);
   Check("blessing optional turn selection can decline P"+owner.NetId,selector.Calls==1&&first.Pile?.Type==PileType.Draw);
   selector=new OfficeSelector{Wanted=first};using(CardSelectCmd.UseSelector(selector))await blessing.AfterPlayerTurnStart(f.Choice,owner);
   Check("blessing repeats on later turns P"+owner.NetId,first.Pile?.Type==PileType.Hand&&selector.Calls==1);
   selector=new OfficeSelector();using(CardSelectCmd.UseSelector(selector)){await blessing.AfterPlayerTurnStart(f.Choice,owner);await blessing.AfterPlayerTurnStart(f.Choice,other?f.P:f.Other);}
   Check("blessing empty and foreign turns have no selection P"+owner.NetId,selector.Calls==0);
   await DongniPut(f,PileType.Draw,owner);while(owner.PlayerCombatState!.Hand.Cards.Count<CardPile.MaxCardsInHand)await DongniPut(f,PileType.Hand,owner);
   selector=new OfficeSelector();using(CardSelectCmd.UseSelector(selector))await blessing.AfterPlayerTurnStart(f.Choice,owner);
   Check("blessing full hand never discards draw card P"+owner.NetId,selector.Calls==0&&owner.PlayerCombatState.DrawPile.Cards.Count==1);
  }
  await OfficeScreenshot();
 }
 static CardReward OfficeReward(Player player)=>new(new CardModel[]{player.RunState.CreateCard<Pack.DongniMeaning>(player),player.RunState.CreateCard<StrikeIronclad>(player)},CardCreationSource.Other,player,CardCreationOptions.ForRoom(player,RoomType.Monster));
 static CardReward? multiOfficeReward;
 static bool ForceOfficeMultiPick(CardReward reward,ref bool __result){if(reward!=multiOfficeReward)return true;__result=true;return false;}
 static async Task DongniRewardGateChecks(){
  using var f=OfficeFixture();var sync=RunManager.Instance.RewardsSetSynchronizer;
  var blessing=(Pack.DongniBlessing)ModelDb.Relic<Pack.DongniBlessing>().ToMutable();f.P.AddRelicInternal(blessing,silent:true);
  var first=OfficeReward(f.P);var second=OfficeReward(f.P);var gold=new GoldReward(25,f.P);var set=new RewardsSet(f.P).WithCustomRewards([gold,first,second]);await set.GenerateWithoutOffering();var waiting=sync.BeginRewardsSet(set);
  Check("blessing keeps native card skip buttons",first.CanSkip&&second.CanSkip&&CardRewardAlternative.Generate(first).Any(a=>a.OptionId=="Skip"));
  Check("blessing pending sets gate proceed and map",Pack.DongniRewardGate.NeedsChoice(set)&&Pack.DongniRewardGate.HasPending(f.P)&&!blessing.ShouldProceedToNextMapPoint());
  using(CardSelectCmd.UseSelector(new OfficeSelector{SkipReward=true}))Check("native card Skip returns without claiming",!await sync.SelectLocalReward(first));
  Check("after Skip card reward remains available and mandatory",!first.SuccessfullySelected&&first.Cards.Count()==2&&Pack.DongniRewardGate.NeedsChoice(set)&&!waiting.IsCompleted);
  int beforeGold=f.P.Gold;await sync.SelectLocalReward(gold);Check("other rewards remain claimable before choosing cards",f.P.Gold==beforeGold+25&&Pack.DongniRewardGate.HasPending(f.P));
  sync.SkipLocalRewardsSet();sync.BeforeLeavingRoom();Check("whole rewards skip and room exit cannot discard required cards",!waiting.IsCompleted&&!sync.IsRewardsSetCompleted(set)&&Pack.DongniRewardGate.HasPending(f.P));
  using(CardSelectCmd.UseSelector(new OfficeSelector()))Check("first required card actually enters deck",await sync.SelectLocalReward(first));
  Check("each card reward requires its own claim",Pack.DongniRewardGate.NeedsChoice(set)&&first.SuccessfullySelected&&!second.SuccessfullySelected&&!waiting.IsCompleted);
  using(CardSelectCmd.UseSelector(new OfficeSelector{SkipReward=true}))Check("second card reward can still return to rewards",!await sync.SelectLocalReward(second));
  using(CardSelectCmd.UseSelector(new OfficeSelector()))Check("second required card actually enters deck",await sync.SelectLocalReward(second));
  Check("all card claims unlock continue and complete set",waiting.IsCompleted&&sync.IsRewardsSetCompleted(set)&&!Pack.DongniRewardGate.HasPending(f.P)&&blessing.ShouldProceedToNextMapPoint());
  first=OfficeReward(f.P);gold=new GoldReward(20,f.P);set=new RewardsSet(f.P).WithCustomRewards([first,gold]);await set.GenerateWithoutOffering();waiting=sync.BeginRewardsSet(set);
  using(CardSelectCmd.UseSelector(new OfficeSelector()))await sync.SelectLocalReward(first);
  Check("non card rewards never become mandatory",!Pack.DongniRewardGate.NeedsChoice(set)&&!waiting.IsCompleted);sync.SkipLocalRewardsSet();Check("remaining gold can be skipped after card claim",waiting.IsCompleted&&!gold.SuccessfullySelected);
  var foreign=OfficeReward(f.Other);var foreignSet=new RewardsSet(f.Other).WithCustomRewards([foreign]);await foreignSet.GenerateWithoutOffering();_ = sync.BeginRewardsSet(foreignSet);
  Check("unblessed player reward unaffected",foreign.CanSkip&&!Pack.DongniRewardGate.NeedsChoice(foreignSet)&&!Pack.DongniRewardGate.HasPending(f.Other));
  var restore=Reward.FromSerializable(OfficeReward(f.P).ToSerializable(),f.P);set=new RewardsSet(f.P).WithCustomRewards([restore]);await set.GenerateWithoutOffering();waiting=sync.BeginRewardsSet(set);
  Check("saved pending reward restores with skip and mandatory claim",restore is CardReward {CanSkip:true}&&Pack.DongniRewardGate.NeedsChoice(set));
  using(CardSelectCmd.UseSelector(new OfficeSelector()))await sync.SelectLocalReward(restore);Check("restored card claim unlocks proceed",waiting.IsCompleted&&!Pack.DongniRewardGate.HasPending(f.P));
  var special=new SpecialCardReward(f.Run.CreateCard<Pack.DongniMeaning>(f.P),f.P);set=new RewardsSet(f.P).WithCustomRewards([special]);await set.GenerateWithoutOffering();waiting=sync.BeginRewardsSet(set);
  Check("specific single card rewards cannot be bypassed",Pack.DongniRewardGate.NeedsChoice(set));await sync.SelectLocalReward(special);Check("specific single card claim unlocks",waiting.IsCompleted&&!Pack.DongniRewardGate.HasPending(f.P));
  first=OfficeReward(f.P);set=new RewardsSet(f.P).WithCustomRewards([first]);await set.GenerateWithoutOffering();waiting=sync.BeginRewardsSet(set);
  var innerGold=new GoldReward(5,f.P);var inner=new RewardsSet(f.P).WithCustomRewards([innerGold]);await inner.GenerateWithoutOffering();var innerWaiting=sync.BeginRewardsSet(inner);sync.SkipLocalRewardsSet();
  Check("nested noncard reward may close while parent card remains required",innerWaiting.IsCompleted&&!waiting.IsCompleted&&Pack.DongniRewardGate.HasPending(f.P));
  using(CardSelectCmd.UseSelector(new OfficeSelector()))await sync.SelectLocalReward(first);
  Check("returning from nested rewards can claim parent card",waiting.IsCompleted&&!Pack.DongniRewardGate.HasPending(f.P));
  first=OfficeReward(f.P);set=new RewardsSet(f.P).WithCustomRewards([first]);await set.GenerateWithoutOffering();waiting=sync.BeginRewardsSet(set);multiOfficeReward=first;
  var h=new Harmony("Aikun020Probe.OfficeMultiPick");var more=AccessTools.Method(typeof(MegaCrit.Sts2.Core.Hooks.Hook),"ShouldAllowSelectingMoreCardRewards");h.Patch(more,prefix:new HarmonyMethod(typeof(Entry),nameof(ForceOfficeMultiPick)));
  try{int cards=f.P.Deck.Cards.Count;using(CardSelectCmd.UseSelector(new OfficeSelector{SkipAfterFirst=true}))await sync.SelectLocalReward(first);
   Check("one actual card satisfies a multi pick reward even after returning",f.P.Deck.Cards.Count==cards+1&&!first.SuccessfullySelected&&!Pack.DongniRewardGate.NeedsChoice(set));sync.SkipLocalRewardsSet();Check("partial optional multi pick reward no longer blocks proceed",waiting.IsCompleted);
  }finally{h.Unpatch(more,HarmonyPatchType.All,h.Id);multiOfficeReward=null;}
  var empty=new CardReward(Array.Empty<CardModel>(),CardCreationSource.Other,f.P,CardCreationOptions.ForRoom(f.P,RoomType.Monster));set=new RewardsSet(f.P).WithCustomRewards([empty]);_ = sync.BeginRewardsSet(set);
  Check("empty invalid card reward cannot soft lock travel",!Pack.DongniRewardGate.NeedsChoice(set));sync.SkipLocalRewardsSet();
  first=OfficeReward(f.P);gold=new GoldReward(9,f.P);var linked=new LinkedRewardSet([first,gold],f.P);set=new RewardsSet(f.P).WithCustomRewards([linked]);await set.GenerateWithoutOffering();waiting=sync.BeginRewardsSet(set);
  bool linkedNeededBefore=Pack.DongniRewardGate.NeedsChoice(set),linkedSameParent=ReferenceEquals(gold.ParentRewardSet,linked);bool linkedGoldResult=await sync.SelectLocalReward(gold);
  File.WriteAllText(Output+"/linked-reward-diagnostics.json",JsonSerializer.Serialize(new{linkedNeededBefore,linkedSameParent,linkedGoldResult,gold.SuccessfullySelected,Pending=Pack.DongniRewardGate.NeedsChoice(set),Count=first.Cards.Count(),TopRewards=set.Rewards.Select(r=>r.GetType().Name),Linked=linked.Rewards.Select(r=>new{Type=r.GetType().Name,r.SuccessfullySelected})},new JsonSerializerOptions{WriteIndented=true}));
  Check("linked noncard alternative cannot discard an unclaimed card",!linkedGoldResult&&!gold.SuccessfullySelected&&Pack.DongniRewardGate.NeedsChoice(set));
  using(CardSelectCmd.UseSelector(new OfficeSelector{SkipReward=true}))await sync.SelectLocalReward(first);
  Check("linked card skip keeps the card available for later",linked.Rewards.Contains(first)&&Pack.DongniRewardGate.NeedsChoice(set));
  using(CardSelectCmd.UseSelector(new OfficeSelector()))await sync.SelectLocalReward(first);
  Check("linked actual card claim fulfills linked choice",!Pack.DongniRewardGate.NeedsChoice(set));if(!waiting.IsCompleted)sync.SkipLocalRewardsSet();Check("linked fulfilled choice can proceed",waiting.IsCompleted);
  await OfficeRewardsUi(f);
 }
 static async Task OfficeRewardsUi(BloodFixture f){
  var reward=OfficeReward(f.P);var gold=new GoldReward(12,f.P);var spareGold=new GoldReward(7,f.P);var set=new RewardsSet(f.P).WithCustomRewards([gold,spareGold,reward]);await set.GenerateWithoutOffering();var sync=RunManager.Instance.RewardsSetSynchronizer;var waiting=sync.BeginRewardsSet(set);
  var viewport=new SubViewport{Size=new(2560,1440),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);
  bool old=TestMode.IsOn;NRewardsScreen? screen=null;
  try{
   TestMode.IsOn=false;string scene=(string)AccessTools.Property(typeof(NRewardsScreen),"ScenePath").GetValue(null)!;
   screen=PreloadManager.Cache.GetScene(scene).Instantiate<NRewardsScreen>();AccessTools.Field(typeof(NRewardsScreen),"_rewardsSet").SetValue(screen,set);AccessTools.Field(typeof(NRewardsScreen),"_runState").SetValue(screen,f.Run);AccessTools.Field(typeof(NRewardsScreen),"_isTerminal").SetValue(screen,true);viewport.AddChild(screen);screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);screen.AfterOverlayShown();await Wait(.7);
   var proceed=(NProceedButton)AccessTools.Field(typeof(NRewardsScreen),"_proceedButton").GetValue(screen)!;
   var buttons=((List<Control>)AccessTools.Field(typeof(NRewardsScreen),"_rewardButtons").GetValue(screen)!).ToArray();
   Check("native reward page disables only continue with cards pending",!proceed.IsEnabled&&buttons.Length==3&&buttons.All(b=>b.IsVisibleInTree()));
   await AfterDraw();Check("native deferred reward screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/dongni-rewards-deferred.png")==Error.Ok);
   AccessTools.Method(typeof(NRewardsScreen),"OnProceedButtonPressed").Invoke(screen,[proceed]);
   Check("native direct continue handler cannot bypass required card",!waiting.IsCompleted&&Pack.DongniRewardGate.HasPending(f.P));
   TestMode.IsOn=true;using(CardSelectCmd.UseSelector(new OfficeSelector{SkipReward=true}))await sync.SelectLocalReward(reward);
   screen.RewardSkippedFrom(buttons[2]);Check("native card skip returns to rewards and keeps continue locked",!proceed.IsEnabled&&!reward.SuccessfullySelected&&buttons[0].IsInsideTree());
   await sync.SelectLocalReward(gold);screen.RewardCollectedFrom(buttons[0]);Check("native other reward can be claimed while card is deferred",!proceed.IsEnabled&&gold.SuccessfullySelected);
   using(CardSelectCmd.UseSelector(new OfficeSelector()))await sync.SelectLocalReward(reward);
   screen.RewardCollectedFrom(buttons[2]);Check("native continue re-enables only after actual card claim",proceed.IsEnabled&&!waiting.IsCompleted&&!spareGold.SuccessfullySelected);
   sync.SkipLocalRewardsSet();Check("native remaining noncard reward can be left",waiting.IsCompleted);
   TestMode.IsOn=false;await Wait(.1);await AfterDraw();viewport.GetTexture().GetImage().SavePng(Output+"/dongni-rewards-claimed.png");
  }finally{TestMode.IsOn=old;viewport.QueueFree();await Wait(.03);}
 }
 static async Task OfficeScreenshot(){
  using var f=OfficeFixture();var sync=RunManager.Instance.EventSynchronizer;sync.BeginEvent(ModelDb.Event<Pack.DongniOffice>());var office=(Pack.DongniOffice)sync.GetLocalEvent();
  var viewport=new SubViewport{Size=new(2560,1440),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);
  host.AddChild(new ColorRect{Size=host.Size,Color=new("181c27")});bool old=TestMode.IsOn;
  try{
   TestMode.IsOn=false;var node=NEventRoom.Create(office,f.Run,false)!;host.AddChild(node);node.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);await Wait(2.2);
   Check("office native event loads office illustration",office.CreateInitialPortrait()==Pack.DongniOfficeArt.Background);
   Check("office native layout displays four options",node.Layout!=null&&office.CurrentOptions.Count==4&&node.Layout.OptionButtons.Count()==4&&node.Layout.OptionButtons.All(b=>b.IsVisibleInTree()));
   Check("office background uses semitransparent full layout",Math.Abs(node.Layout!.GetNode<TextureRect>("DongniOfficeBackground").Modulate.A-Pack.DongniOfficeArt.BackgroundOpacity)<.001);
   await AfterDraw();Check("office native initial screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/dongni-office-initial.png")==Error.Ok);
   sync.ChooseLocalOption(3);await sync.AwaitPendingOptionTasks();await Wait(2.2);await AfterDraw();Check("office synchronized choice reaches chosen outcome",office.IsFinished&&office.Description!.GetFormattedText()=="不听不听，东尼念经。");Check("office native chosen outcome screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/dongni-office-leave.png")==Error.Ok);
  }finally{TestMode.IsOn=old;viewport.QueueFree();await Wait(.03);}
 }
}
