using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
namespace AikunMiscPatchPack;

public static class RuntimeLoader {
 static readonly Assembly Self=typeof(RuntimeLoader).Assembly;
 static readonly Harmony Harmony=new("Aikun.MiscPatchPack.Loader");
 public static readonly Dictionary<string,string> States=new();
 public static PatchTarget[] Catalog{get;private set;}=[];
 public static void Initialize(){
  using var stream=Self.GetManifestResourceStream("patch.catalog")!;Catalog=JsonSerializer.Deserialize<PatchTarget[]>(stream)!;
  Harmony.Patch(AccessTools.Method(typeof(ModManager),"TryLoadMod"),transpiler:new HarmonyMethod(typeof(RuntimeLoader),nameof(Redirect)));
  foreach(var target in Catalog)if(AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name==target.Name))foreach(var key in target.Options)States[key]="加载顺序过晚，补丁未应用；请将本包放在相关模组之前并重启。";
 }
 public static IEnumerable<CodeInstruction> Redirect(IEnumerable<CodeInstruction> source){
  var load=AccessTools.Method(typeof(AssemblyLoadContext),nameof(AssemblyLoadContext.LoadFromAssemblyPath));
  int count=0;foreach(var i in source){if(i.Calls(load)){i.opcode=OpCodes.Call;i.operand=AccessTools.Method(typeof(RuntimeLoader),nameof(Load));count++;}yield return i;}
  if(count!=1)throw new InvalidOperationException("Unexpected assembly loader shape "+count);
 }
 public static Assembly Load(AssemblyLoadContext context,string path){
  string name=Path.GetFileNameWithoutExtension(path);var target=Catalog.FirstOrDefault(t=>t.Name==name);
  if(target!=null){
   int mask=PatchData.Mask(target,k=>Settings.Active(k)&&Options.Eligible(k));
   if(mask!=0){
    try{
     var original=File.ReadAllBytes(path);
     if(PatchData.Hash(original)!=target.OriginalHash){foreach(var k in target.Options)States[k]="目标模组版本未适配，补丁已跳过。";}
     else{
      var variant=target.Variants.Single(v=>v.Mask==mask);string folder=ProjectSettings.GlobalizePath("user://AikunMiscPatchPack/cache/"+variant.Hash);Directory.CreateDirectory(folder);
      string cache=Path.Combine(folder,Path.GetFileName(path));
      if(!File.Exists(cache)||PatchData.Hash(File.ReadAllBytes(cache))!=variant.Hash){using var delta=Self.GetManifestResourceStream("patch."+variant.Resource)!;var patched=PatchData.Apply(original,delta,target,variant);string tmp=cache+".tmp";File.WriteAllBytes(tmp,patched);File.Move(tmp,cache,true);}
      path=cache;foreach(var k in target.Options.Where((k,i)=>(mask&(1<<i))!=0))States[k]="已应用。";
      GD.Print($"[AikunMiscPatchPack] patched {name} mask={mask}; original unchanged");
     }
    }catch(Exception e){foreach(var k in target.Options)States[k]="补丁未应用："+e.Message;GD.Print("[AikunMiscPatchPack] safely skipped "+name+": "+e.Message);}
   }
  }
  var assembly=context.LoadFromAssemblyPath(path);
  // 猪猪加载器先验证工坊原件，再由同一入口加载修补缓存；不绕过其完整性检查。
  if(name=="YuWanCard"){
   var initialize=assembly.GetType("YuWanCard.Loader.LoaderMain")?.GetMethod("Initialize");
   if(initialize!=null)Harmony.Patch(initialize,transpiler:new HarmonyMethod(typeof(RuntimeLoader),nameof(Redirect)));
  }
  return assembly;
 }
}
