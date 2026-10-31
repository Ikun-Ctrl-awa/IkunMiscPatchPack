using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib;
using STS2RitsuLib.Scaffolding.Content;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;

// 只在隔离探针中注册，不参与任何生成池；模拟卡面明确写明可强化三次的素材。
public sealed class RepeatMaterialFixture : ModCardTemplate {
 public RepeatMaterialFixture():base(1,CardType.Attack,CardRarity.Common,TargetType.AnyEnemy){}
 public override CardPoolModel Pool=>ModelDb.CardPool<ColorlessCardPool>();
 public override int MaxUpgradeLevel=>3;
 public override bool CanBeGeneratedInCombat=>false;
 public override bool CanBeGeneratedByModifiers=>false;
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new DamageVar(4,ValueProp.Move)];
 protected override void OnUpgrade()=>DynamicVars["Damage"].UpgradeValueBy(2);
}

public static partial class Entry {
 static CardModel? vetoedMaterial;
 static void MaterialUpgradeVeto(CardModel __instance,ref bool __result){if(ReferenceEquals(__instance,vetoedMaterial))__result=false;}
 static CardModel Fuse(params CardModel[] cards)=>(CardModel)AccessTools.Method(Find("CardFusion.FusedCard"),"CreateFromList").Invoke(null,[cards])!;
 static CardModel[] Materials(CardModel fused)=>((IEnumerable<CardModel>)AccessTools.Property(fused.GetType(),"AllCards").GetValue(fused)!).ToArray();
 static T Up<T>(T card,int n=1) where T:CardModel {for(int i=0;i<n;i++){card.UpgradeInternal();card.FinalizeUpgradeInternal();}return card;}
 static CardModel Card<T>(int level=0) where T:CardModel=>Up(ModelDb.Card<T>().ToMutable(),level);
 static int[] Levels(CardModel parent)=>Materials(parent).Select(PreviewLevel).ToArray();
 static string MaterialSnapshot(CardModel parent)=>string.Join("|",Materials(parent).Select(PreviewSnapshot));
 static void FusionState(string label,CardModel parent,bool canUpgrade,params int[] expected){
  Check(label+" ordered material levels",Levels(parent).SequenceEqual(expected));
  Check(label+" total level",parent.CurrentUpgradeLevel==expected.Sum());
  Check(label+" upgrade eligibility",parent.IsUpgradable==canUpgrade);
  Check(label+" title total",expected.Sum()==0||parent.Title.EndsWith("+"+expected.Sum()));
 }
 static void Scorch(CardModel card){var e=Enchant(Find("MoreEnchant.Enchantments.ScorchingEnchantment"),1);card.EnchantInternal(e,1);e.ModifyCard();}
 static async Task FusionUpgradeChecks(){
  if(Minimal){Check("fusion upgrades safely skips missing CardFusion",Pack.Settings.Status(Pack.Options.All.Single(o=>o.Key=="fusion_upgrades")).Contains("未装载相关模组"));return;}
  string id=ModelDb.GetId<RepeatMaterialFixture>().Entry;
  LocManager.Instance.GetTable("cards").MergeWith(new(){[id+".title"]="重复强化测试素材",[id+".description"]="造成 {Damage} 点伤害。可被强化 3 次。"});
  var first=Card<Bash>(1);var second=Card<DefendIronclad>(1);var third=Card<Anger>(1);
  try{
   Pack.Settings.Startup["fusion_upgrades"]=false;var old=Fuse(first,second,third);
   Check("fusion baseline counts only first two upgraded materials",old.CurrentUpgradeLevel==2);
   Pack.Settings.Startup["fusion_upgrades"]=true;FusionState("all three upgraded",old,false,1,1,1);
   var four=Fuse(Fuse(Card<Bash>(1),Card<DefendIronclad>(1)),Fuse(Card<Anger>(1),Card<StrikeIronclad>(1)));
   FusionState("re-fusion flattens all four levels",four,false,1,1,1,1);
   var repeat=Card<RepeatMaterialFixture>(3);var another=Card<RepeatMaterialFixture>(2);
   FusionState("actual multi-level sum",Fuse(repeat,another),true,3,2);
   var order=Fuse(Card<RepeatMaterialFixture>(1),Card<Bash>());
   FusionState("inherent repeat begins",order,true,1,0);
   Up(order);FusionState("inherent repeat first upgrade",order,true,2,0);
   Up(order);FusionState("inherent repeat second upgrade",order,true,3,0);
   Up(order);FusionState("inherent repeat skips capped first material",order,false,3,1);
   Check("inherent material actual damage upgraded",Materials(order)[0].DynamicVars["Damage"].BaseValue==10);
   var reverse=Fuse(Card<Bash>(),Card<RepeatMaterialFixture>(1));Up(reverse);FusionState("reverse order first",reverse,true,1,1);Up(reverse);FusionState("reverse order second",reverse,true,1,2);
   var curse=Card<Injury>();var status=Card<Dazed>();
   var mixed=Fuse(Card<Bash>(1),curse,Card<DefendIronclad>());
   FusionState("curse skipped before remaining material",mixed,true,1,0,0);Up(mixed);FusionState("mixed materials exhausted",mixed,false,1,0,1);
   var before=MaterialSnapshot(mixed);Up(mixed,3);
   Check("direct repeated upgrade cannot create phantom level",mixed.CurrentUpgradeLevel==2&&MaterialSnapshot(mixed)==before);
   var ordinaryEnchant=Fuse(Card<Bash>(1),Card<DefendIronclad>(1));var sharp=ModelDb.Enchantment<Sharp>().ToMutable();sharp.Amount=3;ordinaryEnchant.EnchantInternal(sharp,3);sharp.ModifyCard();
   FusionState("ordinary enchant does not permit repeat upgrades",ordinaryEnchant,false,1,1);
   var impossible=Fuse(curse,status);FusionState("only unupgradable materials",impossible,false,0,0);Up(impossible);
   Check("unupgradable fusion maximum is zero",impossible.MaxUpgradeLevel==0&&impossible.CurrentUpgradeLevel==0);
   var vetoHarmony=new Harmony("Aikun020Probe.MaterialUpgradeVeto");
   try{
    vetoedMaterial=Card<Bash>();vetoHarmony.Patch(AccessTools.PropertyGetter(typeof(CardModel),nameof(CardModel.IsUpgradable)),postfix:new HarmonyMethod(typeof(Entry),nameof(MaterialUpgradeVeto)){priority=Priority.Last});
    var vetoed=Fuse(vetoedMaterial,Card<Injury>());FusionState("material upgrade veto respected",vetoed,false,0,0);Up(vetoed);
    Check("vetoed material cannot produce empty upgrades",vetoed.CurrentUpgradeLevel==0&&vetoed.MaxUpgradeLevel==0);
   }finally{vetoedMaterial=null;vetoHarmony.UnpatchAll(vetoHarmony.Id);}
   Scorch(impossible);Up(impossible);FusionState("repeat enchant never makes curse or status upgrade",impossible,false,0,0);
   var phantom=Fuse(Card<Bash>(1),Card<DefendIronclad>(1),Card<Injury>());
   Pack.Settings.Startup["fusion_upgrades"]=false;
   Check("fusion baseline phantom upgrade offered",phantom.IsUpgradable);Up(phantom);
   Check("fusion baseline phantom changes no material",phantom.CurrentUpgradeLevel==3&&Levels(phantom).SequenceEqual([1,1,0]));
   Pack.Settings.Startup["fusion_upgrades"]=true;FusionState("phantom upgrade repaired from materials",phantom,false,1,1,0);
   var scorched=Fuse(Card<Bash>(1),Card<DefendIronclad>(1));Scorch(scorched);Up(scorched);
   FusionState("parent scorching upgrades first material again",scorched,true,2,1);Up(scorched);FusionState("parent scorching retains fusion order",scorched,true,3,1);
   var child=Card<Bash>();Scorch(child);Up(child,3);var own=Fuse(child,Card<DefendIronclad>());
   FusionState("child scorching summed at creation",own,true,3,0);Up(own);FusionState("child scorching next upgrade",own,true,4,0);
   var extra=Fuse(Card<Bash>(1),Card<DefendIronclad>(1));Extra(extra,Enchant(Find("MoreEnchant.Enchantments.ScorchingEnchantment"),1));Up(extra);
   FusionState("secondary-slot scorching permits repeat",extra,true,2,1);
   foreach(var sample in new[]{four,order,mixed,scorched,own,extra}){
    var snapshot=MaterialSnapshot(sample);var expected=Levels(sample);var can=sample.IsUpgradable;var loaded=sample;
    for(int i=0;i<3;i++){loaded=Restore(loaded);FusionState("fusion JSON roundtrip "+sample.Title+" "+i,loaded,can,expected);Check("fusion JSON never replays material upgrades "+i,MaterialSnapshot(loaded)==snapshot);}
    var clone=(CardModel)sample.ClonePreservingMutability();FusionState("fusion clone "+sample.Title,clone,can,expected);
    Check("fusion clone owns independent materials",Materials(clone).Zip(Materials(sample)).All(pair=>!ReferenceEquals(pair.First,pair.Second)));
    if(clone.IsUpgradable){Up(clone);Check("fusion clone upgrade does not alter source",MaterialSnapshot(sample)==snapshot);}
   }
   var badSave=mixed.ToSerializable();badSave.CurrentUpgradeLevel=9;
   var repaired=CardModel.FromSerializable(badSave);FusionState("legacy mismatched saved total normalized",repaired,false,1,0,1);
   Check("legacy normalization preserves material effects",MaterialSnapshot(repaired)==MaterialSnapshot(mixed));
   var free=Card<Bash>();AccessTools.Method(Find("FreeLoadout.OverrideStore"),"SetCardLimitBreak").Invoke(null,[free,true]);Up(free,3);
   var freeFusion=Fuse(free,Card<DefendIronclad>(1));FusionState("effective FreeLoadout material levels summed",freeFusion,false,3,1);
   var freeClone=(CardModel)freeFusion.ClonePreservingMutability();FusionState("effective material levels survive fusion clone",freeClone,false,3,1);
   await FusionUpgradePreview(scorched);
  }finally{Pack.Settings.Startup["fusion_upgrades"]=true;}
 }
 static async Task FusionUpgradePreview(CardModel parent){
  run!.AddCard(parent,player!);await CardPileCmd.Add(parent,PileType.Deck,skipVisuals:true);
  int before=parent.CurrentUpgradeLevel;var snapshot=MaterialSnapshot(parent);
  var holder=NGridCardHolder.Create(NCard.Create(parent)!)!;Tree.Root.AddChild(holder);
  NInspectCardScreen? inspect=null;
  try{
   holder.SetIsPreviewingUpgrade(true);var preview=holder.CardNode!.Model!;
   Check("fusion grid previews next total",preview.CurrentUpgradeLevel==before+1&&Levels(preview)[0]==Levels(parent)[0]+1);
   Check("fusion grid preview preserves original materials",MaterialSnapshot(parent)==snapshot);
   inspect=NInspectCardScreen.Create()!;Tree.Root.AddChild(inspect);inspect.Open([parent],0);
   var tick=(NTickbox)AccessTools.Field(typeof(NInspectCardScreen),"_upgradeTickbox").GetValue(inspect)!;tick.IsTicked=true;
   AccessTools.Method(typeof(NInspectCardScreen),"UpdateCardDisplay").Invoke(inspect,null);
   var node=(NCard)AccessTools.Field(typeof(NInspectCardScreen),"_card").GetValue(inspect)!;
   Check("fusion inspect previews same material and total",node.Model!.CurrentUpgradeLevel==preview.CurrentUpgradeLevel&&MaterialSnapshot(node.Model)==MaterialSnapshot(preview));
   await Wait(.3);await AfterDraw();Tree.Root.GetTexture().GetImage().SavePng(Output+"/fusion-upgrade-preview.png");
   Up(parent);Check("fusion actual upgrade matches preview effects",parent.CurrentUpgradeLevel==preview.CurrentUpgradeLevel&&MaterialSnapshot(parent)==MaterialSnapshot(preview));
  }finally{holder.QueueFree();inspect?.Close();await Wait(.3);inspect?.QueueFree();await Wait(.1);}
 }
}
