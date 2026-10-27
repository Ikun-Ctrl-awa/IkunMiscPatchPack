using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static async Task FakeDustyTomeChecks(){
  int first=passes.Count;var evidence=new List<object>();
  var option=Pack.Options.All.Single(o=>o.Key==Pack.FakeDustyTomeFix.Key);
  Check("fake dusty tome independent option defaults on",new Pack.PackConfig().Get(option.Key)&&option.Required.SequenceEqual(new[]{"Fake Merchant Expansion"}));
  if(Minimal){
   Check("fake dusty tome absent mod safely skipped",!Pack.Options.Present("Fake Merchant Expansion")&&Pack.Settings.Status(option).Contains("未装载相关模组"));
  }else{
   Check("fake dusty tome patch installed",Pack.RuntimeLoader.States.GetValueOrDefault(option.Key)=="已应用。");
   var relicType=Find("FakeMerchantExpansion.FakeDustyTome");var selected=AccessTools.Property(relicType,"AncientCard");
   CardModel[] pool=[ModelDb.Card<Caltrops>(),ModelDb.Card<Clash>(),ModelDb.Card<Distraction>(),ModelDb.Card<DualWield>(),ModelDb.Card<Entrench>(),ModelDb.Card<HelloWorld>(),ModelDb.Card<Outmaneuver>(),ModelDb.Card<Rebound>(),ModelDb.Card<RipAndTear>(),ModelDb.Card<Stack>()];
   bool oldTest=TestMode.IsOn,active=Pack.Settings.Startup[option.Key];ulong? oldLocal=LocalContext.NetId;
   var listeners=AccessTools.Field(typeof(CombatStateTracker),"CombatStateChanged");var frontend=listeners.GetValue(CombatManager.Instance.StateTracker);
   try{
    TestMode.IsOn=true;listeners.SetValue(CombatManager.Instance.StateTracker,null);
    foreach(bool enabled in new[]{false,true})foreach(var canonical in pool){
     Pack.Settings.Startup[option.Key]=enabled;
     var p=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var other=Player.CreateForNewRun<Defect>(UnlockState.all,2);
     var state=RunState.CreateForTest([p,other],seed:"DUSTY-TOME-"+canonical.Id.Entry);RunManager.Instance.SetUpTest(state,new NetSingleplayerGameService());LocalContext.NetId=p.NetId;
     var owner=Array.IndexOf(pool,canonical)%2==0?p:other;var spectator=ReferenceEquals(owner,p)?other:p;
     var relic=ModelDb.GetById<RelicModel>(ModelDb.GetId(relicType)).ToMutable();
     selected.SetValue(relic,canonical.Id);
     var preview=relic.HoverTips.OfType<CardHoverTip>().Single().Card;
     string label=canonical.Id+" enabled="+enabled;
     Check("fake dusty tome preview upgrade "+label,preview.Id==canonical.Id&&preview.CurrentUpgradeLevel==(enabled?1:0));
     string description=relic.DynamicDescription.GetFormattedText();
     Check("fake dusty tome description promises upgraded selected card "+label,description.Contains(canonical.Title)&&description.Contains("+"));
     var existing=state.CreateCard(ModelDb.Card<StrikeIronclad>(),owner);await CardPileCmd.Add(existing,PileType.Deck,skipVisuals:true);
     int before=owner.Deck.Cards.Count,otherBefore=spectator.Deck.Cards.Count;
     // 真实拾取命令负责设置主人、登记遗物，再调用作者原有 AfterObtained。
     await RelicCmd.Obtain(relic,owner).WaitAsync(TimeSpan.FromSeconds(8));
     var granted=owner.Deck.Cards.Last();
     Check("fake dusty tome obtains exactly one card for owner "+label,owner.Deck.Cards.Count==before+1&&spectator.Deck.Cards.Count==otherBefore&&ReferenceEquals(granted.Owner,owner));
     Check("fake dusty tome keeps selected identity and upgrade "+label,granted.Id==canonical.Id&&granted.CurrentUpgradeLevel==(enabled?1:0)&&Equals(selected.GetValue(relic),canonical.Id));
     Check("fake dusty tome does not upgrade prior deck card or canonical "+label,existing.CurrentUpgradeLevel==0&&canonical.CurrentUpgradeLevel==0);
     Check("fake dusty tome effect and preview agree "+label,granted.GetDescriptionForPile(PileType.None)==preview.GetDescriptionForPile(PileType.None));
     var relicJson=JsonSerializer.Serialize(relic.ToSerializable(),JsonSerializationUtility.Options);
     var loadedRelic=RelicModel.FromSerializable(JsonSerializer.Deserialize<SerializableRelic>(relicJson,JsonSerializationUtility.Options)!);
     var loadOwner=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);RunState.CreateForTest([loadOwner],seed:"DUSTY-TOME-SAVE");loadOwner.AddRelicInternal(loadedRelic,silent:true);
     Check("fake dusty tome saved card and relic preview agree "+label,Restore(granted).CurrentUpgradeLevel==(enabled?1:0)&&Equals(selected.GetValue(loadedRelic),canonical.Id)&&loadedRelic.HoverTips.OfType<CardHoverTip>().Single().Card.CurrentUpgradeLevel==(enabled?1:0));
     evidence.Add(new{Enabled=enabled,Card=canonical.Id.ToString(),Owner=owner.NetId,Description=description,Upgrade=granted.CurrentUpgradeLevel,Actual=granted.GetDescriptionForPile(PileType.None)});
     RunManager.Instance.CleanUp();
    }
    Pack.Settings.Startup[option.Key]=true;
    var player=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var run=RunState.CreateForTest([player],seed:"DUSTY-TOME-RELATED");RunManager.Instance.SetUpTest(run,new NetSingleplayerGameService());LocalContext.NetId=player.NetId;
    var fresh=ModelDb.GetById<RelicModel>(ModelDb.GetId(relicType)).ToMutable();AccessTools.Method(relicType,"SetupForPlayer").Invoke(fresh,[player]);var chosen=(ModelId)selected.GetValue(fresh)!;
    Check("fake dusty tome retains original ten-card selection pool",pool.Any(c=>c.Id==chosen));
    await RelicCmd.Obtain(fresh,player).WaitAsync(TimeSpan.FromSeconds(8));
    Check("fake dusty tome setup and pickup keep same selection",player.Deck.Cards.Last().Id==chosen&&player.Deck.Cards.Last().CurrentUpgradeLevel==1);
    var unrelated=run.CreateCard(ModelDb.Card<Clash>(),player);Check("fake dusty tome does not upgrade unrelated created cards",unrelated.CurrentUpgradeLevel==0);
    var vanilla=(DustyTome)ModelDb.Relic<DustyTome>().ToMutable();vanilla.AncientCard=ModelDb.Card<Clash>().Id;
    await RelicCmd.Obtain(vanilla,player).WaitAsync(TimeSpan.FromSeconds(8));
    Check("fake dusty tome leaves vanilla tome at one upgrade",player.Deck.Cards.Last().Id==ModelDb.Card<Clash>().Id&&player.Deck.Cards.Last().CurrentUpgradeLevel==1);
   }finally{
    Pack.Settings.Startup[option.Key]=active;LocalContext.NetId=oldLocal;
    try{if(RunManager.Instance.DebugOnlyGetState()!=null)RunManager.Instance.CleanUp();}
    finally{TestMode.IsOn=oldTest;listeners.SetValue(CombatManager.Instance.StateTracker,frontend);}
   }
  }
  File.WriteAllText(Output+"/fake-dusty-tome-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Failed=0,Checks=passes.Skip(first),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
 }
}
