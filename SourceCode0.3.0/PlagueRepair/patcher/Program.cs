using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../.."));
var game=(System.Environment.GetEnvironmentVariable("STS2_GAME_MANAGED_DIR")??throw new InvalidOperationException("请设置 STS2_GAME_MANAGED_DIR"));
var resolver=new DefaultAssemblyResolver();
foreach(var d in new[]{game,Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"3737335127/BaseLib"),Path.GetDirectoryName(typeof(object).Assembly.Location)!})resolver.AddSearchDirectory(d);
IEnumerable<TypeDefinition> Types(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(Types));
var input=args.Length>1?args[1]:Path.Combine(root,"original/plague.dll");
using var m=ModuleDefinition.ReadModule(input,new ReaderParameters{AssemblyResolver=resolver,InMemory=true});
using var host=ModuleDefinition.ReadModule(Path.Combine(game,"sts2.dll"),new ReaderParameters{AssemblyResolver=resolver});
if(args.FirstOrDefault()=="list"){
 foreach(var t in host.Types.Where(t=>t.Name.Contains("SerializationCache")))Console.WriteLine(t.FullName+"\n"+string.Join('\n',t.Methods.Select(x=>x.FullName)));
 return;
}
if(args.FirstOrDefault()=="patch"){
 var types=m.Types.SelectMany(Types).ToArray();
 var entry=types.Single(t=>t.FullName=="Plague.ModEntry");
 int cache=0,damage=0,choice=0;
 // 当前游戏在 ModelIdSerializationCache.Init 中统一缓存 ModelDb 所有模型的 SavedProperty。
 // 不提前写入调试缓存，以免改变属性编号和联机哈希；只移除已废弃的手动注入。
 foreach(var method in types.SelectMany(t=>t.Methods).Where(x=>x.HasBody)){
  method.Body.SimplifyMacros();
  foreach(var ins in method.Body.Instructions.ToArray())if(ins.Operand is MethodReference r&&r.DeclaringType.FullName=="MegaCrit.Sts2.Core.Saves.Runs.SavedPropertiesTypeCache"){
   if(r.Name!="InjectTypeIntoCache"||r.Parameters.Count!=1)throw new Exception("Unexpected cache call");
   ins.OpCode=OpCodes.Pop;ins.Operand=null;cache++;
  }
 }
 void Before(MethodDefinition method,Instruction at,params Instruction[] added){
  var il=method.Body.GetILProcessor();foreach(var ins in added)il.InsertBefore(at,ins);
  foreach(var ins in method.Body.Instructions){if(ins.Operand==at)ins.Operand=added[0];else if(ins.Operand is Instruction[] labels)for(int i=0;i<labels.Length;i++)if(labels[i]==at)labels[i]=added[0];}
  foreach(var eh in method.Body.ExceptionHandlers){if(eh.TryStart==at)eh.TryStart=added[0];if(eh.TryEnd==at)eh.TryEnd=added[0];if(eh.HandlerStart==at)eh.HandlerStart=added[0];if(eh.HandlerEnd==at)eh.HandlerEnd=added[0];if(eh.FilterStart==at)eh.FilterStart=added[0];}
 }
 var blood=types.Single(t=>t.FullName=="Plague.PlagueBloodbathEnchantment");
 var sm=blood.NestedTypes.Single(t=>t.Name.StartsWith("<OnPlay>"));
 var onPlay=blood.Methods.Single(x=>x.Name=="OnPlay");
 var playField=new FieldDefinition("localCardPlay",FieldAttributes.Public,m.ImportReference(onPlay.Parameters.Single(p=>p.Name=="cardPlay").ParameterType));sm.Fields.Add(playField);
 var ctxStore=onPlay.Body.Instructions.Single(i=>i.OpCode==OpCodes.Stfld&&i.Operand is FieldReference f&&f.Name=="choiceContext");
 var local=onPlay.Body.Variables.Single(v=>v.VariableType.FullName==sm.FullName);
 Before(onPlay,ctxStore.Next,Instruction.Create(OpCodes.Ldloca,local),Instruction.Create(OpCodes.Ldarg,onPlay.Parameters.Single(p=>p.Name=="cardPlay")),Instruction.Create(OpCodes.Stfld,playField));
 foreach(var method in types.SelectMany(t=>t.Methods).Where(x=>x.HasBody))foreach(var ins in method.Body.Instructions.ToArray()){
  if(ins.Operand is not MethodReference r)continue;
  if(r.DeclaringType.FullName=="MegaCrit.Sts2.Core.Commands.CreatureCmd"&&r.Name=="Damage"&&(r.Parameters.Count==5||r.Parameters.Count==6)&&r.Parameters.Last().ParameterType.Name=="CardModel"){
   var updated=host.Types.Single(t=>t.FullName==r.DeclaringType.FullName).Methods.Single(x=>x.Name==r.Name&&x.Parameters.Count==r.Parameters.Count+1&&x.Parameters.Take(r.Parameters.Count).Select(p=>p.ParameterType.FullName).SequenceEqual(r.Parameters.Select(p=>p.ParameterType.FullName)));
   if(method.DeclaringType==sm)Before(method,ins,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldfld,playField));
   else if(method.DeclaringType.Name=="PlagueCorruptedStackOnPlayPatch")Before(method,ins,Instruction.Create(OpCodes.Ldarg,method.Parameters.Single(p=>p.Name=="cardPlay")));
   else if(method.DeclaringType.DeclaringType?.Name=="PlaguePoisonBurstEnchantment")Before(method,ins,Instruction.Create(OpCodes.Ldnull));
   else throw new Exception("Unexpected damage caller "+method.FullName);
   ins.Operand=m.ImportReference(updated);damage++;
  }
  if(r.DeclaringType.FullName=="MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext"&&r.Name=="SignalPlayerChoiceBegun"&&r.Parameters.Count==1){
   var playerField=method.DeclaringType.Fields.Single(f=>f.Name=="player");
   if(ins.Previous.OpCode!=OpCodes.Ldc_I4||!Equals(ins.Previous.Operand,0))throw new Exception("Unexpected choice options");
   Before(method,ins.Previous,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldfld,playerField));
   ins.Operand=m.ImportReference(host.Types.Single(t=>t.FullName==r.DeclaringType.FullName).Methods.Single(x=>x.Name==r.Name&&x.Parameters.Count==2));choice++;
  }
 }
 if(cache!=2||damage!=3||choice!=1)throw new Exception($"Unexpected patch counts {cache}/{damage}/{choice}");
 foreach(var method in types.SelectMany(t=>t.Methods).Where(x=>x.HasBody))method.Body.OptimizeMacros();
 Directory.CreateDirectory(Path.Combine(root,"patched"));m.Write(Path.Combine(root,"patched/plague.dll"));
 Console.WriteLine($"PATCH_OK cache={cache} damage={damage} choice={choice}");return;
}
int fail=0,count=0,overrides=0;
foreach(var member in m.GetMemberReferences()){
 if(member.DeclaringType is ArrayType)continue;count++;
 try{if(member.Resolve()==null)throw new Exception("not found");}catch(Exception e){Console.WriteLine("UNRESOLVED "+member.FullName+" "+e.Message);fail++;}
}
foreach(var type in m.Types.SelectMany(Types))foreach(var method in type.Methods.Where(x=>x.IsVirtual&&!x.IsNewSlot&&x.Name!="Finalize")){
 overrides++;bool found=false;
 for(TypeReference? parent=type.BaseType;parent!=null;parent=parent.Resolve().BaseType)
  if(parent.Resolve().Methods.Any(x=>x.IsVirtual&&x.Name==method.Name&&x.ReturnType.FullName==method.ReturnType.FullName&&x.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(method.Parameters.Select(p=>p.ParameterType.FullName)))){found=true;break;}
 if(!found){Console.WriteLine("ORPHAN_OVERRIDE "+method.FullName);fail++;}
}
Console.WriteLine($"AUDIT members={count} overrides={overrides} failures={fail}");Environment.ExitCode=fail==0?0:2;
