using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../.."));
var game=(System.Environment.GetEnvironmentVariable("STS2_GAME_MANAGED_DIR")??throw new InvalidOperationException("请设置 STS2_GAME_MANAGED_DIR"));
var resolver=new DefaultAssemblyResolver();foreach(var d in new[]{game,Path.GetDirectoryName(typeof(object).Assembly.Location)!,Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"3747602295/compat/0.111.0"),Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"3747602295/shared")})resolver.AddSearchDirectory(d);
using var m=ModuleDefinition.ReadModule(args.Length>1?args[1]:Path.Combine(root,"original/YUICore.dll"),new ReaderParameters{AssemblyResolver=resolver,InMemory=true});
IEnumerable<TypeDefinition> Types(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(Types));
if(args.FirstOrDefault()=="patch"){
 using var helper=ModuleDefinition.ReadModule(Path.Combine(root,"helper/bin/Debug/net9.0/Helper.dll"));
 var src=helper.Types.Single(t=>t.Name=="ChoiceContextFix");var sm=src.Methods.Single(x=>x.Name=="Resolve");
 var dst=new TypeDefinition(src.Namespace,src.Name,src.Attributes,m.TypeSystem.Object);m.Types.Add(dst);
 var resolve=new MethodDefinition(sm.Name,sm.Attributes,m.ImportReference(sm.ReturnType));dst.Methods.Add(resolve);foreach(var p in sm.Parameters)resolve.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,m.ImportReference(p.ParameterType)));
 resolve.Body=new MethodBody(resolve){InitLocals=sm.Body.InitLocals,MaxStackSize=sm.Body.MaxStackSize};var vars=sm.Body.Variables.ToDictionary(v=>v,v=>new VariableDefinition(m.ImportReference(v.VariableType)));foreach(var v in vars.Values)resolve.Body.Variables.Add(v);
 var ins=sm.Body.Instructions.ToDictionary(i=>i,i=>Instruction.Create(OpCodes.Nop));foreach(var i in sm.Body.Instructions){var c=ins[i];c.OpCode=i.OpCode;c.Operand=i.Operand switch{
  Instruction t=>ins[t],Instruction[] ts=>ts.Select(t=>ins[t]).ToArray(),VariableDefinition v=>vars[v],ParameterDefinition p=>resolve.Parameters[p.Index],MethodReference r=>m.ImportReference(r),FieldReference r=>m.ImportReference(r),TypeReference r=>m.ImportReference(r),_=>i.Operand};resolve.Body.Instructions.Add(c);}
 var hooks=m.GetType("YuiExtra.Hooks.YuiRelicTriggerHooks");var begin=hooks.Methods.Single(x=>x.Name=="Begin");var scope=hooks.NestedTypes.Single(t=>t.Name=="NotificationScope");
 var current=hooks.Fields.Single(f=>f.Name=="CurrentScope");var context=scope.Fields.Single(f=>f.FieldType.FullName=="MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext");
 var getValue=(MethodReference)begin.Body.Instructions.First(i=>i.Operand is MethodReference r&&r.Name=="get_Value").Operand;
 var setValue=(MethodReference)begin.Body.Instructions.First(i=>i.Operand is MethodReference r&&r.Name=="set_Value").Operand;
 void ContextBody(MethodDefinition method,bool creating){
  method.Body=new MethodBody(method){InitLocals=true,MaxStackSize=3};var parent=new VariableDefinition(scope);method.Body.Variables.Add(parent);var il=method.Body.GetILProcessor();
  il.Emit(OpCodes.Ldsfld,current);il.Emit(OpCodes.Callvirt,getValue);il.Emit(OpCodes.Stloc,parent);
  if(creating)il.Emit(OpCodes.Ldarg_0);else il.Emit(OpCodes.Ldnull);
  il.Emit(OpCodes.Ldloc,parent);var missing=Instruction.Create(OpCodes.Ldnull);var resolved=Instruction.Create(OpCodes.Call,resolve);
  il.Emit(OpCodes.Brfalse,missing);il.Emit(OpCodes.Ldloc,parent);il.Emit(OpCodes.Ldfld,context);il.Emit(OpCodes.Br,resolved);il.Append(missing);il.Append(resolved);
  if(creating){var fresh=new VariableDefinition(scope);method.Body.Variables.Add(fresh);il.Emit(OpCodes.Ldloc,parent);il.Emit(OpCodes.Newobj,scope.Methods.Single(x=>x.IsConstructor&&x.Parameters.Count==2));il.Emit(OpCodes.Stloc,fresh);il.Emit(OpCodes.Ldsfld,current);il.Emit(OpCodes.Ldloc,fresh);il.Emit(OpCodes.Callvirt,setValue);il.Emit(OpCodes.Ldloc,fresh);}
  il.Emit(OpCodes.Ret);
 }
 ContextBody(begin,true);
 var getContext=new MethodDefinition("GetTriggeredEffectChoiceContext",MethodAttributes.Public|MethodAttributes.Static,m.ImportReference(resolve.ReturnType));hooks.Methods.Add(getContext);ContextBody(getContext,false);
 int changed=0;
 foreach(var type in m.Types.SelectMany(Types).Where(t=>t.FullName.Contains("YuiEnergyGainedHookDispatcher")))foreach(var method in type.Methods.Where(x=>x.HasBody))foreach(var i in method.Body.Instructions)
  if(i.OpCode==OpCodes.Newobj&&i.Operand is MethodReference r&&r.DeclaringType.FullName=="MegaCrit.Sts2.Core.GameActions.Multiplayer.ThrowingPlayerChoiceContext"){i.OpCode=OpCodes.Call;i.Operand=getContext;changed++;}
 if(changed!=1)throw new Exception("Unexpected energy context count "+changed);
 if(!m.Types.Any(t=>t.FullName=="YuiExtra.LocalCompatibility.RelicReplayLifetimeFix"))throw new Exception("Previous settlement repair missing");
 foreach(var method in m.Types.SelectMany(Types).SelectMany(t=>t.Methods).Where(x=>x.HasBody))method.Body.OptimizeMacros();
 if(m.AssemblyReferences.Any(a=>a.Name=="Helper"))throw new Exception("Unexpected helper dependency");
 Directory.CreateDirectory(Path.Combine(root,"patched"));m.Write(Path.Combine(root,"patched/YUICore.dll"));Console.WriteLine("PATCH_OK scope_context=1 energy_context=1 previous_replay_fix=preserved");return;
}
int count=0,fail=0;foreach(var r in m.GetMemberReferences()){if(r.DeclaringType is ArrayType)continue;count++;try{if(r.Resolve()==null)throw new Exception("not found");}catch(Exception e){Console.WriteLine("UNRESOLVED "+r.FullName+" "+e.Message);fail++;}}Console.WriteLine($"AUDIT members={count} failures={fail}");Environment.ExitCode=fail==0?0:2;
