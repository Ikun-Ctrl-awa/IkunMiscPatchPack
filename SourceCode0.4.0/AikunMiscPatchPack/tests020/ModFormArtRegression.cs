using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static async Task ModFormArtChecks(){
  int first=passes.Count;
  using var stream=typeof(Pack.Entry).Assembly.GetManifestResourceStream("cardart.mod_form.png")!;
  using var bytes=new MemoryStream();stream.CopyTo(bytes);
  string sourceHash=Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
  Check("form art embeds selected original PNG",sourceHash=="DBA13432F1D0462D409495A03E91498D2B5221CD5803825C60B48627EA435706");
  using var expected=new Image();Check("form art PNG decodes",expected.LoadPngFromBuffer(bytes.ToArray())==Error.Ok);
  expected.Convert(Image.Format.Rgba8);
  var canonical=ModelDb.Card<Pack.ModForm>();var upgraded=canonical.ToMutable();upgraded.UpgradeInternal();upgraded.FinalizeUpgradeInternal();
  var variants=new CardModel[]{canonical,canonical.ToMutable(),upgraded,Restore(upgraded),(CardModel)upgraded.ClonePreservingMutability()};
  for(int i=0;i<variants.Length;i++){
   var card=variants[i];string[] paths=[card.PortraitPath,card.BetaPortraitPath,(string)AccessTools.Property(typeof(CardModel),"PortraitPngPath").GetValue(card)!,(string)AccessTools.Property(typeof(CardModel),"BetaPortraitPngPath").GetValue(card)!];
   Check("form normal beta small and large paths agree "+i,paths.All(p=>p==Pack.ContentArt.PortraitResourcePath));
   Check("form portrait availability and preload paths "+i,card.HasPortrait&&card.HasBetaPortrait&&card.AllPortraitPaths.SequenceEqual(new[]{paths[0]}));
   foreach(string path in paths.Distinct()){
    Check("form path registered in Godot "+i,ResourceLoader.Exists(path)&&ResourceLoader.HasCached(path));
    var loaded=ResourceLoader.Load<Texture2D>(path);using var pixels=loaded.GetImage();pixels.Convert(Image.Format.Rgba8);
    Check("form path loads selected PNG pixels "+i,pixels.GetSize()==expected.GetSize()&&pixels.GetData().SequenceEqual(expected.GetData()));
   }
   Check("form native Portrait loads embedded texture "+i,card.Portrait==Pack.ContentArt.Portrait);
  }
  var other=ModelDb.Card<Bash>();Check("form art does not replace other card paths",other.PortraitPath!=Pack.ContentArt.PortraitResourcePath&&other.BetaPortraitPath!=Pack.ContentArt.PortraitResourcePath&&other.Portrait!=Pack.ContentArt.Portrait);
  bool active=Pack.Settings.Startup["mod_form"];try{Pack.Settings.Startup["mod_form"]=false;Check("existing form art survives disabled acquisition",Restore(upgraded).Portrait==Pack.ContentArt.Portrait);}finally{Pack.Settings.Startup["mod_form"]=active;}
  var viewport=new SubViewport{Size=new(1500,920),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);
  try{
   var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("141c2b")});
   foreach(var (model,x,scale,label) in new[]{(canonical,210f,.65f,"列表 / 小图"),(upgraded,600f,1.15f,"详情 / 大图（升级）")}){
    var node=NCard.Create(model)!;host.AddChild(node);node.Position=new(x,430);node.Scale=Vector2.One*scale;node.UpdateVisuals(PileType.None,CardPreviewMode.Normal);
    Check("native NCard uses new art "+label,node.GetNode<TextureRect>("%Portrait").Texture==Pack.ContentArt.Portrait);
    host.AddChild(new Label{Text=label,Position=new(x-100,830)});
   }
   host.AddChild(new TextureRect{Position=new(940,220),Size=new(500,367),Texture=ResourceLoader.Load<Texture2D>(canonical.BetaPortraitPath),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered});
   host.AddChild(new Label{Text="百科 / 测试图路径直接加载",Position=new(985,620)});
   await Wait(.2);await AfterDraw();
   Check("form art native screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/modform-art-"+(Minimal?"minimal":"full")+".png")==Error.Ok);
  }finally{viewport.QueueFree();await Wait(.03);}
  File.WriteAllText(Output+"/modform-art-"+(Minimal?"minimal":"full")+"-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Failed=0,SourcePngSha256=sourceHash,Width=expected.GetWidth(),Height=expected.GetHeight(),Checks=passes.Skip(first)},new JsonSerializerOptions{WriteIndented=true}));
 }
}
