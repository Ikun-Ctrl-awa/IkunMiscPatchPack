using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../.."));
var game=@"D:\Games\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64";
var resolver=new DefaultAssemblyResolver();
foreach(var path in new[]{game,Path.GetDirectoryName(typeof(object).Assembly.Location)!,@"D:\Games\Steam\steamapps\workshop\content\2868840\3737335127\BaseLib"})resolver.AddSearchDirectory(path);
using var module=ModuleDefinition.ReadModule(args.Length>1?args[1]:Path.Combine(root,"original/CardFusion.dll"),new ReaderParameters{AssemblyResolver=resolver,InMemory=true});
IEnumerable<TypeDefinition> Types(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(Types));
if(args.FirstOrDefault()=="patch"){
 using var helper=ModuleDefinition.ReadModule(Path.Combine(root,"helper/bin/Debug/net9.0/Helper.dll"));
 var src=helper.Types.Single(t=>t.Name=="MoreEnchantFusionBridge");
 var dst=new TypeDefinition(src.Namespace,src.Name,src.Attributes,module.TypeSystem.Object);module.Types.Add(dst);
 var map=new Dictionary<MethodDefinition,MethodDefinition>();
 foreach(var sm in src.Methods){var dm=new MethodDefinition(sm.Name,sm.Attributes,module.ImportReference(sm.ReturnType));dst.Methods.Add(dm);map.Add(sm,dm);foreach(var p in sm.Parameters)dm.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,module.ImportReference(p.ParameterType)));}
 foreach(var sm in src.Methods){
  var dm=map[sm];dm.Body=new MethodBody(dm){InitLocals=sm.Body.InitLocals,MaxStackSize=sm.Body.MaxStackSize};
  var vars=sm.Body.Variables.ToDictionary(v=>v,v=>new VariableDefinition(module.ImportReference(v.VariableType)));foreach(var v in vars.Values)dm.Body.Variables.Add(v);
  var ins=sm.Body.Instructions.ToDictionary(i=>i,i=>Instruction.Create(OpCodes.Nop));
  foreach(var i in sm.Body.Instructions){var c=ins[i];c.OpCode=i.OpCode;c.Operand=i.Operand switch{
   Instruction t=>ins[t],Instruction[] ts=>ts.Select(t=>ins[t]).ToArray(),VariableDefinition v=>vars[v],ParameterDefinition p=>dm.Parameters[p.Index],
   MethodReference r when r.DeclaringType.FullName==src.FullName=>map[r.Resolve()],MethodReference r=>module.ImportReference(r),FieldReference r=>module.ImportReference(r),TypeReference r=>module.ImportReference(r),_=>i.Operand};dm.Body.Instructions.Add(c);}
  foreach(var e in sm.Body.ExceptionHandlers)dm.Body.ExceptionHandlers.Add(new ExceptionHandler(e.HandlerType){TryStart=ins[e.TryStart],TryEnd=e.TryEnd==null?null:ins[e.TryEnd],HandlerStart=ins[e.HandlerStart],HandlerEnd=e.HandlerEnd==null?null:ins[e.HandlerEnd],FilterStart=e.FilterStart==null?null:ins[e.FilterStart],CatchType=e.CatchType==null?null:module.ImportReference(e.CatchType)});
 }
 var method=module.Types.Single(t=>t.FullName=="CardFusion.MultiEnchantmentCompat").Methods.Single(x=>x.Name=="InheritEnchantments");method.Body.SimplifyMacros();
 var il=method.Body.GetILProcessor();var first=method.Body.Instructions[0];
 foreach(var i in new[]{Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Ldarg_2),Instruction.Create(OpCodes.Call,dst.Methods.Single(x=>x.Name=="TryInherit")),Instruction.Create(OpCodes.Brfalse,first),Instruction.Create(OpCodes.Ret)})il.InsertBefore(first,i);
 foreach(var m in module.Types.SelectMany(Types).SelectMany(t=>t.Methods).Where(x=>x.HasBody))m.Body.OptimizeMacros();
 if(module.AssemblyReferences.Any(r=>r.Name=="Helper"))throw new Exception("Unexpected Helper dependency");
 Directory.CreateDirectory(Path.Combine(root,"patched"));module.Write(Path.Combine(root,"patched/CardFusion.dll"));Console.WriteLine("PATCH_OK InheritEnchantments MoreEnchantments bridge");return;
}
int failures=0,count=0,overrides=0;
foreach(var member in module.GetMemberReferences()){
 if(member.DeclaringType is ArrayType)continue;count++;
 try{if(member.Resolve()==null)throw new Exception("not found");}catch(Exception e){Console.WriteLine("UNRESOLVED "+member.FullName+" "+e.Message);failures++;}
}
foreach(var type in module.Types.SelectMany(Types))foreach(var method in type.Methods.Where(m=>m.IsVirtual&&!m.IsNewSlot&&m.Name!="Finalize")){
 overrides++;bool found=false;
 for(TypeReference? parent=type.BaseType;parent!=null;parent=parent.Resolve().BaseType)if(parent.Resolve().Methods.Any(m=>m.IsVirtual&&m.Name==method.Name&&m.ReturnType.FullName==method.ReturnType.FullName&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(method.Parameters.Select(p=>p.ParameterType.FullName)))){found=true;break;}
 if(!found){Console.WriteLine("ORPHAN_OVERRIDE "+method.FullName);failures++;}
}
Console.WriteLine($"AUDIT members={count} overrides={overrides} failures={failures}");Environment.ExitCode=failures==0?0:2;
