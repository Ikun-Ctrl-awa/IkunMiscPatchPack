using Godot;

namespace AikunMiscPatchPack;

// 不将事件背景仅登记到内存中的 res:// 路径：返回菜单的原生清理会 Dispose 纹理。
// 将内嵌 PNG 无损缓存为可真正加载的 CTEX，保持原生释放、线程预加载与重新读档流程。
public static class EmbeddedEventBackgrounds {
 static readonly Dictionary<string,string> paths=new(StringComparer.Ordinal);
 static readonly object gate=new();
 public static string PathFor(string resourceName){
  lock(gate){
   if(paths.TryGetValue(resourceName,out var path)&&File.Exists(ProjectSettings.GlobalizePath(path)))return path;
   using var stream=typeof(EmbeddedEventBackgrounds).Assembly.GetManifestResourceStream(resourceName)??throw new FileNotFoundException(resourceName);
   using var bytes=new MemoryStream();stream.CopyTo(bytes);using var image=new Image();
   if(image.LoadPngFromBuffer(bytes.ToArray())!=Error.Ok)throw new InvalidDataException("事件背景 PNG 无效："+resourceName);
   string file=EnchantmentIconTexture.Cache(image,"event-backgrounds");
   path="user://AikunMiscPatchPack/event-backgrounds/"+System.IO.Path.GetFileName(file);
   paths[resourceName]=path;return path;
  }
 }
}
