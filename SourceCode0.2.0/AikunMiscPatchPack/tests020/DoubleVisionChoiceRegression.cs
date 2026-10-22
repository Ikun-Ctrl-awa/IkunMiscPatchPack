using System.Collections;
using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static int visionChoiceIndex,visionScreenCalls;
 static TaskCompletionSource<RelicModel?>? visionSelectionGate;
 static ModelId? visionFailOnce;
 static bool VisionChoose(IReadOnlyList<RelicModel> relics,ref Task<RelicModel?> __result){
  visionScreenCalls++;__result=visionSelectionGate?.Task??Task.FromResult(visionChoiceIndex<0?null:relics[visionChoiceIndex]);return false;
 }
 static void VisionObtainFailure(RelicModel relic){if(visionFailOnce==relic.Id){visionFailOnce=null;throw new InvalidOperationException("isolated choice obtain failure");}}
 static RelicModel AddVision(Player p){var r=ModelDb.GetById<RelicModel>(ModelDb.GetId(Find("HextechRunes.DoubleVisionRune"))).ToMutable();p.AddRelicInternal(r,silent:true);return r;}
 static Player VisionPlayer(int runes){
  CleanupScrollRoom();var p=Player.CreateForNewRun<Silent>(UnlockState.all,1);var state=RunState.CreateForTest([p],seed:"AIKUN-VISION-CHOICES");RunManager.Instance.SetUpTest(state,new NetSingleplayerGameService());
  for(int i=0;i<runes;i++)AddVision(p);return p;
 }
 static Reward VisionReward(Player p,bool wax=false,bool shared=false,RelicModel? initial=null){
  var relic=initial??ModelDb.Relic<Anchor>().ToMutable();relic.IsWax=wax;
  var ctor=Find("RelicRewardChoices.Rewards.RelicRewardChoiceReward").GetConstructor(F,null,[typeof(RelicReward),typeof(bool),typeof(string)],null)!;
  var reward=(Reward)ctor.Invoke([new RelicReward(relic,p),shared,"aikun-vision-fixture"]);reward.Populate();return reward;
 }
 static RelicModel[] VisionCandidates(Reward reward)=>((IEnumerable)AccessTools.Property(reward.GetType(),"Candidates").GetValue(reward)!).Cast<object>().Select(c=>(RelicModel)AccessTools.Property(c.GetType(),"RuntimeModel").GetValue(c)!).ToArray();
 static Reward VisionRestore(Reward reward,Player p){var json=JsonSerializer.Serialize(reward.ToSerializable(),JsonSerializationUtility.Options);var save=JsonSerializer.Deserialize<SerializableReward>(json,JsonSerializationUtility.Options)!;return Reward.FromSerializable(save,p);}
 static string VisionBags(Player p)=>JsonSerializer.Serialize(p.RelicGrabBag.ToSerializable(),JsonSerializationUtility.Options)+"|"+JsonSerializer.Serialize(p.RunState.SharedRelicGrabBag.ToSerializable(),JsonSerializationUtility.Options);
 static async Task DoubleVisionChoiceChecks(){
  if(Minimal){Check("double vision choice safely skips absent mods",Pack.Settings.Status(Pack.Options.All.Single(o=>o.Key=="double_vision_relic_choice")).Contains("未装载相关模组"));return;}
  int first=passes.Count;var evidence=new List<object>();var oldMode=TestMode.IsOn;var harmony=new Harmony("Aikun020Probe.DoubleVisionChoices");
  var listeners=AccessTools.Field(typeof(CombatStateTracker),"CombatStateChanged");var frontend=listeners.GetValue(CombatManager.Instance.StateTracker);
  var selector=AccessTools.Method(Find("RelicRewardChoices.Patches.RelicChoiceScreenTextPatch"),"OpenLocalizedSelection");
  harmony.Patch(selector,prefix:new HarmonyMethod(typeof(Entry),nameof(VisionChoose)));
  harmony.Patch(AccessTools.Method(typeof(RelicCmd),nameof(RelicCmd.Obtain),[typeof(RelicModel),typeof(Player),typeof(int)]),prefix:new HarmonyMethod(typeof(Entry),nameof(VisionObtainFailure)));
  try{
   listeners.SetValue(CombatManager.Instance.StateTracker,null);TestMode.IsOn=true;
   string? disabledBags=null;ModelId[]? disabledCandidates=null;
   foreach(var (enabled,runes,wax,shared,index) in new[]{(false,1,false,false,0),(true,1,false,false,0),(true,0,false,false,0),(true,2,false,false,0),(true,1,true,false,0),(true,1,false,true,2)}){
    Pack.Settings.Startup["double_vision_relic_choice"]=enabled;var p=VisionPlayer(runes);var reward=VisionReward(p,wax,shared);var candidates=VisionCandidates(reward);string label=$"vision enabled={enabled} runes={runes} wax={wax} shared={shared}";
    Check(label+" three choices",candidates.Length==3);visionChoiceIndex=index;var chosen=candidates[index].Id;
    Check(label+" native selection succeeds",await reward.SelectUnsynchronized());
    int expected=enabled?1+runes:1;var obtained=p.Relics.Where(r=>r.Id==chosen).ToArray();
    Check(label+" copies exactly match native rule",obtained.Length==expected);
    Check(label+" wax preserved",obtained.All(r=>r.IsWax==wax));
    Check(label+" unchosen not granted",candidates.Where((_,i)=>i!=index).All(c=>p.Relics.All(r=>r.Id!=c.Id)));
    if(!enabled){disabledBags=VisionBags(p);disabledCandidates=candidates.Select(c=>c.Id).ToArray();}
    else if(runes==1&&!wax&&!shared){Check("vision fixed candidates unchanged by compatibility",candidates.Select(c=>c.Id).SequenceEqual(disabledCandidates!));Check("vision duplication does not draw more relics or change pool returns",VisionBags(p)==disabledBags);}
    int total=p.Relics.Count;Check(label+" repeated callback stays complete",await reward.SelectUnsynchronized());Check(label+" repeated callback never duplicates",p.Relics.Count==total);
    var restored=VisionRestore(reward,p);
    if(enabled)Check(label+" finalized JSON keeps candidates and completion",VisionCandidates(restored).Select(r=>r.Id).SequenceEqual(candidates.Select(r=>r.Id))&&(int)AccessTools.Property(restored.GetType(),"StateValue").GetValue(restored)! ==5);
    Check(label+" finalized JSON no repeated copy",await restored.SelectUnsynchronized()&&p.Relics.Count==total);
    var loaded=Player.FromSerializable(p.ToSerializable());Check(label+" player save keeps copies and wax",loaded.Relics.Count(r=>r.Id==chosen)==expected&&loaded.Relics.Where(r=>r.Id==chosen).All(r=>r.IsWax==wax));
    evidence.Add(new{enabled,runes,wax,shared,Chosen=chosen.ToString(),Copies=obtained.Length});
   }
   Pack.Settings.Startup["double_vision_relic_choice"]=true;
   var owner=VisionPlayer(1);var pending=VisionReward(owner);var fixedIds=VisionCandidates(pending).Select(r=>r.Id).ToArray();visionChoiceIndex=-1;int before=owner.Relics.Count;
   Check("vision cancellation remains pending",!await pending.SelectUnsynchronized()&&owner.Relics.Count==before);
   pending=VisionRestore(pending,owner);Check("vision pending JSON keeps same candidates",VisionCandidates(pending).Select(r=>r.Id).SequenceEqual(fixedIds));
   visionChoiceIndex=1;Check("vision pending JSON can grant two selected copies",await pending.SelectUnsynchronized()&&owner.Relics.Count(r=>r.Id==fixedIds[1])==2);
   var skipped=VisionReward(owner);before=owner.Relics.Count;skipped.OnSkipped();Check("vision skipped reward grants nothing",await skipped.SelectUnsynchronized()&&owner.Relics.Count==before);
   owner=VisionPlayer(1);pending=VisionReward(owner);var models=VisionCandidates(pending);before=owner.Relics.Count;visionSelectionGate=new();var inFlight=pending.SelectUnsynchronized();
   Check("vision concurrent callback does not grant",!await pending.SelectUnsynchronized()&&owner.Relics.Count==before);visionSelectionGate.SetResult(models[2]);visionSelectionGate=null;
   Check("vision original concurrent selection grants exactly two",await inFlight&&owner.Relics.Count(r=>r.Id==models[2].Id)==2);
   owner=VisionPlayer(1);pending=VisionReward(owner);models=VisionCandidates(pending);before=owner.Relics.Count;visionChoiceIndex=0;visionFailOnce=models[0].Id;
   Check("vision failed initial obtain grants nothing",!await pending.SelectUnsynchronized()&&owner.Relics.Count==before);
   Check("vision obtain failure releases native suppression",!(bool)AccessTools.Method(Find("HextechRunes.DoubleVisionRune"),"IsCommandDuplicationSuppressed").Invoke(null,null)!);
   Check("vision retry after obtain failure grants exactly two",await pending.SelectUnsynchronized()&&owner.Relics.Count(r=>r.Id==models[0].Id)==2);
   // 原始遗物奖励继续由海克斯自身处理；选择海克斯遗物保留其原有排除规则。
   owner=VisionPlayer(1);var vanilla=new RelicReward(ModelDb.Relic<Anchor>().ToMutable(),owner);
   Check("vision ordinary relic reward still duplicates once",await vanilla.SelectUnsynchronized()&&owner.Relics.Count(r=>r is Anchor)==2);
   owner=VisionPlayer(0);var rune=ModelDb.GetById<RelicModel>(ModelDb.GetId(Find("HextechRunes.DoubleVisionRune"))).ToMutable();pending=VisionReward(owner,initial:rune);visionChoiceIndex=0;
   Check("vision acquiring first rune never self duplicates",await pending.SelectUnsynchronized()&&owner.Relics.Count(r=>r.GetType()==rune.GetType())==1);
   owner=VisionPlayer(1);pending=VisionReward(owner,initial:(RelicModel)rune.CanonicalInstance.ToMutable());visionChoiceIndex=0;
   Check("vision native hextech relic exclusion retained",await pending.SelectUnsynchronized()&&owner.Relics.Count(r=>r.GetType()==rune.GetType())==2);
   Check("vision final suppression released",!(bool)AccessTools.Method(Find("HextechRunes.DoubleVisionRune"),"IsCommandDuplicationSuppressed").Invoke(null,null)!);
   File.WriteAllText(Output+"/double-vision-choice-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Failed=0,Checks=passes.Skip(first),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
  }finally{visionSelectionGate=null;visionFailOnce=null;harmony.UnpatchAll(harmony.Id);Pack.Settings.Startup["double_vision_relic_choice"]=true;try{CleanupScrollRoom();}finally{TestMode.IsOn=oldMode;listeners.SetValue(CombatManager.Instance.StateTracker,frontend);}}
 }
}
