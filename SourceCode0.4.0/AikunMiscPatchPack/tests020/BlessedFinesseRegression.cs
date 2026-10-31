using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.TestSupport;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static readonly List<(Player Owner,Reward[] Rewards)> blessingOffers=new();
 static bool CaptureBlessingOffer(Player player,List<Reward> rewards,ref Task __result){
  // 执行真实奖励填充与修改钩子，只替代最后等待用户选择的界面。
  __result=GenerateCapturedBlessing(player,rewards);return false;
 }
 static readonly List<ModelId[]> blessingOriginalOffers=new();
 static async Task GenerateCapturedBlessing(Player player,List<Reward> rewards){
  blessingOriginalOffers.Add(rewards.OfType<CardReward>().SelectMany(r=>r.Cards).Select(c=>c.Id).ToArray());
  var set=new RewardsSet(player).WithCustomRewards(rewards);await set.GenerateWithoutOffering();blessingOffers.Add((player,set.Rewards.ToArray()));
 }
 static Player FinesseRewardPlayer(string seed,bool second=false){
  CleanupScrollRoom();var p=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var other=Player.CreateForNewRun<MegaCrit.Sts2.Core.Models.Characters.Silent>(UnlockState.all,2);
  var run=RunState.CreateForTest([p,other],seed:seed);RunManager.Instance.SetUpTest(run,new NetSingleplayerGameService());run.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Unknown,RoomType.Event,null);return second?other:p;
 }
 static string HairReplacementSeed(){
  var pool=new[]{"FateFusionCard","TemporaryFusionCard","SuperDisassemblyCard","SpaceFusionCard","HotchpotchCard","SmallDeckCard","WatcherHairCard","EngineCard","LivingSlimeCard","SuperAssemblyCard"}.Select(n=>ModelDb.GetById<CardModel>(ModelDb.GetId(Find("FusionExpansionPack."+n)))).ToArray();
  for(int i=0;i<5000;i++){
   string seed="FINESSE-HAIR-"+i;var rng=new RunRngSet(seed).CombatCardGeneration;
   if(rng.NextFloat()>.08f)continue;
   if(rng.WeightedNextItem(pool,c=>c?.Rarity switch{CardRarity.Common=>5f,CardRarity.Uncommon=>3f,CardRarity.Rare=>1f,_=>0f})?.GetType().Name=="WatcherHairCard")return seed;
  }
  throw new Exception("未找到确定的观者头发重现种子");
 }
 static async Task BlessedFinesseChecks(){
  int first=passes.Count;var evidence=new List<object>();
  var option=Pack.Options.All.Single(o=>o.Key==Pack.YuiFusionBlessingRewardFix.Key);
  Check("finesse reward independent switch defaults on",new Pack.PackConfig().Get(option.Key)&&option.Required.SequenceEqual(new[]{"YUISpireExpansion","FusionExpansionPack"}));
  if(Minimal){Check("finesse reward missing target safely skipped",Pack.Settings.Status(option).Contains("未装载相关模组")&&!Pack.RuntimeLoader.States.ContainsKey(option.Key));return;}
  Check("finesse reward patch installed",Pack.RuntimeLoader.States.GetValueOrDefault(option.Key)=="已应用。");
  var harmony=new Harmony("Aikun020Probe.BlessedFinesseRewards");bool active=Pack.Settings.Active(option.Key),oldTest=TestMode.IsOn;
  var listeners=AccessTools.Field(typeof(CombatStateTracker),"CombatStateChanged");var frontend=listeners.GetValue(CombatManager.Instance.StateTracker);
  harmony.Patch(AccessTools.Method(typeof(RewardsCmd),nameof(RewardsCmd.OfferCustom),[typeof(Player),typeof(List<Reward>)]),prefix:new HarmonyMethod(typeof(Entry),nameof(CaptureBlessingOffer)));
  try{
   listeners.SetValue(CombatManager.Instance.StateTracker,null);TestMode.IsOn=true;
   string seed=HairReplacementSeed();var baselines=new Dictionary<string,string>();
   foreach(bool enabled in new[]{false,true})foreach(var (name,cardType) in new[]{("BlessedFinesse",typeof(Finesse)),("BlessedFlashOfSteel",typeof(FlashOfSteel)),("BlessedIronWave",typeof(IronWave))})foreach(bool secondPlayer in new[]{false,true}){
    Pack.Settings.Startup[option.Key]=enabled;var owner=FinesseRewardPlayer(seed,secondPlayer);
    var relic=ModelDb.GetById<RelicModel>(ModelDb.GetId(Find("YuiExtra.Relics."+name))).ToMutable();
    blessingOffers.Clear();int deckBefore=owner.Deck.Cards.Count;
    await RelicCmd.Obtain(relic,owner).WaitAsync(TimeSpan.FromSeconds(8));
    bool protectedGift=enabled;string tag=$"{name} enabled={enabled} second={secondPlayer}";
    Check(tag+" actual relic obtained for correct owner",owner.Relics.Contains(relic)&&relic.Owner==owner);
    Check(tag+" exactly one reward is still offered",blessingOffers.Count==1&&blessingOffers.Single().Rewards.Length==1);
    var offer=blessingOffers.Single();var reward=(CardReward)offer.Rewards.Single();
    Check(tag+" reward owner retained",offer.Owner==owner&&reward.Player==owner);
    string actual=reward.Cards.Single().Id.ToString();string key=name+secondPlayer;
    if(!enabled){baselines[key]=actual;Check(tag+" actual Populate reproduces watcher hair",reward.Cards.Single().GetType().Name=="WatcherHairCard");}
    else Check(tag+" exact candidate after actual generation",protectedGift?reward.Cards.Single().GetType()==cardType:actual==baselines[key]);
    if(protectedGift){
     for(int repeat=0;repeat<3;repeat++){
      var rngBefore=JsonSerializer.Serialize(owner.RunState.Rng.ToSerializable(),MegaCrit.Sts2.Core.Saves.JsonSerializationUtility.Options);reward.Populate();
      Check("finesse repeated population stays specified card "+name+secondPlayer+repeat,reward.Cards.Count()==1&&reward.Cards.Single().GetType()==cardType);
      Check("finesse protected gift never consumes fusion injection RNG "+name+secondPlayer+repeat,JsonSerializer.Serialize(owner.RunState.Rng.ToSerializable(),MegaCrit.Sts2.Core.Saves.JsonSerializationUtility.Options)==rngBefore);
      reward=(CardReward)Reward.FromSerializable(reward.ToSerializable(),owner);reward.Populate();
      Check("finesse native reward serialization restore remains exact "+name+secondPlayer+repeat,reward.Cards.Count()==1&&reward.Cards.Single().GetType()==cardType);
     }
    }
    Check(tag+" no card silently inserted into deck",owner.Deck.Cards.Count==deckBefore);
    int offersBeforeSave=blessingOffers.Count;var loaded=Player.FromSerializable(owner.ToSerializable());
    Check(tag+" saved relic restores without pickup reward",loaded.Relics.Any(r=>r.Id==relic.Id)&&blessingOffers.Count==offersBeforeSave);
    evidence.Add(new{Relic=name,Enabled=enabled,Owner=owner.NetId,RewardOffers=blessingOffers.Count,OfferedCards=blessingOffers.SelectMany(o=>o.Rewards.OfType<CardReward>().SelectMany(r=>r.Cards)).Select(c=>c.Id.ToString()).ToArray()});
   }
   CleanupScrollRoom();
   // 直接走真实 Populate，验证普通奖励与其他指定奖励继续保留融合扩展包注入。
   foreach(var source in new[]{CardCreationSource.Other,CardCreationSource.Encounter})foreach(bool ownsBlessing in new[]{false,true}){
    string? baseline=null;
    foreach(bool enabled in new[]{false,true}){
     Pack.Settings.Startup[option.Key]=enabled;var owner=FinesseRewardPlayer(seed);
     if(ownsBlessing)owner.AddRelicInternal(ModelDb.GetById<RelicModel>(ModelDb.GetId(Find("YuiExtra.Relics.BlessedFinesse"))).ToMutable(),silent:true);
     var options=new CardCreationOptions([owner.Character.CardPool],source,CardRarityOddsType.RegularEncounter);
     var card=owner.RunState.CreateCard<Anger>(owner);var normal=new CardReward([card],source,owner,options);normal.Populate();
     string actual=string.Join(",",normal.Cards.Select(c=>c.Id.ToString()));
     if(!enabled)baseline=actual;else Check("finesse ordinary reward injection retained "+source+ownsBlessing,actual==baseline&&normal.Cards.Single().GetType().Name=="WatcherHairCard");
    }
   }
   CleanupScrollRoom();
   foreach(bool enabled in new[]{false,true}){
    Pack.Settings.Startup[option.Key]=enabled;using var f=new BloodFixture(multiplayer:true);
    var relic=ModelDb.GetById<RelicModel>(ModelDb.GetId(Find("YuiExtra.Relics.BlessedFinesse"))).ToMutable();f.P.AddRelicInternal(relic,silent:true);
    var card=f.Card<Finesse>();int before=f.P.Creature.GetPower<DrawCardsNextTurnPower>()?.Amount??0;
    await f.Play(card);Check("finesse real play still grants next turn draw enabled="+enabled,(f.P.Creature.GetPower<DrawCardsNextTurnPower>()?.Amount??0)==before+1);
    await f.Play(f.Card<Finesse>());Check("finesse repeated play stacks exactly once enabled="+enabled,(f.P.Creature.GetPower<DrawCardsNextTurnPower>()?.Amount??0)==before+2);
    var otherCard=f.Card<Finesse>(f.Other);await f.Play(otherCard);
    Check("finesse other player never receives owner blessing enabled="+enabled,f.Other.Creature.GetPower<DrawCardsNextTurnPower>()==null&&(f.P.Creature.GetPower<DrawCardsNextTurnPower>()?.Amount??0)==before+2);
    await f.Play(f.Card<DefendIronclad>());Check("finesse unrelated play never adds next turn draw enabled="+enabled,(f.P.Creature.GetPower<DrawCardsNextTurnPower>()?.Amount??0)==before+2);
   }
  }finally{CleanupScrollRoom();Pack.Settings.Startup[option.Key]=active;harmony.UnpatchAll(harmony.Id);blessingOffers.Clear();TestMode.IsOn=oldTest;listeners.SetValue(CombatManager.Instance.StateTracker,frontend);}
  File.WriteAllText(Output+"/blessed-finesse-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
 }
}
