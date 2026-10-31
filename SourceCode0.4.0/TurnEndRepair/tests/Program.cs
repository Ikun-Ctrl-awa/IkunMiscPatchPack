using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using YuiExtra.Hooks;
var path=Path.GetFullPath(args[0]);
var dirs=new[]{Path.GetDirectoryName(path)!,(System.Environment.GetEnvironmentVariable("STS2_GAME_MANAGED_DIR")??throw new InvalidOperationException("请设置 STS2_GAME_MANAGED_DIR")),Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"3747602295/compat/0.111.0"),Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"3747602295/shared")};
AssemblyLoadContext.Default.Resolving+=(ctx,name)=>{var p=dirs.Select(d=>Path.Combine(d,name.Name+".dll")).FirstOrDefault(File.Exists);return p==null?null:ctx.LoadFromAssemblyPath(p);};
var mod=AssemblyLoadContext.Default.LoadFromAssemblyPath(path);Run(mod);
static void Run(Assembly mod){
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn=true;
 int pass=0,fail=0;void Check(string name,bool ok){Console.WriteLine($"{(ok?"PASS":"FAIL")} {name}");if(ok)pass++;else fail++;}
 var hooks=mod.GetType("YuiExtra.Hooks.YuiRelicTriggerHooks")!;
 object Begin(PlayerChoiceContext? ctx)=>(object)hooks.GetMethod("Begin",F)!.Invoke(null,new object?[]{ctx})!;
 void Cancel(object scope)=>hooks.GetMethod("Cancel",F)!.Invoke(null,new[]{scope});
 PlayerChoiceContext Context(object scope)=>(PlayerChoiceContext)scope.GetType().GetFields(F).Single(f=>f.FieldType==typeof(PlayerChoiceContext)).GetValue(scope)!;
 var p=(Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));typeof(Player).GetField("_relics",F)!.SetValue(p,new List<RelicModel>());
 bool CanChoose(PlayerChoiceContext c){try{c.SignalPlayerChoiceBegun(p,default).GetAwaiter().GetResult();c.SignalPlayerChoiceEnded().GetAwaiter().GetResult();return true;}catch(NotImplementedException){return false;}}
 var root=Begin(null);Check("Root notification supports nested player choice",CanChoose(Context(root)));var child=Begin(null);Check("Nested notification inherits usable parent",ReferenceEquals(Context(child),Context(root)));Cancel(child);Cancel(root);
 var recording=new RecordingContext();var outer=Begin(recording);Check("Existing action context remains identical",ReferenceEquals(Context(outer),recording));var nested=Begin(new ThrowingPlayerChoiceContext());Check("Throwing child prefers usable parent action context",ReferenceEquals(Context(nested),recording));Cancel(nested);
 var explicitGood=new RecordingContext();var explicitScope=Begin(explicitGood);Check("Explicit usable context takes precedence over parent",ReferenceEquals(Context(explicitScope),explicitGood));Cancel(explicitScope);Cancel(outer);
 var throwing=Begin(new ThrowingPlayerChoiceContext());Check("Explicit Throwing context gains a blocking choice fallback",CanChoose(Context(throwing)));Cancel(throwing);
 var recorder=(EnergyListener)RuntimeHelpers.GetUninitializedObject(typeof(EnergyListener));typeof(AbstractModel).GetProperty("IsMutable",F)!.SetValue(recorder,true);recorder.Owner=p;((List<RelicModel>)p.Relics).Add(recorder);
 var dispatch=mod.GetType("YuiExtra.Hooks.YuiEnergyGainedHookDispatcher")!.GetMethod("Dispatch",F)!;
 bool Dispatch(decimal amount){try{((Task)dispatch.Invoke(null,new object[]{p,amount})!).GetAwaiter().GetResult();return true;}catch(NotImplementedException){return false;}}
 Check("Energy notification can perform a real nested player-choice handshake",Dispatch(1)&&recorder.Begun==1&&recorder.Ended==1);
 recorder.Begun=recorder.Ended=0;outer=Begin(recording);Check("Energy notification reuses active action context",Dispatch(1)&&ReferenceEquals(recorder.Seen,recording));Cancel(outer);
 recorder.Begun=recorder.Ended=0;Check("Zero-energy changes do not invoke callbacks",Dispatch(0)&&recorder.Begun==0);
 var last=Begin(null);Check("Cancelled scopes do not leak an earlier action context",!ReferenceEquals(Context(last),recording)&&CanChoose(Context(last)));Cancel(last);
 Check("Previous relic replacement/replay guard remains present",mod.GetType("YuiExtra.LocalCompatibility.RelicReplayLifetimeFix")!=null);
 Console.WriteLine($"REGRESSION_RESULT passed={pass} failed={fail}");Environment.ExitCode=fail==0?0:2;
}
public class RecordingContext:PlayerChoiceContext{
 public override ulong? OwnerId=>123;public override Task SignalPlayerChoiceBegun(Player chooser,PlayerChoiceOptions options)=>Task.CompletedTask;public override Task SignalPlayerChoiceEnded()=>Task.CompletedTask;
}
public sealed class EnergyListener:RelicModel,IAfterEnergyGainedHook{
 public int Begun,Ended;public PlayerChoiceContext? Seen;public override RelicRarity Rarity=>RelicRarity.Common;
 public async Task AfterEnergyGained(PlayerChoiceContext context,Player player,decimal amount){Seen=context;await context.SignalPlayerChoiceBegun(player,default);Begun++;await context.SignalPlayerChoiceEnded();Ended++;}
}
