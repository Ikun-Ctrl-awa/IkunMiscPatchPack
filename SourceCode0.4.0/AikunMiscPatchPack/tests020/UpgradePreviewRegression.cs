using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens;
using STS2RitsuLib.Models.Capabilities;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static int PreviewLevel(CardModel card){var store=FusionQuestBlessingFix.Entry.Find("FreeLoadout.OverrideStore");return store==null?card.CurrentUpgradeLevel:(int)AccessTools.Method(store,"GetCardUpgradeLevel").Invoke(null,[card])!;}
 static string PreviewSnapshot(CardModel card)=>JsonSerializer.Serialize(card.ToSerializable(),new JsonSerializerOptions{IncludeFields=true})+"|"+card.Title+"|"+string.Join(";",card.DynamicVars.Select(v=>v.Key+"="+v.Value.BaseValue));
 static bool SameUpgradeValues(CardModel a,CardModel b)=>a.DynamicVars.All(v=>b.DynamicVars.TryGetValue(v.Key,out var value)&&value.BaseValue==v.Value.BaseValue)&&a.EnergyCost.Canonical==b.EnergyCost.Canonical&&a.Keywords.SetEquals(b.Keywords);
 static async Task UpgradePreviewChecks(){
  var source=ModelDb.Card<Bash>().ToMutable();source.GetOrCreateCapability<Pack.GeneratedCardUpgrade>().UpgradeLimit=5;
  await PreviewSample(source,"native multi-upgrade",true,false);
  await OrdinaryPreview();
  if(!Minimal){
   var free=ModelDb.Card<Bash>().ToMutable();var store=Find("FreeLoadout.OverrideStore");AccessTools.Method(store,"SetCardLimitBreak").Invoke(null,[free,true]);
   await PreviewSample(free,"FreeLoadout limit break",false,true);
   foreach(var canonical in new CardModel[]{ModelDb.Card<Bash>(),ModelDb.Card<Pack.ModForm>()}){
    var card=canonical.ToMutable();var enchant=Enchant(Find("MoreEnchant.Enchantments.ScorchingEnchantment"),1);card.EnchantInternal(enchant,1);enchant.ModifyCard();
    await PreviewSample(card,"scorching "+canonical.Id,true,false);
   }
  }
 }
 static async Task PreviewSample(CardModel source,string label,bool staleCache,bool external){
  run!.AddCard(source,player!);await CardPileCmd.Add(source,PileType.Deck,skipVisuals:true);
  source.UpgradeInternal();source.FinalizeUpgradeInternal();
  var holder=NGridCardHolder.Create(NCard.Create(source)!)!;Tree.Root.AddChild(holder);
  NInspectCardScreen? inspect=null;NUpgradePreview? comparison=null;
  try{
   // 原生列表把预览缓存在 +1；之后原牌升到 +3，构造用户报告的过时预览。
   for(int i=0;i<2;i++){source.UpgradeInternal();source.FinalizeUpgradeInternal();}
   Check(label+" source starts at +3",PreviewLevel(source)==3);
   var original=PreviewSnapshot(source);
   if(staleCache||external){
    Pack.Settings.Startup["upgrade_preview"]=false;holder.SetIsPreviewingUpgrade(true);
    Check(label+" reproduces old +2 preview",PreviewLevel(holder.CardNode!.Model!)==2);
    holder.SetIsPreviewingUpgrade(false);Pack.Settings.Startup["upgrade_preview"]=true;
   }
   string? freePath=external?ProjectSettings.GlobalizePath("user://freeloadout/run_overrides.json"):null;
   byte[]? freeBefore=freePath!=null&&File.Exists(freePath)?File.ReadAllBytes(freePath):null;
   CardModel? displayed=null;
   for(int i=0;i<3;i++){
    holder.SetIsPreviewingUpgrade(true);displayed=holder.CardNode!.Model;
    Check(label+" grid next level pass "+i,PreviewLevel(displayed!)==4&&displayed!.Title.EndsWith("+4"));
    var title=(string)AccessTools.Method(typeof(NCard),"GetTitleText").Invoke(holder.CardNode,null)!;
    Check(label+" real grid title +4 pass "+i,title.EndsWith("+4"));
    holder.SetIsPreviewingUpgrade(false);
    Check(label+" grid restores original pass "+i,ReferenceEquals(holder.CardNode.Model,source)&&PreviewSnapshot(source)==original);
   }
   comparison=ResourceLoader.Load<PackedScene>("res://scenes/cards/upgrade_preview.tscn").Instantiate<NUpgradePreview>();Tree.Root.AddChild(comparison);comparison.Card=source;
   var after=(Control)AccessTools.Field(typeof(NUpgradePreview),"_after").GetValue(comparison)!;
   var comparisonCard=after.GetChild<NPreviewCardHolder>(0).CardNode!.Model;
   Check(label+" native upgrade comparison +3 to +4",PreviewLevel(comparisonCard!)==4&&SameUpgradeValues(comparisonCard!,displayed!));
   inspect=NInspectCardScreen.Create()!;Tree.Root.AddChild(inspect);inspect.Open([source],0);
   var node=(NCard)AccessTools.Field(typeof(NInspectCardScreen),"_card").GetValue(inspect)!;
   var tick=(NTickbox)AccessTools.Field(typeof(NInspectCardScreen),"_upgradeTickbox").GetValue(inspect)!;
   Check(label+" inspect opens at current +3",!tick.IsTicked&&PreviewLevel(node.Model!)==3&&SameUpgradeValues(node.Model!,source));
   for(int i=0;i<3;i++){
    tick.IsTicked=true;AccessTools.Method(typeof(NInspectCardScreen),"UpdateCardDisplay").Invoke(inspect,null);
    Check(label+" inspect next +4 pass "+i,PreviewLevel(node.Model!)==4&&SameUpgradeValues(node.Model!,displayed!));
    if(i==0){await Wait(.3);await AfterDraw();Tree.Root.GetTexture().GetImage().SavePng(Output+"/upgrade-preview-"+(external?"limit-break":label.StartsWith("native")?"native":"scorching-"+source.Id.Entry)+".png");}
    tick.IsTicked=false;AccessTools.Method(typeof(NInspectCardScreen),"UpdateCardDisplay").Invoke(inspect,null);
    Check(label+" inspect restores +3 pass "+i,PreviewLevel(node.Model!)==3&&SameUpgradeValues(node.Model!,source));
   }
   Check(label+" preview leaves original unmodified",PreviewSnapshot(source)==original);
   if(freeBefore!=null)Check(label+" preview does not write FreeLoadout config",File.ReadAllBytes(freePath!).SequenceEqual(freeBefore));
   source.UpgradeInternal();source.FinalizeUpgradeInternal();
   Check(label+" actual upgrade matches preview +4",PreviewLevel(source)==4&&SameUpgradeValues(source,displayed!));
   holder.SetIsPreviewingUpgrade(true);Check(label+" subsequent preview +5",PreviewLevel(holder.CardNode!.Model!)==5);
   holder.SetIsPreviewingUpgrade(false);
   if(!external&&source.MaxUpgradeLevel==5){
    source.UpgradeInternal();source.FinalizeUpgradeInternal();tick.IsTicked=true;AccessTools.Method(typeof(NInspectCardScreen),"UpdateCardDisplay").Invoke(inspect,null);
    Check(label+" capped inspect remains +5",!source.IsUpgradable&&PreviewLevel(node.Model!)==5&&SameUpgradeValues(node.Model!,source));
   }
  }finally{
   Pack.Settings.Startup["upgrade_preview"]=true;holder.QueueFree();inspect?.Close();await Wait(.3);inspect?.QueueFree();comparison?.QueueFree();await Wait(.1);
  }
 }
 static async Task OrdinaryPreview(){
  var card=ModelDb.Card<Bash>().ToMutable();card.UpgradeInternal();card.FinalizeUpgradeInternal();var snapshot=PreviewSnapshot(card);
  var inspect=NInspectCardScreen.Create()!;Tree.Root.AddChild(inspect);
  try{
   inspect.Open([card],0);var node=(NCard)AccessTools.Field(typeof(NInspectCardScreen),"_card").GetValue(inspect)!;var tick=(NTickbox)AccessTools.Field(typeof(NInspectCardScreen),"_upgradeTickbox").GetValue(inspect)!;
   Check("ordinary one-upgrade preview retains native default",tick.IsTicked&&node.Model!.CurrentUpgradeLevel==1);
   tick.IsTicked=false;AccessTools.Method(typeof(NInspectCardScreen),"UpdateCardDisplay").Invoke(inspect,null);
   Check("ordinary one-upgrade preview retains native base view",node.Model!.CurrentUpgradeLevel==0&&PreviewSnapshot(card)==snapshot);
  }finally{inspect.Close();await Wait(.3);inspect.QueueFree();await Wait(.1);}
 }
}