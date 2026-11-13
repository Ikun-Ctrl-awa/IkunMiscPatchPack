using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace Aikun020Probe;
public static partial class Entry {
 static async Task XArtReferences(){
  var info=new List<object>();
  foreach(var (role,names) in new[]{("Silent",new[]{"StormOfSteel","Shiv"}),("Regent",new[]{"SovereignBlade","StrikeRegent"}),("Defect",new[]{"Glasswork","Defragment"}),("Ironclad",new[]{"FiendFire","Whirlwind"}),("Necrobinder",new[]{"Seance","CaptureSpirit"})}){
   var viewport=new SubViewport{Size=new(1200,500),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("20212c")});
   try{
    for(int i=0;i<names.Length;i++){
     var type=FusionQuestBlessingFix.Entry.Find("MegaCrit.Sts2.Core.Models.Cards."+names[i])??throw new Exception("缺少风格参考卡："+names[i]);var card=ModelDb.GetById<CardModel>(ModelDb.GetId(type));Check("X art verified native pool "+role+"/"+names[i],card.Pool.GetType().Name==role+"CardPool"||card.Rarity==CardRarity.Token);using var pixels=card.Portrait.GetImage();
     string path=Output+"/reference-"+role+"-"+names[i]+".png";Check("X art native style export "+role+"/"+names[i],pixels.SavePng(path)==Error.Ok);
     host.AddChild(new TextureRect{Position=new(20+600*i,60),Size=new(560,410),Texture=card.Portrait,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered});host.AddChild(new Label{Text=role+" · "+card.Title+" / "+names[i],Position=new(30+600*i,15)});
     info.Add(new{Role=role,Card=names[i],Title=card.Title,Pool=card.Pool.GetType().Name,Rarity=card.Rarity.ToString(),NativePath=card.PortraitPath,Export=path,Width=pixels.GetWidth(),Height=pixels.GetHeight()});
    }
    await Wait(.15);await AfterDraw();Check("X art native style sheet "+role,viewport.GetTexture().GetImage().SavePng(Output+"/reference-"+role+".png")==Error.Ok);
   }finally{viewport.QueueFree();await Wait(.03);}
  }
  File.WriteAllText(Output+"/x-art-references.json",JsonSerializer.Serialize(info,new JsonSerializerOptions{WriteIndented=true}));
 }
}
