using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static Task ReleaseCompatibilityChecks(){
  Check("release preserves compact forty-eight options",Pack.Options.All.Length==48&&Pack.Options.All.Count(o=>o.Misc)==8&&Pack.Options.All.Count(o=>!o.Misc)==40);
  Check("release excludes startup optimization types",new[]{"StartupTiming","BloodSourceAnalysis","RegentPreload","JmcScan"}.All(n=>typeof(Pack.Entry).Assembly.GetType("AikunMiscPatchPack."+n)==null));
  if(Minimal){
   foreach(var key in new[]{"anthony_pool","fusion_enchants"})Check("release missing optional dependency "+key,Pack.Settings.Status(Pack.Options.All.Single(o=>o.Key==key)).Contains("未装载相关模组"));
   return Task.CompletedTask;
  }
  foreach(var name in new[]{"AutoAnthony","CardFusion"}){
   var assembly=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name==name);
   var candidates=Pack.RuntimeLoader.Catalog.Where(t=>t.Name==name).ToArray();
   var original=Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),candidates[0].WorkshopRelative);
   var source=candidates.Single(t=>t.OriginalHash==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original))));
   var actual=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
   int mask=Pack.PatchData.Mask(source,k=>Pack.Settings.Active(k)&&Pack.Options.Eligible(k));
   Check("release loaded exact current variant "+name,actual==source.Variants.Single(v=>v.Mask==mask).Hash);
   Check("release legacy sources still registered "+name,candidates.Length==(name=="CardFusion"?3:2));
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
