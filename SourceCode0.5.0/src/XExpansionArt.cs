using Godot;
using MegaCrit.Sts2.Core.Models;

namespace IkunMiscPatchPack;

public static class XExpansionArt {
 static readonly object artLock=new();
 static readonly Dictionary<Type,string> files=new();
 public static string PortraitPath(CardModel card){lock(artLock){
  var type=card.GetType();
  if(!files.TryGetValue(type,out var file)||!File.Exists(file)){
   string name=card switch{KnifeRain=>"knife-rain",HundredForge=>"hundred-forge",GlassFactory=>"glass-factory",SacrificialCombo=>"sacrificial-combo",SoulRitual=>"soul-ritual",_=>throw new ArgumentException("未知的X扩展卡牌",nameof(card))};
   using var stream=type.Assembly.GetManifestResourceStream("cardart.x."+name+".png")??throw new FileNotFoundException("X扩展卡图缺失："+name);
   using var bytes=new MemoryStream();stream.CopyTo(bytes);using var image=new Image();
   if(image.LoadPngFromBuffer(bytes.ToArray())!=Error.Ok)throw new InvalidDataException("X扩展卡图PNG无效："+name);
   // 原图嵌入DLL，并写成可重新读盘的无损原生纹理；所有等级与预览共享对应卡图。
   files[type]=file=EnchantmentIconTexture.Cache(image,"card-portraits");
  }
  return EnchantmentIconTexture.ResourcePath(file);
 }}
}
