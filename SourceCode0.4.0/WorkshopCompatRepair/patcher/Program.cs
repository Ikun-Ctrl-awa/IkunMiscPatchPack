using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
string game = (System.Environment.GetEnvironmentVariable("STS2_GAME_MANAGED_DIR")??throw new InvalidOperationException("请设置 STS2_GAME_MANAGED_DIR"));
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(game);
resolver.AddSearchDirectory(Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"3747602295/compat/0.111.0"));
resolver.AddSearchDirectory(Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"3747602295/shared"));
resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
foreach (var path in Directory.GetFiles(Path.Combine(root,"original"), "*.dll", SearchOption.AllDirectories)) resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);
resolver.AddSearchDirectory(Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"3737335127/BaseLib"));
using var helper = ModuleDefinition.ReadModule(Path.Combine(root,"helper/bin/Debug/net9.0/Helper.dll"), new ReaderParameters{AssemblyResolver=resolver});
using var baselib = ModuleDefinition.ReadModule(Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"3737335127/BaseLib/BaseLib.dll"),new ReaderParameters{AssemblyResolver=resolver});
IEnumerable<TypeDefinition> Types(TypeDefinition t) => new[]{t}.Concat(t.NestedTypes.SelectMany(Types));
TypeDefinition Find(ModuleDefinition m,string name) => m.Types.SelectMany(Types).Single(t=>t.FullName==name);
MethodDefinition Method(TypeDefinition t,string name) => t.Methods.Single(m=>m.Name==name);

TypeDefinition CloneHelper(ModuleDefinition m,string name)
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

void Save(ModuleDefinition m,string name)
{
    foreach(var f in m.Types.SelectMany(Types).SelectMany(t=>t.Methods).Where(t=>t.HasBody)){f.Body.SimplifyMacros();f.Body.OptimizeMacros();}
    Directory.CreateDirectory(Path.Combine(root,"patched",name));
    m.Write(Path.Combine(root,"patched",name,name+".dll"));
    if(m.AssemblyReferences.Any(a=>a.Name=="Helper"))throw new Exception("Unexpected helper dependency");
    Console.WriteLine("PATCH_OK "+name);
}
ModuleDefinition Open(string path)=>ModuleDefinition.ReadModule(Path.Combine(root,"original",path),new ReaderParameters{AssemblyResolver=resolver,InMemory=true});

if(args.FirstOrDefault()=="audit")
{
    foreach(var file in Directory.GetFiles(Path.Combine(root,"patched"),"*.dll",SearchOption.AllDirectories))
    {
        using var m=ModuleDefinition.ReadModule(file,new ReaderParameters{AssemblyResolver=resolver});
        int missing=0;foreach(var r in m.GetMemberReferences()){if(r.DeclaringType is ArrayType)continue;try{if(r.Resolve()==null){Console.WriteLine("MISSING "+r);missing++;}}catch(Exception e){Console.WriteLine("MISSING "+r+" "+e.Message);missing++;}}
        Console.WriteLine($"AUDIT {Path.GetFileName(file)} missing={missing}");if(missing>0)Environment.ExitCode=2;
    }
    return;
}

using(var m=Open("3786611028/AutoAnthony.dll"))
{
    var type=Find(m,"AutoAnthony.ChaosRunDefinitions");
    var method=Method(type,"CaptureOriginalColorlessCards");
    var call=method.Body.Instructions.Single(i=>i.Operand is MethodReference r&&r.Name=="ToArray");
    var fix=CloneHelper(m,"NativePoolFix");
    call.Operand=Method(fix,"NativeCards");
    Save(m,"AutoAnthony");
}
using(var m=Open("3802891146/Gravity.dll"))
{
    var type=Find(m,"Gravity.GravityRunToggle");
    var closure=type.NestedTypes.Single(t=>t.Methods.Any(f=>f.Name.Contains("PositionBelowPortraits")));
    var method=closure.Methods.Single(f=>f.Name.Contains("PositionBelowPortraits"));
    var fix=Method(CloneHelper(m,"TogglePlacementFix"),"KeepVisible");
    // 延迟布局完成后再钳制坐标，保留原版所有布局监听与开关逻辑。
    var last=method.Body.Instructions.Last(i=>i.OpCode==OpCodes.Ret);var il=method.Body.GetILProcessor();
    foreach(string name in new[]{"screen","portraits","toggle"}){il.InsertBefore(last,Instruction.Create(OpCodes.Ldarg_0));il.InsertBefore(last,Instruction.Create(OpCodes.Ldfld,closure.Fields.Single(f=>f.Name==name)));}
    il.InsertBefore(last,Instruction.Create(OpCodes.Call,fix));
    // 原分支可能直接跳到 ret，统一跳到新布局代码入口。
    var start=last.Previous.Previous.Previous.Previous.Previous.Previous.Previous;
    foreach(var i in method.Body.Instructions.Where(i=>i.Operand==last))i.Operand=start;
    Save(m,"Gravity");
}
using(var m=Open("3762574884/FusionExpansionPack.dll"))
{
    var type=Find(m,"FusionExpansionPack.FusionReward");
    var custom=Find(baselib,"BaseLib.Abstracts.CustomReward");
    type.BaseType=m.ImportReference(custom);
    var ctor=type.Methods.Single(t=>t.IsConstructor&&!t.IsStatic);
    var baseCall=ctor.Body.Instructions.Single(i=>i.Operand is MethodReference r&&r.Name==".ctor"&&r.DeclaringType.FullName=="MegaCrit.Sts2.Core.Rewards.Reward");
    baseCall.Operand=m.ImportReference(custom.Methods.Single(t=>t.IsConstructor));
    var defaultCtor=new MethodDefinition(".ctor",MethodAttributes.Public|MethodAttributes.HideBySig|MethodAttributes.SpecialName|MethodAttributes.RTSpecialName,m.TypeSystem.Void);
    type.Methods.Add(defaultCtor);var di=defaultCtor.Body.GetILProcessor();di.Emit(OpCodes.Ldarg_0);di.Emit(OpCodes.Ldnull);di.Emit(OpCodes.Call,ctor);di.Emit(OpCodes.Ret);
    var rewardType=Method(type,"get_RewardType").ReturnType;
    var field=new FieldDefinition("FusionRewardType",FieldAttributes.Public|FieldAttributes.Static,rewardType);type.Fields.Add(field);
    var attrType=Find(baselib,"BaseLib.Patches.Content.CustomEnumAttribute");
    var attr=new CustomAttribute(m.ImportReference(attrType.Methods.Single(t=>t.IsConstructor)));attr.ConstructorArguments.Add(new CustomAttributeArgument(m.TypeSystem.String,null));field.CustomAttributes.Add(attr);
    var getType=Method(type,"get_RewardType");getType.Body=new MethodBody(getType);var ti=getType.Body.GetILProcessor();ti.Emit(OpCodes.Ldsfld,field);ti.Emit(OpCodes.Ret);
    var deserialize=new MethodDefinition("CreateFromSerializable",MethodAttributes.Public|MethodAttributes.Static,m.ImportReference(custom));
    var delegateDefinition=Find(baselib,"BaseLib.Abstracts.CreateRewardFromSave`1");
    var delegateType=new GenericInstanceType(m.ImportReference(delegateDefinition));delegateType.GenericArguments.Add(m.ImportReference(custom));
    var invoke=delegateDefinition.Methods.Single(t=>t.Name=="Invoke");
    foreach(var p in invoke.Parameters)deserialize.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,m.ImportReference(p.ParameterType)));
    type.Methods.Add(deserialize);var si=deserialize.Body.GetILProcessor();si.Emit(OpCodes.Ldarg_1);si.Emit(OpCodes.Newobj,ctor);si.Emit(OpCodes.Ret);
    var get=new MethodDefinition("get_DeserializeMethod",MethodAttributes.Public|MethodAttributes.Virtual|MethodAttributes.HideBySig|MethodAttributes.SpecialName,delegateType);type.Methods.Add(get);
    var dc=new MethodReference(".ctor",m.TypeSystem.Void,delegateType){HasThis=true};dc.Parameters.Add(new ParameterDefinition(m.TypeSystem.Object));dc.Parameters.Add(new ParameterDefinition(m.TypeSystem.IntPtr));
    var gi=get.Body.GetILProcessor();gi.Emit(OpCodes.Ldnull);gi.Emit(OpCodes.Ldftn,deserialize);gi.Emit(OpCodes.Newobj,dc);gi.Emit(OpCodes.Ret);
    type.Properties.Add(new PropertyDefinition("DeserializeMethod",PropertyAttributes.None,delegateType){GetMethod=get});
    var main=Find(m,"FusionExpansionPack.MainFile");var inject=Method(main,"InjectModCards");
    // 战斗结束先登记额外奖励，再让游戏生成战后存档快照；发奖界面出现时登记已经太晚。
    var register=Method(main,"Initialize").Body.Instructions.Single(i=>i.OpCode==OpCodes.Ldstr&&Equals(i.Operand,"OfferRoomEndRewards"));
    register.Operand="OnCombatEnded";
    var factory=inject.Body.Instructions.Single(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName=="MegaCrit.Sts2.Core.Factories.CardFactory"&&r.Name=="CreateForReward");
    factory.Operand=Method(CloneHelper(m,"FusionRewardGenerationFix"),"TryCreate");
    var disassembly=Find(m,"FusionExpansionPack.SuperDisassemblyCard");
    var remove=disassembly.NestedTypes.SelectMany(t=>t.Methods).Where(t=>t.HasBody)
        .SelectMany(t=>t.Body.Instructions).Single(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName=="MegaCrit.Sts2.Core.Commands.CardPileCmd"&&r.Name=="RemoveFromCombat");
    remove.Operand=Method(CloneHelper(m,"DisassemblyRemovalFix"),"RemoveIfStillInCombat");
    Save(m,"FusionExpansionPack");
}
using(var m=Open("3795496596/RelicRewardChoices/RelicRewardChoices.dll"))
{
    var fix=CloneHelper(m,"ChestKeyFix");
    var type=Find(m,"RelicRewardChoices.Rewards.RelicRewardChoiceReward");
    var finalize=Method(type,"FinalizeSkip");var il=finalize.Body.GetILProcessor();
    var setState=finalize.Body.Instructions.Last(i=>i.OpCode==OpCodes.Stfld&&i.Operand is FieldReference f&&f.Name=="_state");
    var next=setState.Next;var skip=Instruction.Create(OpCodes.Nop);
    var playerGetter=m.ImportReference(Find(resolver.Resolve(new AssemblyNameReference("sts2",new Version(1,0,0,0))).MainModule,"MegaCrit.Sts2.Core.Rewards.Reward").Methods.Single(t=>t.Name=="get_Player"));
    var ins=new[]{Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,Method(type,"get_TreasureLifetime")),Instruction.Create(OpCodes.Brfalse,skip),Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldfld,type.Fields.Single(f=>f.Name=="_obtainCompleted")),Instruction.Create(OpCodes.Brtrue,skip),Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,playerGetter),Instruction.Create(OpCodes.Call,Method(fix,"Grant")),skip};
    foreach(var i in ins)il.InsertBefore(next,i);
    var shared=Find(m,"RelicRewardChoices.Multiplayer.SharedTreasureCoordinator");
    var finish=Method(shared,"FinishBarrier");var fi=finish.Body.GetILProcessor();
    var call=finish.Body.Instructions.Single(i=>i.Operand is MethodReference r&&r.Name=="ReturnUnclaimed");
    fi.InsertAfter(call,Instruction.Create(OpCodes.Call,Method(fix,"GrantShared")));fi.InsertAfter(call,Instruction.Create(OpCodes.Ldarg_0));
    Save(m,"RelicRewardChoices");
}
using(var m=Open("3747566068/lib/0.111.0/YuWanCard.Content.dll"))
{
    CloneHelper(m,"PigMinionRegenFix");
    Save(m,"YuWanCard.Content");
}
using(var m=Open("3779056963/DixieRelicSort.dll"))
{
    var fix=Method(CloneHelper(m,"HotkeyNameFix"),"Normalize");
    var rebind=Method(Find(m,"DixieRelicSort.FoldHotkeyManager"),"Rebind");
    var il=rebind.Body.GetILProcessor();var first=rebind.Body.Instructions[0];
    foreach(var i in new[]{Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,fix),Instruction.Create(OpCodes.Starg,rebind.Parameters[0])})il.InsertBefore(first,i);
    foreach(var method in m.Types.Where(t=>t.Namespace!="WorkshopLocalFixes").SelectMany(Types).SelectMany(t=>t.Methods).Where(t=>t.HasBody))
    foreach(var i in method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ldstr&&Equals(i.Operand,"KeypadAdd")))i.Operand="KpAdd";
    Save(m,"DixieRelicSort");
}
