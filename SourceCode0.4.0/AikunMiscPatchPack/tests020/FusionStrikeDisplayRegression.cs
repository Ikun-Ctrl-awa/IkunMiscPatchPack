using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.addons.mega_text;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static int[] StrikeNumbers(string text)=>Regex.Matches(Regex.Replace(text,@"\[[^\]]*\]",""),@"\d+").Select(m=>int.Parse(m.Value)).ToArray();
 static string StrikeText(CardModel card,Creature? target=null){card.UpdateDynamicVarPreview(CardPreviewMode.Normal,target,card.DynamicVars);return card.GetDescriptionForPile(card.Pile?.Type??PileType.None,target);}
 static async Task FusionStrikeDisplayChecks(){
  int start=passes.Count;var evidence=new List<object>();var option=Pack.Options.All.Single(o=>o.Key==Pack.FusionStrikeDisplayFix.Key);
  Check("fusion strike independent option defaults on",new Pack.PackConfig().Get(option.Key)&&Pack.Options.All.Count(o=>o.Key==option.Key)==1);
  if(Minimal){Check("fusion strike optional dependencies safely absent",Pack.Settings.Status(option).Contains("未装载相关模组")&&!Pack.RuntimeLoader.States.ContainsKey(option.Key));return;}
  Check("fusion strike display patch applied",Pack.RuntimeLoader.States.GetValueOrDefault(option.Key)=="已应用。");
  var type=Find("FusionExpansionPack.FusionStrikeCard");var canonical=ModelDb.GetById<CardModel>(ModelDb.GetId(type));
  CardModel Card(int n=0){var card=canonical.ToMutable();if(n>1)Scorch(card);return Up(card,n);}
  async Task<CardModel> Fused(BloodFixture f,int count,PileType pile,bool other=false){var root=Fuse(Enumerable.Range(0,count).Select(_=>ModelDb.Card<StrikeIronclad>().ToMutable()).ToArray());f.Combat.AddCard(root,other?f.Other:f.P);await CardPileCmd.Add(root,pile,skipVisuals:true);return root;}
  bool active=Pack.Settings.Startup[option.Key];
  var table=LocManager.Instance.GetTable("cards");string key=canonical.Description.LocEntryKey,original=table.GetRawText(key);
  try{
   Pack.Settings.Startup[option.Key]=false;var before=canonical.Description;
   Check("fusion strike reproduces legacy placeholders without dynamic vars",original.Contains("{Damage")&&original.Contains("{MagicNumber")&&!canonical.DynamicVars.ContainsKey("Damage")&&!canonical.DynamicVars.ContainsKey("MagicNumber")&&!before.Variables.ContainsKey("Damage"));
   string broken;try{broken=canonical.GetDescriptionForPile(PileType.None);}catch(Exception e){broken=e.GetType().Name+": "+e.Message;}
   evidence.Add(new{BaselineRaw=original,BaselineRendered=broken});Pack.Settings.Startup[option.Key]=true;
   foreach(int n in new[]{0,1,3,5}){
    using var f=new BloodFixture(multiplayer:true);var card=Card(n);f.Combat.AddCard(card,f.P);await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);
    var keys=card.DynamicVars.Keys.Order().ToArray();var desc=card.Description;int extra=3+2*n;string tag="fusion strike level="+n;
    Check(tag+" aliases reference actual variable objects",ReferenceEquals(desc.Variables["Damage"],card.DynamicVars.CalculatedDamage)&&ReferenceEquals(desc.Variables["MagicNumber"],card.DynamicVars.ExtraDamage));
    Check(tag+" zero fusion display uses base eight and correct extra",StrikeNumbers(StrikeText(card,f.Enemy)).Take(2).SequenceEqual(new[]{8,extra}));
    await Fused(f,2,PileType.Draw);
    Check(tag+" counts material quantity rather than fused root quantity",StrikeNumbers(StrikeText(card,f.Enemy)).Take(2).SequenceEqual(new[]{8+2*extra,extra}));
    var second=await Fused(f,3,PileType.Discard);await Fused(f,4,PileType.Hand,true);
    string shown=StrikeText(card,f.Enemy);Check(tag+" all own combat piles counted but other player excluded",StrikeNumbers(shown).Take(2).SequenceEqual(new[]{8+5*extra,extra}));
    await CardPileCmd.Add(second,PileType.Exhaust,skipVisuals:true);
    Check(tag+" exhausting a fused root keeps original all piles count",StrikeNumbers(StrikeText(card,f.Enemy)).First()==8+5*extra);
    foreach(var clone in new[]{(CardModel)card.ClonePreservingMutability(),Restore(card)}){
     Check(tag+" clone and saved upgrade preserve multiplier",clone.DynamicVars.ExtraDamage.IntValue==extra&&ReferenceEquals(clone.Description.Variables["Damage"],clone.DynamicVars.CalculatedDamage)&&ReferenceEquals(clone.Description.Variables["MagicNumber"],clone.DynamicVars.ExtraDamage));
    }
    int hp=f.Enemy.CurrentHp;await f.Play(card,f.Enemy);int actual=hp-f.Enemy.CurrentHp;
    Check(tag+" native card damage equals displayed total",actual==8+5*extra&&actual==StrikeNumbers(shown).First());
    Check(tag+" formatting never adds fake gameplay variables",keys.SequenceEqual(card.DynamicVars.Keys.Order())&&!card.DynamicVars.ContainsKey("Damage")&&!card.DynamicVars.ContainsKey("MagicNumber"));
    evidence.Add(new{Level=n,OwnMaterials=5,OtherPlayerMaterials=4,Extra=extra,Display=shown,Actual=actual});
   }
   using(var f=new BloodFixture()){
    var card=f.Combat.CreateCard(canonical,f.P);await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);await Fused(f,2,PileType.Draw);
    foreach(string modifiers in new[]{"none","strength","vulnerable","weak"}){
     if(modifiers=="strength")await PowerCmd.Apply<StrengthPower>(f.Choice,f.P.Creature,2,f.P.Creature,null);
     if(modifiers=="vulnerable")await PowerCmd.Apply<VulnerablePower>(f.Choice,f.Enemy,1,f.P.Creature,null);
     if(modifiers=="weak")await PowerCmd.Apply<WeakPower>(f.Choice,f.P.Creature,1,f.Enemy,null);
     await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);string shown=StrikeText(card,f.Enemy);int hp=f.Enemy.CurrentHp;await f.Play(card,f.Enemy);int actual=hp-f.Enemy.CurrentHp;
     Check("fusion strike targeted display matches real hit under "+modifiers,StrikeNumbers(shown).First()==actual);
     evidence.Add(new{Modifiers=modifiers,Display=shown,Actual=actual});
    }
    await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);int beforeHp=f.Enemy.CurrentHp;Pack.Settings.Startup[option.Key]=false;await f.Play(card,f.Enemy);int off=beforeHp-f.Enemy.CurrentHp;
    Pack.Settings.Startup[option.Key]=true;beforeHp=f.Enemy.CurrentHp;await f.Play(card,f.Enemy);int on=beforeHp-f.Enemy.CurrentHp;
    Check("fusion strike toggle never changes native damage",off==on&&on>0);
   }
   foreach(var card in new[]{canonical,Card(1),Card(3),Restore(Card(3))}){
    var numbers=StrikeNumbers(card.GetDescriptionForPile(PileType.None));Check("fusion strike outside combat and encyclopedia format "+card.IsMutable+card.CurrentUpgradeLevel,numbers.Take(2).SequenceEqual(new[]{8,card.DynamicVars.ExtraDamage.IntValue}));
   }
   foreach(string template in new[]{"Deal {Damage:diff()} damage. Gain {MagicNumber:diff()} per material.","造成{CalculatedDamage:diff()}点伤害，每张素材额外{ExtraDamage:diff()}点。"}){
    table.MergeWith(new(){[key]=template});var card=Card(1);Check("fusion strike translated and already fixed templates remain valid "+template,StrikeNumbers(card.GetDescriptionForPile(PileType.None)).Take(2).SequenceEqual(new[]{8,5})&&table.GetRawText(key)==template);
   }
   table.MergeWith(new(){[key]=original});
   foreach(var other in new[]{ModelDb.Card<StrikeIronclad>(),ModelDb.GetById<CardModel>(ModelDb.GetId(Find("FusionExpansionPack.FusionBladeCard")))}){
    var originalKeys=other.Description.Variables.Keys.ToArray();Pack.Settings.Startup[option.Key]=false;var disabled=other.Description.Variables.Keys.ToArray();Pack.Settings.Startup[option.Key]=true;
    Check("fusion strike leaves neighboring card description untouched "+other.Id,originalKeys.SequenceEqual(disabled));
   }
   await FusionStrikeScreenshot(canonical);
   File.WriteAllText(Output+"/fusion-strike-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Failed=0,Checks=passes.Skip(start),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
  }finally{Pack.Settings.Startup[option.Key]=active;table.MergeWith(new(){[key]=original});}
 }
 static async Task FusionStrikeScreenshot(CardModel canonical){
  using var f=new BloodFixture();var root=Fuse(ModelDb.Card<StrikeIronclad>().ToMutable(),ModelDb.Card<DefendIronclad>().ToMutable(),ModelDb.Card<Bash>().ToMutable());f.Combat.AddCard(root,f.P);await CardPileCmd.Add(root,PileType.Draw,skipVisuals:true);
  var viewport=new SubViewport{Size=new(1500,900),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("1b202c")});
  try{int i=0;foreach(int level in new[]{0,1,3}){
    var card=canonical.ToMutable();if(level>1)Scorch(card);Up(card,level);f.Combat.AddCard(card,f.P);await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);
    NCard node;bool testMode=TestMode.IsOn;
    try{TestMode.IsOn=false;node=NCard.Create(card)!;host.AddChild(node);node.Position=new(245+500*i++,450);node.Scale=Vector2.One*.9f;node.UpdateVisuals(PileType.Hand,CardPreviewMode.Normal);}
    finally{TestMode.IsOn=testMode;}
    string text=node.GetNode<MegaRichTextLabel>("%DescriptionLabel").Text;
    Check("fusion strike real NCard shows correct damage and increment "+level,StrikeNumbers(text).Take(2).SequenceEqual(new[]{8+3*(3+2*level),3+2*level}));
    if(level==0){
     var preview=(CardModel)card.ClonePreservingMutability();preview.UpgradePreviewType=CardUpgradePreviewType.Combat;preview.UpgradeInternal();NCard previewNode;
     try{TestMode.IsOn=false;previewNode=NCard.Create(preview)!;host.AddChild(previewNode);previewNode.Position=new(-1000,0);previewNode.UpdateVisuals(PileType.Hand,CardPreviewMode.Upgrade);}
     finally{TestMode.IsOn=testMode;}
     Check("fusion strike native upgrade preview displays next damage and extra",StrikeNumbers(previewNode.GetNode<MegaRichTextLabel>("%DescriptionLabel").Text).Take(2).SequenceEqual(new[]{23,5})&&card.CurrentUpgradeLevel==0);
     previewNode.QueueFree();
    }
   }
   await Wait(.2);await AfterDraw();Check("fusion strike native numerical screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/fusion-strike-native.png")==Error.Ok);
  }finally{viewport.QueueFree();await Wait(.03);}
 }
}
