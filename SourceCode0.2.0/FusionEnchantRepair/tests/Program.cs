using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves.Runs;

var path=Path.GetFullPath(args[0]);
var dirs=new[]{Path.GetDirectoryName(path)!,@"D:\Games\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64",@"D:\Games\Steam\steamapps\workshop\content\2868840\3737335127\BaseLib",@"D:\Games\Steam\steamapps\workshop\content\2868840\3747575739",@"D:\Games\Steam\steamapps\workshop\content\2868840\3747515922",@"D:\Games\Steam\steamapps\common\Slay the Spire 2\mods\plague_LocalFix"};
AssemblyLoadContext.Default.Resolving+=(ctx,name)=>{var p=dirs.Select(d=>Path.Combine(d,name.Name+".dll")).FirstOrDefault(File.Exists);return p==null?null:ctx.LoadFromAssemblyPath(p);};
var fusion=AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
var more=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("MoreEnchantments"));
var plague=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("plague"));
Run(fusion,more,plague);
static void Run(Assembly fusion,Assembly more,Assembly plague){
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn=true;
 int pass=0,fail=0;void Check(string n,bool ok){Console.WriteLine($"{(ok?"PASS":"FAIL")} {n}");if(ok)pass++;else fail++;}
 var h=new Harmony("local.fusion-enchant.regression");
 foreach(var name in new[]{"LogInfo","LogWarn"})h.Patch(fusion.GetType("CardFusion.MainFile")!.GetMethod(name,F)!,prefix:new HarmonyMethod(typeof(Quiet).GetMethod(nameof(Quiet.Skip))!));
 h.CreateClassProcessor(more.GetType("MoreEnchantmentsMod.MoreEnchantModelDbGetEntryPatch")!).Patch();
 var stack=more.GetType("MoreEnchantmentsMod.MoreEnchantStack")!;
 object? Call(Type t,string n,params object?[] a)=>t.GetMethod(n,F)!.Invoke(null,a);
 var types=new List<Type>{typeof(Bash),typeof(Anger),typeof(DefendIronclad),typeof(Sharp),fusion.GetType("CardFusion.FusedCard")!,more.GetType("MoreEnchantmentsMod.Spark")!,more.GetType("MoreEnchantmentsMod.Warding")!,more.GetType("MoreEnchantmentsMod.ExtraEnchantCarrier")!,plague.GetType("Plague.PlagueVoidEnchantment")!,plague.GetType("Plague.PlagueCompositeEnchantment")!};
 foreach(var t in types){ModelDb.Inject(t);ModelIdSerializationCache.CacheSavedPropertiesForTypeDebug(t);}typeof(ModelIdSerializationCache).GetField("_initialized",F)!.SetValue(null,true);
 var lightweight=more.GetType("MoreEnchantmentsMod.Lightweight")!;ModelDb.Inject(lightweight);ModelIdSerializationCache.CacheSavedPropertiesForTypeDebug(lightweight);
 CardModel Card()=>ModelDb.Card<Bash>().ToMutable();
 EnchantmentModel Enchant(string type,int amount)=>Make(ModelDb.GetById<EnchantmentModel>(ModelDb.GetId(more.GetType("MoreEnchantmentsMod."+type)??plague.GetType("Plague."+type)!)).ToMutable(),amount);
 EnchantmentModel Make(EnchantmentModel e,int amount){e.Amount=amount;return e;}
 void Attach(CardModel c,EnchantmentModel e){if(c.Enchantment==null){c.EnchantInternal(e,e.Amount);e.ModifyCard();}else stack.GetMethod("RestoreExtra",F,null,new[]{typeof(CardModel),typeof(EnchantmentModel),typeof(bool)},null)!.Invoke(null,new object[]{c,e,true});}
 EnchantmentModel[] Read(CardModel c)=>((IEnumerable<EnchantmentModel>)Call(stack,"AllEnchantments",c)!).ToArray();
 void Inherit(CardModel target,params CardModel[] src)=>Call(fusion.GetType("CardFusion.MultiEnchantmentCompat")!,"InheritEnchantments",target,src,"regression");
 CardModel Fused(CardModel[] children){var c=ModelDb.GetById<CardModel>(ModelDb.GetId(fusion.GetType("CardFusion.FusedCard")!)).ToMutable();c.GetType().GetField("_sourceCards",F)!.SetValue(c,children.ToList());return c;}
 var a=Card();var b=Card();Attach(a,Enchant("Spark",3));Attach(a,Enchant("Warding",2));Attach(b,Enchant("Spark",5));Attach(b,Enchant("PlagueVoidEnchantment",4));
 var sourceVoid=Read(b).Last();sourceVoid.GetType().GetProperty("CombatsSeen")!.SetValue(sourceVoid,2);
 var target=Card();Inherit(target,a,b);var inherited=Read(target);
 Check("All four primary/additional enchantments survive",inherited.Length==4);
 Check("Same type on different mothers retains each amount",inherited.Where(e=>e.GetType().Name=="Spark").Select(e=>e.Amount).Order().SequenceEqual(new[]{3,5}));
 Check("Original enchantments remain bound to mother cards",Read(a).All(e=>ReferenceEquals(e.Card,a))&&Read(b).All(e=>ReferenceEquals(e.Card,b)));
 Check("Cloned enchantments bind to fused result",inherited.All(e=>ReferenceEquals(e.Card,target)));
 Check("Per-enchantment persistent state survives",inherited.Any(e=>e.GetType().Name=="PlagueVoidEnchantment"&&(int)e.GetType().GetProperty("CombatsSeen")!.GetValue(e)! ==2));
 var old=Fused(new[]{a,b});Inherit(old,a,b);var c=Card();Attach(c,Enchant("Warding",7));var next=Card();Inherit(next,a,b,c,old);
 Check("Refusion does not duplicate flattened mother enchantments",Read(next).Length==5&&Read(next).Where(e=>e.GetType().Name=="Spark").Sum(e=>e.Amount)==8);
 var legacy=Fused(new[]{a,b});Attach(legacy,Enchant("Spark",3));var recovered=Card();Inherit(recovered,a,b,legacy);
 Check("Legacy single-slot fused card recovers remaining mother enchantments",Read(recovered).Length==4);
 var empty=Card();Inherit(empty,Card(),Card());Check("Unenchanted fusion stays unenchanted",Read(empty).Length==0);
 // 使用提供方的真实保存/恢复路径，进行三次 JSON 往返。
 foreach(var n in new[]{"MoreEnchantCardToSerializablePatch","MoreEnchantCardFromSerializablePatch","MoreEnchantCardClonePreservingMutabilityPatch"})h.CreateClassProcessor(more.GetType("MoreEnchantmentsMod."+n)!).Patch();
 for(int i=0;i<3;i++){
  var json=System.Text.Json.JsonSerializer.Serialize(target.ToSerializable(),new System.Text.Json.JsonSerializerOptions{IncludeFields=true});var save=System.Text.Json.JsonSerializer.Deserialize<SerializableCard>(json,new System.Text.Json.JsonSerializerOptions{IncludeFields=true})!;target=CardModel.FromSerializable(save);
  Console.WriteLine("ROUNDTRIP "+string.Join(',',Read(target).Select(e=>e.GetType().Name+"="+e.Amount)));
  Check("JSON card save roundtrip "+(i+1),Read(target).Length==4&&Read(target).Where(e=>e.GetType().Name=="Spark").Sum(e=>e.Amount)==8);
 }
 var clone=(CardModel)target.ClonePreservingMutability();Check("Combat/deck cloning preserves additional entries independently",Read(clone).Length==4&&Read(clone).Zip(Read(target)).All(pair=>!ReferenceEquals(pair.First,pair.Second)&&ReferenceEquals(pair.First.Card,clone)));
 var costSource=Card();Attach(costSource,Enchant("Lightweight",1));var zero=ModelDb.Card<Anger>().ToMutable();Inherit(zero,costSource);
 Check("Cost enchantment records actual delta on new zero-cost target",(int)Read(zero)[0].GetType().GetProperty("AppliedEnergyCostChange")!.GetValue(Read(zero)[0])! ==0);
 Console.WriteLine($"REGRESSION_RESULT passed={pass} failed={fail}");Environment.ExitCode=fail==0?0:2;
}
static class Quiet{public static bool Skip()=>false;}
