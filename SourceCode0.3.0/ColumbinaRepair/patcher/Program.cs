using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

// 只修改已核实的接口调用和资源分隔符，保留原有卡牌数值、资源和模型标识。
string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
string game = (System.Environment.GetEnvironmentVariable("STS2_GAME_MANAGED_DIR")??throw new InvalidOperationException("请设置 STS2_GAME_MANAGED_DIR"));
string baselib = Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"3737335127/BaseLib");
var resolver = new DefaultAssemblyResolver();
foreach (string directory in new[] { game, baselib, Path.Combine(root, "original"), Path.GetDirectoryName(typeof(object).Assembly.Location)! })
    resolver.AddSearchDirectory(directory);
var parameters = new ReaderParameters { AssemblyResolver = resolver, InMemory = true, ReadSymbols = true };
string input = args.Length > 1 ? args[1] : Path.Combine(root, "original/ColumbinaMod.dll");
using var module = ModuleDefinition.ReadModule(input, parameters);
using var host = ModuleDefinition.ReadModule(Path.Combine(game, "sts2.dll"), new ReaderParameters { AssemblyResolver = resolver });
IEnumerable<TypeDefinition> Types(TypeDefinition t) => new[] { t }.Concat(t.NestedTypes.SelectMany(Types));
var types = module.Types.SelectMany(Types).ToArray();
MethodDefinition HostMethod(string type, string name, int count) => host.Types.Single(t => t.FullName == type).Methods.Single(m => m.Name == name && m.Parameters.Count == count);
void Before(MethodDefinition method, Instruction target, params Instruction[] added)
{
    var il = method.Body.GetILProcessor();
    foreach (var item in added) il.InsertBefore(target, item);
    // 保持分支和异常区域入口所对应的逻辑不变。
    foreach (var ins in method.Body.Instructions)
    {
        if (ins.Operand == target) ins.Operand = added[0];
        else if (ins.Operand is Instruction[] labels)
            for (int i = 0; i < labels.Length; i++) if (labels[i] == target) labels[i] = added[0];
    }
    foreach (var region in method.Body.ExceptionHandlers)
    {
        if (region.TryStart == target) region.TryStart = added[0];
        if (region.HandlerStart == target) region.HandlerStart = added[0];
        if (region.FilterStart == target) region.FilterStart = added[0];
        if (region.TryEnd == target) region.TryEnd = added[0];
        if (region.HandlerEnd == target) region.HandlerEnd = added[0];
    }
}
void LoadFieldBefore(MethodDefinition method, Instruction target, FieldDefinition field) => Before(method, target,
    Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, field));

if (args.FirstOrDefault() == "patch")
{
    foreach (var method in types.SelectMany(t => t.Methods).Where(m => m.HasBody)) method.Body.SimplifyMacros();
    var fromCard = module.ImportReference(HostMethod("MegaCrit.Sts2.Core.Commands.Builders.AttackCommand", "FromCard", 2));
    var exhaust = module.ImportReference(HostMethod("MegaCrit.Sts2.Core.Commands.CardCmd", "Exhaust", 4));
    var loseBlock = module.ImportReference(HostMethod("MegaCrit.Sts2.Core.Commands.CreatureCmd", "LoseBlock", 4));
    var gaze = types.Single(t => t.Name == "GazeHelper");
    var gazeEntry = gaze.Methods.Single(m => m.Name == "Execute");
    var gazeState = gaze.NestedTypes.Single(t => t.Name.StartsWith("<Execute>"));
    // 让凝视攻击也携带真实 CardPlay，以便测试版的攻击钩子正确关联本次出牌。
    var playType = fromCard.Parameters[1].ParameterType;
    int damageHooks = 0;
    foreach (var hook in types.SelectMany(t => t.Methods).Where(m => m.Name == "ModifyDamageMultiplicative" && m.Parameters.Count == 5))
    {
        hook.Parameters.Add(new ParameterDefinition("cardPlay", ParameterAttributes.None, playType));
        damageHooks++;
    }
    var gazePlay = new FieldDefinition("compatCardPlay", FieldAttributes.Public, playType);
    gazeState.Fields.Add(gazePlay);
    gazeEntry.Parameters.Add(new ParameterDefinition("compatCardPlay", ParameterAttributes.None, playType));
    var firstInit = gazeEntry.Body.Instructions.First(i => i.OpCode == OpCodes.Ldloc && i.Operand == gazeEntry.Body.Variables[0]);
    Before(gazeEntry, firstInit, Instruction.Create(OpCodes.Ldloc, gazeEntry.Body.Variables[0]), Instruction.Create(OpCodes.Ldarg, gazeEntry.Parameters[4]), Instruction.Create(OpCodes.Stfld, gazePlay));

    var compat = new TypeDefinition("ColumbinaMod.LocalCompatibility", "Beta111", TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
    module.Types.Add(compat);
    // Task<T> 继承 Task；保留原异步状态机的等待方式，仅适配新返回类型。
    var oldExhaust = module.GetMemberReferences().OfType<MethodReference>().Single(m => m.DeclaringType.FullName == "MegaCrit.Sts2.Core.Commands.CardCmd" && m.Name == "Exhaust");
    var exhaustCompat = new MethodDefinition("Exhaust", MethodAttributes.Public | MethodAttributes.Static, oldExhaust.ReturnType);
    foreach (var p in oldExhaust.Parameters) exhaustCompat.Parameters.Add(new ParameterDefinition(p.Name, ParameterAttributes.None, p.ParameterType));
    compat.Methods.Add(exhaustCompat);
    var e = exhaustCompat.Body.GetILProcessor();
    foreach (var p in exhaustCompat.Parameters) e.Append(Instruction.Create(OpCodes.Ldarg, p));
    e.Append(Instruction.Create(OpCodes.Call, exhaust));
    e.Append(Instruction.Create(OpCodes.Ret));

    var blockCompat = new MethodDefinition("LoseBlock", MethodAttributes.Public | MethodAttributes.Static, loseBlock.ReturnType);
    blockCompat.Parameters.Add(new ParameterDefinition("target", ParameterAttributes.None, loseBlock.Parameters[1].ParameterType));
    blockCompat.Parameters.Add(new ParameterDefinition("amount", ParameterAttributes.None, loseBlock.Parameters[2].ParameterType));
    blockCompat.Parameters.Add(new ParameterDefinition("context", ParameterAttributes.None, loseBlock.Parameters[0].ParameterType));
    compat.Methods.Add(blockCompat);
    var b = blockCompat.Body.GetILProcessor();
    b.Emit(OpCodes.Ldarg_2); b.Emit(OpCodes.Ldarg_0); b.Emit(OpCodes.Ldarg_1); b.Emit(OpCodes.Ldnull); b.Emit(OpCodes.Call, loseBlock); b.Emit(OpCodes.Ret);
    // 自身效果消耗格挡，remover=null；仍传递实际选择上下文，支持破盾钩子。
    var replace = module.ImportReference(typeof(string).GetMethod("Replace", new[] { typeof(char), typeof(char) })!);
    int attacks = 0, gazes = 0, exhausts = 0, blocks = 0, paths = 0;
    foreach (var method in types.SelectMany(t => t.Methods).Where(m => m.HasBody))
    foreach (var ins in method.Body.Instructions.ToArray())
    {
        if (ins.Operand is not MethodReference call) continue;
        if (call.DeclaringType.FullName == "MegaCrit.Sts2.Core.Commands.Builders.AttackCommand" && call.Name == "FromCard" && call.Parameters.Count == 1)
        {
            var play = method.DeclaringType.Fields.SingleOrDefault(f => f.FieldType.FullName == playType.FullName)
                ?? throw new InvalidOperationException("Missing CardPlay in " + method.FullName);
            LoadFieldBefore(method, ins, play);
            ins.Operand = fromCard;
            attacks++;
        }
        else if (call.DeclaringType == gaze && call.Name == "Execute")
        {
            var play = method.DeclaringType.Fields.Single(f => f.FieldType.FullName == playType.FullName);
            LoadFieldBefore(method, ins, play);
            ins.Operand = gazeEntry;
            gazes++;
        }
        else if (call.DeclaringType.FullName == "MegaCrit.Sts2.Core.Commands.CardCmd" && call.Name == "Exhaust")
        {
            ins.OpCode = OpCodes.Call; ins.Operand = exhaustCompat; exhausts++;
        }
        else if (call.DeclaringType.FullName == "MegaCrit.Sts2.Core.Commands.CreatureCmd" && call.Name == "LoseBlock" && call.Parameters.Count == 2)
        {
            var context = method.DeclaringType.Fields.Single(f => f.Name == "choiceContext");
            LoadFieldBefore(method, ins, context);
            ins.OpCode = OpCodes.Call; ins.Operand = blockCompat; blocks++;
        }
        else if (call.DeclaringType.FullName == "System.IO.Path" && call.Name == "Join")
        {
            var il = method.Body.GetILProcessor();
            var slash = Instruction.Create(OpCodes.Ldc_I4, (int)'\\');
            var forward = Instruction.Create(OpCodes.Ldc_I4, (int)'/');
            il.InsertAfter(ins, slash); il.InsertAfter(slash, forward); il.InsertAfter(forward, Instruction.Create(OpCodes.Callvirt, replace));
            paths++;
        }
    }
    if (attacks != 25 || gazes != 1 || exhausts != 2 || blocks != 1 || paths != 14 || damageHooks != 3)
        throw new InvalidOperationException($"Unexpected patch counts attacks={attacks} gaze={gazes} exhaust={exhausts} block={blocks} paths={paths} hooks={damageHooks}");
    // 月兆满辉只在确实能够减费的牌中随机选择，防止零费/X费/不能打出的牌吞掉效果。
    using (var repairHelper = ModuleDefinition.ReadModule(Path.Combine(root,"moonsign-20261019/helper/bin/Debug/net9.0/Helper.dll")))
    {
        var src=repairHelper.Types.Single(t=>t.Name=="MoonsignCostFix");
        var dst=new TypeDefinition(src.Namespace,src.Name,src.Attributes,module.TypeSystem.Object);module.Types.Add(dst);
        var map=new Dictionary<MethodDefinition,MethodDefinition>();
        foreach(var sm in src.Methods)
        {
            var dm=new MethodDefinition(sm.Name,sm.Attributes,module.ImportReference(sm.ReturnType));dst.Methods.Add(dm);map[sm]=dm;
            foreach(var p in sm.Parameters)dm.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,module.ImportReference(p.ParameterType)));
        }
        foreach(var sm in src.Methods)
        {
            var dm=map[sm];dm.Body=new MethodBody(dm){InitLocals=sm.Body.InitLocals,MaxStackSize=sm.Body.MaxStackSize};
            var vs=sm.Body.Variables.ToDictionary(v=>v,v=>new VariableDefinition(module.ImportReference(v.VariableType)));
            foreach(var v in vs.Values)dm.Body.Variables.Add(v);
            var ins=sm.Body.Instructions.ToDictionary(i=>i,i=>Instruction.Create(OpCodes.Nop));
            foreach(var i in sm.Body.Instructions)
            {
                var c=ins[i];c.OpCode=i.OpCode;c.Operand=i.Operand switch{
                    Instruction t=>ins[t],Instruction[] ts=>ts.Select(t=>ins[t]).ToArray(),VariableDefinition v=>vs[v],ParameterDefinition p=>dm.Parameters[p.Index],
                    MethodReference r when r.DeclaringType.FullName==src.FullName=>map[r.Resolve()],
                    MethodReference r=>module.ImportReference(r),FieldReference r=>module.ImportReference(r),TypeReference r=>module.ImportReference(r),_=>i.Operand};
                dm.Body.Instructions.Add(c);
            }
            foreach(var e2 in sm.Body.ExceptionHandlers)dm.Body.ExceptionHandlers.Add(new ExceptionHandler(e2.HandlerType){TryStart=ins[e2.TryStart],TryEnd=e2.TryEnd==null?null:ins[e2.TryEnd],HandlerStart=ins[e2.HandlerStart],HandlerEnd=e2.HandlerEnd==null?null:ins[e2.HandlerEnd],FilterStart=e2.FilterStart==null?null:ins[e2.FilterStart],CatchType=e2.CatchType==null?null:module.ImportReference(e2.CatchType)});
        }
        var reduce=types.Single(t=>t.Name=="MoonsignAscendantGleam").Methods.Single(m=>m.Name=="ReduceRandomCard");
        reduce.Body=new MethodBody(reduce);var ri=reduce.Body.GetILProcessor();
        ri.Emit(OpCodes.Ldarg_0);ri.Emit(OpCodes.Ldarg_1);ri.Emit(OpCodes.Call,dst.Methods.Single(m=>m.Name=="Reduce"));ri.Emit(OpCodes.Ret);
    }
    foreach (var method in module.Types.SelectMany(Types).SelectMany(t => t.Methods).Where(m => m.HasBody)) method.Body.OptimizeMacros();
    string destination = Path.Combine(root, "patched");
    Directory.CreateDirectory(destination);
    module.Write(Path.Combine(destination, "ColumbinaMod.dll"), new WriterParameters { WriteSymbols = true });
    Console.WriteLine($"PATCH_OK attacks={attacks} gaze={gazes} exhaust={exhausts} block={blocks} paths={paths} hooks={damageHooks}");
}
else
{
    int failures = 0, checkedCount = 0;
    foreach (var member in module.GetMemberReferences())
    {
        checkedCount++;
        try
        {
            if (member.Resolve() == null) { Console.WriteLine("UNRESOLVED " + member.FullName); failures++; }
        }
        catch (Exception error) { Console.WriteLine("UNRESOLVED " + member.FullName + ": " + error.Message); failures++; }
    }
    // 检查覆写是否仍与当前游戏和 BaseLib 的虚方法槽匹配。
    int overrides = 0;
    foreach (var type in types)
    foreach (var method in type.Methods.Where(m => m.IsVirtual && !m.IsNewSlot && m.Name != "Finalize"))
    {
        overrides++;
        bool found = false;
        for (TypeReference? parent = type.BaseType; parent != null; parent = parent.Resolve().BaseType)
        {
            if (parent.Resolve().Methods.Any(m => m.IsVirtual && m.Name == method.Name && m.ReturnType.FullName == method.ReturnType.FullName &&
                m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(method.Parameters.Select(p => p.ParameterType.FullName))))
            { found = true; break; }
        }
        if (!found) { Console.WriteLine("ORPHAN_OVERRIDE " + method.FullName); failures++; }
    }
    Console.WriteLine($"AUDIT members={checkedCount} overrides={overrides} failures={failures}");
    Environment.ExitCode = failures == 0 ? 0 : 2;
}
