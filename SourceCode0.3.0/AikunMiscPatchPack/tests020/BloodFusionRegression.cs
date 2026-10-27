using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.TestSupport;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static void BloodFusionState(CardModel parent,int[] levels,string label){
  if(!Levels(parent).SequenceEqual(levels)||parent.CurrentUpgradeLevel!=levels.Sum())GD.Print("[BloodFusion] STATE "+label+" expected="+string.Join(",",levels)+" actual="+string.Join(",",Levels(parent))+" total="+parent.CurrentUpgradeLevel+" raw="+string.Join(",",Materials(parent).Select(c=>c.CurrentUpgradeLevel)));
  Check(label+" child and outer levels",Levels(parent).SequenceEqual(levels)&&parent.CurrentUpgradeLevel==levels.Sum());
  var children=Materials(parent);foreach(var blood in children.OfType<Pack.BloodSect>()){
   int expectedLevel=levels[Array.IndexOf(children,blood)];if(blood.DynamicVars["LeechPercent"].IntValue!=BloodPct(expectedLevel)||!blood.GetDescriptionForPile(PileType.None).Contains(BloodPct(expectedLevel)+"%"))GD.Print("[BloodFusion] BLOOD "+label+" leech="+blood.DynamicVars["LeechPercent"].IntValue+" description="+blood.GetDescriptionForPile(PileType.None));
   BloodCostState(blood,expectedLevel,label+" blood");
  }
  bool retain=children.Any(c=>c.Keywords.Contains(CardKeyword.Retain));
  Check(label+" outer retain follows materials",parent.Keywords.Contains(CardKeyword.Retain)==retain);
  bool sum=(bool)AccessTools.Property(Find("CardFusion.CardFusionSettings"),"SumCostMode").GetValue(null)!;
  var costs=children.Select(c=>c.EnergyCost.GetWithModifiers(CostModifiers.Local)).ToArray();int expected=sum?costs.Sum():costs.Max()+1;
  Check(label+" outer cost follows actual child costs",parent.EnergyCost.GetWithModifiers(CostModifiers.Local)==expected);
 }
 static void BloodFusionRoundtrip(CardModel source,int[] levels,string tag){
  string before=MaterialSnapshot(source);var clone=(CardModel)source.ClonePreservingMutability();BloodFusionState(clone,levels,tag+" clone");
  Check(tag+" independent child instances",Materials(source).Zip(Materials(clone)).All(p=>!ReferenceEquals(p.First,p.Second)));
  var loaded=Restore(source);for(int i=0;i<3;i++){BloodFusionState(loaded,levels,tag+" save "+i);loaded=Restore(loaded);}
  Check(tag+" original unchanged",MaterialSnapshot(source)==before);
 }
 static async Task BloodFusionChecks(){
  int start=passes.Count;var errors=new List<string>();
  foreach(int n in new[]{0,1}){
   var single=Up(ModelDb.Card<Pack.BloodSect>().ToMutable(),n);BloodCostState(single,n,"related standalone "+n);BloodCostState(Restore(single),n,"related standalone save "+n);
  }
  if(Minimal){Check("blood fusion missing optional mod safely",Pack.Settings.Status(Pack.Options.All.Single(o=>o.Key=="fusion_upgrades")).Contains("未装载相关模组"));return;}
  var store=Find("FreeLoadout.OverrideStore");var setBreak=AccessTools.Method(store,"SetCardLimitBreak");var setLevel=AccessTools.Method(store,"SetCardUpgradeLevel");
  async Task Case(string name,Func<Task> test){try{await test();}catch(Exception e){errors.Add(name+": "+e);GD.Print("[BloodFusion] CASE FAIL "+name+": "+e);}}
  var setting=Find("CardFusion.CardFusionSettings");_ = AccessTools.Property(setting,"SumCostMode").GetValue(null);var sumField=AccessTools.Field(setting,"_sumCostMode");bool old=(bool)sumField.GetValue(null)!;
  try{
   foreach(bool sum in new[]{false,true})foreach(bool reverse in new[]{false,true})await Case("normal sum="+sum+" reverse="+reverse,async()=>{
    sumField.SetValue(null,sum);using var f=new BloodFixture();var blood=f.Card<Pack.BloodSect>();var bash=f.Card<Bash>();var parent=reverse?Fuse(bash,blood):Fuse(blood,bash);f.Run.AddCard(parent,f.P);await CardPileCmd.Add(parent,PileType.Deck,skipVisuals:true);
    BloodFusionState(parent,[0,0],"normal initial");Check("normal initial no retain",!parent.ShouldRetainThisTurn);
    Up(parent);BloodFusionState(parent,[1,0],"normal first");Up(parent);BloodFusionState(parent,[1,1],"normal second");
    Check("normal exhausted materials cannot upgrade",!parent.IsUpgradable);BloodFusionRoundtrip(parent,[1,1],"normal");
    await CardPileCmd.Add(parent,PileType.Hand,skipVisuals:true);Check("normal upgraded fused card retains in hand",parent.ShouldRetainThisTurn);
    var turn=AccessTools.Field(typeof(CombatManager),"_turnState").GetValue(CombatManager.Instance)!;var choice=new HookPlayerChoiceContext(f.P,f.P.NetId,GameActionType.Combat);
    await ((Task)AccessTools.Method(typeof(CombatManager),"FlushPlayerHand").Invoke(CombatManager.Instance,[turn,f.P,choice])!).WaitAsync(TimeSpan.FromSeconds(8));
    Check("normal fused card survives actual turn end",parent.Pile?.Type==PileType.Hand);
   });
   sumField.SetValue(null,false);
   await Case("parent console breakthrough",async()=>{
    using var f=new BloodFixture();var parent=Fuse(f.Card<Pack.BloodSect>(),Up(f.Card<Bash>()));f.Run.AddCard(parent,f.P);await CardPileCmd.Add(parent,PileType.Deck,skipVisuals:true);setBreak.Invoke(null,[parent,true]);
    for(int n=1;n<=6;n++){Check("parent console can upgrade "+n,parent.IsUpgradable);Up(parent);BloodFusionState(parent,[n,1],"parent console "+n);}
    BloodFusionRoundtrip(parent,[6,1],"parent console");var copy=Restore(parent);Up(copy);BloodFusionState(copy,[7,1],"parent console continues after save");
    await BloodFusionPreview(parent,[7,1]);
    var combatCopy=f.Combat.CloneCard(parent);await f.Play(combatCopy,f.Enemy);var power=f.P.Creature.GetPower<Pack.BloodSectPower>()!;Check("parent console actual play uses child grade",power.LeechPercent==200&&power.UpgradeLevel==6&&power.OverflowEnabled);
   });
   await Case("child console before fusion",async()=>{
    using var f=new BloodFixture();var blood=f.Card<Pack.BloodSect>();setBreak.Invoke(null,[blood,true]);Up(blood,4);var parent=Fuse(blood,Up(f.Card<Bash>()));f.Run.AddCard(parent,f.P);await CardPileCmd.Add(parent,PileType.Deck,skipVisuals:true);
    BloodFusionState(parent,[4,1],"child console fused");Check("child console still upgradable in fusion",parent.IsUpgradable);Up(parent);BloodFusionState(parent,[5,1],"child console continued");
    BloodFusionRoundtrip(parent,[5,1],"child console");await BloodFusionPreview(parent,[6,1]);
   });
   await Case("child console set level while fused",async()=>{
    using var f=new BloodFixture();var parent=Fuse(f.Card<Pack.BloodSect>(),Up(f.Card<Bash>()));f.Run.AddCard(parent,f.P);await CardPileCmd.Add(parent,PileType.Deck,skipVisuals:true);
    var blood=Materials(parent).OfType<Pack.BloodSect>().Single();_ = parent.Keywords;_ = parent.GetDescriptionForPile(PileType.Deck);setBreak.Invoke(null,[blood,true]);
    foreach(int n in new[]{5,0,1,3}){setLevel.Invoke(null,[blood,n]);BloodFusionState(parent,[n,1],"direct child assignment "+n);}
    BloodFusionRoundtrip(parent,[3,1],"direct child assignment");
   });
   await Case("repeat enchant and refusion",async()=>{
    using var f=new BloodFixture();var parent=Fuse(f.Card<Pack.BloodSect>(),Up(f.Card<Bash>()));Scorch(parent);Up(parent,5);BloodFusionState(parent,[5,1],"parent scorching");
    var again=Fuse(parent,Up(f.Card<DefendIronclad>()));BloodFusionState(again,[5,1,1],"refusion");BloodFusionRoundtrip(again,[5,1,1],"refusion");await Task.CompletedTask;
   });
   await Case("console outer level and switch",async()=>{
    using var f=new BloodFixture();var parent=Fuse(f.Card<Pack.BloodSect>(),Up(f.Card<Bash>()));f.Run.AddCard(parent,f.P);await CardPileCmd.Add(parent,PileType.Deck,skipVisuals:true);setBreak.Invoke(null,[parent,true]);
    // 控制台沿用融合作者的降级再重放流程，指定总等级会从首个素材重新分配。
    setLevel.Invoke(null,[parent,7]);BloodFusionState(parent,[7,0],"console outer assigned seven");
    setLevel.Invoke(null,[parent,7]);BloodFusionState(parent,[7,0],"console repeated outer assignment never discounts twice");
    setBreak.Invoke(null,[parent,false]);Check("disabling outer breakthrough leaves next ordinary material eligible",parent.IsUpgradable);Up(parent);BloodFusionState(parent,[7,1],"outer breakthrough off upgrades remaining bash");Check("disabled outer breakthrough stops capped blood",!parent.IsUpgradable);
    setBreak.Invoke(null,[parent,true]);Check("reenabling outer breakthrough permits next material upgrade",parent.IsUpgradable);Up(parent);BloodFusionState(parent,[8,1],"console outer resumed");
    BloodFusionRoundtrip(parent,[8,1],"console outer assignment");
    setLevel.Invoke(null,[parent,0]);BloodFusionState(parent,[0,0],"console outer reset zero");setLevel.Invoke(null,[parent,3]);BloodFusionState(parent,[3,0],"console outer upgrade after reset");await Task.CompletedTask;
   });
   await Case("multiple blood and unrelated fusion",async()=>{
    using var f=new BloodFixture();var parent=Fuse(f.Card<Pack.BloodSect>(),Up(f.Card<Pack.BloodSect>()));f.Run.AddCard(parent,f.P);await CardPileCmd.Add(parent,PileType.Deck,skipVisuals:true);setBreak.Invoke(null,[parent,true]);Up(parent,3);BloodFusionState(parent,[3,1],"two blood materials");BloodFusionRoundtrip(parent,[3,1],"two blood materials");
    var unrelated=Fuse(Up(f.Card<Bash>()),Up(f.Card<DefendIronclad>()));setBreak.Invoke(null,[unrelated,true]);Check("unrelated exhausted fusion eligibility unchanged",!unrelated.IsUpgradable);
    var single=f.Card<Pack.BloodSect>();setBreak.Invoke(null,[single,true]);Up(single,5);BloodCostState(Restore(single),5,"related standalone console no pile save");await Task.CompletedTask;
   });
   await Case("explicit zero child then fusion breakthrough save",async()=>{
    using var f=new BloodFixture();var blood=f.Card<Pack.BloodSect>();setBreak.Invoke(null,[blood,true]);setLevel.Invoke(null,[blood,0]);var parent=Fuse(blood,Up(f.Card<Bash>()));f.Run.AddCard(parent,f.P);await CardPileCmd.Add(parent,PileType.Deck,skipVisuals:true);
    Up(parent,4);BloodFusionState(parent,[4,1],"explicit zero child continued");BloodFusionRoundtrip(parent,[4,1],"explicit zero child");await Task.CompletedTask;
   });
  }finally{sumField.SetValue(null,old);}
  File.WriteAllText(Output+"/blood-fusion-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Errors=errors},new JsonSerializerOptions{WriteIndented=true}));
  if(errors.Count>0)throw new Exception(string.Join("\n",errors));
 }
 static async Task BloodFusionPreview(CardModel source,int[] next){
  bool old=TestMode.IsOn;NGridCardHolder? holder=null;NUpgradePreview? comparison=null;string before=MaterialSnapshot(source);
  string file=ProjectSettings.GlobalizePath("user://freeloadout/run_overrides.json");byte[]? config=File.Exists(file)?File.ReadAllBytes(file):null;
  try{
   TestMode.IsOn=false;holder=NGridCardHolder.Create(NCard.Create(source)!)!;Tree.Root.AddChild(holder);holder.Position=new(450,750);holder.SetIsPreviewingUpgrade(true);
   BloodFusionState(holder.CardNode!.Model!,next,"fusion grid preview");holder.SetIsPreviewingUpgrade(false);Check("fusion grid restores same source",ReferenceEquals(holder.CardNode.Model,source));
   comparison=ResourceLoader.Load<PackedScene>("res://scenes/cards/upgrade_preview.tscn").Instantiate<NUpgradePreview>();Tree.Root.AddChild(comparison);comparison.Position=new(1500,750);comparison.Card=source;
   var after=(Control)AccessTools.Field(typeof(NUpgradePreview),"_after").GetValue(comparison)!;BloodFusionState(after.GetChild<NPreviewCardHolder>(0).CardNode!.Model!,next,"fusion detail comparison");
   Check("fusion previews leave original unchanged",MaterialSnapshot(source)==before);if(config!=null)Check("fusion previews do not write console config",File.ReadAllBytes(file).SequenceEqual(config));
   await Wait(.15);await AfterDraw();Tree.Root.GetTexture().GetImage().SavePng(Output+"/blood-fusion-native-preview.png");
  }finally{TestMode.IsOn=old;holder?.QueueFree();comparison?.QueueFree();await Wait(.03);}
 }
}
