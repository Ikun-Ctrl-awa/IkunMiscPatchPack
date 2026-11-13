using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static CombatRoom? quEnteredRoom;
 static int quTransitions;
 static NCombatRoom? quScreenCombat;
 static NEventRoom? quScreenEvent;
 static void QuScreenCombatCreated(NCombatRoom? __result)=>quScreenCombat=__result;
 static bool QuScreenCombatInstance(ref NCombatRoom? __result){if(quScreenCombat==null)return true;__result=quScreenCombat;return false;}
 static bool QuScreenEventInstance(ref NEventRoom? __result){if(quScreenEvent==null)return true;__result=quScreenEvent;return false;}
 static bool CaptureQuCombat(AbstractRoom __0,ref Task __result){
  if(__0 is not CombatRoom room||room.ParentEventId!=ModelDb.GetId<Pack.QuChallengeEvent>())return true;
  quEnteredRoom=room;quTransitions++;__result=Task.CompletedTask;return false;
 }
 static EventSlSession QuSession(int players=1,string seed="QU-EVENT"){
  Player[] group=players==1?[Player.CreateForNewRun<Ironclad>(UnlockState.all,1)]:[Player.CreateForNewRun<Ironclad>(UnlockState.all,1),Player.CreateForNewRun<Defect>(UnlockState.all,2)];
  var state=RunState.CreateForTest(group,seed:seed);
  foreach(var act in state.Acts)act.GenerateRooms(state.Rng.UpFront,state.UnlockState);
  state.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Unknown,RoomType.Event,ModelDb.GetId<Pack.QuChallengeEvent>());
  return new EventSlSession(state);
 }
 static async Task<Pack.QuChallengeEvent[]> QuEvents(EventSlSession f){
  var canonical=ModelDb.Event<Pack.QuChallengeEvent>();f.State.PushRoom(new EventRoom(canonical));
  var sync=new EventCombatSynchronizer(f.State,f.State);sync.InitializeForEvent(canonical);
  var models=new List<Pack.QuChallengeEvent>();
  foreach(var p in f.State.Players){var e=(Pack.QuChallengeEvent)canonical.ToMutable();await e.BeginEvent(p,sync,false);models.Add(e);}
  return models.ToArray();
 }
 static async Task QuEventChecks(){
  int first=passes.Count;var evidence=new List<object>();var canonical=ModelDb.Event<Pack.QuChallengeEvent>();
  Check("qu event independent default-on miscellaneous",Pack.Options.All.Single(o=>o.Key==Pack.QuChallengeContent.Key) is {Misc:true,Required.Length:0}&&new Pack.PackConfig().Get(Pack.QuChallengeContent.Key));
  Check("qu event exactly registered and native combat preview",ModelDb.AllSharedEvents.Count(e=>e is Pack.QuChallengeEvent)==1&&canonical.IsShared&&canonical.LayoutType==EventLayoutType.Combat&&canonical.CanonicalEncounter is PhrogParasiteElite);
  Check("qu event exact title description and native Chinese monster",canonical.Title.GetFormattedText()=="！？区区？！"&&canonical.InitialDescription.GetFormattedText()=="你惊扰了区！！！"&&ModelDb.Monster<PhrogParasite>().Title.GetFormattedText()=="异蛙寄生虫");
  using(var f=QuSession()){
   foreach(var act in ModelDb.Acts.Where(a=>a.IsDefault)){
    var mutable=(ActModel)act.ToMutable();mutable.GenerateRooms(new Rng(510),UnlockState.all);
    var rooms=(RoomSet)AccessTools.Field(typeof(ActModel),"_rooms").GetValue(mutable)!;
    Check("qu event available in native unknown room catalog "+act.Id,rooms.events.Any(e=>e is Pack.QuChallengeEvent));
   }
   for(int index=0;index<f.State.Acts.Count;index++){f.State.CurrentActIndex=index;Check("qu event only act one index="+index,canonical.IsAllowed(f.State)==(index==0));}
   f.State.CurrentActIndex=0;bool on=Pack.Settings.Startup[Pack.QuChallengeContent.Key];
   try{Pack.Settings.Startup[Pack.QuChallengeContent.Key]=false;Check("qu event off prevents future draw",!canonical.IsAllowed(f.State));}
   finally{Pack.Settings.Startup[Pack.QuChallengeContent.Key]=on;}
  }
  var capture=new Harmony("Aikun020Probe.QuEventTransition");
  capture.Patch(AccessTools.Method(typeof(RunManager),nameof(RunManager.EnterRoomWithoutExitingCurrentRoom)),prefix:new HarmonyMethod(typeof(Entry),nameof(CaptureQuCombat)));
  try{
   foreach(int count in new[]{1,2})foreach(int maximum in new[]{100,95}){
    using var f=QuSession(count);var models=await QuEvents(f);quEnteredRoom=null;quTransitions=0;
    foreach(var p in f.State.Players){p.Creature.SetMaxHpInternal(maximum);p.Creature.SetCurrentHpInternal(10);}
    var options=models.Select(e=>e.CurrentOptions.ToArray()).ToArray();
    Check($"qu exact two unlocked options players={count} max={maximum}",models.All(e=>e.CurrentOptions.Count==2&&e.CurrentOptions.Select(o=>o.Title.GetFormattedText()).SequenceEqual(new[]{"迎战！","搜、撤，不打。"})&&e.CurrentOptions.All(o=>!o.IsLocked)));
    for(int i=0;i<count;i++){
     await options[i][0].Chosen();
     Check($"qu heal before combat player={i} count={count} max={maximum}",models[i].Owner!.Creature.CurrentHp==10+(int)(maximum*0.30m));
     Check($"qu shared transition waits for each player {i}/{count}",quTransitions==(i==count-1?1:0));
     await options[i][0].Chosen();await options[i][1].Chosen();
     Check($"qu old buttons cannot heal or retreat twice {i}/{count}",models[i].Owner!.Creature.CurrentHp==10+(int)(maximum*0.30m)&&!models[i].Owner!.Deck.Cards.OfType<Pack.QuCurse>().Any());
    }
    var room=quEnteredRoom??throw new Exception("事件没有产生战斗房间。");
    Check($"qu fixed native elite room players={count} max={maximum}",room.Encounter is PhrogParasiteElite&&room.RoomType==RoomType.Elite&&!room.ShouldResumeParentEventAfterCombat&&room.ParentEventId==canonical.Id&&room.ExtraRewards.Count==0);
    Check($"qu native encounter spawns parasite alone initially {count}/{maximum}",room.Enemies.Count()==1&&room.Enemies.Single().Monster is PhrogParasite&&room.CombatState.Players.Count==count&&room.Encounter.AllPossibleMonsters.Any(m=>m is Wriggler));
    Check($"qu native monster hp not replaced by event {count}/{maximum}",room.Enemies.Single().MaxHp>0&&room.Enemies.Single().CurrentHp==room.Enemies.Single().MaxHp);
    evidence.Add(new{Players=count,MaxHp=maximum,Hp=f.State.Players.Select(p=>p.Creature.CurrentHp),Encounter=room.Encounter.Id.ToString(),room.ShouldResumeParentEventAfterCombat});
    if(maximum==100){
     foreach(var p in f.State.Players){
      var raw=new RewardsSet(p).WithRewardsFromRoom(room);
      Check("qu native elite reward recipe before mod hooks player="+p.NetId+" count="+count,raw.Rewards.OfType<GoldReward>().Count()==1&&raw.Rewards.OfType<RelicReward>().Count()==1&&raw.Rewards.OfType<CardReward>().Count()==1&&raw.Rewards.OfType<PotionReward>().Count()<=1);
      var rewards=await RewardsCmd.GenerateForRoomEnd(p,room);
      bool IsRelicReward(Reward r)=>r is RelicReward||r.GetType().FullName=="RelicRewardChoices.Rewards.RelicRewardChoiceReward";
      // 完整环境原作者会将一份遗物奖励改为遗物三选一；这是精英奖励的正常修改。
      Check("qu real native room-end reward structure player="+p.NetId+" count="+count,rewards.Rewards.OfType<GoldReward>().Count()==1&&rewards.Rewards.Count(IsRelicReward)==1&&rewards.Rewards.OfType<CardReward>().Count()==1&&rewards.Rewards.OfType<PotionReward>().Count()<=1);
      Check("qu native third-party relic choice modification preserved player="+p.NetId+" count="+count,Minimal?rewards.Rewards.OfType<RelicReward>().Count()==1:rewards.Rewards.Count(r=>r.GetType().FullName=="RelicRewardChoices.Rewards.RelicRewardChoiceReward")==1);
      Check("qu populated elite card reward owner player="+p.NetId+" count="+count,rewards.Rewards.All(r=>r.Player==p&&r.IsPopulated)&&rewards.Rewards.OfType<CardReward>().Single().Cards.Any());
      evidence.Add(new{Players=count,Player=p.NetId,Rewards=rewards.Rewards.Select(r=>r.GetType().Name)});
     }
    }
   }
   using(var f=QuSession()){
    f.P.Creature.SetMaxHpInternal(100);f.P.Creature.SetCurrentHpInternal(95);var models=await QuEvents(f);await models[0].CurrentOptions[0].Chosen();
    Check("qu heal is capped at max hp",f.P.Creature.CurrentHp==100);
    var room=quEnteredRoom!;bool potion=false,noPotion=false;
    for(int i=0;i<24;i++){var rewards=new RewardsSet(f.P).WithRewardsFromRoom(room);bool has=rewards.Rewards.OfType<PotionReward>().Any();potion|=has;noPotion|=!has;}
    Check("qu potion remains probabilistic with both native outcomes",potion&&noPotion);
   }
   foreach(int count in new[]{1,2})foreach(bool curseOn in new[]{false,true}){
    using var f=QuSession(count);var models=await QuEvents(f);quTransitions=0;bool was=Pack.Settings.Startup[Pack.QuCurseContent.Key];
    try{
     Pack.Settings.Startup[Pack.QuCurseContent.Key]=curseOn;
     for(int i=0;i<count;i++){
      var owner=f.State.Players[i];var before=owner.Relics.Count;int cards=owner.Deck.Cards.Count,hp=owner.Creature.CurrentHp;
      var buttons=models[i].CurrentOptions.ToArray();TestRngInjector.SetRelicOverride<Vajra>();await buttons[1].Chosen();
      Check($"qu retreat grants native random factory relic and exact curse {count}/{i}/on={curseOn}",owner.Relics.Count==before+1&&owner.Relics.OfType<Vajra>().Count()==1&&owner.Deck.Cards.Count==cards+1&&owner.Deck.Cards.OfType<Pack.QuCurse>().Count()==1&&owner.Creature.CurrentHp==hp);
      Check($"qu retreat ends event and no combat {count}/{i}/on={curseOn}",models[i].IsFinished&&models[i].CurrentOptions.Count==0&&quTransitions==0);
      await buttons[1].Chosen();await buttons[0].Chosen();
      Check($"qu retreat repeated buttons do not duplicate {count}/{i}/on={curseOn}",owner.Relics.Count==before+1&&owner.Deck.Cards.Count==cards+1&&owner.Creature.CurrentHp==hp&&quTransitions==0);
      var card=owner.Deck.Cards.OfType<Pack.QuCurse>().Single();for(int cycle=0;cycle<3;cycle++)card=(Pack.QuCurse)Restore(card);
      Check($"qu event curse restores native persistent model {count}/{i}/on={curseOn}",card.CurrentUpgradeLevel==0&&card.HasTurnEndInHandEffect&&card.DynamicVars.Damage.IntValue==3);
      if(count==2&&i==0)Check("qu retreat does not reward the other player early",!f.State.Players[1].Relics.OfType<Vajra>().Any()&&!f.State.Players[1].Deck.Cards.OfType<Pack.QuCurse>().Any());
     }
    }finally{Pack.Settings.Startup[Pack.QuCurseContent.Key]=was;TestRngInjector.Cleanup();}
   }
  }finally{capture.UnpatchAll(capture.Id);quEnteredRoom=null;}
  // 独立SubViewport没有NRun容器；将原生Instance查找绑定到实际挂载的事件/战斗节点。
  // 不跳过原生建怪、导航、预载或按钮创建，因此画面失败不能以仅有模型断言放行。
  var screenBinding=new Harmony("Aikun020Probe.QuEventScreenBinding");
  screenBinding.Patch(AccessTools.Method(typeof(NCombatRoom),nameof(NCombatRoom.Create)),postfix:new HarmonyMethod(typeof(Entry),nameof(QuScreenCombatCreated)));
  screenBinding.Patch(AccessTools.PropertyGetter(typeof(NCombatRoom),nameof(NCombatRoom.Instance)),prefix:new HarmonyMethod(typeof(Entry),nameof(QuScreenCombatInstance)));
  screenBinding.Patch(AccessTools.PropertyGetter(typeof(NEventRoom),nameof(NEventRoom.Instance)),prefix:new HarmonyMethod(typeof(Entry),nameof(QuScreenEventInstance)));
  try{await QuEventScreenAndSave();}finally{screenBinding.UnpatchAll(screenBinding.Id);quScreenCombat=null;quScreenEvent=null;}
  File.WriteAllText(Output+"/qu-event-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Failed=0,Checks=passes.Skip(first),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
 }
 static async Task QuEventScreenAndSave(){
  var canonical=ModelDb.Event<Pack.QuChallengeEvent>();string checkpoint;
  using(var f=QuSession()){
   f.P.Creature.SetMaxHpInternal(100);f.P.Creature.SetCurrentHpInternal(35);f.State.PushRoom(new EventRoom(canonical));
   await SaveManager.Instance.SaveRun(null,false);var saved=SaveManager.Instance.LoadRunSave();
   Check("qu event real entry checkpoint saved",saved.Success&&saved.SaveData!=null);checkpoint=JsonSerializer.Serialize(saved.SaveData,JsonSerializationUtility.Options);
  }
  for(int cycle=0;cycle<3;cycle++){
   var saved=JsonSerializer.Deserialize<SerializableRun>(checkpoint,JsonSerializationUtility.Options)!;
   using var f=new EventSlSession(RunState.FromSerializable(saved),setup:false);await RunManager.Instance.SetUpSavedSingleplayer(f.State,saved);
   var sync=RunManager.Instance.EventSynchronizer;sync.BeginEvent(canonical);var model=sync.GetLocalEvent();sync.GenerateInternalCombatStateIfNecessary(model);
   Check("qu actual entry readback restores unchosen event cycle="+cycle,model is Pack.QuChallengeEvent&&model.CurrentOptions.Count==2&&!model.IsFinished&&f.P.Creature.CurrentHp==35);
   bool old=TestMode.IsOn;var viewport=new SubViewport{Size=new(2560,1440),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);
   try{
    TestMode.IsOn=false;await PreloadManager.LoadRunAssets(f.State.Players.Select(p=>p.Character));await PreloadManager.LoadActAssets(f.State.Act);await PreloadManager.LoadRoomEventAssets(model,f.State);
    var node=NEventRoom.Create(model,f.State,false)!;quScreenEvent=node;host.AddChild(node);node.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);await Wait(2.2);
    Check("qu native event screen two visible choices cycle="+cycle,node.Layout!=null&&node.Layout.OptionButtons.Count()==2&&node.Layout.OptionButtons.All(b=>b.IsVisibleInTree()));
    Check("qu parasite monster actually rendered cycle="+cycle,node.EmbeddedCombatRoom!=null&&node.EmbeddedCombatRoom.CreatureNodes.Count()==2&&node.EmbeddedCombatRoom.CreatureNodes.Any(n=>n.Entity.Monster is PhrogParasite&&n.IsVisibleInTree()));
    if(cycle==0){await AfterDraw();Check("qu native event screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/qu-event-initial.png")==Error.Ok);}
    TestMode.IsOn=true;TestRngInjector.SetRelicOverride<Vajra>();sync.ChooseLocalOption(1);await sync.AwaitPendingOptionTasks();await Wait(.15);
    Check("qu readback retreat shows native proceed cycle="+cycle,model.IsFinished&&node.Layout!.OptionButtons.Count()==1&&f.P.Deck.Cards.OfType<Pack.QuCurse>().Count()==1&&f.P.Relics.OfType<Vajra>().Count()==1&&f.P.Creature.CurrentHp==35);
    if(cycle==0){TestMode.IsOn=false;await AfterDraw();viewport.GetTexture().GetImage().SavePng(Output+"/qu-event-retreat.png");}
   }finally{TestMode.IsOn=old;TestRngInjector.Cleanup();viewport.QueueFree();await Wait(.1);sync.BeforeExitingRoom();}
  }
 }
}
