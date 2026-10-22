using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../.."));
if(args.FirstOrDefault()=="extract-snapshot"){
 var meta=JsonNode.Parse(File.ReadAllText(root+"/verification/0.2.0/compat-art-localization-before.json"))!;var snapshot=meta["History"]!.GetValue<string>();
 using var old=ModuleDefinition.ReadModule(snapshot+"/src/bin/Release/net9.0/AikunMiscPatchPack.dll");Directory.CreateDirectory(snapshot+"/assets");
 foreach(var res in old.Resources.OfType<EmbeddedResource>().Where(r=>r.Name.StartsWith("patch."))){string dest=res.Name=="patch.catalog"?snapshot+"/catalog.json":snapshot+"/assets/"+res.Name[6..];File.WriteAllBytes(dest,res.GetResourceData());}
 Console.WriteLine("Archived original embedded catalog and patch assets: "+snapshot);return;
}
var game="D:/Games/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64";
var workshop="D:/Games/Steam/steamapps/workshop/content/2868840";
var resolver=new DefaultAssemblyResolver();foreach(var p in new[]{game,workshop+"/3747602295/compat/0.111.0",workshop+"/3747602295/shared",workshop+"/3737335127/BaseLib",Path.GetDirectoryName(typeof(object).Assembly.Location)!})resolver.AddSearchDirectory(p);
string Sha(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
IEnumerable<TypeDefinition> Types(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(Types));
var original=workshop+"/3766908612/Medo.dll";
var native=ModuleDefinition.ReadModule(game+"/sts2.dll",new ReaderParameters{AssemblyResolver=resolver});
using var module=ModuleDefinition.ReadModule(original,new ReaderParameters{AssemblyResolver=resolver});
object Audit(ModuleDefinition m){
 var missing=new List<string>();int members=0;
 foreach(var r in m.GetMemberReferences()){if(r.DeclaringType is ArrayType)continue;members++;try{if(r.Resolve()==null)missing.Add(r.FullName);}catch{missing.Add(r.FullName);}}
 var overrides=new List<object>();var orphans=new List<string>();
 foreach(var t in m.Types.SelectMany(Types))foreach(var method in t.Methods.Where(x=>x.IsVirtual&&!x.IsNewSlot)){
  bool found=false;var baseRef=t.BaseType;
  while(baseRef!=null){var b=baseRef.Resolve();if(b==null)break;found|=b.Methods.Any(x=>x.Name==method.Name&&x.GenericParameters.Count==method.GenericParameters.Count&&x.ReturnType.FullName==method.ReturnType.FullName&&x.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(method.Parameters.Select(p=>p.ParameterType.FullName)));baseRef=b.BaseType;}
  overrides.Add(new{Method=method.FullName,HasMatchingBase=found});if(!found)orphans.Add(method.FullName);
 }
 var sites=m.Types.SelectMany(Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions.Where(i=>i.Operand is MethodReference r&&missing.Contains(r.FullName)).Select(i=>new{Method=m.FullName,Call=((MethodReference)i.Operand).FullName})).ToArray();
 var savedFields=m.Types.SelectMany(Types).SelectMany(t=>t.Fields).Where(f=>f.CustomAttributes.Any(a=>a.AttributeType.Name=="SavedPropertyAttribute")).Select(f=>f.FullName).ToArray();
 var savedProperties=m.Types.SelectMany(Types).SelectMany(t=>t.Properties).Where(p=>p.CustomAttributes.Any(a=>a.AttributeType.Name=="SavedPropertyAttribute")).Select(p=>p.FullName).ToArray();
 return new{MemberReferences=members,MissingMembers=missing,Overrides=overrides,OrphanOverrides=orphans,MissingCallSites=sites,SavedFields=savedFields,SavedProperties=savedProperties};
}
void Save(string path,object value)=>File.WriteAllText(path,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
if(args.FirstOrDefault()=="audit-yui-cards"){
 resolver.AddSearchDirectory(workshop+"/3747562679");
 var path=workshop+"/3747562908/YUICardExpansion.dll";
 using var cards=ModuleDefinition.ReadModule(path,new ReaderParameters{AssemblyResolver=resolver});
 var report=new{OriginalSha256=Sha(path),NativeSha256=Sha(game+"/sts2.dll"),Audit=Audit(cards)};
 Save(root+"/verification/0.2.0/yui-cards-api-audit.json",report);Console.WriteLine(JsonSerializer.Serialize(report));return;
}
var before=Audit(module);
Save(root+"/verification/0.2.0/medo-api-audit-before.json",new{OriginalSha256=Sha(original),NativeSha256=Sha(game+"/sts2.dll"),Audit=before});
if(args.FirstOrDefault()=="build"){
 var target=native.Types.Single(t=>t.FullName=="MegaCrit.Sts2.Core.Commands.CreatureCmd").Methods.Single(m=>m.Name=="Damage"&&m.Parameters.Count==7&&m.Parameters[1].ParameterType.Name=="Creature"&&m.Parameters[2].ParameterType.FullName=="System.Decimal");
 int calls=0;foreach(var method in module.Types.SelectMany(Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody)){
  method.Body.SimplifyMacros();foreach(var ins in method.Body.Instructions.ToArray())if(ins.Operand is MethodReference r&&r.DeclaringType.FullName==target.DeclaringType.FullName&&r.Name=="Damage"&&r.Parameters.Count==6){method.Body.GetILProcessor().InsertBefore(ins,Instruction.Create(OpCodes.Ldnull));ins.Operand=module.ImportReference(target);calls++;}method.Body.OptimizeMacros();
 }
 if(calls!=2)throw new Exception("未适配的昔涟事件调用数量 "+calls);
 // 当前 SavedProperty 仅支持属性；旧版把三个标记放在字段上，序列化器会静默忽略。
 int saved=0;foreach(var t in module.Types.SelectMany(Types))foreach(var field in t.Fields.ToArray()){
  var attribute=field.CustomAttributes.SingleOrDefault(a=>a.AttributeType.Name=="SavedPropertyAttribute");if(attribute==null)continue;
  var get=new MethodDefinition("get_"+field.Name,MethodAttributes.Public|MethodAttributes.HideBySig|MethodAttributes.SpecialName,field.FieldType);t.Methods.Add(get);var il=get.Body.GetILProcessor();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,field);il.Emit(OpCodes.Ret);
  var set=new MethodDefinition("set_"+field.Name,MethodAttributes.Public|MethodAttributes.HideBySig|MethodAttributes.SpecialName,module.TypeSystem.Void);set.Parameters.Add(new ParameterDefinition("value",ParameterAttributes.None,field.FieldType));t.Methods.Add(set);il=set.Body.GetILProcessor();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Stfld,field);il.Emit(OpCodes.Ret);
  var property=new PropertyDefinition(field.Name,PropertyAttributes.None,field.FieldType){GetMethod=get,SetMethod=set};property.CustomAttributes.Add(attribute);field.CustomAttributes.Remove(attribute);t.Properties.Add(property);saved++;
 }
 if(saved!=3)throw new Exception("未适配的昔涟旧存档字段数量 "+saved);
 var dir=root+"/variants/Medo/1";Directory.CreateDirectory(dir);var output=dir+"/Medo.dll";module.Write(output);
 using var fixedModule=ModuleDefinition.ReadModule(output,new ReaderParameters{AssemblyResolver=resolver});Save(root+"/verification/0.2.0/medo-api-audit-after.json",new{PatchedSha256=Sha(output),CallsAdapted=calls,SavedFieldsAdapted=saved,Audit=Audit(fixedModule)});
 var data=File.ReadAllBytes(original);var patched=File.ReadAllBytes(output);var delta=new byte[Math.Max(data.Length,patched.Length)];for(int i=0;i<delta.Length;i++)delta[i]=(byte)((i<data.Length?data[i]:0)^(i<patched.Length?patched[i]:0));
 File.WriteAllBytes(root+"/delta-raw/Medo-1.xor",delta);using(var f=File.Create(root+"/assets/Medo-1.xor.br"))using(var zip=new BrotliStream(f,CompressionLevel.SmallestSize))zip.Write(delta);
 var catalog=JsonNode.Parse(File.ReadAllText(root+"/catalog.json"))!.AsArray();foreach(var existing in catalog.Where(x=>x!["Name"]!.GetValue<string>()=="Medo").ToArray())catalog.Remove(existing);
 catalog.Add(JsonSerializer.SerializeToNode(new{Name="Medo",OriginalHash=Sha(original),Options=new[]{"medo_events_api"},Variants=new[]{new{Mask=1,Resource="Medo-1.xor.br",Length=patched.Length,Hash=Sha(output)}},WorkshopRelative="3766908612/Medo.dll"}));File.WriteAllText(root+"/catalog.json",catalog.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
 var source=root+"/api/medo-columbina/original";Directory.CreateDirectory(source);File.Copy(original,source+"/Medo.dll",true);
 Console.WriteLine("Medo event API calls adapted="+calls+" hash="+Sha(output));
}else Console.WriteLine(JsonSerializer.Serialize(before));
