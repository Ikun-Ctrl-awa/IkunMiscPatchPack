using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using HarmonyLib;
var path=Path.GetFullPath(args[0]);
var dirs=new[]{Path.GetDirectoryName(path)!,(System.Environment.GetEnvironmentVariable("STS2_GAME_MANAGED_DIR")??throw new InvalidOperationException("请设置 STS2_GAME_MANAGED_DIR")),Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"3737335127/BaseLib")};
AssemblyLoadContext.Default.Resolving+=(ctx,name)=>{var p=dirs.Select(d=>Path.Combine(d,name.Name+".dll")).FirstOrDefault(File.Exists);return p==null?null:ctx.LoadFromAssemblyPath(p);};
var mod=AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
Run(mod);
static void Run(Assembly mod){
 const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 int pass=0,fail=0,count=0,skipped=0;
 void Check(string n,bool ok){Console.WriteLine($"{(ok?"PASS":"FAIL")} {n}");if(ok)pass++;else fail++;}
 foreach(var t in mod.GetTypes())foreach(var method in t.GetMethods(flags|BindingFlags.DeclaredOnly).Cast<MethodBase>().Concat(t.GetConstructors(flags))){
  if(method.IsAbstract||method.ContainsGenericParameters||method.GetMethodBody()==null){skipped++;continue;}
  try{RuntimeHelpers.PrepareMethod(method.MethodHandle);count++;}catch(Exception e){Console.WriteLine($"JIT_FAIL {method} {e.GetType().Name}: {e.Message}");fail++;}
 }
 Check($"JIT all concrete methods prepared={count} skipped={skipped}",count>500);
 MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn=true;
 if(Environment.GetEnvironmentVariable("PLAGUE_CHECK_PATCHES")=="1"){
  var h=new Harmony("local.plague.patch-registration");
  foreach(var t in mod.GetTypes().Where(t=>t.GetCustomAttributes<HarmonyPatch>().Any())){
   try{var result=h.CreateClassProcessor(t).Patch();Console.WriteLine($"HARMONY_OK {t.Name} targets={result?.Count??0}");}
   catch(Exception e){Console.WriteLine($"HARMONY_FAIL {t.Name} {e}");fail++;}
  }
  Console.WriteLine($"PATCH_REGISTRATION_RESULT failures={fail}");Environment.ExitCode=fail==0?0:2;return;
 }
 // 仅为独立进程测试填充与游戏 Init 相同的反射缓存，不修改游戏文件或存档。
 var cache=typeof(ModelIdSerializationCache);
 foreach(var t in mod.GetTypes().Where(t=>typeof(AbstractModel).IsAssignableFrom(t)&&!t.IsAbstract))ModelIdSerializationCache.CacheSavedPropertiesForTypeDebug(t);
 cache.GetField("_initialized",flags)!.SetValue(null,true);
 foreach(var (type,prop) in new[]{("PlagueVoidEnchantment","CombatsSeen"),("PlagueStackedShadowEnchantment","ExtraUpgradeCount")}){
  var t=mod.GetType("Plague."+type)!;var a=Activator.CreateInstance(t)!;var b=Activator.CreateInstance(t)!;
  t.GetProperty(prop,flags)!.SetValue(a,7);var saved=SavedProperties.FromInternal(a,null);saved!.FillInternal(b);
  Check($"SavedProperty roundtrip {type}.{prop}",(int)t.GetProperty(prop,flags)!.GetValue(b)! ==7);
 }
 var composite=mod.GetType("Plague.PlagueCompositeEnchantment")!;
 Check("Composite private serialized enchantments remains cached",ModelIdSerializationCache.GetJsonPropertiesForType(composite)!.Any(p=>p.Name=="SavedEnchantmentsJson"));
 // 拦截引擎边界，验证真实附魔 OnPlay/Prefix 传递伤害与 CardPlay，而不模拟整场战斗。
 var harmony=new Harmony("local.plague.regression");
 foreach(var damage in typeof(CreatureCmd).GetMethods().Where(m=>m.Name=="Damage"&&m.GetParameters().Length is 6 or 7&&m.GetParameters().Last().ParameterType==typeof(CardPlay)))
  harmony.Patch(damage,prefix:new HarmonyMethod(typeof(DamageCapture).GetMethod(nameof(DamageCapture.Prefix))!));
 var player=(Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
 typeof(Player).GetField("<Creature>k__BackingField",flags)!.SetValue(player,new Creature(player,80,80));
 var card=new MegaCrit.Sts2.Core.Models.Cards.Bash();typeof(AbstractModel).GetProperty("IsMutable",flags)!.SetValue(card,true);card.Owner=player;
 var play=(CardPlay)RuntimeHelpers.GetUninitializedObject(typeof(CardPlay));
 EnchantmentModel Enchant(Type type){var e=(EnchantmentModel)Activator.CreateInstance(type)!;typeof(AbstractModel).GetProperty("IsMutable",flags)!.SetValue(e,true);e.Card=card;e.Amount=3;return e;}
 var blood=Enchant(mod.GetType("Plague.PlagueBloodbathEnchantment")!);
 blood.OnPlay(null!,play).GetAwaiter().GetResult();
 Check("Bloodbath passes damage amount and actual CardPlay",DamageCapture.Amount==3&&ReferenceEquals(DamageCapture.Play,play));
 var corrupt=Enchant(typeof(MegaCrit.Sts2.Core.Models.Enchantments.Corrupted));
 var args=new object?[]{corrupt,null,play,null};mod.GetType("Plague.PlagueCorruptedStackOnPlayPatch")!.GetMethod("Prefix",flags)!.Invoke(null,args);
 ((Task)args[3]!).GetAwaiter().GetResult();
 Check("Stacked Corrupted passes scaled damage and actual CardPlay",DamageCapture.Amount==6&&ReferenceEquals(DamageCapture.Play,play));
 Console.WriteLine($"REGRESSION_RESULT passed={pass} failed={fail}");Environment.ExitCode=fail==0?0:2;
}
static class DamageCapture{
 public static decimal Amount;public static CardPlay? Play;
 public static bool Prefix(object[] __args,ref Task<IEnumerable<DamageResult>> __result){Amount=(decimal)__args[2];Play=(CardPlay?)__args.Last();__result=Task.FromResult<IEnumerable<DamageResult>>(Array.Empty<DamageResult>());return false;}
}
