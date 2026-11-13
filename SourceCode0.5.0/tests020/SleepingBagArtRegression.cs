using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.addons.mega_text;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static async Task SleepingBagArtChecks(){
  int first=passes.Count;using var stream=typeof(Pack.Entry).Assembly.GetManifestResourceStream("relicart.portable_sleeping_bag.png");
  Check("bag art embedded PNG exists",stream!=null);using var bytes=new MemoryStream();stream!.CopyTo(bytes);string hash=Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
  Check("bag art exact generated PNG embedded",hash=="BFDF2EC63295D4C32B21381FFD1B4BC07240144502B5CCE83C1630B0930F440E");
  using var original=new Image();Check("bag art source decodes square",original.LoadPngFromBuffer(bytes.ToArray())==Error.Ok&&original.GetWidth()==original.GetHeight());original.Convert(Image.Format.Rgba8);
  var source=original.GetData();int transparent=0,opaque=0,partial=0;for(int i=3;i<source.Length;i+=4){if(source[i]==0)transparent++;if(source[i]>=240)opaque++;if(source[i]>0&&source[i]<255)partial++;}
  Check("bag art genuine alpha transparency",transparent>source.Length/40&&opaque>source.Length/40&&partial>0&&original.GetPixel(0,0).A==0&&original.GetPixel(original.GetWidth()-1,original.GetHeight()-1).A==0);
  var canonical=ModelDb.Relic<Pack.PortableSleepingBag>();var mutable=(Pack.PortableSleepingBag)canonical.ToMutable();mutable.Charges=4;
  var json=new JsonSerializerOptions{IncludeFields=true};var restored=(Pack.PortableSleepingBag)RelicModel.FromSerializable(JsonSerializer.Deserialize<SerializableRelic>(JsonSerializer.Serialize(mutable.ToSerializable(),json),json)!);
  var variants=new[]{canonical,mutable,(Pack.PortableSleepingBag)mutable.ClonePreservingMutability(),restored};
  for(int index=0;index<variants.Length;index++){
   var bag=variants[index];Check("bag art all paths bound variant="+index,bag.PackedIconPath==Pack.SleepingBagArt.IconPath&&bag.CustomIconPath==Pack.SleepingBagArt.IconPath&&bag.CustomBigIconPath==Pack.SleepingBagArt.BigIconPath&&bag.CustomIconOutlinePath==Pack.SleepingBagArt.OutlinePath&&bag.IconPath!=ModelDb.Relic<RegalPillow>().IconPath);
   Check("bag art native small big outline available variant="+index,bag.Icon is CompressedTexture2D&&bag.BigIcon is CompressedTexture2D&&bag.IconOutline is CompressedTexture2D&&bag.Icon.GetSize()==new Vector2(128,128)&&bag.BigIcon.GetSize()==new Vector2(256,256)&&bag.IconOutline.GetSize()==new Vector2(128,128));
   foreach(var (path,size) in new[]{(bag.CustomIconPath!,128),(bag.CustomBigIconPath!,256)}){
    using var expected=Image.CreateFromData(original.GetWidth(),original.GetHeight(),false,Image.Format.Rgba8,source);expected.Resize(size,size,Image.Interpolation.Lanczos);
    using var pixels=ResourceLoader.Load<Texture2D>(path,cacheMode:ResourceLoader.CacheMode.Ignore).GetImage();pixels.Convert(Image.Format.Rgba8);
    Check($"bag art exact resized source pixels {index}/{size}",pixels.GetData().SequenceEqual(expected.GetData()));
   }
   Check("bag art cloned and restored charges preserved variant="+index,bag.DisplayAmount==(index==0?1:4)&&bag.ShowCounter);
  }
  using(var pixels=canonical.Icon.GetImage())using(var outline=canonical.IconOutline.GetImage()){
   var a=pixels.GetData();var b=outline.GetData();bool white=true,covers=true,expands=false;
   for(int i=0;i<a.Length;i+=4){if(b[i+3]>0)white&=b[i]==255&&b[i+1]==255&&b[i+2]==255;covers&=b[i+3]>=a[i+3];expands|=b[i+3]>0&&a[i+3]==0;}
   Check("bag art distinct white transparent outline covers and expands silhouette",white&&covers&&expands&&outline.GetPixel(0,0).A==0);
  }
  Check("bag art does not replace native pillow",ModelDb.Relic<RegalPillow>().IconPath!=canonical.IconPath&&ModelDb.Relic<RegalPillow>().BigIcon!=canonical.BigIcon);
  bool active=Pack.Settings.Startup[Pack.SleepingBagContent.Key];try{Pack.Settings.Startup[Pack.SleepingBagContent.Key]=false;Check("bag art existing saved relic survives disabled acquisition",restored.Icon==canonical.Icon&&restored.BigIcon==canonical.BigIcon&&restored.Charges==4);}finally{Pack.Settings.Startup[Pack.SleepingBagContent.Key]=active;}
  foreach(var path in new[]{Pack.SleepingBagArt.IconPath,Pack.SleepingBagArt.BigIconPath,Pack.SleepingBagArt.OutlinePath}){
   string disk=ProjectSettings.GlobalizePath(path),data=Path.GetFullPath(OS.GetUserDataDir());Check("bag art cache stays within dedicated test data "+path,data.EndsWith("SlayTheSpire2-Aikun020Test")&&Path.GetFullPath(disk).StartsWith(data+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));File.Delete(disk);
  }
  Check("bag art all caches rebuild without source files",ResourceLoader.Exists(Pack.SleepingBagArt.IconPath)&&ResourceLoader.Exists(Pack.SleepingBagArt.BigIconPath)&&ResourceLoader.Exists(Pack.SleepingBagArt.OutlinePath));
  using(var f=NewDongniFixture()){
   var bag=Bag(f.P,3);var arrange=new Pack.ArrangeSleepingBagOption(f.P,bag);Check("bag art arrange uses same portable sleeping bag",arrange.CustomIconPath==Pack.SleepingBagArt.IconPath&&arrange.Icon==bag.Icon);
   Check("bag art arrange still adds one charge once",await arrange.OnSelect()&&bag.Charges==4&&!await arrange.OnSelect());
   await SleepingBagArtScreenshot(bag);
  }
  File.WriteAllText(Output+"/sleeping-bag-art-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Failed=0,SourcePngSha256=hash,SourceWidth=original.GetWidth(),SourceHeight=original.GetHeight(),TransparentPixels=transparent,OpaquePixels=opaque,Checks=passes.Skip(first)},new JsonSerializerOptions{WriteIndented=true}));
 }
 static async Task SleepingBagArtScreenshot(Pack.PortableSleepingBag bag){
  var viewport=new SubViewport{Size=new(1300,750),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("1b2533")});bool old=TestMode.IsOn;
  try{
   TestMode.IsOn=false;var holder=NRelicInventoryHolder.Create(bag)!;host.AddChild(holder);holder.Position=new(100,150);holder.Scale=Vector2.One*2;
   Check("bag art actual inventory holder uses new icon and outline",holder.Relic.Icon.Texture==bag.Icon&&holder.Relic.Outline.Texture==bag.IconOutline);
   var amount=(MegaLabel)AccessTools.Field(typeof(NRelicInventoryHolder),"_amountLabel").GetValue(holder)!;bag.Charges=7;
   Check("bag art native counter continues to update",amount.Text=="7");
   var large=NRelic.Create(bag,NRelic.IconSize.Large)!;host.AddChild(large);large.Position=new(480,220);large.Scale=Vector2.One*2;
   Check("bag art native large relic uses new big icon",large.Icon.Texture==bag.BigIcon&&!large.Outline.Visible);
   var option=new Pack.ArrangeSleepingBagOption(bag.Owner,bag);var button=NRestSiteButton.Create(option);host.AddChild(button);button.Position=new(830,260);
   Check("bag art native arrange button uses new icon",button.GetNode<TextureRect>("%Icon").Texture==bag.Icon);
   host.AddChild(new Label{Text="便携睡袋 · 遗物栏与次数",Position=new(80,70)});host.AddChild(new Label{Text="遗物大图",Position=new(440,70)});host.AddChild(new Label{Text="休息选项：整理",Position=new(830,70)});
   TestMode.IsOn=old;await Wait(.7);await AfterDraw();Check("bag art native inventory large and arrange screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/sleeping-bag-art-native.png")==Error.Ok);
  }finally{TestMode.IsOn=old;viewport.QueueFree();await Wait(.08);}
 }
}
