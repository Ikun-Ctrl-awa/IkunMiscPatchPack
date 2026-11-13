using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Models.Capabilities;
using STS2RitsuLib.Utils.Persistence;

namespace IkunMiscPatchPack;

public static class LegacyNameMigration {
 public const string OldId="AikunMiscPatchPack";
 const string OldEntry="AIKUN_MISC_PATCH_PACK_",NewEntry="IKUN_MISC_PATCH_PACK_";
 public static string Normalize(string value){
  if(value==OldId||value.StartsWith(OldId+".",StringComparison.Ordinal)||value.StartsWith(OldId+"/",StringComparison.Ordinal))return "IkunMiscPatchPack"+value[OldId.Length..];
  if(value.StartsWith(OldEntry,StringComparison.Ordinal))return NewEntry+value[OldEntry.Length..];
  int split=value.IndexOf('.');
  if(split>=0&&value[(split+1)..].StartsWith(OldEntry,StringComparison.Ordinal))return value[..(split+1)]+NewEntry+value[(split+1+OldEntry.Length)..];
  return value;
 }
 static void NormalizeEntry(ref string __1)=>__1=Normalize(__1);
 static void NormalizeCapability(ref string __0)=>__0=Normalize(__0);
 // 仅复制尚不存在的新配置文件；旧目录及已有新配置均保留。
 public static int CopyConfiguration(string oldDirectory,string newDirectory){
  if(!Directory.Exists(oldDirectory))return 0;
  string root=Path.GetFullPath(newDirectory);int copied=0;
  foreach(var source in Directory.EnumerateFiles(oldDirectory,"*",new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint})){
   string target=Path.GetFullPath(Path.Combine(root,Path.GetRelativePath(oldDirectory,source)));
   if(!target.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("配置迁移路径越界。");
   if(File.Exists(target))continue;
   Directory.CreateDirectory(Path.GetDirectoryName(target)!);
   File.Copy(source,target,false);copied++;
  }
  return copied;
 }
 public static void Install(){
  var h=new Harmony("IkunMiscPatchPack.LegacyNameMigration");
  // 构造时归一化，保证旧卡牌、遗物、事件、能力以及嵌套融合素材再次保存时写出新标识。
  h.Patch(AccessTools.Constructor(typeof(ModelId),[typeof(string),typeof(string)]),prefix:new HarmonyMethod(typeof(LegacyNameMigration),nameof(NormalizeEntry)));
  h.Patch(AccessTools.Method(typeof(ModelId),nameof(ModelId.Deserialize)),prefix:new HarmonyMethod(typeof(LegacyNameMigration),nameof(NormalizeCapability)));
  foreach(string name in new[]{"TryCreate","TryGetCapabilityType"}){
   var method=typeof(ModelCapabilityRegistry).GetMethods().Single(m=>m.Name==name&&!m.IsGenericMethod&&m.GetParameters().Length==2&&m.GetParameters()[0].ParameterType==typeof(string));
   h.Patch(method,prefix:new HarmonyMethod(typeof(LegacyNameMigration),nameof(NormalizeCapability)));
  }
  string oldDirectory=ProjectSettings.GlobalizePath(ProfileManager.GetAccountBasePath(OldId));
  string newDirectory=ProjectSettings.GlobalizePath(ProfileManager.GetAccountBasePath(Settings.Id));
  int copied=CopyConfiguration(oldDirectory,newDirectory);
  GD.Print($"[IkunMiscPatchPack] legacy configuration migration copied={copied}; originals retained");
 }
}
