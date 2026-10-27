using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static Task ReleaseCompatibilityChecks(){
  Check("release preserves compact forty options",Pack.Options.All.Length==40);
  Check("release excludes startup optimization types",new[]{"StartupTiming","BloodSourceAnalysis","RegentPreload","JmcScan"}.All(n=>typeof(Pack.Entry).Assembly.GetType("AikunMiscPatchPack."+n)==null));
  if(Minimal){
   foreach(var key in new[]{"anthony_pool","fusion_enchants"})Check("release missing optional dependency "+key,Pack.Settings.Status(Pack.Options.All.Single(o=>o.Key==key)).Contains("未装载相关模组"));
   return Task.CompletedTask;
  }
  foreach(var name in new[]{"AutoAnthony","CardFusion"}){
   var assembly=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name==name);
   var source=Pack.RuntimeLoader.Catalog.Where(t=>t.Name==name).Single(t=>t.OriginalHash==(name=="AutoAnthony"?"EACE09DAF5DB7873BDC683AD8EF4BB6E223C58E118400CEC1B69D93384D0548D":"10EB2C911D2591EB227308E2F041BB8A47A0D692B3CC3268DFAD4BA36B41F662"));
   var actual=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
   Check("release loaded exact new variant "+name,actual==source.Variants.Single(v=>v.Mask==1).Hash);
   Check("release legacy source still registered "+name,Pack.RuntimeLoader.Catalog.Count(t=>t.Name==name)==2);
   foreach(var key in source.Options)Check("release enabled patch applied "+key,Pack.RuntimeLoader.States.GetValueOrDefault(key)=="已应用。");
  }
  var all=ModelDb.CardPool<ColorlessCardPool>().AllCards.ToArray();
  var helper=Find("WorkshopLocalFixes.NativePoolFix");
  var filtered=(CardModel[])AccessTools.Method(helper,"NativeCards").Invoke(null,[all])!;
  var expected=all.Where(c=>c.GetType().Assembly==typeof(CardModel).Assembly).ToArray();
  Check("release colorless pool contains third-party cards",all.Any(c=>c.GetType().Assembly!=typeof(CardModel).Assembly));
  Check("release Anthony captures exactly native sixty-five",filtered.Length==65&&filtered.SequenceEqual(expected));
  Check("release filtering preserves global mod card pool",ModelDb.CardPool<ColorlessCardPool>().AllCards.SequenceEqual(all));
  var definitions=Find("AutoAnthony.ChaosRunDefinitions");
  AccessTools.Method(definitions,"CaptureOriginalColorlessCards").Invoke(null,null);
  Check("release Anthony real capture excludes mod cards",((CardModel[])AccessTools.Field(definitions,"_originalColorlessCards").GetValue(null)!).SequenceEqual(expected));
  return Task.CompletedTask;
 }
}
