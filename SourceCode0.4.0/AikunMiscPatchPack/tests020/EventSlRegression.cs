using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.Helpers;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 sealed class EventSlSession : IDisposable {
  readonly bool oldTest=TestMode.IsOn;readonly ulong? oldLocal=LocalContext.NetId;
  readonly System.Reflection.FieldInfo listeners=AccessTools.Field(typeof(CombatStateTracker),"CombatStateChanged");readonly object? frontend;
  public readonly RunState State;public Player P=>State.Players[0];
  public EventSlSession(RunState? state=null,bool setup=true){
   frontend=listeners.GetValue(CombatManager.Instance.StateTracker);listeners.SetValue(CombatManager.Instance.StateTracker,null);TestMode.IsOn=true;
   State=state??RunState.CreateForTest([Player.CreateForNewRun<Ironclad>(UnlockState.all,1)],seed:"AIKUN-EVENT-SL");
   if(state==null){foreach(var act in State.Acts)act.GenerateRooms(State.Rng.UpFront,State.UnlockState);State.AppendToMapPointHistory(MapPointType.Unknown,RoomType.Event,null);P.Gold=200;}
   LocalContext.NetId=P.NetId;if(setup)RunManager.Instance.SetUpTest(State,new NetSingleplayerGameService(),shouldSave:true);
  }
  public void Dispose(){try{CombatManager.Instance.Reset(false);RunManager.Instance.CleanUp();}finally{LocalContext.NetId=oldLocal;TestMode.IsOn=oldTest;listeners.SetValue(CombatManager.Instance.StateTracker,frontend);}}
 }
 static async Task EventSlChecks(){
  var records=new List<object>();
  // 对照旧实现：仅 TakeOverPath 的纹理在原生缓存清理后确实不能再读取。
  const string transient="res://AikunMiscPatchPack/probe/event-sl-unbacked.png";
  using(var image=Pack.BloodInitiationArt.Background.GetImage()){
   var texture=ImageTexture.CreateFromImage(image);texture.TakeOverPath(transient);var cache=new AssetCache();cache.GetTexture2D(transient);cache.UnloadMissedCacheAssets();await Wait(.08);
   Check("event SL reproduces unbacked texture disposed by native cleanup",!GodotObject.IsInstanceValid(texture)&&!ResourceLoader.Exists(transient));
  }
  EventModel[] models=[ModelDb.Event<Pack.BloodSectInitiation>(),ModelDb.Event<Pack.ThunderSectInitiation>(),ModelDb.Event<Pack.DongniOffice>()];
  foreach(var model in models){
   string checkpoint;
   using(var f=new EventSlSession()){
    if(model is Pack.BloodSectInitiation)f.P.Deck.AddInternal(f.State.CreateCard<Pack.BloodSect>(f.P),silent:true);
    if(model is Pack.ThunderSectInitiation)f.P.Deck.AddInternal(f.State.CreateCard<Zap>(f.P),silent:true);
    f.State.PushRoom(new EventRoom(model));await SaveManager.Instance.SaveRun(null,false);
    var saved=SaveManager.Instance.LoadRunSave();Check("event SL actual entry save reads "+model.Id,saved.Success&&saved.SaveData!=null);
    checkpoint=JsonSerializer.Serialize(saved.SaveData,JsonSerializationUtility.Options);
    File.WriteAllText(Output+"/checkpoint-"+model.GetType().Name+".json",checkpoint);
   }
   for(int cycle=0;cycle<3;cycle++){
    var save=JsonSerializer.Deserialize<SerializableRun>(checkpoint,JsonSerializationUtility.Options)!;
    using var f=new EventSlSession(RunState.FromSerializable(save),setup:false);
    await RunManager.Instance.SetUpSavedSingleplayer(f.State,save);
    string path=model is Pack.BloodSectInitiation?Pack.BloodInitiationArt.BackgroundPath:model is Pack.ThunderSectInitiation?Pack.ThunderInitiationArt.BackgroundPath:Pack.DongniOfficeArt.BackgroundPath;
    var cache=new AssetCache();var before=cache.GetTexture2D(path);cache.UnloadMissedCacheAssets();await Wait(.08);
    Check($"event SL native disposal runs {model.Id} cycle {cycle}",!GodotObject.IsInstanceValid(before));
    Check($"event SL background remains reloadable {model.Id} cycle {cycle}",ResourceLoader.Exists(path)&&File.Exists(ProjectSettings.GlobalizePath(path)));
    await RestoredEventScreen(f,model,path,cycle,cycle==0);
    records.Add(new{Event=model.Id.ToString(),Cycle=cycle,Path=path,Gold=f.P.Gold,Cards=f.P.Deck.Cards.Count});
   }
  }
  await SectOptionTextChecks();
  if(!Minimal&&System.Environment.GetEnvironmentVariable("AIKUN_EVENT_SL_INCIDENT_SAVE") is {} file&&File.Exists(file)){
  var text=File.ReadAllText(file);
   var save=JsonSerializer.Deserialize<SerializableRun>(text,JsonSerializationUtility.Options)!;
   using var f=new EventSlSession(RunState.FromSerializable(save),setup:false);
   await RunManager.Instance.SetUpSavedSingleplayer(f.State,save);
   Check("event SL reported save copy loads through native saved-run setup",f.State.Players.Count>0&&f.P.Creature.IsAlive);
   await RestoredEventScreen(f,ModelDb.Event<Pack.BloodSectInitiation>(),Pack.BloodInitiationArt.BackgroundPath,9,true);
   Check("event SL reported save input remains unchanged",File.ReadAllText(file)==text);
  }
  File.WriteAllText(Output+"/event-sl-evidence.json",JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
 }
 static async Task RestoredEventScreen(EventSlSession f,EventModel canonical,string path,int cycle,bool screenshot){
  var sync=RunManager.Instance.EventSynchronizer;sync.BeginEvent(canonical);var model=sync.GetLocalEvent();int gold=f.P.Gold,cards=f.P.Deck.Cards.Count,relics=f.P.Relics.Count;
  var viewport=new SubViewport{Size=new(2560,1440),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("181c27")});bool old=TestMode.IsOn;
  try{
   TestMode.IsOn=false;
   await PreloadManager.LoadRunAssets(f.State.Players.Select(p=>p.Character)).WaitAsync(TimeSpan.FromSeconds(30));
   await PreloadManager.LoadActAssets(f.State.Act).WaitAsync(TimeSpan.FromSeconds(30));
   await PreloadManager.LoadRoomEventAssets(canonical,f.State).WaitAsync(TimeSpan.FromSeconds(25));
   Check($"event SL native threaded preload restores background {canonical.Id} cycle {cycle}",GodotObject.IsInstanceValid(PreloadManager.Cache.GetTexture2D(path))&&PreloadManager.Cache.GetTexture2D(path).GetWidth()==1672);
   var node=NEventRoom.Create(model,f.State,false)!;host.AddChild(node);node.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);await Wait(2.2);
   Check($"event SL restored event has background and four visible choices {canonical.Id} cycle {cycle}",node.Layout!=null&&node.Layout.OptionButtons.Count()==4&&node.Layout.OptionButtons.All(b=>b.IsVisibleInTree())&&model.CurrentOptions.Count==4);
   string bg=canonical is Pack.BloodSectInitiation?"BloodSectBackground":canonical is Pack.ThunderSectInitiation?"ThunderSectBackground":"DongniOfficeBackground";
   Check("event SL actual layout uses reloaded texture "+canonical.Id,node.Layout!.GetNode<TextureRect>(bg).Texture==PreloadManager.Cache.GetTexture2D(path));
   if(screenshot){await AfterDraw();Check("event SL restored screenshot saved "+canonical.Id,viewport.GetTexture().GetImage().SavePng(Output+"/event-sl-"+canonical.GetType().Name+"-"+cycle+".png")==Error.Ok);}
   sync.ChooseLocalOption(3);await sync.AwaitPendingOptionTasks();await Wait(.3);
   Check($"event SL can leave and exposes native continue {canonical.Id} cycle {cycle}",model.IsFinished&&node.Layout.OptionButtons.Count()==1&&((List<EventOption>)AccessTools.Field(typeof(NEventRoom),"_connectedOptions").GetValue(node)!).Any(o=>o.IsProceed));
   Check("event SL leaving never duplicates gold cards or relics "+canonical.Id,f.P.Gold==gold&&f.P.Deck.Cards.Count==cards&&f.P.Relics.Count==relics);
  }finally{TestMode.IsOn=old;viewport.QueueFree();await Wait(.08);sync.BeforeExitingRoom();}
 }
 static async Task SectOptionTextChecks(){
  foreach(bool blood in new[]{true,false})foreach(bool locked in new[]{false,true}){
   using var f=new EventSlSession();EventModel canonical=blood?ModelDb.Event<Pack.BloodSectInitiation>():ModelDb.Event<Pack.ThunderSectInitiation>();
   if(locked){f.P.Gold=149;f.P.AddRelicInternal(blood?ModelDb.Relic<Pack.ThunderSectToken>().ToMutable():ModelDb.Relic<Pack.BloodSectToken>().ToMutable(),silent:true);}
   else if(blood)f.P.Deck.AddInternal(f.State.CreateCard<Pack.BloodSect>(f.P),silent:true);else f.P.Deck.AddInternal(f.State.CreateCard<Zap>(f.P),silent:true);
   var model=canonical.ToMutable();await model.BeginEvent(f.P,null,false);var options=model.CurrentOptions;
   Check($"sect option text keeps eligibility locked={locked} blood={blood}",options.Take(3).All(o=>o.IsLocked==locked)&&!options[3].IsLocked);
   foreach(var option in options.Take(3))Check($"sect option description hides prerequisite {option.TextKey} locked={locked}",locked?option.Description.GetFormattedText()=="不能选择该选项。":!new[]{"需要：","不持有","牌组中有","指定雷电牌","悬停查看"}.Any(option.Description.GetFormattedText().Contains));
   Check("sect option text preserves original option history keys "+canonical.Id,options.Take(3).Select(o=>o.TextKey.Split('.').Last()).SequenceEqual(new[]{"JOIN","MEMBER","BUY"}));
   if(!locked){Check("sect option text preserves purchase cost and reward",options[2].Description.GetFormattedText().Contains("150")&&options[0].Description.GetFormattedText().Contains("弟子令"));}
   else{
    await RestoredEventScreen(f,canonical,blood?Pack.BloodInitiationArt.BackgroundPath:Pack.ThunderInitiationArt.BackgroundPath,8,true);
   }
  }
 }
}
