using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using YuiExtra.Hooks;

var path=Path.GetFullPath(args[0]);
var dirs=new[]{Path.GetDirectoryName(path)!,@"D:\Games\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64",@"D:\Games\Steam\steamapps\workshop\content\2868840\3747602295\compat\0.111.0",@"D:\Games\Steam\steamapps\workshop\content\2868840\3747602295\shared"};
AssemblyLoadContext.Default.Resolving+=(ctx,name)=>{var p=dirs.Select(d=>Path.Combine(d,name.Name+".dll")).FirstOrDefault(File.Exists);return p==null?null:ctx.LoadFromAssemblyPath(p);};
var mod=AssemblyLoadContext.Default.LoadFromAssemblyPath(path);Run(mod);
static void Run(Assembly mod){
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn=true;
 int passed=0,failed=0;void Check(string name,bool ok){Console.WriteLine($"{(ok?"PASS":"FAIL")} {name}");if(ok)passed++;else failed++;}
 (Player,RelicModel,ProbeRepeater[]) Fixture(int repeats=1){
  var p=(Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));typeof(Player).GetField("_relics",F)!.SetValue(p,new List<RelicModel>());typeof(Player).GetField("<Creature>k__BackingField",F)!.SetValue(p,new Creature(p,80,80));
  RelicModel Relic(Type t){var r=(RelicModel)RuntimeHelpers.GetUninitializedObject(t);typeof(AbstractModel).GetProperty("IsMutable",F)!.SetValue(r,true);r.Owner=p;((List<RelicModel>)p.Relics).Add(r);return r;}
  var target=Relic(typeof(SwordOfStone));var rs=Enumerable.Range(0,repeats).Select(_=>(ProbeRepeater)Relic(typeof(ProbeRepeater))).ToArray();return(p,target,rs);
 }
 var replay=mod.GetType("YuiExtra.Hooks.YuiRelicTriggerReplay")!;
 IEnumerator<AbstractModel> Begin(RelicModel r){var e=((IEnumerable<AbstractModel>)replay.GetMethod("Wrap",F)!.Invoke(null,new object[]{new AbstractModel[]{r}})!).GetEnumerator();if(!e.MoveNext())throw new Exception("No original hook");replay.GetMethod("MarkFlashed",F)!.Invoke(null,new object[]{r,Array.Empty<Creature>()});return e;}
 {
  var(p,r,rs)=Fixture();using var e=Begin(r);((List<RelicModel>)p.Relics).Remove(r);r.RemoveInternal();
  Check("Transformed SwordOfStone is not replayed after leaving inventory",!e.MoveNext());Check("Invalid replay does not consume ElegantMirror opportunity",rs[0].Calls==0);
 }
 {
  var(p,r,rs)=Fixture();using var e=Begin(r);((List<RelicModel>)p.Relics).Remove(r);
  Check("Detached instance is rejected even without removal marker",!e.MoveNext());
 }
 {
  var(p,r,rs)=Fixture();using var e=Begin(r);Check("Ordinary owned relic still replays",e.MoveNext()&&ReferenceEquals(e.Current,r)&&rs[0].Calls==1);Check("Single repeater yields no unrequested extra replay",!e.MoveNext());
 }
 {
  var(p,r,rs)=Fixture(2);using var e=Begin(r);Check("First of two repeaters remains functional",e.MoveNext());((List<RelicModel>)p.Relics).Remove(r);r.RemoveInternal();
  Check("Removed during first replay skips later snapshot repeaters",!e.MoveNext()&&rs[1].Calls==0);
 }
 {
  var(p,r,rs)=Fixture(2);using var e=Begin(r);rs[0].Callback=()=>{((List<RelicModel>)p.Relics).Remove(rs[1]);rs[1].RemoveInternal();return false;};
  Check("Removed repeater from captured list is not invoked",!e.MoveNext()&&rs[1].Calls==0);
 }
 {
  var(p,r,rs)=Fixture();var(other,otherRelic,otherRepeaters)=Fixture();((List<RelicModel>)p.Relics).Remove(rs[0]);((List<RelicModel>)p.Relics).Add(otherRepeaters[0]);using var e=Begin(r);
  Check("Foreign-owner repeater is not invoked",!e.MoveNext()&&otherRepeaters[0].Calls==0);
 }
 Check("No temporary Helper or self assembly dependency",mod.GetReferencedAssemblies().All(a=>a.Name is not "Helper" and not "YUICore"));
 Console.WriteLine($"REGRESSION_RESULT passed={passed} failed={failed}");Environment.ExitCode=failed==0?0:2;
}
public sealed class ProbeRepeater:RelicModel,IRelicTriggerRepeater{
 public int Calls;public Func<bool>? Callback;public override RelicRarity Rarity=>RelicRarity.Common;
 public bool TryRepeat(RelicModel relic,IReadOnlyList<Creature> targets){Calls++;return Callback?.Invoke()??true;}
}
