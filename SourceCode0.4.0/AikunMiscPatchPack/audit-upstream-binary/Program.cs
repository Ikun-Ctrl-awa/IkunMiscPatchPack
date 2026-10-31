using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
string root=Path.GetFullPath(args[0]),workshop=(System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR"));
string Sha(byte[] data)=>Convert.ToHexString(SHA256.HashData(data));
IEnumerable<TypeDefinition> Types(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(Types));
Dictionary<string,string> Bodies(ModuleDefinition m)=>m.Types.SelectMany(Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody).ToDictionary(m=>m.FullName,m=>string.Join("\n",m.Body.Instructions.Select(i=>i.ToString())));
var catalog=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"catalog.json"))).RootElement.EnumerateArray().ToArray();var rows=new List<object>();
foreach(var group in catalog.GroupBy(t=>t.GetProperty("Name").GetString())){
 var current=File.ReadAllBytes(Path.Combine(workshop,group.First().GetProperty("WorkshopRelative").GetString()!));string hash=Sha(current);
 var entry=group.FirstOrDefault(t=>t.GetProperty("OriginalHash").GetString()==hash);
 if(entry.ValueKind==JsonValueKind.Undefined){rows.Add(new{Assembly=group.Key,Hash=hash,KnownBuggyOriginal=false,Status="new original requires native controls"});continue;}
 using var original=ModuleDefinition.ReadModule(new MemoryStream(current));var before=Bodies(original);var options=entry.GetProperty("Options").EnumerateArray().Select(v=>v.GetString()!).ToArray();
 for(int index=0;index<options.Length;index++){
  var variant=entry.GetProperty("Variants").EnumerateArray().Single(v=>v.GetProperty("Mask").GetInt32()==1<<index);
  using var br=new BrotliStream(File.OpenRead(Path.Combine(root,"assets",variant.GetProperty("Resource").GetString()!)),CompressionMode.Decompress);using var raw=new MemoryStream();br.CopyTo(raw);byte[] delta=raw.ToArray(),patched=new byte[variant.GetProperty("Length").GetInt32()];
  for(int i=0;i<patched.Length;i++)patched[i]=(byte)(delta[i]^(i<current.Length?current[i]:0));
  if(Sha(patched)!=variant.GetProperty("Hash").GetString())throw new Exception("Delta verification failed");
  using var fixedModule=ModuleDefinition.ReadModule(new MemoryStream(patched));var after=Bodies(fixedModule);
  var changes=before.Where(kv=>after.TryGetValue(kv.Key,out var body)&&body!=kv.Value).Select(kv=>new{Method=kv.Key,OriginalIL=kv.Value,RepairedIL=after[kv.Key]}).ToArray();
  var added=after.Keys.Except(before.Keys).ToArray();
  if(changes.Length==0&&added.Length==0)throw new Exception("Empty repair "+options[index]);
  rows.Add(new{Option=options[index],Assembly=group.Key,OriginalHash=hash,KnownBuggyOriginal=true,ChangedMethods=changes,AddedMethods=added,VerifiedVariantHash=Sha(patched)});
  Console.WriteLine(options[index]+" current=known-buggy changes="+changes.Length+" added="+added.Length);
 }
}
File.WriteAllText(Path.Combine(root,"verification/0.4.0/upstream-audit/binary-controls.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
