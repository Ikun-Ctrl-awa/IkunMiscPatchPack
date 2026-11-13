using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using Pack=IkunMiscPatchPack;
using STS2RitsuLib.Settings;

namespace Aikun020Probe;
public static partial class Entry {
 static void Act4Trace(string message){File.AppendAllText(Output+"/act4-audio-trace.txt",DateTime.Now.ToString("O")+" "+message+"\n");GD.Print("[Act4AudioProbe] "+message);}
 static bool Act4AmbienceReady(Node proxy){using var e=proxy.Get("_ambienceEv");return e.VariantType==Variant.Type.Object&&GodotObject.IsInstanceValid(e.AsGodotObject());}
 static string? Act4CachedAmbience(NRunMusicController music)=>(string?)AccessTools.Field(typeof(NRunMusicController),"_currentAmbience").GetValue(music);
 static async Task Act4AudioChecks(){
  int first=passes.Count;
  var option=Pack.Options.All.Single(o=>o.Key==Pack.Act4AudioFix.Key);
  Check("act4 audio independent default on option",new Pack.PackConfig().Get(option.Key)&&option.Required.SequenceEqual(new[]{"Act4Heart"}));
  if(Minimal){Check("act4 audio optional mod absent",!Pack.Options.Eligible(option.Key)&&!Pack.RuntimeLoader.States.ContainsKey(option.Key));return;}
  Check("act4 audio patch installed",Pack.RuntimeLoader.States.GetValueOrDefault(option.Key)=="已应用。");
  bool old=TestMode.IsOn;var local=LocalContext.NetId;
  bool enabled=Pack.Settings.Active(option.Key);
  try{
   // 先关闭上一项设置页夹具的真实子菜单栈，避免遮挡房间及误把设置页当成火堆截图。
   foreach(var stack in SettingsNodes(Tree.Root).OfType<NSubmenuStack>().ToArray())while(stack.SubmenusOpen)stack.Pop();
   await Wait(.2);
   var path=VersionOutput+"/act4-crash-20261028-235528/private-player-copy/current_run.save";
   var save=JsonSerializer.Deserialize<SerializableRun>(File.ReadAllText(path),JsonSerializationUtility.Options)!;
   var state=RunState.FromSerializable(save);LocalContext.NetId=state.Players[0].NetId;
   Act4Trace("native saved-run setup BEGIN");await RunManager.Instance.SetUpSavedSingleplayer(state,save);
   TestMode.IsOn=false;nativeAnimationWaitFixture=true;Act4Trace($"native NGame.LoadRun BEGIN interactive={!NonInteractiveMode.IsActive} test={TestMode.IsOn}");
   await NGame.Instance!.LoadRun(state,save.PreFinishedRoom).WaitAsync(TimeSpan.FromSeconds(60));
   Act4Trace("native NGame.LoadRun END");
   Check("act4 audio reported saved run enters native scene",NRun.Instance!=null&&state.CurrentActIndex==3);
   await Wait(2);await AfterDraw();Tree.Root.GetTexture().GetImage().SavePng(Output+"/act4-native-entry.png");
   var music=NRun.Instance!.RunMusicController;
   Act4Trace($"original UpdateTrack direct BEGIN interactive={!NonInteractiveMode.IsActive} test={TestMode.IsOn}");music.UpdateTrack();Act4Trace("original UpdateTrack direct END");
   Act4Trace("transition back to native act3 BEGIN");await RunManager.Instance.EnterAct(2,false).WaitAsync(TimeSpan.FromSeconds(30));
   await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false).WaitAsync(TimeSpan.FromSeconds(30));Act4Trace("native act3 rest site END");
   var proxy=(Node)AccessTools.Field(typeof(NRunMusicController),"_proxy").GetValue(music)!;
   Act4Trace($"proxy before transition ambience={proxy.Get("_currentAmbience")} eventType={proxy.Get("_ambienceEv").VariantType}");
   Act4Trace("native EnterAct4 BEGIN");await RunManager.Instance.EnterAct(3,false).WaitAsync(TimeSpan.FromSeconds(30));Act4Trace("native EnterAct4 END");
   Act4Trace($"proxy after transition ambience={proxy.Get("_currentAmbience")} eventType={proxy.Get("_ambienceEv").VariantType}");
   Act4Trace("native act4 first rest site BEGIN");await RunManager.Instance.EnterMapCoord(new MapCoord(3,0)).WaitAsync(TimeSpan.FromSeconds(30));Act4Trace("native act4 first rest site END");
   Check("act4 audio native third to fourth transition and first rest site",state.CurrentRoom is RestSiteRoom&&state.CurrentActIndex==3);
   Act4Trace("legacy boss control return to act3 BEGIN");await RunManager.Instance.EnterAct(2,false).WaitAsync(TimeSpan.FromSeconds(30));
   await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false).WaitAsync(TimeSpan.FromSeconds(30));
   Act4Trace("actual legacy boss victory callback BEGIN");AccessTools.Method(Find("ActsFromThePast.LegacyBossHelper"),"OnBossVictory").Invoke(null,null);Act4Trace("actual legacy boss victory callback END");
   Act4Trace($"after boss victory ambience={proxy.Get("_currentAmbience")} eventType={proxy.Get("_ambienceEv").VariantType}");
   Act4Trace("after victory native EnterAct4 BEGIN");await RunManager.Instance.EnterAct(3,false).WaitAsync(TimeSpan.FromSeconds(30));Act4Trace("after victory native EnterAct4 END");
   Act4Trace($"before first campfire ambience={proxy.Get("_currentAmbience")} eventType={proxy.Get("_ambienceEv").VariantType}");
   Act4Trace("global Progress standalone BEGIN");proxy.Call("update_global_parameter","Progress",3);Act4Trace("global Progress standalone END");
   Act4Trace("after victory native first campfire BEGIN");await RunManager.Instance.EnterMapCoord(new MapCoord(3,0)).WaitAsync(TimeSpan.FromSeconds(30));Act4Trace("after victory native first campfire END");
   Check("act4 audio actual legacy boss victory to first campfire survives",state.CurrentRoom is RestSiteRoom);
   Check("act4 audio actual native ambience restored",Act4AmbienceReady(proxy));
   // 关闭时只观察旧缓存状态，不再次调用已证明会终止进程的失效营火事件。
   Pack.Settings.Startup[option.Key]=false;string? previous=Act4CachedAmbience(music);music.StopMusic();
   Check("act4 audio disabled reproduces stale cache after stop",!Act4AmbienceReady(proxy)&&!string.IsNullOrEmpty(previous)&&Act4CachedAmbience(music)==previous);
   Pack.Settings.Startup[option.Key]=true;music.UpdateMusic();music.UpdateTrack();
   Check("act4 audio repairs existing stale cache in same rest site",Act4AmbienceReady(proxy));
   music.TriggerCampfireGoingOut();Check("act4 audio campfire going out retains valid event",Act4AmbienceReady(proxy));
   music.StopMusic();Check("act4 audio direct campfire out starts with released event",!Act4AmbienceReady(proxy));
   music.TriggerCampfireGoingOut();Check("act4 audio direct campfire out restores missing event",Act4AmbienceReady(proxy));
   music.UpdateMusic();
   for(int cycle=0;cycle<3;cycle++){
    await RunManager.Instance.EnterAct(2,false);await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
    Check("act4 audio native act3 ambience unchanged cycle="+cycle,Act4AmbienceReady(proxy));
    AccessTools.Method(Find("ActsFromThePast.LegacyBossHelper"),"OnBossVictory").Invoke(null,null);
    Check("act4 audio stop clears actual event and controller cache cycle="+cycle,!Act4AmbienceReady(proxy)&&string.IsNullOrEmpty(Act4CachedAmbience(music)));
    await RunManager.Instance.EnterAct(3,false);await RunManager.Instance.EnterMapCoord(new MapCoord(3,0));
    Check("act4 audio repeated actual boss victory to fourth campfire cycle="+cycle,state.CurrentActIndex==3&&state.CurrentRoom is RestSiteRoom&&Act4AmbienceReady(proxy));
    var audio=NRun.Instance.GetNodeOrNull<AudioStreamPlayer>("Act4HeartPlayer");
    Check("act4 audio original fourth act OGG still playing cycle="+cycle,audio?.Playing==true&&audio.Stream is AudioStreamOggVorbis);
    music.TriggerCampfireGoingOut();Check("act4 audio native campfire out survives cycle="+cycle,Act4AmbienceReady(proxy));
   }
   await SaveManager.Instance.SaveRun(null,false);var checkpoint=SaveManager.Instance.LoadRunSave();
   Check("act4 audio real native checkpoint saves after transition",checkpoint.Success&&checkpoint.SaveData!=null);
   var text=JsonSerializer.Serialize(checkpoint.SaveData,JsonSerializationUtility.Options);
   for(int cycle=0;cycle<3;cycle++){
    var restored=RunState.FromSerializable(JsonSerializer.Deserialize<SerializableRun>(text,JsonSerializationUtility.Options)!);
    Check("act4 audio native checkpoint readback keeps act and deck cycle="+cycle,restored.CurrentActIndex==3&&restored.Act.Id==state.Act.Id&&restored.Players[0].Deck.Cards.Count==state.Players[0].Deck.Cards.Count);
   }
   NModalContainer.Instance?.Clear();await Wait(.2);
   Check("act4 audio actual rest site visible without settings overlay",SettingsNodes(NRun.Instance!).OfType<NRestSiteRoom>().Any(n=>n.IsVisibleInTree())&&!SettingsNodes(Tree.Root).OfType<RitsuModSettingsSubmenu>().Any(n=>n.IsVisibleInTree()));
   await AfterDraw();Check("act4 audio fixed native campfire screenshot saved",Tree.Root.GetTexture().GetImage().SavePng(Output+"/act4-native-fixed-campfire.png")==Error.Ok);
  }finally{
   CombatManager.Instance.Reset(false);RunManager.Instance.CleanUp();
   Pack.Settings.Startup[option.Key]=enabled;nativeAnimationWaitFixture=false;TestMode.IsOn=old;LocalContext.NetId=local;
   File.WriteAllText(Output+"/act4-audio-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Checks=passes.Skip(first)},new JsonSerializerOptions{WriteIndented=true}));
  }
 }
}
