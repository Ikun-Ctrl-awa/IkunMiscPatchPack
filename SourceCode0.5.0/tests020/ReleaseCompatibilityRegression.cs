using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static Task ReleaseCompatibilityChecks(){
  Check("release preserves compact fifty-one options",Pack.Options.All.Length==51&&Pack.Options.All.Count(o=>o.Misc)==10&&Pack.Options.All.Count(o=>!o.Misc)==41);
  Check("release excludes startup optimization types",new[]{"StartupTiming","BloodSourceAnalysis","RegentPreload","JmcScan"}.All(n=>typeof(Pack.Entry).Assembly.GetType("IkunMiscPatchPack."+n)==null));
  if(Minimal){
   foreach(var key in new[]{"anthony_pool","fusion_enchants"})Check("release missing optional dependency "+key,Pack.Settings.Status(Pack.Options.All.Single(o=>o.Key==key)).Contains("未装载相关模组"));
   return Task.CompletedTask;
  }
  var originals=new[]{"AutoAnthony","CardFusion"}.Select(name=>{
   var candidates=Pack.RuntimeLoader.Catalog.Where(t=>t.Name==name).ToArray();
   var original=Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),candidates[0].WorkshopRelative);
   var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original)));
   return new{Name=name,OriginalHash=hash,Supported=candidates.Any(t=>t.OriginalHash==hash),SupportedHashes=candidates.Select(t=>t.OriginalHash).ToArray(),States=candidates[0].Options.ToDictionary(k=>k,k=>Pack.RuntimeLoader.States.GetValueOrDefault(k))};
  }).ToArray();
  File.WriteAllText(Output+"/release-compatibility-originals.json",JsonSerializer.Serialize(originals,new JsonSerializerOptions{WriteIndented=true}));
  foreach(var name in new[]{"AutoAnthony","CardFusion"}){
   var assembly=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name==name);
   var candidates=Pack.RuntimeLoader.Catalog.Where(t=>t.Name==name).ToArray();
   var original=Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),candidates[0].WorkshopRelative);
   var originalHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original)));
   var source=candidates.SingleOrDefault(t=>t.OriginalHash==originalHash);
   Check("release current original supported "+name+" hash="+originalHash,source!=null);
   var actual=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
   int mask=Pack.PatchData.Mask(source!,k=>Pack.Settings.Active(k)&&Pack.Options.Eligible(k));
   Check("release loaded exact current variant "+name,actual==source!.Variants.Single(v=>v.Mask==mask).Hash);
   Check("release legacy sources still registered "+name,candidates.Length==3);
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
