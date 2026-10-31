using System.Security.Cryptography;
using Godot;

namespace AikunMiscPatchPack;

// 原生附魔 Icon 要求 CompressedTexture2D。按 Godot 的无损 CTEX 格式装载原图，
// 让图鉴、选牌界面、介绍和卡牌角标直接读取同一纹理，不再借用其他附魔图标。
// 格式依据：https://github.com/godotengine/godot/blob/4.5/editor/import/resource_importer_texture.cpp
public static class EnchantmentIconTexture {
 public static string ResourcePath(string file)=>"user://"+Path.GetRelativePath(OS.GetUserDataDir(),file).Replace('\\','/');
 public static CompressedTexture2D Create(Image image){
  string file=Cache(image,"enchantment-icons");
  var texture=new CompressedTexture2D();var error=texture.Load(file.Replace('\\','/'));
  if(error!=Error.Ok){texture.Dispose();throw new InvalidDataException("附魔图标载入失败："+error);}
  return texture;
 }
 // 背景也使用相同的无损容器，但单独缓存；原生资源加载器可在清理后重新读盘。
 public static string Cache(Image image,string cacheGroup){
  if(image.IsEmpty()||image.HasMipmaps()||image.GetWidth()>ushort.MaxValue||image.GetHeight()>ushort.MaxValue)
   throw new InvalidDataException("附魔图标必须为不带多级纹理的有效小图。");
  using var bytes=new MemoryStream();
  using(var writer=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true)){
   writer.Write("GST2"u8);writer.Write(1u); // CTEX 格式版本。
   writer.Write((uint)image.GetWidth());writer.Write((uint)image.GetHeight());
   writer.Write(0u);writer.Write(-1); // 不启用流式加载、检测或 mipmap。
   writer.Write(0u);writer.Write(0u);writer.Write(0u);
   writer.Write(0u); // DATA_FORMAT_IMAGE，原始像素无损保留颜色与透明度。
   writer.Write((ushort)image.GetWidth());writer.Write((ushort)image.GetHeight());
   writer.Write(0u);writer.Write((uint)image.GetFormat());
   writer.Write(image.GetData());
  }
  var content=bytes.ToArray();
  // 导出的游戏不包含编辑器导入器；只在当前游戏用户目录缓存本包资源。
  string folder=Path.Combine(OS.GetUserDataDir(),"AikunMiscPatchPack",cacheGroup);
  Directory.CreateDirectory(folder);
  string file=Path.Combine(folder,Convert.ToHexString(SHA256.HashData(content))+".ctex");
  if(!File.Exists(file)||!File.ReadAllBytes(file).AsSpan().SequenceEqual(content))File.WriteAllBytes(file,content);
  return file;
 }
}
