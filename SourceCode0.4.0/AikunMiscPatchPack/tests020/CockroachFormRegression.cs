using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.addons.mega_text;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static int CockroachCap(BloodFixture f)=>f.P.Creature.GetPower<HardToKillPower>()?.Amount??0;
 static async Task CockroachFormChecks(){
  int first=passes.Count;var evidence=new List<object>();
  var canonical=ModelDb.Card<Pack.CockroachForm>();
  Check("cockroach own miscellaneous option without third party dependency",Pack.Options.All.Single(o=>o.Key==Pack.CockroachFormContent.Key) is {Misc:true,Required.Length:0}&&new Pack.PackConfig().Get(Pack.CockroachFormContent.Key));
  Check("cockroach remains an independent miscellaneous with unique option keys",Pack.Options.All.Single(o=>o.Key==Pack.CockroachFormContent.Key).Misc&&Pack.Options.All.Select(o=>o.Key).Distinct().Count()==Pack.Options.All.Length);
  Check("cockroach canonical 3 cost gold colorless power",canonical.EnergyCost.Canonical==3&&canonical.Rarity==CardRarity.Rare&&canonical.Pool is ColorlessCardPool&&canonical.Type==CardType.Power&&canonical.TargetType==TargetType.Self);
  Check("cockroach enters ordinary and unlocked colorless pool",canonical.Pool.AllCards.Any(c=>c is Pack.CockroachForm)&&canonical.Pool.GetUnlockedCards(UnlockState.all,CardMultiplayerConstraint.None).Any(c=>c is Pack.CockroachForm));
  Check("cockroach available to mod form generation",Pack.ModFormContent.IsCandidate(canonical)&&Pack.ModFormContent.Groups.SelectMany(g=>g.Cards).Any(c=>c is Pack.CockroachForm));
  foreach(int level in new[]{0,1}){
   var card=Up(canonical.ToMutable(),level);int initial=level==0?5:1;string tag="cockroach L"+level;
   Check(tag+" exact initial amount and per-turn three",card.DynamicVars["HardToKillPower"].IntValue==initial&&card.DynamicVars["PerTurn"].IntValue==3);
   Check(tag+" cost unchanged retain only upgraded",card.EnergyCost.Canonical==3&&card.Keywords.Contains(CardKeyword.Retain)==(level>0)&&!card.Keywords.Contains(CardKeyword.Exhaust));
   Check(tag+" formatted native description",card.GetDescriptionForPile(PileType.None).Contains(initial+"层")&&card.GetDescriptionForPile(PileType.None).Contains("3层")&&card.GetDescriptionForPile(PileType.None).Contains("难以杀灭"));
   for(int i=0;i<3;i++){card=Restore(card);Check(tag+" JSON exact amount cost retain "+i,card.CurrentUpgradeLevel==level&&card.DynamicVars["HardToKillPower"].IntValue==initial&&card.EnergyCost.Canonical==3&&card.Keywords.Contains(CardKeyword.Retain)==(level>0));}
   var clone=(CardModel)card.ClonePreservingMutability();Check(tag+" clone preserves amount independent variables",clone.DynamicVars["HardToKillPower"].IntValue==initial&&!ReferenceEquals(clone.DynamicVars,card.DynamicVars));
   using var f=new BloodFixture(multiplayer:true);var played=Up(f.Card<Pack.CockroachForm>(),level);
   Check(tag+" fresh combat has no carried power",CockroachCap(f)==0&&!f.P.Creature.HasPower<Pack.CockroachFormPower>());
   await f.Play(played);var form=f.P.Creature.GetPower<Pack.CockroachFormPower>()!;
   Check(tag+" actual play applies initial cap and recurring three once",CockroachCap(f)==initial&&form.Amount==3&&f.P.Creature.Powers.OfType<Pack.CockroachFormPower>().Count()==1);
   Check(tag+" power card leaves hand without exhaust",played.Pile?.Type==PileType.Play||played.Pile==null);
   int hp=f.P.Creature.CurrentHp;await CreatureCmd.Damage(f.Choice,f.P.Creature,30,ValueProp.Unpowered,f.Enemy,null,null);
   Check(tag+" real damage limited by original hard-to-kill power",hp-f.P.Creature.CurrentHp==initial);
   await Hook.AfterSideTurnStart(f.Combat,CombatSide.Enemy,[f.Enemy]);
   Check(tag+" enemy turn does not grant",CockroachCap(f)==initial);
   await Hook.AfterSideTurnStart(f.Combat,CombatSide.Player,[f.Other.Creature]);
   Check(tag+" ally-only turn does not grant",CockroachCap(f)==initial&&!f.Other.Creature.HasPower<HardToKillPower>());
   for(int turn=1;turn<=3;turn++){
    await Hook.AfterSideTurnStart(f.Combat,CombatSide.Player,[f.P.Creature]);
    Check(tag+" native turn hook adds exactly three turn="+turn,CockroachCap(f)==initial+3*turn&&form.Amount==3);
   }
   hp=f.P.Creature.CurrentHp;await CreatureCmd.Damage(f.Choice,f.P.Creature,30,ValueProp.Unpowered,f.Enemy,null,null);await CreatureCmd.Damage(f.Choice,f.P.Creature,30,ValueProp.Unpowered,f.Enemy,null,null);
   Check(tag+" multiple hits each use updated original damage cap",hp-f.P.Creature.CurrentHp==2*(initial+9));
   var copied=(PowerModel)form.ClonePreservingMutability();Check(tag+" recurring power clone retains counter",copied.Amount==3&&copied.Owner==null);
   await f.Play(f.Card<Pack.CockroachForm>());
   Check(tag+" second form adds initial five and recurring amount",CockroachCap(f)==initial+14&&form.Amount==6&&f.P.Creature.Powers.OfType<Pack.CockroachFormPower>().Count()==1);
   await Hook.AfterSideTurnStart(f.Combat,CombatSide.Player,[f.P.Creature]);
   Check(tag+" stacked forms grant six instead of resetting",CockroachCap(f)==initial+20&&form.Amount==6);
   var other=Up(f.Card<Pack.CockroachForm>(f.Other));await f.Play(other);
   Check(tag+" second player upgraded amount isolated",f.Other.Creature.GetPower<HardToKillPower>()?.Amount==1&&f.Other.Creature.GetPower<Pack.CockroachFormPower>()?.Amount==3&&form.Amount==6);
   await Hook.AfterSideTurnStart(f.Combat,CombatSide.Player,[f.Other.Creature]);
   Check(tag+" second player own turn isolated",f.Other.Creature.GetPower<HardToKillPower>()?.Amount==4&&CockroachCap(f)==initial+20);
   evidence.Add(new{level,Initial=initial,AfterThreeTurns=initial+9,AfterSecondFormAndTurn=CockroachCap(f),Recurring=form.Amount});
  }
  using(var f=new BloodFixture()){
   var normal=f.Card<Pack.CockroachForm>();var retained=Up(f.Card<Pack.CockroachForm>());
   await CardPileCmd.Add(normal,PileType.Hand,skipVisuals:true);await CardPileCmd.Add(retained,PileType.Hand,skipVisuals:true);
   Check("cockroach upgrade reports native retain",retained.ShouldRetainThisTurn&&!normal.ShouldRetainThisTurn);
   var turn=AccessTools.Field(typeof(CombatManager),"_turnState").GetValue(CombatManager.Instance)!;
   await ((Task)AccessTools.Method(typeof(CombatManager),"FlushPlayerHand").Invoke(CombatManager.Instance,[turn,f.P,new HookPlayerChoiceContext(f.P,f.P.NetId,GameActionType.Combat)])!).WaitAsync(TimeSpan.FromSeconds(8));
   Check("cockroach real hand flush retains only upgraded form",retained.Pile?.Type==PileType.Hand&&normal.Pile?.Type==PileType.Discard);
  }
  bool active=Pack.Settings.Startup[Pack.CockroachFormContent.Key];
  try{
   Pack.Settings.Startup[Pack.CockroachFormContent.Key]=false;
   Check("cockroach disabled hides all acquisition routes",!canonical.CanBeGeneratedInCombat&&!canonical.CanBeGeneratedByModifiers&&!Pack.ModFormContent.IsCandidate(canonical)&&!canonical.Pool.AllCards.Any(c=>c is Pack.CockroachForm)&&!canonical.Pool.GetUnlockedCards(UnlockState.all,CardMultiplayerConstraint.None).Any(c=>c is Pack.CockroachForm));
   using var f=new BloodFixture();var saved=Restore(Up(canonical.ToMutable()));f.Combat.AddCard(saved,f.P);await f.Play(saved);
   Check("cockroach existing saved card still usable while disabled",CockroachCap(f)==1&&f.P.Creature.GetPower<Pack.CockroachFormPower>()?.Amount==3);
   await Hook.AfterSideTurnStart(f.Combat,CombatSide.Player,[f.P.Creature]);Check("cockroach existing effect remains while disabled",CockroachCap(f)==4);
  }finally{Pack.Settings.Startup[Pack.CockroachFormContent.Key]=active;}
  if(!Minimal){
   var repeated=canonical.ToMutable();Scorch(repeated);Up(repeated,5);repeated=Restore(repeated);
   Check("cockroach external repeated upgrades never create negative cap",repeated.CurrentUpgradeLevel==5&&repeated.DynamicVars["HardToKillPower"].IntValue==1&&repeated.Keywords.Contains(CardKeyword.Retain));
   using var f=new BloodFixture();var fused=Fuse(Up(canonical.ToMutable()),ModelDb.Card<DefendIronclad>().ToMutable());f.Combat.AddCard(fused,f.P);await f.Play(fused);
   Check("cockroach fusion child applies same upgraded native powers",CockroachCap(f)==1&&f.P.Creature.GetPower<Pack.CockroachFormPower>()?.Amount==3);
  }
  await CockroachScreenshot();
  File.WriteAllText(Output+"/cockroach-form-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Checks=passes.Skip(first).ToArray(),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
 }
 static async Task CockroachScreenshot(){
  using var f=new BloodFixture();var viewport=new SubViewport{Size=new(1300,1000),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("1b1528")});bool test=TestMode.IsOn;
  try{
   for(int level=0;level<=1;level++){
    var card=Up(f.Card<Pack.CockroachForm>(),level);TestMode.IsOn=false;var node=NCard.Create(card)!;host.AddChild(node);node.Position=new(360+580*level,450);node.Scale=Vector2.One*.95f;node.UpdateVisuals(PileType.None,CardPreviewMode.Normal);TestMode.IsOn=test;
    string text=node.GetNode<MegaRichTextLabel>("%DescriptionLabel").Text;
    Check("cockroach rendered card description and retain L"+level,text.Contains("难以杀灭")&&text.Contains(level==0?"5":"1")&&text.Contains("保留")== (level==1));
    Check("cockroach portrait cached real resource L"+level,card.PortraitPath.StartsWith("user://AikunMiscPatchPack/card-portraits/")&&ResourceLoader.Exists(card.PortraitPath)&&card.Portrait.GetSize()==new Vector2(1536,1024)&&card.BetaPortraitPath==card.PortraitPath);
    Check("cockroach private beta PNG path matches artwork L"+level,(string)AccessTools.Property(typeof(CardModel),"BetaPortraitPngPath").GetValue(card)! ==card.PortraitPath);
   }
   await Wait(.2);await AfterDraw();Check("cockroach native normal upgraded screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/cockroach-form-native.png")==Error.Ok);
  }finally{TestMode.IsOn=test;viewport.QueueFree();await Wait(.05);}
 }
}
