using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.TestSupport;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static async Task LagArtChecks(){
  int start=passes.Count;
  using var source=typeof(Pack.Entry).Assembly.GetManifestResourceStream("cardart.lag_curse.png");
  Check("lag art embedded PNG exists",source!=null);using var bytes=new MemoryStream();source!.CopyTo(bytes);
  string hash=Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
  Check("lag art exact generated PNG embedded",hash=="8530A0C4BAF20D6AA338DCF9721E5D865EE6D9F7D99A01D13D080C2DC61FB97F");
  using var expected=new Image();Check("lag art PNG 1536x1024 decodes",expected.LoadPngFromBuffer(bytes.ToArray())==Error.Ok&&expected.GetSize()==new Vector2(1536,1024));expected.Convert(Image.Format.Rgba8);
  var canonical=ModelDb.Card<Pack.LagCurse>();var upgraded=Up(canonical.ToMutable());
  var variants=new CardModel[]{canonical,canonical.ToMutable(),upgraded,Restore(upgraded),(CardModel)upgraded.ClonePreservingMutability()};
  foreach(var (card,index) in variants.Select((c,i)=>(c,i))){
   string[] paths=[card.PortraitPath,card.BetaPortraitPath,(string)AccessTools.Property(typeof(CardModel),"PortraitPngPath").GetValue(card)!,(string)AccessTools.Property(typeof(CardModel),"BetaPortraitPngPath").GetValue(card)!,((Pack.LagCurse)card).CustomPortraitPath!,((Pack.LagCurse)card).CustomBetaPortraitPath!];
   Check("lag art all native portrait paths agree variant="+index,paths.All(p=>p==Pack.LagCurseArt.PortraitPath)&&paths[0].StartsWith("user://IkunMiscPatchPack/card-portraits/")&&card.AllPortraitPaths.SequenceEqual(new[]{paths[0]})&&card.HasPortrait&&card.HasBetaPortrait);
   Check("lag art replaces Injury variant="+index,paths.All(p=>p!=ModelDb.Card<Injury>().PortraitPath)&&card.Portrait is CompressedTexture2D);
   var texture=ResourceLoader.Load<Texture2D>(paths[0]);using var pixels=texture.GetImage();pixels.Convert(Image.Format.Rgba8);
   Check("lag art native pixels match source variant="+index,pixels.GetSize()==expected.GetSize()&&pixels.GetData().SequenceEqual(expected.GetData()));
   var reread=ResourceLoader.Load<Texture2D>(paths[0],cacheMode:ResourceLoader.CacheMode.Ignore);using var rereadPixels=reread.GetImage();rereadPixels.Convert(Image.Format.Rgba8);
   Check("lag art disk reload matches source variant="+index,rereadPixels.GetData().SequenceEqual(expected.GetData()));
   Check("lag art keywords cost upgrade preserved variant="+index,card.EnergyCost.Canonical==3&&card.Keywords.SetEquals(card.IsUpgraded?new[]{CardKeyword.Retain,CardKeyword.Sly}:new[]{CardKeyword.Retain,CardKeyword.Sly,CardKeyword.Exhaust}));
  }
  bool active=Pack.Settings.Startup[Pack.TowerCurseContent.Key];try{Pack.Settings.Startup[Pack.TowerCurseContent.Key]=false;Check("lag art disabled acquisition keeps saved portrait",Restore(upgraded).PortraitPath==canonical.PortraitPath);}finally{Pack.Settings.Startup[Pack.TowerCurseContent.Key]=active;}
  var others=new CardModel[]{ModelDb.Card<Injury>(),ModelDb.Card<Pack.QuCurse>(),ModelDb.Card<Pack.DongniCurse>(),ModelDb.Card<Pack.DongniDisgust>()};
  Check("lag art leaves other cards unchanged",others.All(c=>c.PortraitPath!=canonical.PortraitPath&&c.BetaPortraitPath!=canonical.PortraitPath));
  // 缓存文件只在独立引擎测试用户目录中删除，确认能由嵌入资源重建。
  string disk=ProjectSettings.GlobalizePath(canonical.PortraitPath),data=Path.GetFullPath(OS.GetUserDataDir());
  Check("lag art cache inside dedicated sandbox",data.EndsWith("SlayTheSpire2-Aikun020Test")&&Path.GetFullPath(disk).StartsWith(data+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
  File.Delete(disk);string rebuilt=Pack.LagCurseArt.PortraitPath;
  Check("lag art cache recreated after missing file",rebuilt==canonical.PortraitPath&&File.Exists(disk));
  using(var pixels=ResourceLoader.Load<Texture2D>(rebuilt,cacheMode:ResourceLoader.CacheMode.Ignore).GetImage()){pixels.Convert(Image.Format.Rgba8);Check("lag art recreated pixels exact",pixels.GetData().SequenceEqual(expected.GetData()));}
  await LagArtScreenshot();await LagScreenshot();
  File.WriteAllText(Output+"/lag-art-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Failed=0,SourcePngSha256=hash,Checks=passes.Skip(start).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
 }
 static async Task LagArtScreenshot(){
  using var f=NewDongniFixture();var viewport=new SubViewport{Size=new(1800,1050),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("14111d")});bool old=TestMode.IsOn;
  try{
   foreach(var (level,x,scale) in new[]{(0,250f,.70f),(1,760f,1.15f)}){
    var card=Up(f.Card<Pack.LagCurse>(),level);TestMode.IsOn=false;var node=NCard.Create(card)!;host.AddChild(node);node.Position=new(x,470);node.Scale=Vector2.One*scale;node.UpdateVisuals(PileType.None,CardPreviewMode.Normal);TestMode.IsOn=old;
    Check("lag art actual native small large NCard level="+level,node.GetNode<TextureRect>("%Portrait").Texture==card.Portrait);
   }
   var cardForPreview=f.Card<Pack.LagCurse>();TestMode.IsOn=false;var holder=NGridCardHolder.Create(NCard.Create(cardForPreview)!)!;host.AddChild(holder);holder.Position=new(-400,-400);holder.SetIsPreviewingUpgrade(true);
   Check("lag art actual upgrade preview keeps selected portrait",holder.CardNode!.Model!.CurrentUpgradeLevel==1&&holder.CardNode.Model.PortraitPath==Pack.LagCurseArt.PortraitPath&&holder.CardNode.GetNode<TextureRect>("%Portrait").Texture==cardForPreview.Portrait&&cardForPreview.CurrentUpgradeLevel==0);holder.QueueFree();TestMode.IsOn=old;
   host.AddChild(new TextureRect{Position=new(1120,250),Size=new(620,414),Texture=ResourceLoader.Load<Texture2D>(Pack.LagCurseArt.PortraitPath),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered});
   await Wait(.2);await AfterDraw();Check("lag art native small large beta screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/lag-art-native.png")==Error.Ok);
  }finally{TestMode.IsOn=old;viewport.QueueFree();await Wait(.05);}
 }
}
