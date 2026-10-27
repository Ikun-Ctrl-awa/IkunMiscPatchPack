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
using MegaCrit.Sts2.addons.mega_text;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static int BloodPct(int level)=>100+20*Math.Max(0,level-1);
 static void BloodCostState(CardModel card,int level,string tag){
  Check(tag+" effective upgrade count",Pack.BloodSectConsoleCompat.Level(card)==level);
  Check(tag+" cost changes only on first upgrade",card.EnergyCost.GetWithModifiers(CostModifiers.Local)==(level==0?4:3));
  Check(tag+" retain starts with first upgrade",card.Keywords.Contains(CardKeyword.Retain)==(level>=1));
  Check(tag+" leech variable and description agree",card.DynamicVars["LeechPercent"].IntValue==BloodPct(level)&&card.GetDescriptionForPile(card.Pile?.Type??PileType.None).Contains(BloodPct(level)+"%"));
 }
 static async Task BloodCostConsoleChecks(){
  int start=passes.Count;var evidence=new List<object>();
  Check("blood cost canonical is four energy without retain",ModelDb.Card<Pack.BloodSect>().EnergyCost.GetWithModifiers(CostModifiers.Local)==4&&!ModelDb.Card<Pack.BloodSect>().Keywords.Contains(CardKeyword.Retain));
  foreach(int level in Minimal?new[]{0,1}:new[]{0,1,2,5,10}){
   var card=ModelDb.Card<Pack.BloodSect>().ToMutable();if(level>1)Scorch(card);Up(card,level);string tag="blood normal cost n="+level;
   BloodCostState(card,level,tag);BloodCostState((CardModel)card.ClonePreservingMutability(),level,tag+" clone");BloodCostState(Restore(card),level,tag+" save");
   evidence.Add(new{Path="normal/scorching",Level=level,Cost=card.EnergyCost.GetWithModifiers(CostModifiers.Local),Retain=card.Keywords.Contains(CardKeyword.Retain),Leech=card.DynamicVars["LeechPercent"].IntValue});
  }
  using(var f=new BloodFixture()){
   var basic=f.Card<Pack.BloodSect>();var retained=Up(f.Card<Pack.BloodSect>());var ordinary=f.Card<StrikeIronclad>();
   foreach(var card in new CardModel[]{basic,retained,ordinary})await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);
   Check("blood upgraded card reports native retain in hand",retained.ShouldRetainThisTurn&&!basic.ShouldRetainThisTurn&&!ordinary.ShouldRetainThisTurn);
   var turn=AccessTools.Field(typeof(CombatManager),"_turnState").GetValue(CombatManager.Instance)!;
   for(int round=1;round<=2;round++){
    var choice=new HookPlayerChoiceContext(f.P,f.P.NetId,GameActionType.Combat);
    await ((Task)AccessTools.Method(typeof(CombatManager),"FlushPlayerHand").Invoke(CombatManager.Instance,[turn,f.P,choice])!).WaitAsync(TimeSpan.FromSeconds(8));
    Check("blood survives real native end turn hand flush "+round,retained.Pile?.Type==PileType.Hand&&retained.ShouldRetainThisTurn&&basic.Pile?.Type==PileType.Discard&&ordinary.Pile?.Type==PileType.Discard);
   }
  }
  if(Minimal){Check("blood console integration safely absent",!Pack.BloodSectConsoleCompat.Available);await BloodCostScreenshot();Write();return;}
  Check("blood console actual optional integration installed",Pack.BloodSectConsoleCompat.Available);
  var store=Find("FreeLoadout.OverrideStore");var setBreak=AccessTools.Method(store,"SetCardLimitBreak");var setLevel=AccessTools.Method(store,"SetCardUpgradeLevel");
  using(var f=new BloodFixture()){
   var card=f.Card<Pack.BloodSect>();await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);setBreak.Invoke(null,[card,true]);
   for(int level=1;level<=6;level++){
    Check("blood console remains eligible before upgrade "+level,card.IsUpgradable);CardCmd.Upgrade(card,CardPreviewStyle.None);BloodCostState(card,level,"blood console sequential n="+level);
    evidence.Add(new{Path="console sequential",Level=level,NativeLevel=card.CurrentUpgradeLevel,Cost=card.EnergyCost.GetWithModifiers(CostModifiers.Local),Leech=card.DynamicVars["LeechPercent"].IntValue});
   }
   var clone=(CardModel)card.ClonePreservingMutability();BloodCostState(clone,6,"blood console raw clone");
   var loaded=Restore(card);for(int round=0;round<3;round++){BloodCostState(loaded,6,"blood console repeated save "+round);loaded=Restore(loaded);}
   Up(loaded);BloodCostState(loaded,7,"blood console continues after load");BloodCostState(card,6,"blood console source unchanged after clone load");
   await f.Play(card);var power=f.P.Creature.GetPower<Pack.BloodSectPower>()!;
   Check("blood console played power uses effective level and 200 leech",power.LeechPercent==200&&power.UpgradeLevel==6&&power.OverflowEnabled);
   await f.Play(f.Card<Pack.BloodSect>());f.P.Creature.SetCurrentHpInternal(95);await f.Damage(f.Enemy,5);
   Check("blood console stacks with base copy and preserves overflow",power.LeechPercent==300&&power.OverflowEnabled&&f.P.Creature.CurrentHp==100&&f.Pool==10);
  }
  using(var f=new BloodFixture()){
   var card=f.Card<Pack.BloodSect>();await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);setBreak.Invoke(null,[card,true]);
   foreach(int level in new[]{8,2,0,1,5}){setLevel.Invoke(null,[card,level]);BloodCostState(card,level,"blood console direct assignment n="+level);}
   var bash=f.Card<Bash>();setBreak.Invoke(null,[bash,true]);Up(bash,3);
   Check("blood console leaves other card upgrades unchanged",PreviewLevel(bash)==3&&bash.EnergyCost.GetWithModifiers(CostModifiers.Local)==2&&!bash.Keywords.Contains(CardKeyword.Retain));
  }
  using(var f=new BloodFixture()){
   var card=f.Card<Pack.BloodSect>();await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);setBreak.Invoke(null,[card,true]);Up(card,4);
   await BloodConsolePreview(card);BloodCostState(card,4,"blood console source after UI previews");
  }
  await BloodCostScreenshot();Write();
  void Write()=>File.WriteAllText(Output+"/blood-cost-console-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Failed=0,Checks=passes.Skip(start),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
 }
 static async Task BloodConsolePreview(CardModel source){
  bool test=TestMode.IsOn;NGridCardHolder? holder=null;NUpgradePreview? comparison=null;
  string path=ProjectSettings.GlobalizePath("user://freeloadout/run_overrides.json");byte[]? before=File.Exists(path)?File.ReadAllBytes(path):null;
  try{
   TestMode.IsOn=false;holder=NGridCardHolder.Create(NCard.Create(source)!)!;Tree.Root.AddChild(holder);
   for(int i=0;i<2;i++){
    holder.SetIsPreviewingUpgrade(true);var preview=holder.CardNode!.Model!;BloodCostState(preview,5,"blood console grid next level "+i);
    Check("blood console grid title follows effective level "+i,preview.Title.EndsWith("+5"));
    holder.SetIsPreviewingUpgrade(false);Check("blood console grid restores exact source "+i,ReferenceEquals(holder.CardNode.Model,source));
   }
   comparison=ResourceLoader.Load<PackedScene>("res://scenes/cards/upgrade_preview.tscn").Instantiate<NUpgradePreview>();Tree.Root.AddChild(comparison);comparison.Card=source;
   var after=(Control)AccessTools.Field(typeof(NUpgradePreview),"_after").GetValue(comparison)!;var next=after.GetChild<NPreviewCardHolder>(0).CardNode!.Model!;
   BloodCostState(next,5,"blood console native upgrade comparison");
   if(before!=null)Check("blood console UI preview never changes saved console config",File.ReadAllBytes(path).SequenceEqual(before));
  }finally{TestMode.IsOn=test;holder?.QueueFree();comparison?.QueueFree();await Wait(.03);}
 }
 static async Task BloodCostScreenshot(){
  using var f=new BloodFixture();var viewport=new SubViewport{Size=new(1600,1000),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("181522")});bool test=TestMode.IsOn;
  try{int i=0;foreach(int level in Minimal?new[]{0,1}:new[]{0,1,3,6}){
    var card=f.Card<Pack.BloodSect>();if(level>1){AccessTools.Method(Find("FreeLoadout.OverrideStore"),"SetCardLimitBreak").Invoke(null,[card,true]);}Up(card,level);await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);
    TestMode.IsOn=false;var node=NCard.Create(card)!;host.AddChild(node);node.Position=new(200+400*i++,490);node.Scale=Vector2.One*.78f;node.UpdateVisuals(PileType.Hand,CardPreviewMode.Normal);TestMode.IsOn=test;
    Check("blood real native card description retain n="+level,node.GetNode<MegaRichTextLabel>("%DescriptionLabel").Text.Contains("保留")== (level>=1));
    BloodCostState(card,level,"blood real native card n="+level);
   }
   await Wait(.2);await AfterDraw();Check("blood cost and retain screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/blood-cost-console-native.png")==Error.Ok);
  }finally{TestMode.IsOn=test;viewport.QueueFree();await Wait(.03);}
 }
}
