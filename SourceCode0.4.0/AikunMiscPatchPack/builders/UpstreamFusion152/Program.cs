using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System.IO.Compression;
using System.Security.Cryptography;
string root=Path.GetFullPath(args[0]);
var resolver=new DefaultAssemblyResolver();
resolver.AddSearchDirectory((System.Environment.GetEnvironmentVariable("STS2_GAME_MANAGED_DIR")??throw new InvalidOperationException("请设置 STS2_GAME_MANAGED_DIR")));
resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
foreach(var directory in Directory.EnumerateFiles((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"*.dll",SearchOption.AllDirectories).Select(p=>Path.GetDirectoryName(p)!).Distinct())resolver.AddSearchDirectory(directory);
IEnumerable<TypeDefinition> Types(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(Types));
MethodDefinition Method(TypeDefinition t,string name)=>t.Methods.Single(m=>m.Name==name);
TypeDefinition CloneHelper(ModuleDefinition m,string name,ModuleDefinition helper)
{
    var src=helper.Types.Single(t=>t.Name==name);
    var dest=new TypeDefinition(src.Namespace,src.Name,src.Attributes,m.TypeSystem.Object);
    m.Types.Add(dest);
    foreach(var sa in src.CustomAttributes.Where(a=>a.AttributeType.FullName=="HarmonyLib.HarmonyPatch"))
    {
        var da=new CustomAttribute(m.ImportReference(sa.Constructor));
        foreach(var a in sa.ConstructorArguments)da.ConstructorArguments.Add(new CustomAttributeArgument(m.ImportReference(a.Type),a.Value is TypeReference t?m.ImportReference(t):a.Value));
        dest.CustomAttributes.Add(da);
    }
    var map=new Dictionary<MethodDefinition,MethodDefinition>();
    foreach(var sm in src.Methods)
    {
        var dm=new MethodDefinition(sm.Name,sm.Attributes,m.ImportReference(sm.ReturnType));dest.Methods.Add(dm);map.Add(sm,dm);
        foreach(var p in sm.Parameters)dm.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,m.ImportReference(p.ParameterType)));
    }
    foreach(var sm in src.Methods)
    {
        var dm=map[sm];dm.Body=new MethodBody(dm){InitLocals=sm.Body.InitLocals,MaxStackSize=sm.Body.MaxStackSize};
        var vs=sm.Body.Variables.ToDictionary(v=>v,v=>new VariableDefinition(m.ImportReference(v.VariableType)));
        foreach(var v in vs.Values)dm.Body.Variables.Add(v);
        var ins=sm.Body.Instructions.ToDictionary(i=>i,i=>Instruction.Create(OpCodes.Nop));
        foreach(var i in sm.Body.Instructions)
        {
            var c=ins[i];c.OpCode=i.OpCode;
            c.Operand=i.Operand switch{
                Instruction t=>ins[t],Instruction[] ts=>ts.Select(t=>ins[t]).ToArray(),VariableDefinition v=>vs[v],ParameterDefinition p=>dm.Parameters[p.Index],
                MethodReference r when r.DeclaringType.FullName==src.FullName=>map[r.Resolve()],
                MethodReference r=>m.ImportReference(r),FieldReference r=>m.ImportReference(r),TypeReference r=>m.ImportReference(r),_=>i.Operand};
            dm.Body.Instructions.Add(c);
        }
        foreach(var e in sm.Body.ExceptionHandlers)dm.Body.ExceptionHandlers.Add(new ExceptionHandler(e.HandlerType){TryStart=ins[e.TryStart],TryEnd=e.TryEnd==null?null:ins[e.TryEnd],HandlerStart=ins[e.HandlerStart],HandlerEnd=e.HandlerEnd==null?null:ins[e.HandlerEnd],FilterStart=e.FilterStart==null?null:ins[e.FilterStart],CatchType=e.CatchType==null?null:m.ImportReference(e.CatchType)});
    }
    return dest;
}

foreach(var spec in new[]{
 (Name:"CardFusion", Hash:"7B6FD1EDB04CF9D1735E1A680FC1CEF7BA9B0DD04D5D700CBC419C7C6D152B74", Helper:"MoreEnchantFusionBridge")}){
 string input=Path.Combine(root,"api/release-blockers/original",spec.Hash,spec.Name+".dll");
 byte[] original=File.ReadAllBytes(input);
 if(Convert.ToHexString(SHA256.HashData(original))!=spec.Hash)throw new Exception("Input hash changed");
 using var module=ModuleDefinition.ReadModule(new MemoryStream(original),new ReaderParameters{AssemblyResolver=resolver});
 using var helper=ModuleDefinition.ReadModule(Path.Combine(root,"variants",spec.Name,"1",spec.Name+".dll"),new ReaderParameters{AssemblyResolver=resolver});
 var method=module.Types.SelectMany(Types).Single(t=>t.FullName==(spec.Name=="AutoAnthony"?"AutoAnthony.ChaosRunDefinitions":"CardFusion.MultiEnchantmentCompat")).Methods.Single(m=>m.Name==(spec.Name=="AutoAnthony"?"CaptureOriginalColorlessCards":"InheritEnchantments"));
 var added=CloneHelper(module,spec.Helper,helper);
 method.Body.SimplifyMacros();
 if(spec.Name=="AutoAnthony"){
  var call=method.Body.Instructions.Single(i=>i.Operand is MethodReference m&&m.Name=="ToArray");
  call.Operand=Method(added,"NativeCards");
 }else{
  var first=method.Body.Instructions[0];var il=method.Body.GetILProcessor();
  foreach(var i in new[]{Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Ldarg_2),Instruction.Create(OpCodes.Call,Method(added,"TryInherit")),Instruction.Create(OpCodes.Brfalse,first),Instruction.Create(OpCodes.Ret)})il.InsertBefore(first,i);
 }
 method.Body.OptimizeMacros();
 if(module.AssemblyReferences.Any(a=>a.Name=="Helper"))throw new Exception("Unexpected helper dependency");
 string output=Path.Combine(root,"variants",spec.Name,spec.Hash[..12],"1");Directory.CreateDirectory(output);
 string dll=Path.Combine(output,spec.Name+".dll");module.Write(dll);
 byte[] patched=File.ReadAllBytes(dll),delta=new byte[Math.Max(original.Length,patched.Length)];
 for(int i=0;i<delta.Length;i++)delta[i]=(byte)((i<original.Length?original[i]:0)^(i<patched.Length?patched[i]:0));
 string resource=spec.Name+"-"+spec.Hash[..12]+"-1.xor.br";
 using(var file=File.Create(Path.Combine(root,"assets",resource)))using(var br=new BrotliStream(file,CompressionLevel.SmallestSize))br.Write(delta);
 Console.WriteLine("PATCH_OK "+spec.Name+" "+Convert.ToHexString(SHA256.HashData(patched)));
}
