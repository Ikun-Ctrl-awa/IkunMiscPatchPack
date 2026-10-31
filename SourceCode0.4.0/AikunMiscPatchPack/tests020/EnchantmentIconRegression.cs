using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.TestSupport;
using STS2RitsuLib.Models.Capabilities;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static IEnumerable<Node> IconNodes(Node root){yield return root;foreach(var child in root.GetChildren())foreach(var nested in IconNodes(child))yield return nested;}
 static async Task EnchantmentIconChecks(){
  EnchantmentModel[] models=[ModelDb.Enchantment<Pack.WeepingBlood>(),ModelDb.Enchantment<Pack.BloodScorching>(),ModelDb.Enchantment<Pack.ThunderArc>()];
  string[] paths=[Pack.BloodInitiationArt.IconPath(Pack.BloodInitiationArt.WeepingPath),Pack.BloodInitiationArt.IconPath(Pack.BloodInitiationArt.ScorchingPath),Pack.ThunderInitiationArt.IconPath(Pack.ThunderInitiationArt.ArcPath)];
  var evidence=new List<object>();
  for(int i=0;i<models.Length;i++){
   var model=models[i];var icon=model.Icon;var expected=Pack.EnchantmentView.ResolveIcon(model);
   Check("own enchant native icon is same as introduction "+model.Id,ReferenceEquals(icon,model.HoverTip.Icon)&&ReferenceEquals(icon,expected));
   Check("own enchant native path and cached compressed resource "+model.Id,model.IconPath==paths[i]&&icon.ResourcePath==paths[i]&&ResourceLoader.Exists(paths[i])&&ReferenceEquals(ResourceLoader.Load<CompressedTexture2D>(paths[i]),icon)&&ReferenceEquals(PreloadManager.Cache.GetCompressedTexture2D(paths[i]),icon));
   Check("own enchant no longer borrows native icon "+model.Id,!ReferenceEquals(icon,ModelDb.Enchantment<Sharp>().Icon)&&!ReferenceEquals(icon,ModelDb.Enchantment<Sown>().Icon));
   using(var image=icon.GetImage()){
    Check("own enchant complete transparent 256 pixel image "+model.Id,image.GetWidth()==256&&image.GetHeight()==256&&image.GetPixel(0,0).A==0);
    Check("own enchant native icon PNG saved "+model.Id,image.SavePng(Output+"/enchant-icon-"+i+".png")==Error.Ok);
   }
   var mutable=model.ToMutable();var saved=EnchantmentModel.FromSerializable(mutable.ToSerializable());
   Check("own enchant canonical mutable and restored icons identical "+model.Id,mutable.Icon==icon&&saved.Icon==icon&&saved.Id==model.Id&&saved.HoverTip.Icon==icon);
   evidence.Add(new{Id=model.Id.ToString(),Name=model.Title.GetFormattedText(),NativeIconPath=model.IconPath,TextureType=icon.GetType().FullName,MatchesIntroduction=ReferenceEquals(icon,model.HoverTip.Icon)});
  }
  using var f=NewDongniFixture();var viewport=new SubViewport{Size=new(2100,1300),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);
  var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("1b202c")});
  try{
   for(int i=0;i<models.Length;i++)for(int slot=0;slot<2;slot++){
    var card=f.Card<StrikeIronclad>();
    if(slot==0)card.EnchantInternal(models[i].ToMutable(),1);
    else{var slots=card.GetOrCreateCapability<Pack.BloodEnchantments>();if(i==0)slots.Add<Pack.WeepingBlood>();else if(i==1)slots.Add<Pack.BloodScorching>();else slots.Add<Pack.ThunderArc>();}
    await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);bool old=TestMode.IsOn;NCard node;
    try{TestMode.IsOn=false;node=NCard.Create(card)!;host.AddChild(node);node.Position=new(350+700*i,335+640*slot);node.Scale=Vector2.One*.85f;node.UpdateVisuals(PileType.Hand,CardPreviewMode.Normal);}finally{TestMode.IsOn=old;}
    var tab=slot==0?node.EnchantmentTab:node.EnchantmentTab.GetParent().GetChildren().OfType<Control>().Single(c=>c.Name=="AikunEnchantment0");
    Check($"native card enchant icon matches introduction slot={slot} {models[i].Id}",tab.IsVisibleInTree()&&tab.GetNode<TextureRect>("Icon").Texture==models[i].Icon&&card.HoverTips.OfType<HoverTip>().Any(t=>t.Icon==models[i].Icon));
   }
   await Wait(.2);await AfterDraw();Check("own enchant native main and extra slot screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/enchant-icons-cards.png")==Error.Ok);
  }finally{viewport.QueueFree();await Wait(.03);}
  if(!Minimal)await EnchantmentGalleryScreenshot(models);
  File.WriteAllText(Output+"/enchant-icons-evidence.json",JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
 }
 static async Task EnchantmentGalleryScreenshot(EnchantmentModel[] models){
  var rootType=Find("EnchantmentCompendium.EnchantmentBrowserRoot");var entryType=Find("EnchantmentCompendium.EnchantmentCompendiumEntry");
  var viewport=new SubViewport{Size=new(2560,1440),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);
  host.AddChild(new ColorRect{Size=host.Size,Color=new("181c27")});bool old=TestMode.IsOn;
  try{
   TestMode.IsOn=false;var gallery=(Control)Activator.CreateInstance(rootType,[models.Concat(new EnchantmentModel[]{ModelDb.Enchantment<Sharp>(),ModelDb.Enchantment<Sown>()}).ToArray()])!;host.AddChild(gallery);await Wait(.8);
   var entries=IconNodes(gallery).Where(n=>n.GetType()==entryType).ToArray();Check("actual compendium contains own and base enchantments",entries.Length==5);
   foreach(var entry in entries){var model=(EnchantmentModel)AccessTools.Field(entryType,"_model").GetValue(entry)!;var icon=(TextureRect)AccessTools.Field(entryType,"_iconRect").GetValue(entry)!;
    Check("actual compendium entry reads native intended icon "+model.Id,icon.Texture==model.Icon&&icon.Texture==model.HoverTip.Icon);}
   var hover=new NHoverTipSet();host.AddChild(hover);AccessTools.Method(typeof(NHoverTipSet),"Init").Invoke(hover,[gallery,new IHoverTip[]{models[0].HoverTip}]);hover.Position=new(1550,300);await Wait(.2);await AfterDraw();
   Check("actual popup icon matches actual compendium entry",IconNodes(hover).OfType<TextureRect>().Any(n=>n.Texture==models[0].Icon));
   Check("actual compendium and popup screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/enchant-icons-compendium.png")==Error.Ok);
  }finally{TestMode.IsOn=old;viewport.QueueFree();await Wait(.03);}
 }
}
