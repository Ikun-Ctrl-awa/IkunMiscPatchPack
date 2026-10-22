using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
string game = @"D:\Games\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64";
var resolver = new DefaultAssemblyResolver();
foreach (var dir in new[] { game, @"D:\Games\Steam\steamapps\workshop\content\2868840\3737335127\BaseLib", @"D:\Games\Steam\steamapps\workshop\content\2868840\3812834784", Path.GetDirectoryName(typeof(object).Assembly.Location)! }) resolver.AddSearchDirectory(dir);
string input = args.Length > 1 ? args[1] : Path.Combine(root, "original/BetterAscension.dll");
using var m = ModuleDefinition.ReadModule(input, new ReaderParameters { AssemblyResolver = resolver, InMemory = true, ReadSymbols = true });
IEnumerable<TypeDefinition> Types(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Types));
TypeDefinition Type(string name) => m.Types.Single(t => t.FullName == name);
if (args.FirstOrDefault() == "patch")
{
    // 撤掉两个会在读取期间修改词条的旧补丁；不改其他补丁类。
    foreach (string name in new[] { "AscendersBaneKeywordsCachePatch", "AscendersBaneEtherealPatch" })
    {
        var type = Type("BetterAscension.Patches." + name);
        var attribute = type.CustomAttributes.Single(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch");
        type.CustomAttributes.Remove(attribute);
    }
    using var helper = ModuleDefinition.ReadModule(Path.Combine(root, "helper/bin/Debug/net9.0/Helper.dll"), new ReaderParameters { AssemblyResolver = resolver });
    var sourceType = helper.Types.Single(t => t.Name == "NewRunKeywordFix");
    var source = sourceType.Methods.Single(t => t.Name == "InitializeBane");
    var fixType = new TypeDefinition(sourceType.Namespace, sourceType.Name, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, m.TypeSystem.Object);
    m.Types.Add(fixType);
    var dest = new MethodDefinition(source.Name, source.Attributes, m.ImportReference(source.ReturnType));
    fixType.Methods.Add(dest);
    foreach (var p in source.Parameters) dest.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, m.ImportReference(p.ParameterType)));
    dest.Body = new MethodBody(dest) { InitLocals = source.Body.InitLocals, MaxStackSize = source.Body.MaxStackSize };
    var variables = source.Body.Variables.ToDictionary(v => v, v => new VariableDefinition(m.ImportReference(v.VariableType)));
    foreach (var v in variables.Values) dest.Body.Variables.Add(v);
    var instructions = source.Body.Instructions.ToDictionary(i => i, i => Instruction.Create(OpCodes.Nop));
    foreach (var i in source.Body.Instructions)
    {
        var cloned = instructions[i]; cloned.OpCode = i.OpCode;
        cloned.Operand = i.Operand switch
        {
            Instruction target => instructions[target],
            Instruction[] targets => targets.Select(t => instructions[t]).ToArray(),
            VariableDefinition v => variables[v],
            ParameterDefinition p => dest.Parameters[p.Index],
            MethodReference method => m.ImportReference(method),
            FieldReference field => m.ImportReference(field),
            TypeReference type => m.ImportReference(type),
            _ => i.Operand
        };
        dest.Body.Instructions.Add(cloned);
    }
    foreach (var region in source.Body.ExceptionHandlers) dest.Body.ExceptionHandlers.Add(new ExceptionHandler(region.HandlerType)
    {
        TryStart = instructions[region.TryStart], TryEnd = region.TryEnd == null ? null : instructions[region.TryEnd],
        HandlerStart = instructions[region.HandlerStart], HandlerEnd = region.HandlerEnd == null ? null : instructions[region.HandlerEnd],
        FilterStart = region.FilterStart == null ? null : instructions[region.FilterStart], CatchType = region.CatchType == null ? null : m.ImportReference(region.CatchType)
    });
    var runtime = Type("BetterAscension.AscensionConfigRuntime");
    var freeze = runtime.Methods.Single(t => t.Name == "FreezeForNewRun");
    var active = Type("BetterAscension.Patches.AscendersBaneEtherealPatch").Methods.Single(t => t.Name == "get_ShouldRemoveEthereal");
    // 插在入口调用返回之后，保证本局配置与当前 RunState 已经就绪；读档入口不执行此操作。
    var freezePatch = Type("BetterAscension.Patches.RunConfigFreezePatch").Methods.Single(t => t.Name == "Postfix");
    var callFreeze = freezePatch.Body.Instructions.Single(i => i.Operand == freeze);
    var stateGetter = freezePatch.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Single(r => r.Name == "get_State");
    var il = freezePatch.Body.GetILProcessor();
    Instruction previous = callFreeze;
    foreach (var i in new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Callvirt, stateGetter), Instruction.Create(OpCodes.Call, active), Instruction.Create(OpCodes.Call, dest) })
    { il.InsertAfter(previous, i); previous = i; }

    // 真正换幕时执行；加载房间不会再次触发换幕钩子。
    var attrition = Type("BetterAscension.Patches.ActAttritionPatch");
    var attr = attrition.CustomAttributes.Single(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch");
    if ((string)attr.ConstructorArguments[1].Value != "BeforeRoomEntered") throw new InvalidOperationException("Unexpected attrition target");
    attr.ConstructorArguments[1] = new CustomAttributeArgument(m.TypeSystem.String, "AfterActEntered");
    var prefix = attrition.Methods.Single(t => t.Name == "Prefix");
    foreach (var i in prefix.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr && ((string)i.Operand).Contains("幕的第一个房间")))
        i.Operand = ((string)i.Operand).Replace("幕的第一个房间", "幕（真实换幕事件）");

    // 为读入的当前幕恢复“已处理”标记，额外抵御其他模组重发换幕钩子的情况。
    var restore = runtime.Methods.Single(t => t.Name == "RestoreForLoadedRun");
    var bookkeeping = Type("BetterAscension.RunBookkeeping");
    var forRun = bookkeeping.Methods.Single(t => t.Name == "For");
    var applied = bookkeeping.NestedTypes.Single(t => t.Name == "Book").Fields.Single(f => f.Name == "AttritionAppliedActs");
    var add = prefix.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Single(r => r.Name == "Add");
    var actIndex = prefix.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Single(r => r.Name == "get_CurrentActIndex");
    var seed = new MethodDefinition("MarkLoadedAct", MethodAttributes.Public | MethodAttributes.Static, m.TypeSystem.Void);
    seed.Parameters.Add(new ParameterDefinition("state", ParameterAttributes.None, restore.Parameters[0].ParameterType));
    fixType.Methods.Add(seed);
    var s = seed.Body.GetILProcessor(); var end = Instruction.Create(OpCodes.Ret);
    s.Emit(OpCodes.Ldarg_0); s.Emit(OpCodes.Brfalse, end);
    s.Emit(OpCodes.Ldarg_0); s.Emit(OpCodes.Call, forRun); s.Emit(OpCodes.Ldfld, applied);
    s.Emit(OpCodes.Ldarg_0); s.Emit(OpCodes.Callvirt, actIndex); s.Emit(OpCodes.Callvirt, add); s.Emit(OpCodes.Pop); s.Append(end);
    var ri = restore.Body.GetILProcessor(); var first = restore.Body.Instructions[0];
    ri.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0)); ri.InsertBefore(first, Instruction.Create(OpCodes.Call, seed));
    // 原版存档不保存临时关键词集合，读档完成配置恢复后重建该牌的初始词条。
    var restorePatch = Type("BetterAscension.Patches.RunConfigRestorePatch").Methods.Single(t => t.Name == "Postfix");
    var callRestore = restorePatch.Body.Instructions.Single(i => i.Operand == restore);
    var restoreIl = restorePatch.Body.GetILProcessor();
    previous = callRestore;
    foreach (var i in new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Callvirt, stateGetter), Instruction.Create(OpCodes.Call, active), Instruction.Create(OpCodes.Call, dest) })
    { restoreIl.InsertAfter(previous, i); previous = i; }
    // 调整新增指令后的短分支距离，避免把原异常处理结构改坏。
    foreach (var method in m.Types.SelectMany(Types).SelectMany(t => t.Methods).Where(t => t.HasBody)) { method.Body.SimplifyMacros(); method.Body.OptimizeMacros(); }
    Directory.CreateDirectory(Path.Combine(root, "patched"));
    m.Write(Path.Combine(root, "patched/BetterAscension.dll"), new WriterParameters { WriteSymbols = true });
    Console.WriteLine("PATCH_OK keyword-read patches removed=2; new-run Bane initialization=1; attrition hook=AfterActEntered; loaded-act guard=1");
}
else
{
    int count = 0, failures = 0;
    foreach (var reference in m.GetMemberReferences())
    {
        count++;
        try { if (reference.Resolve() == null) { Console.WriteLine("MISSING " + reference.FullName); failures++; } }
        catch (Exception error) { Console.WriteLine("MISSING " + reference.FullName + " " + error.Message); failures++; }
    }
    Console.WriteLine($"AUDIT members={count} failures={failures}");
    Environment.ExitCode = failures == 0 ? 0 : 2;
}
