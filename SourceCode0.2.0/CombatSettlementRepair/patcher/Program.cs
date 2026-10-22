using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../.."));
var game=@"D:\Games\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64";
var resolver=new DefaultAssemblyResolver();
foreach(var d in new[]{game,Path.GetDirectoryName(typeof(object).Assembly.Location)!,Path.Combine(root,"original"),@"D:\Games\Steam\steamapps\workshop\content\2868840\3747602295\compat\0.111.0",@"D:\Games\Steam\steamapps\workshop\content\2868840\3747602295\shared"})resolver.AddSearchDirectory(d);
using var m=ModuleDefinition.ReadModule(args.Length>1?args[1]:Path.Combine(root,"original/YUICore.dll"),new ReaderParameters{AssemblyResolver=resolver,InMemory=true});
IEnumerable<TypeDefinition> Types(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(Types));
if(args.FirstOrDefault()=="patch"){
 using var helper=ModuleDefinition.ReadModule(Path.Combine(root,"helper/bin/Debug/net9.0/Helper.dll"));
 var src=helper.Types.Single(t=>t.Name=="RelicReplayLifetimeFix");
 var dst=new TypeDefinition(src.Namespace,src.Name,src.Attributes,m.TypeSystem.Object);m.Types.Add(dst);
 TypeReference ImportType(TypeReference type)=>m.GetType(type.FullName)??m.ImportReference(type);
 var sm=src.Methods.Single(x=>x.Name=="TryRepeatIfAttached");var dm=new MethodDefinition(sm.Name,sm.Attributes,ImportType(sm.ReturnType));dst.Methods.Add(dm);
 foreach(var p in sm.Parameters)dm.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,ImportType(p.ParameterType)));
 dm.Body=new MethodBody(dm){InitLocals=sm.Body.InitLocals,MaxStackSize=sm.Body.MaxStackSize};
 var vars=sm.Body.Variables.ToDictionary(v=>v,v=>new VariableDefinition(ImportType(v.VariableType)));foreach(var v in vars.Values)dm.Body.Variables.Add(v);
 var ins=sm.Body.Instructions.ToDictionary(i=>i,i=>Instruction.Create(OpCodes.Nop));
 foreach(var i in sm.Body.Instructions){var c=ins[i];c.OpCode=i.OpCode;c.Operand=i.Operand switch{
  Instruction t=>ins[t],Instruction[] ts=>ts.Select(t=>ins[t]).ToArray(),VariableDefinition v=>vars[v],ParameterDefinition p=>dm.Parameters[p.Index],
  MethodReference r when m.GetType(r.DeclaringType.FullName) is TypeDefinition existing=>existing.Methods.Single(x=>x.Name==r.Name&&x.Parameters.Count==r.Parameters.Count),
  MethodReference r=>m.ImportReference(r),FieldReference r=>m.ImportReference(r),TypeReference r=>ImportType(r),_=>i.Operand};dm.Body.Instructions.Add(c);}
 int count=0;
 foreach(var method in m.Types.SelectMany(Types).Where(t=>t!=dst).SelectMany(t=>t.Methods).Where(x=>x.HasBody))foreach(var call in method.Body.Instructions){
  if(call.Operand is MethodReference r&&r.DeclaringType.FullName=="YuiExtra.Hooks.IRelicTriggerRepeater"&&r.Name=="TryRepeat"){
   if(!method.DeclaringType.FullName.Contains("YuiRelicTriggerReplay/"))throw new Exception("Unexpected replay caller");
   call.OpCode=OpCodes.Call;call.Operand=dm;count++;
  }
 }
 if(count!=1)throw new Exception("Unexpected patch count "+count);
 foreach(var method in m.Types.SelectMany(Types).SelectMany(t=>t.Methods).Where(x=>x.HasBody))method.Body.OptimizeMacros();
 if(m.AssemblyReferences.Any(r=>r.Name is "Helper" or "YUICore"))throw new Exception("Unexpected helper/self dependency");
 Directory.CreateDirectory(Path.Combine(root,"patched"));m.Write(Path.Combine(root,"patched/YUICore.dll"));Console.WriteLine("PATCH_OK replay_lifetime_calls="+count);return;
}
int countRefs=0,fail=0;foreach(var member in m.GetMemberReferences()){
 if(member.DeclaringType is ArrayType)continue;countRefs++;
 try{if(member.Resolve()==null)throw new Exception("not found");}catch(Exception e){Console.WriteLine("UNRESOLVED "+member.FullName+" "+e.Message);fail++;}
}
Console.WriteLine($"AUDIT members={countRefs} failures={fail}");Environment.ExitCode=fail==0?0:2;
