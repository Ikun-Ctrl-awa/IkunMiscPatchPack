using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.addons.mega_text;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static readonly Type[] xTypes=[typeof(Pack.KnifeRain),typeof(Pack.HundredForge),typeof(Pack.GlassFactory)];
 static readonly Type[] xAllTypes=[..xTypes,typeof(Pack.SacrificialCombo),typeof(Pack.SoulRitual)];
 static readonly List<(CardModel Card,Creature? Target,int Spent,bool Auto)> xPlays=new();
 static readonly List<(decimal Amount,Player Player)> xForges=new();
 static CardModel? xForeignAuto;
 static void ObserveXPlay(CardPlay cardPlay){if(cardPlay.Card is Pack.XExpansionCard or Shiv)xPlays.Add((cardPlay.Card,cardPlay.Target,cardPlay.Resources.EnergySpent,cardPlay.IsAutoPlay));}
 static void ObserveXForge(decimal amount,Player player)=>xForges.Add((amount,player));
 static void XForeignReplay(CardPlay cardPlay,ref Task __result){
  if(xForeignAuto is {} foreign&&cardPlay.Card is Pack.XExpansionCard&&cardPlay.Card.IsDupe){xForeignAuto=null;__result=XPlayForeign(__result,foreign);}
 }
 static async Task XPlayForeign(Task original,CardModel foreign){await original;await CardCmd.AutoPlay(new ThrowingPlayerChoiceContext(),foreign,null,skipCardPileVisuals:true);}
 static BloodFixture XFixture(Type card,bool multi=false){
  CharacterModel character=card==typeof(Pack.KnifeRain)?ModelDb.Character<Silent>():card==typeof(Pack.HundredForge)?ModelDb.Character<Regent>():card==typeof(Pack.SacrificialCombo)?ModelDb.Character<Ironclad>():card==typeof(Pack.SoulRitual)?ModelDb.Character<Necrobinder>():ModelDb.Character<Defect>();
  var f=new BloodFixture(multiplayer:multi,character:character);
  foreach(var p in f.Run.Players)foreach(var c in p.PlayerCombatState!.AllCards.ToArray())c.RemoveFromCurrentPile();
  f.P.PlayerCombatState!.OrbQueue.Clear();return f;
 }
 static CardModel XCard(BloodFixture f,Type type,int level=0){var card=f.Combat.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(type)),f.P);return Up(card,level);}
 static int XShivs=>xPlays.Count(p=>p.Card is Shiv);
 static async Task XExpansionChecks(){
  int first=passes.Count;var evidence=new List<object>();bool active=Pack.Settings.Active(Pack.XCardExpansion.Key);
  var h=new Harmony("Aikun020Probe.XExpansion");h.Patch(AccessTools.Method(typeof(Hook),nameof(Hook.BeforeCardPlayed)),prefix:new HarmonyMethod(typeof(Entry),nameof(ObserveXPlay)));
  h.Patch(AccessTools.Method(typeof(ForgeCmd),nameof(ForgeCmd.Forge)),prefix:new HarmonyMethod(typeof(Entry),nameof(ObserveXForge)));
  h.Patch(AccessTools.Method(typeof(Hook),nameof(Hook.BeforeCardPlayed)),postfix:new HarmonyMethod(typeof(Entry),nameof(XForeignReplay)));
  try{
   Check("X independent miscellaneous defaults on",Pack.Options.All.Single(o=>o.Key==Pack.XCardExpansion.Key) is {Misc:true,Required.Length:0}&&new Pack.PackConfig().Get(Pack.XCardExpansion.Key));
   Check("X five models registered without optional dependencies",Pack.RuntimeLoader.States.GetValueOrDefault(Pack.XCardExpansion.Key)=="已应用。");
   var poolTypes=new[]{typeof(SilentCardPool),typeof(RegentCardPool),typeof(DefectCardPool),typeof(IroncladCardPool),typeof(NecrobinderCardPool)};
   for(int i=0;i<xAllTypes.Length;i++){
    var c=ModelDb.GetById<CardModel>(ModelDb.GetId(xAllTypes[i]));
    Check("X native cost rarity and own character pool "+c.GetType().Name,c.EnergyCost.CostsX&&c.Type==(c is Pack.SacrificialCombo?CardType.Attack:c is Pack.SoulRitual?CardType.Power:CardType.Skill)&&c.Rarity==(c is Pack.SoulRitual?CardRarity.Uncommon:CardRarity.Rare)&&c.Pool.GetType()==poolTypes[i]);
    Check("X own native acquisition pool "+c.GetType().Name,c.Pool.GetUnlockedCards(UnlockState.all,CardMultiplayerConstraint.None).Any(v=>v.GetType()==c.GetType()));
    Check("X correct exhaust "+c.GetType().Name,c.Keywords.Contains(CardKeyword.Exhaust)==(c is Pack.HundredForge));
    foreach(int level in new[]{0,1}){
     var saved=Up(c.ToMutable(),level);
     for(int n=0;n<3;n++){saved=Restore(saved);Check($"X saved identity upgrade and cost {c.GetType().Name}/{level}/{n}",saved.GetType()==c.GetType()&&saved.CurrentUpgradeLevel==level&&saved.EnergyCost.CostsX&&saved.Pool.GetType()==poolTypes[i]);}
    }
   }
   foreach(bool enabled in new[]{false,true}){
    Pack.Settings.Startup[Pack.XCardExpansion.Key]=enabled;
    foreach(var type in xAllTypes){var c=ModelDb.GetById<CardModel>(ModelDb.GetId(type));Check("X acquisition toggle "+type.Name+enabled,c.Pool.AllCards.Any(v=>v.GetType()==type)==enabled&&c.CanBeGeneratedInCombat==enabled&&c.CanBeGeneratedByModifiers==enabled);}
   }
   Pack.Settings.Startup[Pack.XCardExpansion.Key]=true;
   foreach(var type in xTypes)foreach(int level in new[]{0,1})foreach(int x in new[]{0,1,3})foreach(bool chemical in new[]{false,true}){
    using var f=XFixture(type);var card=XCard(f,type,level);if(chemical)f.P.AddRelicInternal(ModelDb.Relic<ChemicalX>().ToMutable(),silent:true);
    var queue=f.P.PlayerCombatState!.OrbQueue;int effective=x+(chemical?2:0),count=effective+level;queue.AddCapacity(Math.Max(0,count+2-queue.Capacity));
    xPlays.Clear();xForges.Clear();f.P.PlayerCombatState.Energy=x;int hp=f.Enemy.CurrentHp;await DongniManualPlay(f,card);
    string tag=$"X {type.Name} level={level} paid={x} chemical={chemical}";
    Check(tag+" captured payment and one spend",card.EnergyCost.CapturedXValue==x&&f.P.PlayerCombatState.Energy==0&&xPlays.Count(p=>p.Card==card)==1&&xPlays.Single(p=>p.Card==card).Spent==x);
    Check(tag+" native result pile",card.Pile?.Type==(type==typeof(Pack.HundredForge)?PileType.Exhaust:PileType.Discard));
    if(card is Pack.KnifeRain){Check(tag+" exact generated automatic shivs",XShivs==2*effective&&xPlays.Where(p=>p.Card is Shiv).All(p=>p.Auto&&p.Spent==0&&p.Card.CurrentUpgradeLevel==level&&p.Card.Pile?.Type==PileType.Exhaust));Check(tag+" actual native damage",hp-f.Enemy.CurrentHp==2*effective*(level==0?4:6));}
    if(card is Pack.HundredForge){Check(tag+" one native forge exact amount",xForges.Count==1&&xForges[0].Amount==(level==0?15:20)*effective&&xForges[0].Player==f.P);Check(tag+" actual sovereign blade damage",f.P.PlayerCombatState.AllCards.OfType<SovereignBlade>().Single().DynamicVars.Damage.BaseValue==ModelDb.Card<SovereignBlade>().DynamicVars.Damage.BaseValue+(level==0?15:20)*effective);}
    if(card is Pack.GlassFactory){Check(tag+" actual native glass orb count",queue.Orbs.Count==count&&queue.Orbs.All(o=>o is GlassOrb));Check(tag+" temporary focus before glass orbs",f.P.Creature.GetPowerAmount<FocusPower>()==1&&f.P.Creature.GetPowerAmount<Pack.GlassFactoryFocusPower>()==1&&queue.Orbs.All(o=>o.PassiveVal==5));}
    evidence.Add(new{Card=type.Name,Level=level,X=x,Chemical=chemical,Shivs=XShivs,Damage=hp-f.Enemy.CurrentHp,Orbs=queue.Orbs.Count});
   }
   // 与独立原生目标随机流对照，证明每张小刀重抽目标，而不是只抽一次重复攻击。
   foreach(int level in new[]{0,1}){
    using var f=XFixture(typeof(Pack.KnifeRain));var other=f.Combat.CreateCreature(ModelDb.Monster<CalcifiedCultist>().ToMutable(),CombatSide.Enemy,"X-second-target");f.Combat.AddCreature(other);other.SetMaxHpInternal(10000);other.SetCurrentHpInternal(10000);
    var targets=f.Combat.HittableEnemies.ToArray();var rng=new Rng(f.Run.Rng.CombatTargets.ToSerializable());var expected=Enumerable.Range(0,12).Select(_=>rng.NextItem(targets)).ToArray();
    var card=XCard(f,typeof(Pack.KnifeRain),level);f.P.PlayerCombatState!.Energy=6;xPlays.Clear();await DongniManualPlay(f,card);
    Check("X shivs native independent random sequence level="+level,xPlays.Where(p=>p.Card is Shiv).Select(p=>p.Target).SequenceEqual(expected));
    Check("X shivs actual split damage level="+level,expected.Distinct().Count()==2&&10000-f.Enemy.CurrentHp==expected.Count(t=>t==f.Enemy)*(level==0?4:6)&&10000-other.CurrentHp==expected.Count(t=>t==other)*(level==0?4:6));
   }
   using(var f=XFixture(typeof(Pack.KnifeRain))){
    var other=f.Combat.CreateCreature(ModelDb.Monster<CalcifiedCultist>().ToMutable(),CombatSide.Enemy,"X-living-target");f.Combat.AddCreature(other);other.SetMaxHpInternal(10000);other.SetCurrentHpInternal(10000);
    var rng=new Rng(f.Run.Rng.CombatTargets.ToSerializable());var firstTarget=rng.NextItem(f.Combat.HittableEnemies)!;firstTarget.SetCurrentHpInternal(4);var survivor=firstTarget==f.Enemy?other:f.Enemy;
    xPlays.Clear();f.P.PlayerCombatState!.Energy=3;await DongniManualPlay(f,XCard(f,typeof(Pack.KnifeRain)));
    Check("X shiv retargets after kill",XShivs==6&&xPlays.Where(p=>p.Card is Shiv).First().Target==firstTarget&&xPlays.Where(p=>p.Card is Shiv).Skip(1).All(p=>p.Target==survivor)&&10000-survivor.CurrentHp==20);
   }
   using(var f=XFixture(typeof(Pack.GlassFactory),true)){
    var queue=f.P.PlayerCombatState!.OrbQueue;queue.AddCapacity(Math.Max(0,3-queue.Capacity));int cap=queue.Capacity;var card=XCard(f,typeof(Pack.GlassFactory),1);f.P.PlayerCombatState.Energy=cap+1;int hp=f.Enemy.CurrentHp;await DongniManualPlay(f,card);
    Check("X glass full queue native evokes with current focus",queue.Orbs.Count==cap&&hp-f.Enemy.CurrentHp==20);
    await PowerCmd.Apply<FocusPower>(f.Choice,f.P.Creature,3,f.P.Creature,null);await PowerCmd.Apply<HotfixPower>(f.Choice,f.P.Creature,2,f.P.Creature,null);
    var power=f.P.Creature.GetPower<Pack.GlassFactoryFocusPower>()!;await power.AfterSideTurnEnd(f.Choice,f.Other.Creature.Side,[f.Other.Creature]);
    Check("X focus does not expire on other player's turn",f.P.Creature.GetPowerAmount<FocusPower>()==6&&f.Other.Creature.GetPowerAmount<FocusPower>()==0);
    await power.AfterSideTurnEnd(f.Choice,f.P.Creature.Side,[f.P.Creature]);Check("X focus expires own increment only",f.P.Creature.GetPowerAmount<FocusPower>()==5&&!f.P.Creature.HasPower<Pack.GlassFactoryFocusPower>());
    await f.P.Creature.GetPower<HotfixPower>()!.AfterSideTurnEnd(f.Choice,f.P.Creature.Side,[f.P.Creature]);Check("X other native temporary focus remains independent",f.P.Creature.GetPowerAmount<FocusPower>()==3);
   }
   foreach(var type in xTypes){
    using var f=XFixture(type);await DongniCurseInHand(f);var card=XCard(f,type);var queue=f.P.PlayerCombatState!.OrbQueue;queue.AddCapacity(Math.Max(0,5-queue.Capacity));f.P.PlayerCombatState.Energy=3;xPlays.Clear();xForges.Clear();await DongniManualPlay(f,card);
    Check("X delayed pending capture "+type.Name,Pack.DongniCurseRules.PendingCount(f.P)==1&&card.EnergyCost.CapturedXValue==3&&XShivs==0&&xForges.Count==0&&queue.Orbs.Count==0);
    f.P.PlayerCombatState.Energy=1;await DongniNext(f);Check("X delayed never repays "+type.Name,f.P.PlayerCombatState.Energy==1&&Pack.DongniCurseRules.PendingCount(f.P)==0);
    Check("X delayed locks original three energy "+type.Name,type==typeof(Pack.KnifeRain)?XShivs==6:type==typeof(Pack.HundredForge)?xForges.Single().Amount==45:queue.Orbs.Count==3);
    int hp=f.Enemy.CurrentHp;await DongniManualPlay(f,f.Card<StrikeIronclad>(),f.Enemy);
    Check("X replay scope restores normal curse afterward "+type.Name,Pack.DongniCurseRules.PendingCount(f.P)==1&&hp==f.Enemy.CurrentHp);
   }
   using(var f=XFixture(typeof(Pack.KnifeRain),true)){
    await DongniCurseInHand(f);await DongniCurseInHand(f,owner:f.Other);f.P.PlayerCombatState!.Energy=3;f.Other.PlayerCombatState!.Energy=7;
    var card=XCard(f,typeof(Pack.KnifeRain));await DongniManualPlay(f,card);xForeignAuto=f.Card<StrikeIronclad>(f.Other);xPlays.Clear();await DongniNext(f);
    Check("X nested replay bypass isolated to its owner",XShivs==6&&Pack.DongniCurseRules.PendingCount(f.P)==0&&Pack.DongniCurseRules.PendingCount(f.Other)==1&&f.Other.PlayerCombatState.Energy==7);
    int hp=f.Enemy.CurrentHp;await DongniNext(f,f.Other);Check("X foreign owner delayed card resolves on own turn",hp-f.Enemy.CurrentHp==6&&Pack.DongniCurseRules.PendingCount(f.Other)==0);
   }
   foreach(var type in xTypes){
    using var f=XFixture(type);var card=XCard(f,type,1);var queue=f.P.PlayerCombatState!.OrbQueue;queue.AddCapacity(Math.Max(0,5-queue.Capacity));f.P.PlayerCombatState.Energy=2;xPlays.Clear();xForges.Clear();await CardCmd.AutoPlay(f.Choice,card,null,skipCardPileVisuals:true);
    Check("X native free autoplay "+type.Name,f.P.PlayerCombatState.Energy==2&&(type==typeof(Pack.KnifeRain)?XShivs==4:type==typeof(Pack.HundredForge)?xForges.Single().Amount==40:queue.Orbs.Count==3));
   }
   await SacrificialComboChecks();
   await SoulRitualChecks();
   await XExpansionScreenshots();
  }finally{
   Pack.Settings.Startup[Pack.XCardExpansion.Key]=active;h.UnpatchAll(h.Id);xPlays.Clear();xForges.Clear();xForeignAuto=null;
   File.WriteAllText(Output+"/x-expansion-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Checks=passes.Skip(first),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
  }
 }
 static async Task XExpansionScreenshots(){
  using var f=XFixture(typeof(Pack.GlassFactory));var viewport=new SubViewport{Size=new(2600,1300),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("181e26")});
  try{
   for(int level=0;level<2;level++)for(int i=0;i<xAllTypes.Length;i++){
    var card=XCard(f,xAllTypes[i],level);await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);bool old=TestMode.IsOn;NCard node;
    try{TestMode.IsOn=false;node=NCard.Create(card)!;host.AddChild(node);node.Position=new(280+i*520,330+640*level);node.Scale=Vector2.One*.88f;node.UpdateVisuals(PileType.Hand,CardPreviewMode.Normal);}finally{TestMode.IsOn=old;}
    string text=node.GetNode<MegaRichTextLabel>("%DescriptionLabel").Text;Check("X rendered original upgrade native card "+xAllTypes[i].Name+level,node.GetNode<MegaLabel>("%EnergyLabel").Text=="X"&&node.GetNode<TextureRect>("%Portrait").Texture!=null&&!text.Contains('{'));
    Check("X native portrait reload path "+xAllTypes[i].Name+level,ResourceLoader.Exists(card.PortraitPath)&&card.HasPortrait);
    if(card is Pack.KnifeRain)Check("X knife rain compact description "+level,!text.Contains("独立")&&!text.Contains("每张小刀"));
    string plain=CompactVisibleText(QuText(text));File.AppendAllText(Output+"/x-card-labels.jsonl",JsonSerializer.Serialize(new{Card=xAllTypes[i].Name,Level=level,Raw=text,Plain=plain})+"\n");
    if(card is Pack.SacrificialCombo)Check("X combo rendered upgrade changes only counts "+level,plain.Contains(level==0?"至多X张":"至多X+1张")&&plain.Contains(level==0?"伤害X次":"伤害X+1次"));
    if(card is Pack.SoulRitual)Check("X ritual native energy and souls text "+level,plain.Contains(level==0?"灵魂到手牌":"灵魂+到手牌")&&System.Text.RegularExpressions.Regex.Matches(text,@"\[img\][^\[]*_energy_icon\.png\[/img\]").Count==1+level);
   }
   await Wait(.2);await AfterDraw();Check("X five normal and upgraded cards screenshot",viewport.GetTexture().GetImage().SavePng(Output+"/x-expansion-native-cards.png")==Error.Ok);
  }finally{viewport.QueueFree();await Wait(.05);}
 }
}
