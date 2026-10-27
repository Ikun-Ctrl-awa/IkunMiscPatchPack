using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using Pack=AikunMiscPatchPack;
namespace Aikun020Probe;
public static partial class Entry {
 static async Task PortraitLocalizationChecks(){
  int first=passes.Count;
  Check("enchantment localization patch applied",Pack.RuntimeLoader.States.GetValueOrDefault("enchantment_localization")=="已应用。");
  if(Minimal){Check("Columbina art safely skips absent mod",Pack.Settings.Status(Pack.Options.All.Single(o=>o.Key=="columbina_portraits")).Contains("未装载相关模组"));return;}
  var names=new[]{"Herta.HertaCode.Enchantments.VictoryPursuitEnchantment","YuiExtra.Enhancement.TemporarySown","Plague.PlagueCompositeEnchantment"};var entries=new List<object>();
  foreach(string n in names){var e=Enchant(Find(n),3);var card=ModelDb.Card<Bash>().ToMutable();card.EnchantInternal(e,3);string title=e.Title.GetFormattedText(),description=e.DynamicDescription.GetFormattedText();
   Check("enchantment restored Chinese title "+n,Regex.IsMatch(title,"[\u4e00-\u9fff]")&&!title.Contains(".title"));
   Check("enchantment restored description "+n,description.Length>10&&!description.Contains(".description")&&!description.Contains("{Amount}"));
   if(e.HasExtraCardText)Check("enchantment extra card text formats "+n,!e.DynamicExtraCardText!.GetFormattedText().Contains(".extraCardText"));entries.Add(new{Type=n,Title=title,Description=description});
  }
  var table=LocManager.Instance.GetTable("enchantments");var language=LocManager.Instance.Language;string saved=table.GetRawText("TEMPORARY_SOWN.title");
  try{
   table.MergeWith(new(){["TEMPORARY_SOWN.title"]="已有中文译名"});Pack.EnchantmentLocalizationFix.Apply();Check("enchantment translation preserves existing Chinese",table.GetRawText("TEMPORARY_SOWN.title")=="已有中文译名");
   AccessTools.Property(typeof(LocManager),nameof(LocManager.Language)).SetValue(LocManager.Instance,"eng");table.MergeWith(new(){["TEMPORARY_SOWN.title"]="English sentinel"});Pack.EnchantmentLocalizationFix.Apply();Check("enchantment Chinese patch inactive for English",table.GetRawText("TEMPORARY_SOWN.title")=="English sentinel");
  }finally{AccessTools.Property(typeof(LocManager),nameof(LocManager.Language)).SetValue(LocManager.Instance,language);table.MergeWith(new(){["TEMPORARY_SOWN.title"]=saved});}
  var cards=ModelDb.AllCards.Where(c=>c.GetType().Assembly.GetName().Name=="ColumbinaMod").ToArray();Check("Columbina all 94 cards audited",cards.Length==94);
  foreach(var card in cards){Pack.Settings.Startup["columbina_portraits"]=false;string before=card.PortraitPath;Pack.Settings.Startup["columbina_portraits"]=true;string after=card.PortraitPath;
   Check("Columbina matching normal and beta portraits "+card.Id,before.EndsWith("/card.png")&&after.EndsWith("/"+card.Id.Entry.Split('-').Last().ToLowerInvariant()+".png")&&card.BetaPortraitPath==after&&ResourceLoader.Load<Texture2D>(after)!=null);
  }
  // 对照百科实际读取的小图资源路径，使用模组原有图片生成检查图。
  var viewport=new SubViewport{Size=new(1500,860),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("17232b")});
  try{var selected=cards.Where(c=>c.GetType().Name is "StrikeColumbina" or "DefendColumbina" or "LunarChargedBaseAttack" or "ElementalResonance" or "GazeBaseAttack").Take(5).ToArray();
   foreach(bool enabled in new[]{false,true}){Pack.Settings.Startup["columbina_portraits"]=enabled;int y=enabled?440:20;host.AddChild(new Label{Text=enabled?"修复后：读取对应专属大图":"修复前：小图缺失，回退到通用占位图",Position=new(20,y)});int i=0;foreach(var c in selected){var texture=new TextureRect{ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,Texture=ResourceLoader.Load<Texture2D>(c.PortraitPath)};host.AddChild(texture);texture.Position=new(20+i*295,y+45);texture.Size=new(275,300);host.AddChild(new Label{Text=c.Title,Position=new(20+i*295,y+350)});i++;}}
   await Wait(.15);await AfterDraw();viewport.GetTexture().GetImage().SavePng(Output+"/columbina-portraits-before-after.png");
  }finally{Pack.Settings.Startup["columbina_portraits"]=true;viewport.QueueFree();await Wait(.03);}
  File.WriteAllText(Output+"/portrait-localization-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Failed=0,Checks=passes.Skip(first),Translations=entries,ColumbinaCards=cards.Length},new JsonSerializerOptions{WriteIndented=true}));
 }
}
