using System.Collections;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using STS2RitsuLib;
using STS2RitsuLib.Networking.ManagedActions;
using STS2RitsuLib.Scaffolding.Content;

namespace IkunMiscPatchPack;

public sealed class PortableSleepingBag : ModRelicTemplate {
 int charges=1;
 [SavedProperty] public int Charges{get=>charges;set{AssertMutable();charges=Math.Clamp(value,0,999999999);InvokeDisplayAmountChanged();}}
 public override RelicRarity Rarity=>RelicRarity.Shop;
 public override bool IsAllowed(IRunState runState)=>Settings.Active(SleepingBagContent.Key)&&base.IsAllowed(runState);
 public override bool ShowCounter=>true;
 public override int DisplayAmount=>Charges;
 public override string? CustomIconPath=>SleepingBagArt.IconPath;
 public override string? CustomIconOutlinePath=>SleepingBagArt.OutlinePath;
 public override string? CustomBigIconPath=>SleepingBagArt.BigIconPath;
 public override bool TryModifyRestSiteOptions(Player player,ICollection<RestSiteOption> options){
  if(SleepingBagContent.IsActive||player!=Owner||options.Any(o=>o is ArrangeSleepingBagOption))return false;
  options.Add(new ArrangeSleepingBagOption(player,this));return true;
 }
}

public sealed class ArrangeSleepingBagOption(Player owner,PortableSleepingBag bag) : ModRestSiteOptionTemplate(owner) {
 bool used;
 public override string OptionId=>"AIKUN_ARRANGE_SLEEPING_BAG";
 public override string? CustomIconPath=>SleepingBagArt.IconPath;
 public override bool IsEnabled=>!SleepingBagContent.IsActive&&!used&&Owner.Relics.Contains(bag)&&bag.Charges<999999999;
 public override Task<bool> OnSelect(){
  if(!IsEnabled)return Task.FromResult(false);
  used=true;bag.Charges++;bag.Flash();return Task.FromResult(true);
 }
}

public static class SleepingBagContent {
 public const string Key="portable_sleeping_bag";
 static readonly RitsuLibManagedNetActionDescriptor<bool> action=new(Settings.Id,"sleeping_bag_room",b=>[b?(byte)1:(byte)0],
  bytes=>bytes.Length==1?bytes[0]==1:throw new InvalidDataException("睡袋动作数据无效。"),
  async context=>{if(context.Message)await Close(context.Player);else if(context.Player.Relics.OfType<PortableSleepingBag>().FirstOrDefault() is {} bag)await Open(bag);},GameActionType.NonCombat);
 static readonly FieldInfo roomContainer=AccessTools.Field(typeof(NRun),"_roomContainer");
 static readonly FieldInfo restSites=AccessTools.Field(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.RestSiteSynchronizer),"_restSites");
 static readonly PropertyInfo capstoneScreen=AccessTools.Property(typeof(NCapstoneContainer),nameof(NCapstoneContainer.CurrentCapstoneScreen));
 static readonly FieldInfo overlays=AccessTools.Field(typeof(NOverlayStack),"_overlays");
 static readonly FieldInfo[] hoverFields=new[]{"_hoverMessageTask","_hoveredMessage","_lastHoverMessageMsec"}.Select(n=>AccessTools.Field(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.RestSiteSynchronizer),n)).ToArray();
 static Session? session;
 static bool closing,restoring;
 public static bool IsActive=>session!=null;
 sealed class Session(RunState state,PortableSleepingBag bag){
  public readonly RunState State=state;
  public readonly PortableSleepingBag Bag=bag;
  public readonly AbstractRoom Parent=state.CurrentRoom!;
  public readonly RestSiteRoom Room=new();
  public readonly List<object> RestSites=((IList)restSites.GetValue(RunManager.Instance.RestSiteSynchronizer)!).Cast<object>().ToList();
  public readonly List<(FieldInfo Field,Delegate Listener)> Detached=[];
  public readonly Dictionary<FieldInfo,object?> HoverState=hoverFields.ToDictionary(f=>f,f=>f.GetValue(RunManager.Instance.RestSiteSynchronizer));
  // 皇家枕头的激活状态只表示当前是否位于火堆；其他遗物在额外房间中产生的状态变化必须保留。
  public readonly Dictionary<RegalPillow,RelicStatus> PillowStatuses=state.Players.SelectMany(p=>p.Relics).OfType<RegalPillow>().ToDictionary(r=>r,r=>r.Status);
  public NSceneContainer? OriginalContainer,TemporaryContainer;
  public bool OriginalVisible,MapWasOpen,TravelEnabled;
  public Node.ProcessModeEnum OriginalProcess;
  public ICapstoneScreen? Capstone;
  public Control? CapstoneNode;
  public bool CapstoneVisible;
  public Node.ProcessModeEnum CapstoneProcess;
  public readonly Control? ShakeTarget=NGame.Instance?.ScreenshakeTarget;
  public readonly List<IOverlayScreen> Overlays=[];
  public readonly List<(Control Node,bool Visible,Node.ProcessModeEnum Process)> OverlayNodes=[];
  public Control? Backstop;
  public Color BackstopColor;
  public Control.MouseFilterEnum BackstopMouse;
  public int BackstopIndex;
 }
 public static bool CanUse(PortableSleepingBag bag){
  var state=RunManager.Instance.DebugOnlyGetState();
  if(session!=null||closing||!bag.IsMutable||bag.Charges<=0||bag.Owner?.Creature.IsAlive!=true||state?.CurrentRoom==null||!state.Players.Contains(bag.Owner)||!bag.Owner.Relics.Contains(bag))return false;
  if(state.CurrentRoom.IsVictoryRoom||NMapScreen.Instance?.IsTraveling==true||NCapstoneContainer.Instance?.InUse==true)return false;
  // 奖励清单可以临时保留；正在进行的选牌等选择必须先完成，避免中断结算。
  if(NOverlayStack.Instance is {} stack&&((IList)overlays.GetValue(stack)!).Cast<object>().Any(o=>o is not NRewardsScreen))return false;
  // 战斗中禁止使用；结算后的战斗房间仍属于“战斗外”。
  return state.CurrentRoom is not CombatRoom combat||combat.IsPreFinished||CombatManager.Instance.IsOverOrEnding;
 }
 public static bool Request(PortableSleepingBag bag){
  if(!LocalContext.IsMe(bag.Owner)||!CanUse(bag))return false;
  return RitsuLibManagedNetActions.Request(RunManager.Instance,action,false,bag.Owner.NetId);
 }
 public static async Task<bool> Open(PortableSleepingBag bag){
  if(!CanUse(bag))return false;
  if(SaveManager.Instance.CurrentRunSaveTask is {} save)await save;
  await FlushHover();
  if(!CanUse(bag))return false;
  var s=new Session(RunManager.Instance.DebugOnlyGetState()!,bag);session=s;
  try{
   bag.Charges--;bag.Flash();
   // 保留原房间实例、事件/商店现场和待领取奖励，不调用它们的退出/重新进入。
   var sync=RunManager.Instance.RestSiteSynchronizer;
   foreach(var field in sync.GetType().GetFields(BindingFlags.Instance|BindingFlags.NonPublic))if(typeof(Delegate).IsAssignableFrom(field.FieldType)&&field.GetValue(sync) is Delegate listeners){
    foreach(var listener in listeners.GetInvocationList().Where(d=>d.Target is NRestSiteRoom)){
     s.Detached.Add((field,listener));field.SetValue(sync,Delegate.Remove((Delegate?)field.GetValue(sync),listener));
    }
   }
   if(NRun.Instance is {} run){
    s.OriginalContainer=(NSceneContainer)roomContainer.GetValue(run)!;s.OriginalVisible=s.OriginalContainer.Visible;s.OriginalProcess=s.OriginalContainer.ProcessMode;
    s.OriginalContainer.Hide();s.OriginalContainer.ProcessMode=Node.ProcessModeEnum.Disabled;
    s.TemporaryContainer=new NSceneContainer{Name="AikunExtraRestRoom"};run.AddChild(s.TemporaryContainer);
    run.MoveChild(s.TemporaryContainer,s.OriginalContainer.GetIndex()+1);
    s.TemporaryContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    roomContainer.SetValue(run,s.TemporaryContainer);
    var map=NMapScreen.Instance!;s.MapWasOpen=map.IsOpen;s.TravelEnabled=map.IsTravelEnabled;map.Close();map.SetTravelEnabled(false);
    var container=NCapstoneContainer.Instance!;s.Capstone=container.CurrentCapstoneScreen;
    if(s.Capstone is Control node){s.CapstoneNode=node;s.CapstoneVisible=node.Visible;s.CapstoneProcess=node.ProcessMode;node.Hide();node.ProcessMode=Node.ProcessModeEnum.Disabled;}
    capstoneScreen.SetValue(container,null);
    var stack=NOverlayStack.Instance!;var screens=(IList)overlays.GetValue(stack)!;
    foreach(IOverlayScreen screen in screens){s.Overlays.Add(screen);if(screen is Control view){s.OverlayNodes.Add((view,view.Visible,view.ProcessMode));view.Hide();view.ProcessMode=Node.ProcessModeEnum.Disabled;}}
    screens.Clear();
    s.Backstop=(Control)AccessTools.Field(typeof(NOverlayStack),"_backstop").GetValue(stack)!;
    s.BackstopColor=s.Backstop.Modulate;s.BackstopMouse=s.Backstop.MouseFilter;s.BackstopIndex=s.Backstop.GetIndex();
    ((Tween?)AccessTools.Field(typeof(NOverlayStack),"_backstopFade").GetValue(stack))?.Kill();
    s.Backstop.Modulate=Colors.Transparent;s.Backstop.MouseFilter=Control.MouseFilterEnum.Ignore;
   }
   await RunManager.Instance.EnterRoomWithoutExitingCurrentRoom(s.Room,fadeToBlack:false);
   ActiveScreenContext.Instance.Update();return true;
  }catch{
   bag.Charges++;Restore(s);throw;
  }
 }
 public static async Task Close(Player requester){
  if(session is not {} s||closing||NOverlayStack.Instance?.ScreenCount>0||NCapstoneContainer.Instance?.InUse==true)return;closing=true;
  try{
   // 仅请求者发送自己的跳过消息；其他玩家继续完成各自的原生休息选择。
   if(LocalContext.IsMe(requester))RunManager.Instance.RestSiteSynchronizer.BeforeLocalRestSiteExited();
   await RunManager.Instance.RestSiteSynchronizer.AfterAllRestSitesCompleted();
   await FlushHover();
   if(s.State.CurrentRoom==s.Room)s.State.PopCurrentRoom();
   await s.Room.Exit(s.State);
  }finally{Restore(s);closing=false;}
 }
 static void Restore(Session s){
  bool previous=restoring;restoring=true;
  try{RestoreCore(s);}finally{restoring=previous;}
 }
 static async Task FlushHover(){
  if(hoverFields[0].GetValue(RunManager.Instance.RestSiteSynchronizer) is Task {IsCompleted:false} task)await task;
 }
 static void RestoreCore(Session s){
  if(s.State.CurrentRoom==s.Room)s.State.PopCurrentRoom();
  // 先让临时房间退树，移除它自己的事件监听，再恢复父火堆的同步记录和监听。
  if(s.TemporaryContainer is {} temporary&&GodotObject.IsInstanceValid(temporary)){
   temporary.GetParent()?.RemoveChild(temporary);temporary.QueueFree();
  }
  var sync=RunManager.Instance.RestSiteSynchronizer;
  var sites=(IList)restSites.GetValue(sync)!;sites.Clear();foreach(var site in s.RestSites)sites.Add(site);
  foreach(var (field,value) in s.HoverState)field.SetValue(sync,value);
  foreach(var (field,listener) in s.Detached)field.SetValue(sync,Delegate.Combine((Delegate?)field.GetValue(sync),listener));
  if(NRun.Instance is {} run&&s.OriginalContainer is {} original&&GodotObject.IsInstanceValid(original)){
   roomContainer.SetValue(run,original);original.ProcessMode=s.OriginalProcess;original.Visible=s.OriginalVisible;
   capstoneScreen.SetValue(NCapstoneContainer.Instance,s.Capstone);
   if(s.CapstoneNode is {} node&&GodotObject.IsInstanceValid(node)){node.ProcessMode=s.CapstoneProcess;node.Visible=s.CapstoneVisible;}
   var screens=(IList)overlays.GetValue(NOverlayStack.Instance!)!;screens.Clear();foreach(var screen in s.Overlays)screens.Add(screen);
   foreach(var (view,visible,process) in s.OverlayNodes)if(GodotObject.IsInstanceValid(view)){view.ProcessMode=process;view.Visible=visible;}
   if(s.Backstop is {} backstop&&GodotObject.IsInstanceValid(backstop)){backstop.Modulate=s.BackstopColor;backstop.MouseFilter=s.BackstopMouse;backstop.GetParent().MoveChild(backstop,s.BackstopIndex);}
   foreach(var pair in s.PillowStatuses)if(pair.Key.Owner?.Relics.Contains(pair.Key)==true)pair.Key.Status=pair.Value;
  }
  session=null;
  if(NGame.Instance is {} game){if(s.ShakeTarget is {} target&&GodotObject.IsInstanceValid(target))game.SetScreenShakeTarget(target);else game.ClearScreenShakeTarget();}
  RunManager.Instance.RunLocationTargetedBuffer.OnLocationChanged(s.State.RunLocation);
  if(NRun.Instance is {} n){NMapScreen.Instance!.SetTravelEnabled(s.TravelEnabled);if(s.MapWasOpen)NMapScreen.Instance.Open();AccessTools.Method(n.GlobalUi.TopBar.RoomIcon.GetType(),"UpdateIcon").Invoke(n.GlobalUi.TopBar.RoomIcon,null);n.RunMusicController.UpdateTrack();n.RunMusicController.UpdateAmbience();}
  ActiveScreenContext.Instance.Update();
 }
 static bool Click(NClickableControl __instance,InputEvent inputEvent){
  if(__instance is not NRelicInventoryHolder holder||holder.Relic?.Model is not PortableSleepingBag bag||inputEvent is not InputEventMouseButton {ButtonIndex:MouseButton.Right} mouse)return true;
  if(mouse.Pressed&&holder.IsVisibleInTree()){Request(bag);holder.GetViewport().SetInputAsHandled();}return false;
 }
 static bool Proceed(){
  if(session==null)return true;
  if(!closing)RitsuLibManagedNetActions.Request(RunManager.Instance,action,true);return false;
 }
 static bool OpenMap()=>session==null;
 static bool Hover()=>!restoring;
 static bool Save(ref Task __result){if(session==null)return true;__result=Task.CompletedTask;return false;}
 static void CleanUp(){if(session is {} s){Restore(s);closing=false;}}
 public static void Initialize(){
  RitsuLibFramework.CreateContentPack(Settings.Id+".SleepingBag").Relic<SharedRelicPool,PortableSleepingBag>().Apply();
  RitsuLibManagedNetActions.Register(action);
  var h=new Harmony(Settings.Id+".SleepingBag");
  h.Patch(AccessTools.Method(typeof(NClickableControl),nameof(NClickableControl._GuiInput)),prefix:new HarmonyMethod(typeof(SleepingBagContent),nameof(Click)));
  h.Patch(AccessTools.Method(typeof(NRestSiteRoom),"OnProceedButtonReleased"),prefix:new HarmonyMethod(typeof(SleepingBagContent),nameof(Proceed)));
  h.Patch(AccessTools.Method(typeof(NMapScreen),nameof(NMapScreen.Open)),prefix:new HarmonyMethod(typeof(SleepingBagContent),nameof(OpenMap)));
  // 退树中的按钮不再排队发送额外房间悬停；保留正常火堆与正常互动时的原生同步。
  h.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.RestSiteSynchronizer),"LocalOptionHovered"),prefix:new HarmonyMethod(typeof(SleepingBagContent),nameof(Hover)));
  h.Patch(AccessTools.Method(typeof(SaveManager),nameof(SaveManager.SaveRun),[typeof(AbstractRoom),typeof(bool)]),prefix:new HarmonyMethod(typeof(SleepingBagContent),nameof(Save)));
  h.Patch(AccessTools.Method(typeof(RunManager),nameof(RunManager.CleanUp)),prefix:new HarmonyMethod(typeof(SleepingBagContent),nameof(CleanUp)));
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.Initialize)),postfix:new HarmonyMethod(typeof(SleepingBagContent),nameof(Localize)));
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.SetLanguage)),postfix:new HarmonyMethod(typeof(SleepingBagContent),nameof(Localize)));
  Localize();RuntimeLoader.States[Key]="已应用。";
 }
 static void Localize(){
  if(LocManager.Instance==null)return;string id=ModelDb.GetId<PortableSleepingBag>().Entry;
  string text="战斗外，右键点击以消耗1次可用次数，进入一个额外的[gold]休息房间[/gold]，随后返回原房间。\n初始有1次可用次数。在普通休息处选择[gold]整理[/gold]，补充1次可用次数。额外休息处不能整理。";
  LocManager.Instance.GetTable("relics").MergeWith(new(){[id+".title"]="便携睡袋",[id+".description"]=text,[id+".eventDescription"]=text,[id+".flavor"]="收拾妥当，随处安眠。"});
  LocManager.Instance.GetTable("rest_site_ui").MergeWith(new(){["OPTION_AIKUN_ARRANGE_SLEEPING_BAG.name"]="整理",["OPTION_AIKUN_ARRANGE_SLEEPING_BAG.description"]="整理便携睡袋，补充[gold]1[/gold]次可用次数。"});
 }
}
