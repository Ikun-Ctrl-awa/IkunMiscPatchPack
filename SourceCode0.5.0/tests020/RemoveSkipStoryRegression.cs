using System.Diagnostics;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using STS2RitsuLib.Settings;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static async Task RemoveSkipStoryChecks(){
  int first=passes.Count;
  Check("removed skip story leaves 51 main 10 misc 41 patch options",Pack.Options.All.Length==51&&Pack.Options.All.Count(o=>o.Misc)==10&&Pack.Options.All.Count(o=>!o.Misc)==41);
  Check("removed skip story no option startup or runtime status",!Pack.Options.All.Any(o=>o.Key=="skip_story")&&!Pack.Settings.Startup.ContainsKey("skip_story")&&!Pack.RuntimeLoader.States.ContainsKey("skip_story"));
  Check("removed skip story implementation absent from compiled assembly",typeof(Pack.Entry).Assembly.GetType("IkunMiscPatchPack.SkipStory")==null);
  Check("removed skip story binding properties absent",typeof(Pack.PackConfig).GetProperty("SkipStoryKey")==null&&typeof(Pack.Settings).GetProperty("StartupSkipStoryKey")==null);
  Check("removed skip story no input animation or speed Harmony hooks",Harmony.GetAllPatchedMethods().All(m=>Harmony.GetPatchInfo(m)!.Owners.All(o=>o!="Ikun.MiscPatchPack.SkipStory")));
  var legacy=JsonSerializer.Deserialize<Pack.PackConfig>("{\"Enabled\":{\"skip_story\":true,\"mod_form\":false,\"x_card_expansion\":false},\"SkipStoryKey\":\"Ctrl+F8\",\"ModFormIncludeSelf\":true,\"ModFormIncludeTokens\":false}")!;
  Check("removed skip story legacy config still reads unrelated values",!legacy.Get("mod_form")&&!legacy.Get("x_card_expansion")&&legacy.ModFormIncludeSelf&&!legacy.ModFormIncludeTokens);
  Check("removed skip story old key cannot restore binding",!JsonSerializer.Serialize(legacy).Contains("SkipStoryKey")&&!Pack.Options.All.Any(o=>o.Key=="skip_story"));
  var pages=ModSettingsRegistry.GetPages().Where(p=>p.ModId==Pack.Settings.Id&&p.Id!=Pack.Settings.Id).ToArray();
  Check("removed skip story native registry has no section or hotkey control",pages.SelectMany(p=>p.Sections).All(s=>s.Id!="skip_story"&&s.Entries.All(e=>e.Id!="hotkey")));
  Check("removed skip story native category counts",pages.Single(p=>p.Id.EndsWith(".misc")).Sections.Count==10&&pages.Single(p=>p.Id.EndsWith(".patches")).Sections.Count==41);
  string config=JsonSerializer.Serialize(Pack.Settings.Current);
  var misc=pages.Single(p=>p.Id.EndsWith(".misc"));Check("removed skip story native settings opens",ModSettingsNavigator.RequestOpenByIds(Pack.Settings.Id,misc.Id,misc.Sections.First().Id,null).Success);await Wait(.5);
  Check("removed skip story native UI has no deleted description",!SettingsNodes(Tree.Root).Any(n=>n.Name=="AikunDescription_skip_story"));await AfterDraw();Check("removed skip story settings screenshot saved",Tree.Root.GetTexture().GetImage().SavePng(Output+"/removed-skip-story-settings.png")==Error.Ok);
  Check("removed skip story navigation preserves config",JsonSerializer.Serialize(Pack.Settings.Current)==config);
  foreach(var stack in SettingsNodes(Tree.Root).OfType<NSubmenuStack>().ToArray())while(stack.SubmenusOpen)stack.Pop();
  using(var f=NewDongniFixture()){
   var speed=SaveManager.Instance.PrefsSave.FastMode;double timeScale=Engine.TimeScale;var executor=RunManager.Instance.ActionExecutor;var current=executor.CurrentlyRunningAction;
   try{
    nativeAnimationWaitFixture=true;SaveManager.Instance.PrefsSave.FastMode=FastModeType.Normal;
    var action=new ResolutionAction(f.P.NetId,()=>Cmd.Wait(.75f));action.OnEnqueued(_=>{},1);AccessTools.Property(typeof(ActionExecutor),nameof(ActionExecutor.CurrentlyRunningAction)).SetValue(executor,action);
    var watch=Stopwatch.StartNew();var pending=action.Execute();await Wait(.02);Check("removed skip story native action timer still waits",!pending.IsCompleted);
    NGame.Instance!._Input(new InputEventKey{Keycode=Key.Space,Pressed=true});NGame.Instance._Input(new InputEventKey{Keycode=Key.Space,Pressed=false});
    Check("removed skip story space does not finish native timer",!pending.IsCompleted);await pending.WaitAsync(TimeSpan.FromSeconds(8));
    Check("removed skip story timer finishes normally without forced acceleration",watch.Elapsed.TotalSeconds>=.4&&action.State==GameActionState.Finished&&SaveManager.Instance.PrefsSave.FastMode==FastModeType.Normal&&Engine.TimeScale==timeScale);
   }finally{AccessTools.Property(typeof(ActionExecutor),nameof(ActionExecutor.CurrentlyRunningAction)).SetValue(executor,current);SaveManager.Instance.PrefsSave.FastMode=speed;nativeAnimationWaitFixture=false;}
  }
  File.WriteAllText(Output+"/removed-skip-story-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Failed=0,Checks=passes.Skip(first)},new JsonSerializerOptions{WriteIndented=true}));
 }
}
