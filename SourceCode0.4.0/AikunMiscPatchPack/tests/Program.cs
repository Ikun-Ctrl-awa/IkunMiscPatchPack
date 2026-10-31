using System.Text.Json;
using AikunMiscPatchPack;
using Mono.Cecil;
var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../"));
var workshop=(System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR"));
var inputs=System.Environment.GetEnvironmentVariable("STS2_PATCH_INPUTS_DIR")??throw new InvalidOperationException("请设置 STS2_PATCH_INPUTS_DIR");
var targets=JsonSerializer.Deserialize<PatchTarget[]>(File.ReadAllText(Path.Combine(inputs,"catalog.json")))!;
if(args.FirstOrDefault()=="audit"){
 var resolver=new DefaultAssemblyResolver();foreach(var directory in new[]{(System.Environment.GetEnvironmentVariable("STS2_GAME_MANAGED_DIR")??throw new InvalidOperationException("请设置 STS2_GAME_MANAGED_DIR")),workshop+"/3747602295/compat/0.111.0",workshop+"/3747602295/shared",workshop+"/3737335127/BaseLib",workshop+"/3812834784",Path.GetDirectoryName(typeof(object).Assembly.Location)!})resolver.AddSearchDirectory(directory);
 foreach(var directory in Directory.EnumerateFiles(workshop,"*.dll",SearchOption.AllDirectories).Select(p=>Path.GetDirectoryName(p)!).Distinct())resolver.AddSearchDirectory(directory);
 int failures=0,count=0;foreach(var path in args.Skip(1)){
  using var module=ModuleDefinition.ReadModule(path,new ReaderParameters{AssemblyResolver=resolver});foreach(var reference in module.GetMemberReferences()){
   if(reference.DeclaringType is ArrayType)continue;count++;try{if(reference.Resolve()==null)throw new Exception("not found");}catch(Exception e){failures++;Console.WriteLine("MISSING "+reference+" "+e.Message);}
  }
 }
 Console.WriteLine($"AUDIT members={count} failures={failures}");Environment.ExitCode=failures==0?0:1;return;
}
int checks=0;void Check(string label,bool ok){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
IEnumerable<TypeDefinition> Types(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(Types));
foreach(var group in targets.GroupBy(t=>t.Name)){
 var current=File.ReadAllBytes(Path.Combine(workshop,group.First().WorkshopRelative));
 Check(group.Key+" installed source is explicitly supported",PatchData.SelectSupported(targets,group.Key,PatchData.Hash(current))!=null);
 Check(group.Key+" unknown source rejected",PatchData.SelectSupported(targets,group.Key,new string('0',64))==null);
}
foreach(var target in targets){
 var original=File.ReadAllBytes(Path.Combine(workshop,target.WorkshopRelative));
 // 工坊更新后仍验证原有适配，旧原件夹具必须与发布清单的精确哈希一致。
 if(PatchData.Hash(original)!=target.OriginalHash)original=File.ReadAllBytes(Path.Combine(inputs,"api/release-blockers/original",target.OriginalHash,target.Name+".dll"));
 Check(target.Name+" source fixture hash "+target.OriginalHash[..12],PatchData.Hash(original)==target.OriginalHash);
 Check(target.Name+" exact version selection "+target.OriginalHash[..12],ReferenceEquals(PatchData.SelectSupported(targets,target.Name,PatchData.Hash(original)),target));
 Check(target.Name+" all off selects original",PatchData.Mask(target,_=>false)==0);
 foreach(var v in target.Variants){
  using var compressed=File.OpenRead(Path.Combine(inputs,"assets",v.Resource));var result=PatchData.Apply(original,compressed,target,v);
  var fixedPath=Path.Combine(inputs,"variants",target.Name,target.OriginalHash[..12],v.Mask.ToString(),target.Name+".dll");
  if(!File.Exists(fixedPath))fixedPath=Path.Combine(inputs,"variants",target.Name,v.Mask.ToString(),target.Name+".dll");
  Check(target.Name+" mask="+v.Mask+" roundtrip exact",result.SequenceEqual(File.ReadAllBytes(fixedPath)));
  Check(target.Name+" mask="+v.Mask+" selection independent",PatchData.Mask(target,k=>(v.Mask&(1<<Array.IndexOf(target.Options,k)))!=0)==v.Mask);
  using var module=ModuleDefinition.ReadModule(new MemoryStream(result));var types=module.Types.SelectMany(Types).ToArray();
  bool Has(string name)=>types.Any(t=>t.Name==name);
  bool Calls(string type,string method,string called)=>types.Single(t=>t.FullName==type).Methods.Single(m=>m.Name==method).Body.Instructions.Any(i=>i.Operand is MethodReference r&&r.Name==called);
  if(target.Name=="AutoAnthony")Check("Anthony native-only colorless capture "+target.OriginalHash[..12],Calls("AutoAnthony.ChaosRunDefinitions","CaptureOriginalColorlessCards","NativeCards"));
  if(target.Name=="CardFusion")Check("Fusion MoreEnchantments bridge "+target.OriginalHash[..12],Calls("CardFusion.MultiEnchantmentCompat","InheritEnchantments","TryInherit"));
  if(target.Name=="ColumbinaMod"){
   Check("Columbina API flag "+v.Mask,Has("Beta111")==((v.Mask&1)!=0));Check("Moon flag "+v.Mask,Has("MoonsignCostFix")==((v.Mask&2)!=0));
  }
  if(target.Name=="Medo"){
   var calls=types.SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Select(i=>i.Operand).OfType<MethodReference>().Where(r=>r.DeclaringType.FullName=="MegaCrit.Sts2.Core.Commands.CreatureCmd"&&r.Name=="Damage").ToArray();
   Check("Medo both event damage calls use current CardPlay API",calls.Length==2&&calls.All(r=>r.Parameters.Count==7));
   Check("Medo saved fields promoted to native saved properties",types.SelectMany(t=>t.Properties).Count(p=>p.CustomAttributes.Any(a=>a.AttributeType.Name=="SavedPropertyAttribute"))==3&&!types.SelectMany(t=>t.Fields).Any(f=>f.CustomAttributes.Any(a=>a.AttributeType.Name=="SavedPropertyAttribute")));
  }
  if(target.Name=="BetterAscension"){
   var bane=types.Single(t=>t.Name=="AscendersBaneEtherealPatch");
   Check("Bane independent "+v.Mask,bane.CustomAttributes.Any(a=>a.AttributeType.Name=="HarmonyPatch")==((v.Mask&1)==0));
   var attrition=types.Single(t=>t.Name=="ActAttritionPatch").CustomAttributes.Single(a=>a.AttributeType.Name=="HarmonyPatch");
   Check("Attrition independent "+v.Mask,(string)attrition.ConstructorArguments[1].Value==((v.Mask&2)!=0?"AfterActEntered":"BeforeRoomEntered"));
   Check("Loaded bane restoration independent "+v.Mask,Calls("BetterAscension.Patches.RunConfigRestorePatch","Postfix","InitializeBane")==((v.Mask&1)!=0));
  }
  if(target.Name=="FusionExpansionPack"){
   Check("Reward save independent "+v.Mask,types.Single(t=>t.Name=="FusionReward").BaseType.Name==((v.Mask&1)!=0?"CustomReward":"Reward"));
   Check("Super disassembly independent "+v.Mask,Has("DisassemblyRemovalFix")==((v.Mask&2)!=0));
  }
  if(target.Name=="YUICore"){
   Check("Replay independent "+v.Mask,Has("RelicReplayLifetimeFix")==((v.Mask&1)!=0));Check("Choice independent "+v.Mask,Has("ChoiceContextFix")==((v.Mask&2)!=0));
  }
 }
 bool refused=false;try{using var file=File.OpenRead(Path.Combine(inputs,"assets",target.Variants[0].Resource));PatchData.Apply([1,2,3],file,target,target.Variants[0]);}catch(InvalidDataException){refused=true;}
 Check(target.Name+" changed source refused",refused);
}
using(var pack=ModuleDefinition.ReadModule(Path.Combine(root,"src/bin/Release/net9.0/AikunMiscPatchPack.dll"))){
 var forbidden=targets.Select(t=>t.Name).ToHashSet();Check("No hard references to patched mods",!pack.AssemblyReferences.Any(r=>forbidden.Contains(r.Name)||r.Name is "Medo" or "Act4Heart" or "newsanguo"));
 Check("Exactly one mod initializer",pack.Types.SelectMany(Types).Count(t=>t.CustomAttributes.Any(a=>a.AttributeType.Name=="ModInitializerAttribute"))==1);
}
Console.WriteLine($"TEST_RESULT checks={checks} targets={targets.Length} variants={targets.Sum(t=>t.Variants.Length)} failed=0");
