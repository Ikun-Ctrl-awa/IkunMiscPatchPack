using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using STS2RitsuLib.Settings;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static bool nativeAnimationWaitFixture,waitFixtureInstalled;
 static void KeepNativeWaits(ref bool __result){if(nativeAnimationWaitFixture)__result=false;}
 static void InstallNativeWaitFixture(){
  // 必须先于其他战斗夹具安装，避免等待命令提前内联未修补的非交互模式判断。
  if(!waitFixtureInstalled){new Harmony("Aikun020Probe.NativeWaitFixture").Patch(AccessTools.PropertyGetter(typeof(NonInteractiveMode),nameof(NonInteractiveMode.IsActive)),postfix:new HarmonyMethod(typeof(Entry),nameof(KeepNativeWaits)));waitFixtureInstalled=true;}
 }
 sealed class ResolutionAction(ulong owner,Func<Task> body):GameAction {
  public override ulong OwnerId=>owner;
  public override GameActionType ActionType=>GameActionType.Combat;
  protected override Task ExecuteAction()=>body();
  public override INetAction ToNetAction()=>throw new NotSupportedException("仅测试，不发送网络消息");
 }
 sealed record SkipResult(string Scenario,bool Skipped,int[] Hp,int Channels,int Orbs,int Hits,string Rng,string[] Targets,double Seconds,int Waits);
 static async Task SkipStoryChecks(){
  // TestMode 负责隔离前端，但它默认也关闭所有等待；此测试专门保留真实 Cmd 计时器。
  InstallNativeWaitFixture();
  Check("skip option installed",Pack.RuntimeLoader.States.GetValueOrDefault("skip_story")=="已应用。");
  Check("default and old config space binding",new Pack.PackConfig().SkipStoryKey=="Space"&&JsonSerializer.Deserialize<Pack.PackConfig>("{}")!.SkipStoryKey=="Space");
  Check("binding normalization and fallback",Pack.SkipStory.NormalizeKey("ctrl+f8")=="Ctrl+F8"&&Pack.SkipStory.NormalizeKey("bad-invalid-key")=="Space");
  Check("combo binding and modifier match",Pack.SkipStory.Matches(new InputEventKey{Keycode=Key.F8,CtrlPressed=true},"Ctrl+F8")&&!Pack.SkipStory.Matches(new InputEventKey{Keycode=Key.F8},"Ctrl+F8")&&!Pack.SkipStory.Matches(new InputEventKey{Keycode=Key.Space,CtrlPressed=true},"Space"));
  var section=ModSettingsRegistry.GetPages().Single(p=>p.Id==Pack.Settings.Id+".misc").Sections.Single(s=>s.Id=="skip_story");
  Check("skip enable and key binding in misc settings",section.Entries.Any(e=>e.Id=="enabled")&&section.Entries.Any(e=>e.Id=="hotkey"));
  var mouse=(Dictionary<StringName,Key>)AccessTools.Property(typeof(NInputManager),"DefaultHotkeyInputMap").GetValue(null)!;
  var keyboard=(Dictionary<StringName,Key>)AccessTools.Property(typeof(NInputManager),"DefaultKbOnlyInputMap").GetValue(null)!;
  Check("space default conflicts verified from game maps",mouse[MegaInput.peek]==Key.Space&&keyboard[MegaInput.select]==Key.Space);
  Check("idle skip key preserves native behavior",!Pack.SkipStory.HandleKey(new InputEventKey{Keycode=Key.Space,Pressed=true}));
  var results=new List<SkipResult>();
  // 完整组合中的第三方充能球显示钩子依赖真实房间；原生 Voltaic 在最小环境验证，
  // 完整组合验证随机伤害与东尼算法触发执行器，不移除任何第三方结算钩子。
  if(!Minimal){
   var patchInfo=Harmony.GetPatchInfo(AccessTools.Method(typeof(OrbCmd),"Channel",[typeof(PlayerChoiceContext),typeof(OrbModel),typeof(Player)]));
   File.WriteAllText(Output+"/orb-channel-patches.txt",string.Join('\n',patchInfo.Prefixes.Concat(patchInfo.Postfixes).Concat(patchInfo.Transpilers).Select(p=>p.owner+" "+p.PatchMethod.DeclaringType?.FullName+"."+p.PatchMethod.Name)));
  }
  foreach(var scenario in Minimal?new[]{"Voltaic300","RandomDamage300"}:new[]{"RandomDamage300","AutoAnthonyTriggered300"}){
   var baseline=await SkipResolution(scenario,false);var skipped=await SkipResolution(scenario,true);results.Add(baseline);results.Add(skipped);
   Check(scenario+" same final HP orbs and channels",baseline.Hp.SequenceEqual(skipped.Hp)&&baseline.Orbs==skipped.Orbs&&baseline.Channels==skipped.Channels);
   Check(scenario+" same random stream and target order",baseline.Rng==skipped.Rng&&baseline.Targets.SequenceEqual(skipped.Targets));
   Check(scenario+" completes promptly after key",skipped.Seconds<5&&skipped.Waits>0);
   if(scenario=="Voltaic300")Check("Voltaic actually channels all 300 lightning orbs",skipped.Channels==600&&skipped.Orbs==3);
   else Check(scenario+" all 300 two-damage hits retained",skipped.Hits==300&&skipped.Hp.Sum()==30000-600);
  }
  File.WriteAllText(Output+"/skip-animations-"+(Minimal?"minimal":"full")+".json",JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
 }
 static async Task<SkipResult> SkipResolution(string scenario,bool skip){
  bool oldTest=TestMode.IsOn,enabled=Pack.Settings.Active("skip_story");string binding=Pack.Settings.StartupSkipStoryKey;
  var speed=SaveManager.Instance.PrefsSave.FastMode;double timeScale=Engine.TimeScale;
  var listeners=AccessTools.Field(typeof(CombatStateTracker),"CombatStateChanged");var frontend=listeners.GetValue(CombatManager.Instance.StateTracker);
  try{
   listeners.SetValue(CombatManager.Instance.StateTracker,null);TestMode.IsOn=true;nativeAnimationWaitFixture=true;
   Pack.Settings.Startup["skip_story"]=true;Pack.Settings.StartupSkipStoryKey="Space";
   var p=Player.CreateForNewRun<Defect>(UnlockState.all,1);var state=RunState.CreateForTest([p],seed:"SKIP-"+scenario);RunManager.Instance.SetUpTest(state,new NetSingleplayerGameService());
   var combat=new CombatState(runState:state);combat.AddPlayer(p);var enemies=new List<Creature>();
   var damageEvents=new List<(int Target,int Amount)>();
   for(int i=0;i<3;i++){int index=i;var enemy=combat.CreateCreature(ModelDb.Monster<CalcifiedCultist>().ToMutable(),CombatSide.Enemy,"skip-"+i);combat.AddCreature(enemy);enemy.SetMaxHpInternal(10000);enemy.SetCurrentHpInternal(10000);enemy.CurrentHpChanged+=(before,after)=>{if(after<before)damageEvents.Add((index,before-after));};enemies.Add(enemy);}
   CombatManager.Instance.SetUpCombat(combat);var turn=AccessTools.Field(typeof(CombatManager),"_turnState").GetValue(CombatManager.Instance)!;turn.GetType().GetProperty("IsInProgress")!.SetValue(turn,true);
   var executor=RunManager.Instance.ActionExecutor;AccessTools.Field(typeof(ActionExecutor),"_isPaused").SetValue(executor,false);
   if(scenario=="Voltaic300")for(int i=0;i<300;i++){var orb=ModelDb.Orb<LightningOrb>().ToMutable();orb.Owner=p;CombatManager.Instance.History.OrbChanneled(combat,orb);}
   var card=combat.CreateCard(ModelDb.Card<Voltaic>(),p);await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);var choice=new ThrowingPlayerChoiceContext();int hits=0;string[] targets=[];
   CardModel? anthonyCard=scenario=="AutoAnthonyTriggered300"?await AnthonyTriggerCard(combat,p):null;
   Check("fixture source card belongs to live combat",card.CombatState!=null);
   SaveManager.Instance.PrefsSave.FastMode=skip?FastModeType.Normal:FastModeType.Instant;
   var action=new ResolutionAction(p.NetId,async()=>{
    if(scenario=="Voltaic300"){
     var play=new CardPlay{Card=card,Player=p,Target=null,ResultPile=PileType.Exhaust,Resources=default,IsAutoPlay=false,PlayIndex=0,PlayCount=1};
     await (Task)typeof(Voltaic).GetMethod("OnPlay",F)!.Invoke(card,[choice,play])!;
    }else if(anthonyCard!=null){
     // 让输入发生在动作开头的真实 Cmd 等待中，再执行模组原始触发与伤害管线。
     await Cmd.Wait(.1f);
     var execute=Find("AutoAnthony.ChaosOperationExecutor").GetMethods(F).Single(m=>m.Name=="ExecuteTriggered"&&m.GetParameters()[0].ParameterType==Find("AutoAnthony.ChaosCardModel"));
     await (Task)execute.Invoke(null,[anthonyCard,0,choice,null,null,null,0m,null])!;
     hits=damageEvents.Count;targets=damageEvents.Select(e=>e.Target.ToString()).ToArray();
     Check("actual Anthony trigger retains two damage per hit skip="+skip,damageEvents.All(e=>e.Amount==2));
    }else{
     var attack=await DamageCmd.Attack(2).FromCard(card,null).TargetingRandomOpponents(combat).WithHitCount(300).WithWaitBeforeHit(.02f,.03f).Execute(choice);
     var damage=attack.Results.SelectMany(r=>r).ToArray();hits=damage.Length;targets=damage.Select(r=>enemies.IndexOf(r.Receiver).ToString()).ToArray();
     Check("individual two-damage results preserved skip="+skip,damage.All(r=>r.UnblockedDamage==2));
    }
   });
   action.OnEnqueued(_=>{},1);AccessTools.Property(typeof(ActionExecutor),nameof(ActionExecutor.CurrentlyRunningAction)).SetValue(executor,action);
   int waits=Pack.SkipStory.SkippedWaits;var task=action.Execute();var watch=Stopwatch.StartNew();
   GD.Print($"[Aikun020Probe] SKIP_FIXTURE scenario={scenario} skip={skip} completed={task.IsCompleted} pending={Pack.SkipStory.PendingWaits} capacity={p.PlayerCombatState!.OrbQueue.Capacity} mode={SaveManager.Instance.PrefsSave.FastMode} channels={CombatManager.Instance.History.Entries.OfType<OrbChanneledEntry>().Count()}");
   if(skip){
    Check(scenario+" native wait is pending",!task.IsCompleted&&Pack.SkipStory.PendingWaits>0);
    Pack.Settings.Startup["skip_story"]=false;Check("disabled switch rejects skip",!Pack.SkipStory.RequestSkip());Pack.Settings.Startup["skip_story"]=true;
    AccessTools.Property(typeof(GameAction),nameof(GameAction.State)).SetValue(action,GameActionState.GatheringPlayerChoice);
    Check("choice state rejects skip",!Pack.SkipStory.RequestSkip());AccessTools.Property(typeof(GameAction),nameof(GameAction.State)).SetValue(action,GameActionState.Executing);
    var key=Key.Space;bool ctrl=false;
    if(scenario=="RandomDamage300"){
     Pack.Settings.StartupSkipStoryKey="Ctrl+F8";key=Key.F8;ctrl=true;
     Check("rebinding stops default space in resolution",!Pack.SkipStory.HandleKey(new InputEventKey{Keycode=Key.Space,Pressed=true}));
    }
    Check(scenario+" key press starts skip",Pack.SkipStory.HandleKey(new InputEventKey{Keycode=key,CtrlPressed=ctrl,Pressed=true}));
    Check("instant speed override applies only during chain",SaveManager.Instance.PrefsSave.FastMode==FastModeType.Instant);
    Check("skip release consumed",Pack.SkipStory.HandleKey(new InputEventKey{Keycode=key,CtrlPressed=ctrl,Pressed=false}));
    using var cancelled=new CancellationTokenSource();cancelled.Cancel();Check("skipped wait preserves cancellation",Cmd.Wait(2,cancelled.Token).IsCanceled);
    Check("second skip down accepted before focus-loss fixture",Pack.SkipStory.HandleKey(new InputEventKey{Keycode=key,CtrlPressed=ctrl,Pressed=true}));
   }
   await task.WaitAsync(TimeSpan.FromSeconds(12));watch.Stop();
   Check(scenario+" auto resets after action",action.State==GameActionState.Finished&&!Pack.SkipStory.IsSkipping);
   Check(scenario+" restores saved speed and engine time scale",SaveManager.Instance.PrefsSave.FastMode==(skip?FastModeType.Normal:FastModeType.Instant)&&Engine.TimeScale==timeScale);
   if(skip){var code=scenario=="RandomDamage300"?Key.F8:Key.Space;bool ctrl=code==Key.F8;Check("stale skip key does not swallow idle key release",!Pack.SkipStory.HandleKey(new InputEventKey{Keycode=code,CtrlPressed=ctrl,Pressed=true})&&!Pack.SkipStory.HandleKey(new InputEventKey{Keycode=code,CtrlPressed=ctrl,Pressed=false}));}
   // 新动作不继承跳过；正常等待仍需经过真实时间。
   if(skip){
    var next=new ResolutionAction(p.NetId,()=>Cmd.Wait(.12f));next.OnEnqueued(_=>{},2);AccessTools.Property(typeof(ActionExecutor),nameof(ActionExecutor.CurrentlyRunningAction)).SetValue(executor,next);
    var following=next.Execute();Check("next action not automatically skipped",!following.IsCompleted&&!Pack.SkipStory.IsSkipping);await following;
   }
   return new(scenario,skip,enemies.Select(e=>e.CurrentHp).ToArray(),CombatManager.Instance.History.Entries.OfType<OrbChanneledEntry>().Count(),p.PlayerCombatState!.OrbQueue.Orbs.Count,hits,JsonSerializer.Serialize(state.Rng.CombatTargets.ToSerializable(),new JsonSerializerOptions{IncludeFields=true}),targets,watch.Elapsed.TotalSeconds,Pack.SkipStory.SkippedWaits-waits);
  }finally{
   Pack.SkipStory.Reset();nativeAnimationWaitFixture=false;SaveManager.Instance.PrefsSave.FastMode=speed;Pack.Settings.Startup["skip_story"]=enabled;Pack.Settings.StartupSkipStoryKey=binding;
   try{if(RunManager.Instance.DebugOnlyGetState()!=null){CombatManager.Instance.Reset(false);RunManager.Instance.CleanUp();}}
   finally{TestMode.IsOn=oldTest;listeners.SetValue(CombatManager.Instance.StateTracker,frontend);}
  }
 }
 static async Task<CardModel> AnthonyTriggerCard(CombatState combat,Player owner){
  var type=Find("AutoAnthony.ChaosCardModel");var canonical=ModelDb.AllCards.First(c=>type.IsAssignableFrom(c.GetType()));
  var card=combat.CreateCard(canonical,owner);var character=type.GetProperty("Character",F)!.GetValue(card)!.ToString();
  var json=JsonSerializer.Serialize(new{Cost=0,Type="Power",Target="Other",Rarity="Rare",ChineseDescription="在你的回合开始时，随机对敌人造成2点伤害300次。",Tags=Array.Empty<string>(),Character=character,Operations=new[]{
   new{Template="A:turnStart",Scope="AbilityTrigger",ChineseText="在你的回合开始时。",Parameters=new Dictionary<string,int>()},
   new{Template="N:RandomD",Scope="NonTargeted",ChineseText="随机对敌人造成2点伤害300次。",Parameters=new Dictionary<string,int>{{"triggerIndex",0}}}
  }});
  var options=new JsonSerializerOptions();options.Converters.Add(new JsonStringEnumConverter());
  var definition=JsonSerializer.Deserialize(json,Find("ChaosCardGenerator.GeneratedCard"),options)!;
  foreach(var operation in ((System.Collections.IEnumerable)definition.GetType().GetProperty("Operations")!.GetValue(definition)!).Cast<object>()){
   var spec=Find("ChaosCardGenerator.OperationRuntimeSpecCompiler").GetMethod("CompileRequired",F)!.Invoke(null,[operation]);operation.GetType().GetProperty("RuntimeSpec")!.SetValue(operation,spec);
  }
  type.GetMethod("ApplyCapturedDefinition",F)!.Invoke(card,[definition]);await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);
  Check("real Anthony runtime source with captured trigger",type.IsAssignableFrom(card.GetType())&&card.Type==CardType.Power);return card;
 }
}
