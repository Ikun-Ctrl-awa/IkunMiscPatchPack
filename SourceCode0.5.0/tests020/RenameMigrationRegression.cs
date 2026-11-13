using System.Text.Json;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2RitsuLib.Models.Capabilities;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static string LegacyJson(string text)=>text.Replace("IKUN_MISC_PATCH_PACK_","AIKUN_MISC_PATCH_PACK_").Replace("IkunMiscPatchPack","AikunMiscPatchPack");
 static Task RenameMigrationChecks(){
  int first=passes.Count;
  Check("rename assembly and mod id",typeof(Pack.Entry).Assembly.GetName().Name=="IkunMiscPatchPack"&&Pack.Settings.Id=="IkunMiscPatchPack");
  var types=typeof(Pack.Entry).Assembly.GetTypes().Where(t=>!t.IsAbstract&&typeof(AbstractModel).IsAssignableFrom(t)&&t.Namespace=="IkunMiscPatchPack").ToArray();
  foreach(var type in types){
   var id=ModelDb.GetId(type);if(!id.Entry.StartsWith("IKUN_MISC_PATCH_PACK_"))continue;
   var legacy=ModelId.Deserialize(LegacyJson(id.ToString()));
   Check("rename legacy model id canonical "+type.Name,legacy==id&&!legacy.ToString().Contains("AIKUN_MISC_PATCH_PACK_"));
  }
  foreach(var canonical in new CardModel[]{ModelDb.Card<Pack.DongniCurse>(),ModelDb.Card<Pack.DongniDisgust>(),ModelDb.Card<Pack.LagCurse>(),ModelDb.Card<Pack.SoulRitual>(),ModelDb.Card<Pack.ModForm>()}){
   var card=canonical.ToMutable();card.UpgradeInternal();card.FinalizeUpgradeInternal();
   string json=LegacyJson(JsonSerializer.Serialize(card.ToSerializable(),new JsonSerializerOptions{IncludeFields=true}));
   var restored=CardModel.FromSerializable(JsonSerializer.Deserialize<SerializableCard>(json,new JsonSerializerOptions{IncludeFields=true})!);
   Check("rename old upgraded card restores "+card.GetType().Name,restored.GetType()==card.GetType()&&restored.IsUpgraded&&restored.Id.Entry.StartsWith("IKUN_MISC_PATCH_PACK_"));
  }
  var generated=Pack.ModFormContent.PrepareGenerated(ModelDb.Card<Bash>().ToMutable(),3);
  var data=JsonSerializer.Serialize(generated.ToSerializable(),new JsonSerializerOptions{IncludeFields=true});
  var old=LegacyJson(data);var loaded=CardModel.FromSerializable(JsonSerializer.Deserialize<SerializableCard>(old,new JsonSerializerOptions{IncludeFields=true})!);
  Check("rename old capability keeps generated upgrade limit",loaded.CurrentUpgradeLevel==3&&loaded.Capability<Pack.GeneratedCardUpgrade>()?.UpgradeLimit==3);
  string capabilityId=ModelCapabilityRegistry.GetCapabilityId<Pack.GeneratedCardUpgrade>()!;
  Check("rename old capability factory resolves new id",ModelCapabilityRegistry.TryCreate(LegacyJson(capabilityId),out var capability)&&capability.CapabilityId==capabilityId);
  var bag=(Pack.PortableSleepingBag)ModelDb.Relic<Pack.PortableSleepingBag>().ToMutable();bag.Charges=4;
  string relicJson=LegacyJson(JsonSerializer.Serialize(bag.ToSerializable(),new JsonSerializerOptions{IncludeFields=true}));
  var relic=(Pack.PortableSleepingBag)RelicModel.FromSerializable(JsonSerializer.Deserialize<SerializableRelic>(relicJson,new JsonSerializerOptions{IncludeFields=true})!);
  Check("rename old sleeping bag charges retained",relic.Charges==4&&relic.Id==bag.Id);
  if(!Minimal){
   var lag=ModelDb.Card<Pack.LagCurse>().ToMutable();lag.UpgradeInternal();lag.FinalizeUpgradeInternal();
   var fused=Fuse(lag,ModelDb.Card<Bash>().ToMutable());string original=LagFusionState(fused);
   string fusedJson=LegacyJson(JsonSerializer.Serialize(fused.ToSerializable(),new JsonSerializerOptions{IncludeFields=true}));
   var restored=CardModel.FromSerializable(JsonSerializer.Deserialize<SerializableCard>(fusedJson,new JsonSerializerOptions{IncludeFields=true})!);
   Check("rename old fusion nested sources and upgrades retained",LagFusionState(restored)==original&&LagFusionState(restored).Contains("IKUN_MISC_PATCH_PACK"));
  }
  string source=Path.Combine(Output,"legacy-config"),target=Path.Combine(Output,"new-config");Directory.CreateDirectory(source);
  string oldPath=Path.Combine(source,"options.json"),newPath=Path.Combine(target,"options.json");
  var config=new Pack.PackConfig{ModFormIncludeSelf=true,ModFormIncludeTokens=false};config.Enabled["portable_sleeping_bag"]=false;config.PastChances["boss"]=73;
  var bytes=System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config));File.WriteAllBytes(oldPath,bytes);
  Check("rename config copied without touching old bytes",Pack.LegacyNameMigration.CopyConfiguration(source,target)==1&&File.ReadAllBytes(oldPath).SequenceEqual(bytes));
  var migrated=JsonSerializer.Deserialize<Pack.PackConfig>(File.ReadAllText(newPath))!;
  Check("rename old configuration values preserved",migrated.ModFormIncludeSelf&&!migrated.ModFormIncludeTokens&&!migrated.Get("portable_sleeping_bag")&&migrated.Chance("boss")==73);
  File.WriteAllText(newPath,"{\"Enabled\":{\"portable_sleeping_bag\":true}}");var existing=File.ReadAllBytes(newPath);
  Check("rename migration preserves existing new config",Pack.LegacyNameMigration.CopyConfiguration(source,target)==0&&File.ReadAllBytes(newPath).SequenceEqual(existing)&&File.ReadAllBytes(oldPath).SequenceEqual(bytes));
  Check("rename unrelated identifiers unchanged",Pack.LegacyNameMigration.Normalize("CARD.BASH")=="CARD.BASH"&&Pack.LegacyNameMigration.Normalize("OtherMod.Card")=="OtherMod.Card");
  File.WriteAllText(Output+"/rename-migration-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Checks=passes.Skip(first)},new JsonSerializerOptions{WriteIndented=true}));
  return Task.CompletedTask;
 }
}
