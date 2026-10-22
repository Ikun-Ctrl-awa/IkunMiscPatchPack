"""从此前修补源码生成各独立开关组合；不修改原工程或工坊文件。"""
from pathlib import Path
import subprocess, shutil, json, hashlib

ROOT = Path(__file__).parent
HOME = ROOT.parent
DOTNET = HOME / 'ColumbinaRepair/tools/dotnet/dotnet.exe'
CECIL = HOME / 'ColumbinaRepair/tools/cecil/lib/netstandard2.0'
OUT = ROOT / 'variants'
OUT.mkdir(exist_ok=True)

def generate(name, source, project_root):
    folder = ROOT / 'build-source' / name
    folder.mkdir(parents=True, exist_ok=True)
    source = source.replace('string root = Path.GetFullPath(Path.combine', 'string root = Path.GetFullPath(Path.combine')
    import re
    source = re.sub(r'(?:string|var) root\s*=\s*Path.GetFullPath\(Path.Combine\(AppContext.BaseDirectory,\s*"../../../.."\)\);', 'string root = @"'+str(project_root).replace('\\','/')+'";', source)
    source = source.replace('ReadSymbols = true', 'ReadSymbols = false').replace('WriteSymbols = true', 'WriteSymbols = false')
    source = source.replace('Path.Combine(root, "patched")', 'args[3]').replace('Path.Combine(root,"patched")','args[3]')
    source = re.sub(r'Path.Combine\(root,\s*"patched/([^" ]+)"\)', r'Path.Combine(args[3],"\1")', source)
    (folder/'Program.cs').write_text(source, encoding='utf8')
    (folder/'Builder.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><Reference Include="Mono.Cecil"><HintPath>{CECIL.as_posix()}/Mono.Cecil.dll</HintPath></Reference><Reference Include="Mono.Cecil.Rocks"><HintPath>{CECIL.as_posix()}/Mono.Cecil.Rocks.dll</HintPath></Reference></ItemGroup></Project>''', encoding='utf8')
    subprocess.run([str(DOTNET),'build',str(folder/'Builder.csproj'),'-c','Release','--nologo','-v','q'],check=True)
    return folder/'bin/Release/net9.0/Builder.dll'

def run(builder, source, mask, target, name):
    folder=OUT/target/str(mask);folder.mkdir(parents=True,exist_ok=True)
    subprocess.run([str(DOTNET),str(builder),'patch',str(source),str(mask),str(folder)],check=True)
    return folder/name

def wrap(source, start, end, condition):
    a=source.index(start);b=source.index(end,a)
    return source[:a]+f'if({condition}){{\n'+source[a:b]+'\n}\n'+source[b:]

# 哥伦比娅：旧 API 和月兆减费分别生成，关闭其中一项不影响另一项。
p=HOME/'ColumbinaRepair'
s=(p/'patcher/Program.cs').read_text(encoding='utf-8-sig')
s=wrap(s,'    foreach (var method in types.SelectMany(t => t.Methods).Where(m => m.HasBody)) method.Body.SimplifyMacros();','    // 月兆满辉','(int.Parse(args[2])&1)!=0')
s=wrap(s,'    using (var repairHelper','    foreach (var method in module.Types.SelectMany(Types).SelectMany(t => t.Methods).Where(m => m.HasBody)) method.Body.OptimizeMacros();','(int.Parse(args[2])&2)!=0')
s=s.replace('Console.WriteLine($"PATCH_OK attacks={attacks} gaze={gazes} exhaust={exhausts} block={blocks} paths={paths} hooks={damageHooks}");','Console.WriteLine("PATCH_OK Columbina independent mask="+args[2]);')
b=generate('Columbina',s,p)
for mask in (1,2,3):run(b,p/'original/ColumbinaMod.dll',mask,'ColumbinaMod','ColumbinaMod.dll')

# 更好的进阶：A15 词条与 A12 换幕分别生成。
p=HOME/'BetterAscensionRepair';s=(p/'patcher/Program.cs').read_text(encoding='utf-8-sig')
s=wrap(s,'    foreach (string name in new[]','    using var helper','(int.Parse(args[2])&1)!=0')
s=s.replace('    var restore = runtime.Methods.Single(t => t.Name == "RestoreForLoadedRun");','')
s=s.replace('    // 真正换幕时执行','    var restore = runtime.Methods.Single(t => t.Name == "RestoreForLoadedRun");\n    // 真正换幕时执行')
s=wrap(s,'    var il = freezePatch.Body.GetILProcessor();','    var restore = runtime.Methods.Single','(int.Parse(args[2])&1)!=0')
s=wrap(s,'    var attrition = Type','    // 原版存档不保存','(int.Parse(args[2])&2)!=0')
s=wrap(s,'    var restorePatch = Type','    // 调整新增指令','(int.Parse(args[2])&1)!=0')
s=s.replace('    previous = callRestore;','    var previous = callRestore;')
b=generate('BetterAscension',s,p)
for mask in (1,2,3):run(b,p/'original/BetterAscension.dll',mask,'BetterAscension','BetterAscension.dll')

# 工坊合集按目标生成；融合奖励保存与超级拆解独立控制。
p=HOME/'WorkshopCompatRepair';s=(p/'patcher/Program.cs').read_text(encoding='utf-8-sig')
s=s.replace('Path.Combine(root,"patched",name)', 'Path.Combine(args[3],name)').replace('Path.Combine(root,"patched",name,name+".dll")','Path.Combine(args[3],name,name+".dll")')
for file in ['AutoAnthony','Gravity','FusionExpansionPack','RelicRewardChoices','YuWanCard.Content','DixieRelicSort']:
    import re
    pattern=r'using\(var m=Open\("([^"\n]*'+re.escape(file)+r'\.dll)"\)\)'
    s,n=re.subn(pattern,lambda m:'if(args[4]=="'+file+'")\n'+m.group(0),s)
    assert n==1,(file,n)
s=wrap(s,'    var type=Find(m,"FusionExpansionPack.FusionReward");','    var disassembly=Find(m,"FusionExpansionPack.SuperDisassemblyCard");','(int.Parse(args[2])&1)!=0')
s=wrap(s,'    var disassembly=Find(m,"FusionExpansionPack.SuperDisassemblyCard");','    Save(m,"FusionExpansionPack");','(int.Parse(args[2])&2)!=0')
# Open 仍使用已备份的原始文件，不使用用户目前的本地修复 DLL。
b=generate('Workshop',s,p)
for name in ['AutoAnthony','Gravity','FusionExpansionPack','RelicRewardChoices','YuWanCard.Content','DixieRelicSort']:
    for mask in ((1,2,3) if name=='FusionExpansionPack' else (1,)):
        folder=OUT/name/str(mask);folder.mkdir(parents=True,exist_ok=True)
        subprocess.run([str(DOTNET),str(b),'patch','unused',str(mask),str(folder),name],check=True)
        generated=folder/name/(name+'.dll')
        shutil.copy2(generated,folder/(name+'.dll'))

# YUI：先前两个修复有独立开关，choice-only 也从未修复的原件生成。
p=HOME/'TurnEndRepair';s=(p/'patcher/Program.cs').read_text(encoding='utf-8-sig')
s=s.replace(' if(!m.Types.Any(t=>t.FullName=="YuiExtra.LocalCompatibility.RelicReplayLifetimeFix"))throw new Exception("Previous settlement repair missing");','')
b=generate('YuiChoice',s,p)
run(b,HOME/'CombatSettlementRepair/original/YUICore.dll',2,'YUICore','YUICore.dll')
for mask,source in [(1,HOME/'CombatSettlementRepair/patched/YUICore.dll'),(3,p/'patched/YUICore.dll')]:
    folder=OUT/'YUICore'/str(mask);folder.mkdir(parents=True,exist_ok=True);shutil.copy2(source,folder/'YUICore.dll')
for name,source in [('CardFusion',HOME/'FusionEnchantRepair/patched/CardFusion.dll'),('plague',HOME/'PlagueRepair/patched/plague.dll')]:
    folder=OUT/name/'1';folder.mkdir(parents=True,exist_ok=True);shutil.copy2(source,folder/(name+'.dll'))
# 昔涟：从固定原件生成两个事件调用与三个存档属性的兼容组合。
subprocess.run([str(DOTNET),'run','--project',str(ROOT/'audit020/Audit.csproj'),'-c','Release','--','build'],check=True)
print('ALL_VARIANTS_BUILT')
