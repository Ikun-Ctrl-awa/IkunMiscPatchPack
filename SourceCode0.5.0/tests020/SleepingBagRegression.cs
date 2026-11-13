using System.Collections;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static void ObserveBagProceed()=>File.AppendAllText(Output+"/sleeping-bag-trace.txt","native proceed callback hit\n");
 static Pack.PortableSleepingBag Bag(Player p,int charges=1){var bag=(Pack.PortableSleepingBag)ModelDb.Relic<Pack.PortableSleepingBag>().ToMutable();bag.Charges=charges;p.AddRelicInternal(bag,silent:true);return bag;}
 static async Task ChooseBagOption(Player p,Func<RestSiteOption,bool> predicate){
  var sync=RunManager.Instance.RestSiteSynchronizer;int index=sync.GetOptionsForPlayer(p).ToList().FindIndex(o=>predicate(o));
  if(index<0)throw new Exception("测试没有找到休息选项："+p.NetId);
  await ((Task<bool>)AccessTools.Method(sync.GetType(),"ChooseOption").Invoke(sync,[p,index])!).WaitAsync(TimeSpan.FromSeconds(12));
 }
 static async Task SleepingBagChecks(){
  int first=passes.Count;var canonical=ModelDb.Relic<Pack.PortableSleepingBag>();
  Check("bag independent default on miscellaneous",new Pack.PackConfig().Get(Pack.SleepingBagContent.Key)&&Pack.Options.All.Single(o=>o.Key==Pack.SleepingBagContent.Key) is {Misc:true,Required.Length:0});
  Check("bag shop rarity native shared pool counter and assets",canonical.Rarity==RelicRarity.Shop&&canonical.Pool is SharedRelicPool&&((Pack.PortableSleepingBag)canonical.ToMutable()).Charges==1&&canonical.Icon!=null&&canonical.BigIcon!=null);
  var json=new JsonSerializerOptions{IncludeFields=true};
  foreach(int count in new[]{0,1,4}){
   var saved=(Pack.PortableSleepingBag)canonical.ToMutable();saved.Charges=count;
   for(int i=0;i<3;i++){saved=(Pack.PortableSleepingBag)RelicModel.FromSerializable(JsonSerializer.Deserialize<SerializableRelic>(JsonSerializer.Serialize(saved.ToSerializable(),json),json)!);Check($"bag charges DTO restore {count}/{i}",saved.Charges==count&&saved.ShowCounter&&saved.DisplayAmount==count);}
  }
  bool enabled=Pack.Settings.Active(Pack.SleepingBagContent.Key);
  try{
   Pack.Settings.Startup[Pack.SleepingBagContent.Key]=false;
   using(var f=new BloodFixture()){var bag=Bag(f.P);Check("bag switch hides new acquisition only",!canonical.IsAllowed(f.Run)&&RestSiteOption.Generate(f.P).OfType<Pack.ArrangeSleepingBagOption>().Count()==1);}
  }finally{Pack.Settings.Startup[Pack.SleepingBagContent.Key]=enabled;}
  using(var f=new BloodFixture(multiplayer:true)){
   var bag=Bag(f.P);var foreign=Bag(f.Other,5);
   Check("bag unavailable in live battle",!Pack.SleepingBagContent.CanUse(bag));
   var options=RestSiteOption.Generate(f.P);var arrange=options.OfType<Pack.ArrangeSleepingBagOption>().Single();
   Check("bag arrange once adds one own charge",await arrange.OnSelect()&&bag.Charges==2&&foreign.Charges==5&&!await arrange.OnSelect());
   var unowned=new Pack.ArrangeSleepingBagOption(f.Other,bag);Check("bag arrange rejects foreign owner",!await unowned.OnSelect());
  }
  foreach(bool parentCompleted in new[]{false,true}){
   using var f=new BloodFixture();CombatManager.Instance.Reset(false);var bag=Bag(f.P,2);var parent=new RestSiteRoom();f.Run.PushRoom(parent);
   var sync=RunManager.Instance.RestSiteSynchronizer;sync.BeginRestSite();var oldSite=((IList)AccessTools.Field(sync.GetType(),"_restSites").GetValue(sync)!).Cast<object>().Single();
   if(parentCompleted)await ChooseBagOption(f.P,o=>o is Pack.ArrangeSleepingBagOption);
   var originalOptions=sync.GetLocalOptions().ToArray();int charges=bag.Charges,roomCount=f.Run.CurrentRoomCount,deck=f.P.Deck.Cards.Count,hp=f.P.Creature.CurrentHp;f.P.Creature.SetCurrentHpInternal(30);
   Check("bag enter extra native rest from parent completed="+parentCompleted,await Pack.SleepingBagContent.Open(bag)&&f.Run.CurrentRoom is RestSiteRoom&&f.Run.CurrentRoom!=parent&&f.Run.CurrentRoomCount==roomCount+1&&bag.Charges==charges-1);
   Check("bag extra rest has no arrange option completed="+parentCompleted,!sync.GetLocalOptions().OfType<Pack.ArrangeSleepingBagOption>().Any());
   var bypass=new Pack.ArrangeSleepingBagOption(f.P,bag);Check("bag extra rest rejects direct arrange completed="+parentCompleted,!bypass.IsEnabled&&!await bypass.OnSelect()&&bag.Charges==charges-1);
   Check("bag disallows nested entry",!Pack.SleepingBagContent.CanUse(bag)&&!await Pack.SleepingBagContent.Open(bag));
   await ChooseBagOption(f.P,o=>o is HealRestSiteOption);Check("bag native rest heals",f.P.Creature.CurrentHp==60);
   await Pack.SleepingBagContent.Close(f.P);
   Check("bag restores exact parent synchronizer and options completed="+parentCompleted,f.Run.CurrentRoom==parent&&f.Run.CurrentRoomCount==roomCount&&((IList)AccessTools.Field(sync.GetType(),"_restSites").GetValue(sync)!)[0]==oldSite&&sync.GetLocalOptions().SequenceEqual(originalOptions)&&f.P.Deck.Cards.Count==deck&&bag.Charges==charges-1);
  }
  using(var f=new BloodFixture()){
   CombatManager.Instance.Reset(false);var bag=Bag(f.P);var bank=(MawBank)ModelDb.Relic<MawBank>().ToMutable();f.P.AddRelicInternal(bank,silent:true);
   f.Run.PushRoom(new MerchantRoom());var stale=new Pack.ArrangeSleepingBagOption(f.P,bag);await Pack.SleepingBagContent.Open(bag);await bank.AfterItemPurchased(f.P,null!,1);
   Check("bag extra rest rejects retained normal arrange",!stale.IsEnabled&&!await stale.OnSelect()&&bag.Charges==0);
   await ChooseBagOption(f.P,o=>o is HealRestSiteOption);await Pack.SleepingBagContent.Close(f.P);
   Check("bag extra room cannot refund spent use",bag.Charges==0&&!Pack.SleepingBagContent.IsActive&&f.Run.CurrentRoom is MerchantRoom);
   Check("bag normal arrange works again after return",stale.IsEnabled&&await stale.OnSelect()&&bag.Charges==1);
   Check("bag keeps permanent relic changes made in extra room",bank.HasItemBeenBought&&bank.Status==RelicStatus.Disabled);
   bag.Charges=0;Check("bag zero charges does not open",!await Pack.SleepingBagContent.Open(bag)&&f.Run.CurrentRoomCount==1);
  }
  using(var f=new BloodFixture(multiplayer:true)){
   CombatManager.Instance.Reset(false);var bag=Bag(f.P,2);var other=Bag(f.Other,5);f.Run.PushRoom(new MerchantRoom());f.Other.Creature.SetMaxHpInternal(100);f.Other.Creature.SetCurrentHpInternal(30);
   await Pack.SleepingBagContent.Open(bag);Check("bag party entry spends requester only",bag.Charges==1&&other.Charges==5);
   Check("bag party extra rest blocks arrange for all players",new[]{f.P,f.Other}.All(p=>!RunManager.Instance.RestSiteSynchronizer.GetOptionsForPlayer(p).OfType<Pack.ArrangeSleepingBagOption>().Any())&&!await new Pack.ArrangeSleepingBagOption(f.Other,other).OnSelect()&&other.Charges==5);
   await ChooseBagOption(f.P,o=>o is HealRestSiteOption);File.AppendAllText(Output+"/sleeping-bag-trace.txt","party requester healed\n");
   await ChooseBagOption(f.Other,o=>o is HealRestSiteOption);File.AppendAllText(Output+"/sleeping-bag-trace.txt","party other healed\n");
   var sync=RunManager.Instance.RestSiteSynchronizer;
   File.AppendAllText(Output+"/sleeping-bag-trace.txt",$"party remaining own={sync.GetOptionsForPlayer(f.P).Count} other={sync.GetOptionsForPlayer(f.Other).Count}\n");
   // 单进程夹具代替远端发送其结束休息消息；保留更多选择的模组下，选择一次不等于结束休息。
   var skip=AccessTools.Method(sync.GetType(),"HandleRestSiteSkippedMessage");var message=Activator.CreateInstance(skip.GetParameters()[0].ParameterType)!;skip.Invoke(sync,[message,f.Other.NetId]);
   await Pack.SleepingBagContent.Close(f.P).WaitAsync(TimeSpan.FromSeconds(12));
   Check("bag party native choices keep charge ownership",bag.Charges==1&&other.Charges==5&&f.Other.Creature.CurrentHp==60&&f.Run.CurrentRoom is MerchantRoom);
  }
  await SleepingBagNativeChecks();
  File.WriteAllText(Output+"/sleeping-bag-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Checks=passes.Skip(first)},new JsonSerializerOptions{WriteIndented=true}));
 }
 static async Task CompleteBagHextechChoice(Pack.PortableSleepingBag bag,Task? pending=null){
  // 完整回归的前序存档可启用海克斯。先完成该模组真实排队的选择，不能把选择中的禁止进入误判为睡袋入口失败。
  await Wait(2);
  var seen=new HashSet<object>();
  for(int i=0;i<300;i++){
   var screen=NOverlayStack.Instance?.Peek();
   if(screen?.GetType().FullName=="HextechRunes.HextechRuneSelectionScreen"&&seen.Add(screen)){
    Check("bag blocks entry during pending Hextech choice",!Pack.SleepingBagContent.CanUse(bag));
    var type=screen.GetType();bool enemy=(bool)AccessTools.Field(type,"_enemyOnly").GetValue(screen)!;
    if(enemy)AccessTools.Method(type,"PresenterConfirmEnemyOnly").Invoke(screen,null);
    else AccessTools.Method(type,"PresenterChoose").Invoke(screen,[0]);
   }
   else if(screen?.GetType().FullName!="HextechRunes.HextechRuneSelectionScreen"&&pending?.IsCompleted!=false)break;
   await Wait(.1);
  }
  if(pending!=null)await pending.WaitAsync(TimeSpan.FromSeconds(1));
  Check("bag native pending choices completed before entry",NOverlayStack.Instance?.ScreenCount==0);
 }
 static async Task SleepingBagNativeChecks(){
  bool old=TestMode.IsOn;var local=LocalContext.NetId;
  var observe=new Harmony("Aikun020Probe.SleepingBagUI");observe.Patch(AccessTools.Method(typeof(NRestSiteRoom),"OnProceedButtonReleased"),postfix:new HarmonyMethod(typeof(Entry),nameof(ObserveBagProceed)));
  try{
   foreach(var stack in SettingsNodes(Tree.Root).OfType<NSubmenuStack>().ToArray())while(stack.SubmenusOpen)stack.Pop();
   RunManager.Instance.CleanUp();
   var p=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var state=RunState.CreateForTest([p],seed:"AIKUN-SLEEPING-BAG");
   LocalContext.NetId=p.NetId;RunManager.Instance.SetUpNewSingleplayer(state,shouldSave:true);
   foreach(string ftue in new[]{"map_select_ftue","rest_site_ftue","merchant_ftue"})SaveManager.Instance.MarkFtueAsComplete(ftue);
   TestMode.IsOn=false;nativeAnimationWaitFixture=true;
   await NGame.Instance!.LoadRun(state,null).WaitAsync(TimeSpan.FromSeconds(60));
   var bag=await RelicCmd.Obtain<Pack.PortableSleepingBag>(p);bag!.Charges=8;
   await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false).WaitAsync(TimeSpan.FromSeconds(30));
   await CompleteBagHextechChoice(bag);
   var merchant=(MerchantRoom)state.CurrentRoom!;var merchantNode=NRun.Instance!.MerchantRoom;var inventories=merchant.Inventories.ToArray();
   var containerField=AccessTools.Field(typeof(NRun),"_roomContainer");var originalContainer=containerField.GetValue(NRun.Instance);
   string map=JsonSerializer.Serialize(SerializableActMap.FromActMap(state.Map)),visited=JsonSerializer.Serialize(state.VisitedMapCoords.ToArray());int floor=state.TotalFloor;
   var holder=SettingsNodes(NRun.Instance.GlobalUi).OfType<NRelicInventoryHolder>().Single(n=>n.Relic.Model==bag);
   var shakeTarget=NGame.Instance.ScreenshakeTarget;
   await SaveManager.Instance.SaveRun(null,false);string originalSave=JsonSerializer.Serialize(SaveManager.Instance.LoadRunSave().SaveData,JsonSerializationUtility.Options);
   await Wait(.5);var click=holder.GetGlobalRect().GetCenter();Tree.Root.PushInput(new InputEventMouseMotion{Position=click,GlobalPosition=click},true);await Wait(.05);
   foreach(bool pressed in new[]{true,false,true,false})Tree.Root.PushInput(new InputEventMouseButton{Position=click,GlobalPosition=click,ButtonIndex=MouseButton.Right,Pressed=pressed},true);
   for(int i=0;i<100&&(!Pack.SleepingBagContent.IsActive||NRestSiteRoom.Instance==null);i++)await Wait(.1);
   if(!Pack.SleepingBagContent.IsActive){await AfterDraw();Tree.Root.GetTexture().GetImage().SavePng(Output+"/sleeping-bag-entry-failure.png");File.AppendAllText(Output+"/sleeping-bag-trace.txt",$"entry failure canUse={Pack.SleepingBagContent.CanUse(bag)} capstone={NCapstoneContainer.Instance?.InUse} overlays={NOverlayStack.Instance?.ScreenCount} holderVisible={holder.IsVisibleInTree()} room={state.CurrentRoom?.GetType().Name} test={TestMode.IsOn}\n");}
   Check("bag actual inventory right click opens native rest",Pack.SleepingBagContent.IsActive&&state.CurrentRoom is RestSiteRoom&&bag.Charges==7&&NRestSiteRoom.Instance?.IsVisibleInTree()==true);
   var extraContainer=(Control)containerField.GetValue(NRun.Instance)!;
   Check("bag extra room stays below native top bar",extraContainer.GetIndex()<NRun.Instance.GlobalUi.GetIndex()&&NRun.Instance.GlobalUi.TopBar.IsVisibleInTree()&&holder.IsVisibleInTree());
   NMapScreen.Instance!.Open();Check("bag cannot leave extra rest through map",!NMapScreen.Instance.IsOpen&&Pack.SleepingBagContent.IsActive);
   await Wait(.3);await AfterDraw();Tree.Root.GetTexture().GetImage().SavePng(Output+"/sleeping-bag-native-rest.png");
   await SaveManager.Instance.SaveRun(null);Check("bag no unsupported temporary room checkpoint",originalSave==JsonSerializer.Serialize(SaveManager.Instance.LoadRunSave().SaveData,JsonSerializationUtility.Options));
   var extraNode=NRestSiteRoom.Instance!;
   Check("bag native extra rest hides arrange",!RunManager.Instance.RestSiteSynchronizer.GetLocalOptions().OfType<Pack.ArrangeSleepingBagOption>().Any());
   var restOption=RunManager.Instance.RestSiteSynchronizer.GetLocalOptions().OfType<HealRestSiteOption>().Single();
   var restButton=extraNode.GetButtonForOption(restOption)!;
   // 原生按钮淡入完成前MouseFilter为Ignore，必须等到可接收真实鼠标事件。
   for(int i=0;i<100&&restButton.MouseFilter!=Control.MouseFilterEnum.Stop;i++)await Wait(.1);
   Check("bag rest native button finishes intro",restButton.MouseFilter==Control.MouseFilterEnum.Stop&&restButton.IsEnabled);
   var restPoint=restButton.GetGlobalTransformWithCanvas()*(restButton.Size*.5f);
   File.AppendAllText(Output+"/sleeping-bag-trace.txt",$"rest pos={restPoint} size={restButton.Size} enabled={restButton.IsEnabled}\n");
   Tree.Root.PushInput(new InputEventMouseMotion{Position=restPoint,GlobalPosition=restPoint},true);await Wait(.05);
   Tree.Root.PushInput(new InputEventMouseButton{Position=restPoint,GlobalPosition=restPoint,ButtonIndex=MouseButton.Left,Pressed=true},true);await Wait(.05);
   Tree.Root.PushInput(new InputEventMouseButton{Position=restPoint,GlobalPosition=restPoint,ButtonIndex=MouseButton.Left,Pressed=false},true);
   for(int i=0;i<100&&!extraNode.ProceedButton.IsEnabled;i++)await Wait(.1);
   Check("bag native proceed becomes usable after rest",extraNode.ProceedButton.IsEnabled);
   await Wait(.8);
   var exitPoint=extraNode.ProceedButton.GetGlobalTransformWithCanvas()*(extraNode.ProceedButton.Size*.5f);Tree.Root.PushInput(new InputEventMouseMotion{Position=exitPoint,GlobalPosition=exitPoint},true);await Wait(.05);
   File.AppendAllText(Output+"/sleeping-bag-trace.txt",$"proceed pos={exitPoint} size={extraNode.ProceedButton.Size} enabled={extraNode.ProceedButton.IsEnabled}\n");
   Tree.Root.PushInput(new InputEventMouseButton{Position=exitPoint,GlobalPosition=exitPoint,ButtonIndex=MouseButton.Left,Pressed=true},true);await Wait(.05);
   Tree.Root.PushInput(new InputEventMouseButton{Position=exitPoint,GlobalPosition=exitPoint,ButtonIndex=MouseButton.Left,Pressed=false},true);
   for(int i=0;i<100&&Pack.SleepingBagContent.IsActive;i++)await Wait(.1);
   if(Pack.SleepingBagContent.IsActive){await AfterDraw();Tree.Root.GetTexture().GetImage().SavePng(Output+"/sleeping-bag-proceed-failure.png");File.AppendAllText(Output+"/sleeping-bag-trace.txt",$"proceed still active closing={AccessTools.Field(typeof(Pack.SleepingBagContent),"closing").GetValue(null)}\n");}
   Check("bag native proceed button returns",!Pack.SleepingBagContent.IsActive);await Wait(.2);
   Check("bag merchant inventory and scene preserved",state.CurrentRoom==merchant&&ReferenceEquals(NRun.Instance.MerchantRoom,merchantNode)&&merchant.Inventories.SequenceEqual(inventories)&&containerField.GetValue(NRun.Instance)==originalContainer&&bag.Charges==7);
   Check("bag restores original screen shake target",NGame.Instance.ScreenshakeTarget==shakeTarget&&(shakeTarget==null||GodotObject.IsInstanceValid(shakeTarget)));
   Check("bag native map routes and floor untouched",map==JsonSerializer.Serialize(SerializableActMap.FromActMap(state.Map))&&visited==JsonSerializer.Serialize(state.VisitedMapCoords.ToArray())&&floor==state.TotalFloor);
   await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);var parent=state.CurrentRoom;var node=NRestSiteRoom.Instance;var options=RunManager.Instance.RestSiteSynchronizer.GetLocalOptions().ToArray();
   await Pack.SleepingBagContent.Open(bag);await ChooseBagOption(p,o=>o is HealRestSiteOption);await Pack.SleepingBagContent.Close(p);
   Check("bag native parent rest UI and remaining choices preserved",state.CurrentRoom==parent&&NRestSiteRoom.Instance==node&&node!.IsVisibleInTree()&&RunManager.Instance.RestSiteSynchronizer.GetLocalOptions().SequenceEqual(options));
   var normalArrange=RunManager.Instance.RestSiteSynchronizer.GetLocalOptions().OfType<Pack.ArrangeSleepingBagOption>().Single();int beforeArrange=bag.Charges;await ChooseBagOption(p,o=>o==normalArrange);Check("bag native normal rest still arranges once",bag.Charges==beforeArrange+1&&!await normalArrange.OnSelect());
   await RunManager.Instance.EnterRoom(new EventRoom(ModelDb.Event<Pack.DongniOffice>()));var parentEvent=state.CurrentRoom;var eventNode=NRun.Instance.EventRoom;
   await Pack.SleepingBagContent.Open(bag);await ChooseBagOption(p,o=>o is HealRestSiteOption);await Pack.SleepingBagContent.Close(p);
   Check("bag native event instance and UI preserved",state.CurrentRoom==parentEvent&&NRun.Instance.EventRoom==eventNode&&eventNode!.IsVisibleInTree());
   // 原生胜利后房间：真实奖励界面、可领取奖励与原房间共同保存，不重新生成。
   var encounter=state.Act.AllRegularEncounters.First();var finished=CombatRoom.FromSerializable(new SerializableRoom{RoomType=RoomType.Monster,EncounterId=encounter.Id,IsPreFinished=true,GoldProportion=1},state);
   await RunManager.Instance.EnterRoom(finished);await Wait(.5);var rewards=NOverlayStack.Instance!.Peek();var combatNode=NRun.Instance.CombatRoom;
   Check("bag native prefinished combat has rewards",rewards!=null&&finished.IsPreFinished&&Pack.SleepingBagContent.CanUse(bag));
   var rewardSet=(RewardsSet)AccessTools.Field(typeof(NRewardsScreen),"_rewardsSet").GetValue(rewards)!;var rewardObjects=rewardSet.Rewards.ToArray();var claimed=rewardObjects.Select(r=>r.SuccessfullySelected).ToArray();
   await Pack.SleepingBagContent.Open(bag);Check("bag hides pending rewards only for extra rest",NOverlayStack.Instance.Peek()==null&&(rewards as Control)?.Visible==false);
   await ChooseBagOption(p,o=>o is HealRestSiteOption);await Pack.SleepingBagContent.Close(p);
   Check("bag original victory rewards and combat scene preserved",state.CurrentRoom==finished&&NOverlayStack.Instance.Peek()==rewards&&NRun.Instance.CombatRoom==combatNode&&(rewards as Control)?.IsVisibleInTree()==true);
   Check("bag pending reward objects and claimed flags preserved",rewardSet.Rewards.SequenceEqual(rewardObjects)&&rewardSet.Rewards.Select(r=>r.SuccessfullySelected).SequenceEqual(claimed));
   var gold=rewardSet.Rewards.OfType<GoldReward>().First();int before=p.Gold;await RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(gold);Check("bag original gold remains actually claimable",gold.SuccessfullySelected&&p.Gold>before);
   await SaveManager.Instance.SaveRun(finished,false);var checkpoint=SaveManager.Instance.LoadRunSave();
   Check("bag original native checkpoint readable after return",checkpoint.Success&&checkpoint.SaveData?.PreFinishedRoom?.IsPreFinished==true);
   for(int i=0;i<3;i++){var restored=RunState.FromSerializable(checkpoint.SaveData!);Check("bag native run save preserves charges cycle="+i,restored.Players[0].Relics.OfType<Pack.PortableSleepingBag>().Single().Charges==bag.Charges);}
   Check("bag native map still unchanged after all origins",map==JsonSerializer.Serialize(SerializableActMap.FromActMap(state.Map))&&visited==JsonSerializer.Serialize(state.VisitedMapCoords.ToArray()));
   if(!Minimal&&state.Acts.Count>3&&state.Acts[3].GetType().Assembly.GetName().Name=="Act4Heart"){
    File.AppendAllText(Output+"/sleeping-bag-trace.txt","fourth act with concurrent Hextech choice BEGIN\n");
    await CompleteBagHextechChoice(bag,RunManager.Instance.EnterAct(3,false));
    await CompleteBagHextechChoice(bag,RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false));var fourth=state.CurrentRoom;
    await Pack.SleepingBagContent.Open(bag);await ChooseBagOption(p,o=>o is HealRestSiteOption);await Pack.SleepingBagContent.Close(p);
    Check("bag actual fourth act extra rest returns without audio crash",state.CurrentActIndex==3&&state.CurrentRoom==fourth&&NRestSiteRoom.Instance?.IsVisibleInTree()==true);
   }
  }catch(Exception e){File.WriteAllText(Output+"/sleeping-bag-error.txt",e.ToString());throw;}
  finally{observe.UnpatchAll(observe.Id);RunManager.Instance.CleanUp();nativeAnimationWaitFixture=false;TestMode.IsOn=old;LocalContext.NetId=local;}
 }
}
