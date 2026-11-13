using Godot;

namespace IkunMiscPatchPack;

// 用原生无损纹理缓存保存透明图标，避免大图、遗物栏和休息选项回退到借用图。
public static class SleepingBagArt {
 static readonly object artLock=new();
 static string? icon,big,outline;
 public static string IconPath {get{Ensure();return EnchantmentIconTexture.ResourcePath(icon!);}}
 public static string BigIconPath {get{Ensure();return EnchantmentIconTexture.ResourcePath(big!);}}
 public static string OutlinePath {get{Ensure();return EnchantmentIconTexture.ResourcePath(outline!);}}
 static void Ensure(){lock(artLock){
  if(icon!=null&&big!=null&&outline!=null&&File.Exists(icon)&&File.Exists(big)&&File.Exists(outline))return;
  using var stream=typeof(PortableSleepingBag).Assembly.GetManifestResourceStream("relicart.portable_sleeping_bag.png")??throw new FileNotFoundException("便携睡袋图标缺失");
  using var bytes=new MemoryStream();stream.CopyTo(bytes);using var source=new Image();
  if(source.LoadPngFromBuffer(bytes.ToArray())!=Error.Ok)throw new InvalidDataException("便携睡袋图标PNG无效");
  source.Convert(Image.Format.Rgba8);
  using var small=Image.CreateFromData(source.GetWidth(),source.GetHeight(),false,Image.Format.Rgba8,source.GetData());
  small.Resize(128,128,Image.Interpolation.Lanczos);icon=EnchantmentIconTexture.Cache(small,"relic-icons");
  source.Resize(256,256,Image.Interpolation.Lanczos);big=EnchantmentIconTexture.Cache(source,"relic-icons");
  // 单独生成透明轮廓：保持同一画布，将不透明区域向外扩两像素，供原生遗物高亮使用。
  var rgba=small.GetData();var mask=new byte[rgba.Length];
  for(int y=0;y<128;y++)for(int x=0;x<128;x++){
   byte alpha=0;
   for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++){
    if(dx*dx+dy*dy>4)continue;int xx=x+dx,yy=y+dy;
    if(xx>=0&&xx<128&&yy>=0&&yy<128)alpha=Math.Max(alpha,rgba[(yy*128+xx)*4+3]);
   }
   int offset=(y*128+x)*4;mask[offset]=mask[offset+1]=mask[offset+2]=255;mask[offset+3]=alpha;
  }
  using var silhouette=Image.CreateFromData(128,128,false,Image.Format.Rgba8,mask);
  outline=EnchantmentIconTexture.Cache(silhouette,"relic-icons");
 }}
}
