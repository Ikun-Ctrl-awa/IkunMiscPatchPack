using System.Reflection;
using System.Threading;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using STS2RitsuLib.RuntimeInput;

namespace AikunMiscPatchPack;

public static class SkipStory {
 static readonly Harmony H=new("Aikun.MiscPatchPack.SkipStory");
 static readonly Dictionary<SceneTreeTimer,GameAction> timers=new();
 static readonly HashSet<Key> consumedKeys=new();
 static GameAction? skippedAction;
 static int batch;
 public static int SkippedWaits{get;private set;}
 public static int PendingWaits=>timers.Count;
 static GameAction? Current=>RunManager.Instance?.ActionExecutor?.CurrentlyRunningAction;
 public static bool CanSkip=>Settings.Active("skip_story")&&CombatManager.Instance.IsInProgress
  &&Current is {State:GameActionState.Executing,ActionType:GameActionType.Combat or GameActionType.CombatPlayPhaseOnly}
  &&RunManager.Instance.ActionExecutor?.IsPaused==false&&!UiBlocks();
 public static bool IsSkipping=>skippedAction!=null&&ReferenceEquals(Current,skippedAction)&&CanSkip;

 public static string NormalizeKey(string? value){
  if(!RuntimeHotkeyService.TryNormalizeBinding(value,out var normalized)||normalized.StartsWith("action:",StringComparison.OrdinalIgnoreCase))return "Space";
  return TryKey(normalized,out _,out _,out _,out _,out _)?normalized:"Space";
 }
 static bool TryKey(string normalized,out Key key,out bool ctrl,out bool alt,out bool shift,out bool meta){
  var parts=normalized.Split('+');ctrl=parts.Contains("Ctrl");alt=parts.Contains("Alt");shift=parts.Contains("Shift");meta=parts.Contains("Meta");
  return Enum.TryParse(parts[^1],true,out key)&&key is not (Key.None or Key.Ctrl or Key.Alt or Key.Shift or Key.Meta);
 }
 public static bool Matches(InputEventKey key,string binding){
  return TryKey(NormalizeKey(binding),out var primary,out var ctrl,out var alt,out var shift,out var meta)
   &&(key.Keycode==primary||key.PhysicalKeycode==primary)&&key.CtrlPressed==ctrl&&key.AltPressed==alt&&key.ShiftPressed==shift&&key.MetaPressed==meta;
 }
 static bool UiBlocks(){
  if(NDevConsole.IsConsoleVisible)return true;
  if(NRun.Instance!=null&&NCapstoneContainer.Instance?.CurrentCapstoneScreen!=null)return true;
  if(NOverlayStack.Instance?.ScreenCount>0)return true;
  return NGame.Instance?.GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit;
 }
 public static void Install(){
  H.Patch(AccessTools.Method(typeof(NGame),nameof(NGame._Input)),prefix:new HarmonyMethod(typeof(SkipStory),nameof(Input)) {priority=Priority.First});
  // 输入管理器的后备入口；处理顺序变化时仍能阻止 Peek／Select 的重复触发。
  H.Patch(AccessTools.Method(typeof(NInputManager),nameof(NInputManager._UnhandledKeyInput)),prefix:new HarmonyMethod(typeof(SkipStory),nameof(Input)) {priority=Priority.First});
  foreach(var method in typeof(Cmd).GetMethods(BindingFlags.Public|BindingFlags.Static).Where(m=>m.Name is "Wait" or "CustomScaledWait"))
   H.Patch(method,prefix:new HarmonyMethod(typeof(SkipStory),nameof(Wait)) {priority=Priority.First});
  H.Patch(AccessTools.Method(typeof(Cmd),"WaitInternal"),prefix:new HarmonyMethod(typeof(SkipStory),nameof(BeforeTimer)),postfix:new HarmonyMethod(typeof(SkipStory),nameof(AfterTimer)));
  H.Patch(AccessTools.PropertyGetter(typeof(PrefsSave),nameof(PrefsSave.FastMode)),postfix:new HarmonyMethod(typeof(SkipStory),nameof(FastMode)) {priority=Priority.Last});
  H.Patch(AccessTools.Method(typeof(CreatureCmd),nameof(CreatureCmd.TriggerAnim)),prefix:new HarmonyMethod(typeof(SkipStory),nameof(Wait)) {priority=Priority.First});
  foreach(var method in typeof(VfxCmd).GetMethods(BindingFlags.Public|BindingFlags.Static).Where(m=>m.ReturnType==typeof(void)&&m.Name.StartsWith("Play",StringComparison.Ordinal)))
   H.Patch(method,prefix:new HarmonyMethod(typeof(SkipStory),nameof(Visual)));
  foreach(var name in new[]{"AddOrbAnim","EvokeOrbAnim","AddSlotAnim","RemoveSlotAnim"})
   H.Patch(AccessTools.Method(typeof(NOrbManager),name),prefix:new HarmonyMethod(typeof(SkipStory),nameof(BeforeOrbVisual)),postfix:new HarmonyMethod(typeof(SkipStory),nameof(AfterOrbVisual)));
  H.Patch(AccessTools.Method(typeof(GameAction),nameof(GameAction.PauseForPlayerChoice)),prefix:new HarmonyMethod(typeof(SkipStory),nameof(Reset)));
  H.Patch(AccessTools.Method(typeof(CombatManager),nameof(CombatManager.Reset)),prefix:new HarmonyMethod(typeof(SkipStory),nameof(Reset)));
  ((SceneTree)Engine.GetMainLoop()).ProcessFrame+=Tick;
 }
 public static bool HandleKey(InputEventKey key){
  var code=key.Keycode==Key.None?key.PhysicalKeycode:key.Keycode;
  if(!key.Pressed)return consumedKeys.Remove(code);
  if(key.Echo)return consumedKeys.Contains(code);
  if(!Matches(key,Settings.StartupSkipStoryKey)||!RequestSkip()){
   // 切换窗口可能丢失上一次抬键；新的原版按键必须能正常收到对应的抬键。
   consumedKeys.Remove(code);return false;
  }
  consumedKeys.Add(code);return true;
 }
 static bool Input(InputEvent inputEvent){
  if(NGame.Instance?.Transition.InTransition==true||!NGame.IsGameFocusedWindow())return true;
  if(inputEvent is not InputEventKey key||!HandleKey(key))return true;
  NGame.Instance?.GetViewport().SetInputAsHandled();return false;
 }
 public static bool RequestSkip(){
  if(!CanSkip)return false;
  skippedAction=Current;batch=0;
  // 只提前结束当前动作已经开始的 Cmd 等待，不绕过异步伤害、选牌或同步任务。
  foreach(var (timer,owner) in timers.ToArray())if(ReferenceEquals(owner,skippedAction)&&GodotObject.IsInstanceValid(timer))timer.TimeLeft=0;
  return true;
 }
 static void Tick(){if(skippedAction!=null&&!IsSkipping)Reset();}
 public static void Reset(){skippedAction=null;batch=0;}
 static bool Wait(object[] __args,ref Task __result){
  if(!IsSkipping)return true;
  var token=__args.OfType<CancellationToken>().FirstOrDefault();SkippedWaits++;
  // 每批仍让出一帧，防止大量同步触发阻塞输入和绘制；不按视觉速度合并伤害。
  __result=token.IsCancellationRequested?Task.FromCanceled(token):(++batch%32==0?NextFrame(token):Task.CompletedTask);return false;
 }
 static async Task NextFrame(CancellationToken token){
  token.ThrowIfCancellationRequested();await ((SceneTree)Engine.GetMainLoop()).ToSignal(Engine.GetMainLoop(),SceneTree.SignalName.ProcessFrame);token.ThrowIfCancellationRequested();
 }
 static void BeforeTimer(SceneTreeTimer timer){if(CombatManager.Instance.IsInProgress&&Current is {State:GameActionState.Executing} action)timers[timer]=action;}
 static void AfterTimer(SceneTreeTimer timer,ref Task __result){if(timers.ContainsKey(timer))__result=Track(__result,timer);}
 static async Task Track(Task original,SceneTreeTimer timer){try{await original;}finally{timers.Remove(timer);}}
 static void FastMode(ref FastModeType __result){if(IsSkipping)__result=FastModeType.Instant;}
 static bool Visual()=>!IsSkipping;
 static void BeforeOrbVisual(out HashSet<ulong>? __state){__state=IsSkipping?((SceneTree)Engine.GetMainLoop()).GetProcessedTweens().Select(t=>t.GetInstanceId()).ToHashSet():null;}
 static void AfterOrbVisual(HashSet<ulong>? __state){
  if(__state==null)return;
  // 保留充能球节点的增删和最终布局，只立即走完本次表现新建的 Tween。
  foreach(var tween in ((SceneTree)Engine.GetMainLoop()).GetProcessedTweens().ToArray())if(!__state.Contains(tween.GetInstanceId())&&tween.IsValid())tween.CustomStep(60);
 }
}
