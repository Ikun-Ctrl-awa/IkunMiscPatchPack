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
 static readonly string[] xArtNames=["knife-rain","hundred-forge","glass-factory","sacrificial-combo","soul-ritual"];
 static readonly string[] xArtHashes=["972C45010D3508C8AA425B83C630E4445B1D9C7D48E3FA8B53AE964F464A26DE","BF4D8A175DAF03B985BE8B3F1A9AF88B763EFE109F20DBCE9BCB311EA37EA1BF","8BC9851F74FBB2ABBE10391B100D532F11002D15AC2872FBED990494AA5466CB","D79FD3BFF31ED66DA333A24ABAD4377FF1DBEE777582ABC006F60426986E6036","ADB983725229B7B3E411D6100A88CB7F89DE384B53681D6DF702F7367EBBA980"];
 static async Task XArtChecks(){
  int first=passes.Count;var paths=new HashSet<string>();var sources=new List<object>();
  var oldCards=new CardModel[]{ModelDb.Card<StormOfSteel>(),ModelDb.Card<SovereignBlade>(),ModelDb.Card<Glasswork>(),ModelDb.Card<FiendFire>(),ModelDb.Card<ForgottenRitual>()};
  for(int index=0;index<xAllTypes.Length;index++){
   var type=xAllTypes[index];var canonical=ModelDb.GetById<CardModel>(ModelDb.GetId(type));string tag="X art "+type.Name;
   using var stream=typeof(Pack.Entry).Assembly.GetManifestResourceStream("cardart.x."+xArtNames[index]+".png");Check(tag+" embedded PNG exists",stream!=null);using var bytes=new MemoryStream();stream!.CopyTo(bytes);string hash=Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
   Check(tag+" selected original hash",hash==xArtHashes[index]);using var expected=new Image();Check(tag+" native ratio source decoded",expected.LoadPngFromBuffer(bytes.ToArray())==Error.Ok&&expected.GetSize()==new Vector2(1448,1086));expected.Convert(Image.Format.Rgba8);var expectedPixels=expected.GetData();
   var upgraded=Up(canonical.ToMutable());var variants=new[]{canonical,canonical.ToMutable(),upgraded,Restore(upgraded),(CardModel)upgraded.ClonePreservingMutability()};
   for(int variant=0;variant<variants.Length;variant++){
    var card=variants[variant];var custom=(Pack.XExpansionCard)card;
    string[] all=[card.PortraitPath,card.BetaPortraitPath,(string)AccessTools.Property(typeof(CardModel),"PortraitPngPath").GetValue(card)!,(string)AccessTools.Property(typeof(CardModel),"BetaPortraitPngPath").GetValue(card)!,custom.CustomPortraitPath!,custom.CustomBetaPortraitPath!];
    Check(tag+" all normal beta small large paths agree variant="+variant,all.All(p=>p==Pack.XExpansionArt.PortraitPath(card))&&card.AllPortraitPaths.SequenceEqual(new[]{all[0]})&&card.HasPortrait&&card.HasBetaPortrait&&all[0].StartsWith("user://IkunMiscPatchPack/card-portraits/"));
    Check(tag+" native compressed portrait variant="+variant,card.Portrait is CompressedTexture2D&&card.Portrait.GetSize()==expected.GetSize());
    using var pixels=card.Portrait.GetImage();pixels.Convert(Image.Format.Rgba8);Check(tag+" source pixels exact variant="+variant,pixels.GetData().SequenceEqual(expectedPixels));
    Check(tag+" X cost pool ID and upgrade preserved variant="+variant,card.Id==canonical.Id&&card.EnergyCost.CostsX&&card.Pool.GetType()==canonical.Pool.GetType()&&card.CurrentUpgradeLevel==(variant<2?0:1)&&card.Type==canonical.Type&&card.Rarity==canonical.Rarity);
   }
   using(var pixels=ResourceLoader.Load<Texture2D>(canonical.PortraitPath,cacheMode:ResourceLoader.CacheMode.Ignore).GetImage()){pixels.Convert(Image.Format.Rgba8);Check(tag+" disk reload exact",pixels.GetData().SequenceEqual(expectedPixels));}
   Check(tag+" no native card replacement",oldCards.All(c=>c.PortraitPath!=canonical.PortraitPath));Check(tag+" unique per card",paths.Add(canonical.PortraitPath));
   bool active=Pack.Settings.Startup[Pack.XCardExpansion.Key];try{Pack.Settings.Startup[Pack.XCardExpansion.Key]=false;Check(tag+" saved portrait survives acquisition off",Restore(upgraded).PortraitPath==canonical.PortraitPath);}finally{Pack.Settings.Startup[Pack.XCardExpansion.Key]=active;}
   string disk=ProjectSettings.GlobalizePath(canonical.PortraitPath),data=Path.GetFullPath(OS.GetUserDataDir());Check(tag+" cache within dedicated test directory",data.EndsWith("SlayTheSpire2-Aikun020Test")&&Path.GetFullPath(disk).StartsWith(data+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));File.Delete(disk);string regenerated=Pack.XExpansionArt.PortraitPath(canonical);
   using(var pixels=ResourceLoader.Load<Texture2D>(regenerated,cacheMode:ResourceLoader.CacheMode.Ignore).GetImage()){pixels.Convert(Image.Format.Rgba8);Check(tag+" missing cache rebuilt from embedded PNG",File.Exists(disk)&&pixels.GetData().SequenceEqual(expectedPixels));}
   using(var f=XFixture(type)){
    var sourceCard=XCard(f,type);bool old=TestMode.IsOn;NGridCardHolder? holder=null;
    try{TestMode.IsOn=false;holder=NGridCardHolder.Create(NCard.Create(sourceCard)!)!;Tree.Root.AddChild(holder);holder.Position=new(-600,-600);holder.SetIsPreviewingUpgrade(true);var preview=holder.CardNode!.Model!;Check(tag+" native upgrade preview uses new art",preview.CurrentUpgradeLevel==1&&preview.PortraitPath==canonical.PortraitPath&&holder.CardNode.GetNode<TextureRect>("%Portrait").Texture==canonical.Portrait&&sourceCard.CurrentUpgradeLevel==0);}
    finally{TestMode.IsOn=old;holder?.QueueFree();await Wait(.03);}
   }
   sources.Add(new{Card=type.Name,SourcePngSha256=hash,Width=expected.GetWidth(),Height=expected.GetHeight(),Path=canonical.PortraitPath});
  }
  Check("X art five distinct illustrations",paths.Count==5);
  await XExpansionScreenshots();
  File.WriteAllText(Output+"/x-art-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Failed=0,Sources=sources,Checks=passes.Skip(first)},new JsonSerializerOptions{WriteIndented=true}));
 }
}
