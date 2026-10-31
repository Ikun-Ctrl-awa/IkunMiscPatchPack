using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.addons.mega_text;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static int EmberCost(CardModel card)=>card.EnergyCost.GetWithModifiers(CostModifiers.All);
 static void Ember(CardModel card,string slot="primary"){
  var ember=ModelDb.Enchantment<TezcatarasEmber>().ToMutable();
  if(slot=="primary")card.EnchantInternal(ember,1);
  else{
   card.EnchantInternal(ModelDb.Enchantment<Sharp>().ToMutable(),1);
   if(slot=="extra")Extra(card,ember);
   else{
    var wrapper=(EnchantmentModel)AccessTools.Method(Find("Plague.PlagueCompositeEnchantment"),"ConvertFrom").Invoke(null,[card,card.Enchantment])!;
    AccessTools.Method(wrapper.GetType(),"ImportExistingEnchantment").Invoke(wrapper,[ember]);
   }
  }
  ember.ModifyCard();card.FinalizeUpgradeInternal();
 }
 static CardModel EmberFusion(string slot="primary",bool reverse=false){
  var first=Card<Bash>();first.EnchantInternal(ModelDb.Enchantment<Sown>().ToMutable(),1);
  var second=Card<StrikeIronclad>();Ember(second,slot);
  return reverse?Fuse(second,first):Fuse(first,second);
 }
 static async Task<CardModel> EmberBattleCopy(BloodFixture f,CardModel deck){
  f.Run.AddCard(deck,f.P);await CardPileCmd.Add(deck,PileType.Deck,skipVisuals:true);
  var copy=f.Combat.CloneCard(deck);await CardPileCmd.Add(copy,PileType.Hand,skipVisuals:true);return copy;
 }
 static async Task FusionEmberChecks(){
  int first=passes.Count;string key=Pack.FusionEmberCostFix.Key;
  Check("ember independent default-on CardFusion compatibility option",Pack.Options.All.Single(o=>o.Key==key) is {Misc:false,Required:["CardFusion"]}&&new Pack.PackConfig().Get(key));
  if(Minimal){Check("ember absent CardFusion safely skipped",!Pack.Options.Eligible(key)&&!Pack.RuntimeLoader.States.ContainsKey(key));return;}
  Check("ember compatibility patch applied",Pack.RuntimeLoader.States.GetValueOrDefault(key)=="已应用。");
  var sum=AccessTools.Field(Find("CardFusion.CardFusionSettings"),"_sumCostMode");var synth=AccessTools.Field(Find("CardFusion.CardFusionSettings"),"_synthesizeMode");
  object oldSum=sum.GetValue(null)!,oldSynth=synth.GetValue(null)!;bool active=Pack.Settings.Startup[key];var evidence=new List<object>();
  try{
   foreach(bool enabled in new[]{false,true}){
    Pack.Settings.Startup[key]=enabled;sum.SetValue(null,false);synth.SetValue(null,false);using var f=new BloodFixture();
    var deck=EmberFusion();var card=await EmberBattleCopy(f,deck);int before=EmberCost(card);
    Check("ember original max-plus-one bug versus fixed zero enabled="+enabled,before==(enabled?0:3));
    Check("ember actual combat copy retains ember on source card enabled="+enabled,Materials(card).Any(c=>Pack.EnchantmentView.Read(c).Any(e=>e is TezcatarasEmber)));
    Up(card);int after=EmberCost(card);if(enabled)Check("ember patched native battle upgrade preserves zero",after==0);
    evidence.Add(new{Enabled=enabled,BeforeUpgrade=before,AfterUpgrade=after});
   }
   Pack.Settings.Startup[key]=true;
   foreach(bool sumMode in new[]{false,true})foreach(bool synthMode in new[]{false,true})foreach(string slot in new[]{"primary","extra","plague"}){
    sum.SetValue(null,sumMode);synth.SetValue(null,synthMode);using var f=new BloodFixture();var deck=EmberFusion(slot);var card=await EmberBattleCopy(f,deck);
    string tag=$"ember sum={sumMode} synth={synthMode} slot={slot}";
    Check(tag+" before upgrade both canonical and actual default zero",card.EnergyCost.Canonical==0&&EmberCost(card)==0);
    Check(tag+" independent combat children and permanent deck unaffected",EmberCost(deck)==0&&Materials(card).Zip(Materials(deck)).All(p=>!ReferenceEquals(p.First,p.Second))&&deck.CurrentUpgradeLevel==0);
    int energy=f.P.PlayerCombatState!.Energy;var spent=await card.SpendResources();int hp=f.Enemy.CurrentHp;
    await card.OnPlayWrapper(f.Choice,f.Enemy,false,new ResourceInfo{EnergySpent=spent.Item1,EnergyValue=0,StarsSpent=spent.Item2,StarValue=0},skipCardPileVisuals:true).WaitAsync(TimeSpan.FromSeconds(8));
    Check(tag+" actual manual play spends zero energy and still resolves attack",spent.Item1==0&&f.P.PlayerCombatState.Energy>=energy&&f.Enemy.CurrentHp<hp);
    Check(tag+" preserves discard result and eternal keyword",card.Pile?.Type==PileType.Discard&&card.Keywords.Contains(CardKeyword.Eternal));
    Up(card);Check(tag+" local upgrade remains zero",EmberCost(card)==0&&deck.CurrentUpgradeLevel==0);
    var clone=(CardModel)card.ClonePreservingMutability();Check(tag+" cloned upgraded fusion remains zero",EmberCost(clone)==0&&clone.EnergyCost.Canonical==0);
   }
   sum.SetValue(null,false);synth.SetValue(null,false);
   foreach(bool reversed in new[]{false,true}){
    using var f=new BloodFixture();var saved=EmberFusion(reverse:reversed);
    for(int i=0;i<3;i++){
     saved=Restore(saved);Check("ember JSON restored default zero reversed="+reversed+" cycle="+i,EmberCost(saved)==0&&saved.EnergyCost.Canonical==0);
    }
    var card=await EmberBattleCopy(f,saved);Check("ember restored battle copy zero independent of source order reversed="+reversed,EmberCost(card)==0);
    var again=Fuse(saved,Card<DefendIronclad>());var combined=await EmberBattleCopy(f,again);
    Check("ember re-fusion with additional non-ember material remains zero reversed="+reversed,Materials(combined).Length==3&&EmberCost(combined)==0);
   }
   using(var f=new BloodFixture()){
    var card=await EmberBattleCopy(f,EmberFusion());
    card.EnergyCost.AddThisCombat(2);Check("ember native temporary additive cost still applies",EmberCost(card)==2);
    AccessTools.Method(card.GetType(),"FixEnergyCostFields").Invoke(card,null);Check("ember default resync does not clear additive cost",EmberCost(card)==2);
    card.EnergyCost.SetThisTurn(4);Check("ember native absolute temporary cost still applies",EmberCost(card)==4);
    card.EnergyCost.EndOfTurnCleanup();Check("ember turn cleanup restores default plus combat modifier",EmberCost(card)==2);
   }
   foreach(bool sumMode in new[]{false,true}){
    sum.SetValue(null,sumMode);using var f=new BloodFixture();var plain=await EmberBattleCopy(f,Fuse(Card<Bash>(),Card<DefendIronclad>()));
    Check("ember unrelated fusion retains original cost sum="+sumMode,EmberCost(plain)==3);
    var x=Card<Whirlwind>();Ember(x);var xFusion=await EmberBattleCopy(f,Fuse(x,Card<DefendIronclad>()));
    Check("ember X-cost fusion retains X rule sum="+sumMode,xFusion.EnergyCost.CostsX);
    var single=f.Card<StrikeIronclad>();Ember(single);Check("ember standalone native enchant remains zero",EmberCost(single)==0);
   }
   // 当前真实卡牌副本含多重附魔、灼热升级和保存字段；不修改玩家文件。
   if(System.Environment.GetEnvironmentVariable("AIKUN_EMBER_INCIDENT_CARD") is {} incidentCard&&File.Exists(incidentCard)){
   using(var f=new BloodFixture()){
    var raw=File.ReadAllText(incidentCard);
    var saved=CardModel.FromSerializable(JsonSerializer.Deserialize<SerializableCard>(raw,JsonSerializationUtility.Options)!);
    var card=await EmberBattleCopy(f,saved);Check("ember exact reported saved fusion is zero immediately on entering battle",EmberCost(card)==0&&Materials(card).Any(c=>Pack.EnchantmentView.Read(c).Any(e=>e is TezcatarasEmber)));
    var snapshot=MaterialSnapshot(saved);Up(card);Check("ember reported fusion remains zero after native upgrade without changing permanent card",EmberCost(card)==0&&MaterialSnapshot(saved)==snapshot);
    await EmberScreenshot(f,saved);
   }
   }
  }finally{
   Pack.Settings.Startup[key]=active;sum.SetValue(null,oldSum);synth.SetValue(null,oldSynth);
   File.WriteAllText(Output+"/fusion-ember-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Checks=passes.Skip(first).ToArray(),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
  }
 }
 static async Task EmberScreenshot(BloodFixture f,CardModel saved){
  bool test=TestMode.IsOn;var viewport=new SubViewport{Size=new(1400,1000),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("171923")});
  try{
   for(int i=0;i<2;i++){
    var card=f.Combat.CloneCard(saved);if(i==1)Up(card);await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);TestMode.IsOn=false;
    var node=NCard.Create(card)!;host.AddChild(node);node.Position=new(340+i*660,470);node.Scale=Vector2.One*.85f;node.UpdateVisuals(PileType.Hand,CardPreviewMode.Normal);TestMode.IsOn=test;
    Check("ember native combat cost label displays zero before/after upgrade="+i,node.GetNode<MegaLabel>("%EnergyLabel").Text=="0");
   }
   await Wait(.2);await AfterDraw();Check("ember native combat card comparison screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/fusion-ember-native.png")==Error.Ok);
  }finally{TestMode.IsOn=test;viewport.QueueFree();await Wait(.03);}
 }
}
