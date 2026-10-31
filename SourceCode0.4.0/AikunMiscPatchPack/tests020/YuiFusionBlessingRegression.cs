using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static bool HasCardBlessingBase(Type type){
  for(Type? t=type;t!=null;t=t.BaseType)if(t.IsGenericType&&t.GetGenericTypeDefinition().FullName=="YuiExtra.Relics.CardBlessingRelic`1")return true;
  return false;
 }
 static async Task YuiFusionBlessingChecks(){
  int first=passes.Count;var evidence=new List<object>();
  var option=Pack.Options.All.Single(o=>o.Key==Pack.YuiFusionBlessingRewardFix.Key);
  Check("blessing fusion compatibility keeps legacy switch",option.Title.Contains("×")&&new Pack.PackConfig{Enabled=new(){["yui_finesse_reward"]=false}}.Get(option.Key)==false);
  if(Minimal){Check("blessing fusion missing dependencies safely skip",!Pack.RuntimeLoader.States.ContainsKey(option.Key)&&Pack.Settings.Status(option).Contains("未装载相关模组"));return;}
  Check("blessing fusion compatibility installed",Pack.RuntimeLoader.States.GetValueOrDefault(option.Key)=="已应用。");
  var types=Find("YuiExtra.Relics.CardBlessingRegistry").Assembly.GetTypes().Where(t=>!t.IsAbstract&&typeof(RelicModel).IsAssignableFrom(t)&&HasCardBlessingBase(t)).OrderBy(t=>t.FullName).ToArray();
  Check("blessing fusion concrete reward catalog covers current 56",types.Length==56);
  var harmony=new Harmony("Aikun020Probe.AllBlessingRewards");bool oldTest=TestMode.IsOn,oldActive=Pack.Settings.Active(option.Key);var oldLocal=LocalContext.NetId;
  var listeners=AccessTools.Field(typeof(CombatStateTracker),"CombatStateChanged");var frontend=listeners.GetValue(CombatManager.Instance.StateTracker);
  harmony.Patch(AccessTools.Method(typeof(RewardsCmd),nameof(RewardsCmd.OfferCustom),[typeof(MegaCrit.Sts2.Core.Entities.Players.Player),typeof(List<Reward>)]),prefix:new HarmonyMethod(typeof(Entry),nameof(CaptureBlessingOffer)));
  try{
   listeners.SetValue(CombatManager.Instance.StateTracker,null);TestMode.IsOn=true;LocalContext.NetId=1;
   string seed=HairReplacementSeed();
   foreach(bool enabled in new[]{false,true})foreach(var type in types)foreach(bool second in new[]{false,true}){
    Pack.Settings.Startup[option.Key]=enabled;var owner=FinesseRewardPlayer(seed,second);
    var relic=ModelDb.GetById<RelicModel>(ModelDb.GetId(type)).ToMutable();int deck=owner.Deck.Cards.Count;
    blessingOffers.Clear();blessingOriginalOffers.Clear();
    await RelicCmd.Obtain(relic,owner).WaitAsync(TimeSpan.FromSeconds(8));
    string tag=$"all blessings {type.Name} active={enabled} second={second}";
    Check(tag+" relic and reward still granted",owner.Relics.Contains(relic)&&blessingOffers.Count==1&&blessingOffers[0].Rewards.Length==1);
    var reward=(CardReward)blessingOffers[0].Rewards.Single();var original=blessingOriginalOffers.Single();var actual=reward.Cards.Select(c=>c.Id).ToArray();
    Check(tag+" same reward count and owner",reward.Player==owner&&actual.Length==original.Length&&actual.Length>0);
    Check(tag+(enabled?" preserves author candidates":" reproduces fusion replacement"),enabled?actual.SequenceEqual(original):reward.Cards.First().GetType().Name=="WatcherHairCard");
    Check(tag+" no silent deck insert",owner.Deck.Cards.Count==deck);
    evidence.Add(new{Relic=type.FullName,Enabled=enabled,Owner=owner.NetId,Seed=seed,Before=original.Select(x=>x.ToString()).ToArray(),After=actual.Select(x=>x.ToString()).ToArray()});
   }
   // 两种基础牌赐福直接发牌，不应被改成奖励界面，也不应被取消。
   foreach(bool enabled in new[]{false,true})foreach(string name in new[]{"BlessedStrike","BlessedDefend"}){
    Pack.Settings.Startup[option.Key]=enabled;var owner=FinesseRewardPlayer(seed);var relic=ModelDb.GetById<RelicModel>(ModelDb.GetId(Find("YuiExtra.Relics."+name))).ToMutable();
    // 新测试局不会自动 Launch；显式设定本地玩家后才会进入原作者的本地发牌分支。
    LocalContext.NetId=owner.NetId;var before=owner.Deck.Cards.ToArray();blessingOffers.Clear();await RelicCmd.Obtain(relic,owner).WaitAsync(TimeSpan.FromSeconds(8));
    var added=owner.Deck.Cards.Except(before).ToArray();
    Check("basic blessing direct add preserved "+name+enabled,blessingOffers.Count==0&&owner.Deck.Cards.Count==before.Length+1&&added.Length==1&&added[0].Rarity==CardRarity.Basic&&added[0].Owner==owner);
   }
   // 覆盖原生主动重投：已经重投的随机候选继续由扩展包处理。
   string? rerollBaseline=null;
   foreach(bool enabled in new[]{false,true}){
    Pack.Settings.Startup[option.Key]=enabled;var owner=FinesseRewardPlayer(seed);owner.AddRelicInternal(ModelDb.GetById<RelicModel>(ModelDb.GetId(Find("YuiExtra.Relics.BlessedFinesse"))).ToMutable(),silent:true);
    var options=CardCreationOptions.ForNonCombatWithUniformOdds([ModelDb.CardPool<ColorlessCardPool>()],c=>c is Finesse).WithFlags(CardCreationFlags.NoCardPoolModifications|CardCreationFlags.NoCardModelModifications);
    var reward=new CardReward([owner.RunState.CreateCard<Finesse>(owner)],CardCreationSource.Other,owner,options);reward.Reroll();
    string actual=string.Join(",",reward.Cards.Select(c=>c.Id.ToString()));
    if(!enabled)rerollBaseline=actual;else Check("blessing fusion active reroll retains original fusion injection",actual==rerollBaseline&&reward.Cards.Single().GetType().Name=="WatcherHairCard");
   }
  }finally{
   CleanupScrollRoom();Pack.Settings.Startup[option.Key]=oldActive;harmony.UnpatchAll(harmony.Id);blessingOffers.Clear();blessingOriginalOffers.Clear();TestMode.IsOn=oldTest;LocalContext.NetId=oldLocal;listeners.SetValue(CombatManager.Instance.StateTracker,frontend);
   File.WriteAllText(Output+"/yui-fusion-blessing-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Catalog=types.Select(t=>t.FullName).ToArray(),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
  }
 }
}
