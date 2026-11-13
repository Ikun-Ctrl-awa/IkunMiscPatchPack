using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.TestSupport;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static readonly Dictionary<CardModel,int> disgustAdds=new(ReferenceEqualityComparer.Instance);
 static void DisgustObserveAdd(CardModel __instance,CardKeyword keyword){if(keyword==CardKeyword.Eternal&&disgustAdds.ContainsKey(__instance))disgustAdds[__instance]++;}
 sealed class DisgustFlowSelector:ICardSelector {
  public CardModel[] Pair=[],Clear=[];public int FusionCalls,CleanupCalls;
  public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options,int minSelect,int maxSelect){
   var available=options.ToArray();if(minSelect==2&&maxSelect==2){FusionCalls++;return Task.FromResult<IEnumerable<CardModel>>(Pair.Where(available.Contains));}
   CleanupCalls++;return Task.FromResult<IEnumerable<CardModel>>(Clear.Where(available.Contains).Take(maxSelect));
  }
  public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,IReadOnlyList<CardRewardAlternative> alternatives)=>throw new NotSupportedException();
 }
 static CardModel[] DisgustParts(CardModel card)=>new[]{card}.Concat(FusionQuestBlessingFix.Entry.Descendants(card)).Distinct().ToArray();
 static async Task<bool> DisgustNativeFuse(Player player,DisgustFlowSelector selector,params CardModel[] pair){
  selector.Pair=pair;var option=Activator.CreateInstance(Find("CardFusion.FuseRestSiteOption"),player)!;
  using(CardSelectCmd.UseSelector(selector))return await ((Task<bool>)AccessTools.Method(option.GetType(),"OnSelect").Invoke(option,null)!).WaitAsync(TimeSpan.FromSeconds(12));
 }
 static async Task DisgustEternalFusionChecks(){
  int start=passes.Count;var errors=new List<string>();
  async Task Case(string name,Func<Task> test){try{await test();}catch(Exception e){errors.Add(name+": "+e);Godot.GD.Print("[DisgustCase] "+name+" "+e);}}
  for(int level=0;level<=1;level++){int l=level;await Case("native eternal "+l,()=>DisgustExistingEternal(l));}
  if(!Minimal)for(int level=0;level<=1;level++)foreach(bool transform in new[]{false,true})foreach(bool other in new[]{false,true}){int l=level;bool t=transform,o=other;await Case($"actual rest fusion {l}/{t}/{o}",()=>DisgustActualFusion(l,t,o));}
  if(!Minimal)for(int level=0;level<=1;level++){int l=level;await Case("permanent fusion immunity "+l,()=>DisgustFusionImmunity(l));}
  File.WriteAllText(Output+"/disgust-eternal-fusion-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Errors=errors,Checks=passes.Skip(start)},new JsonSerializerOptions{WriteIndented=true}));
  if(errors.Count>0)throw new Exception(string.Join("\n",errors));
 }
 static async Task DisgustExistingEternal(int level){
  using var f=NewDongniFixture();var p=f.P;
  var ember=f.Run.CreateCard<Bash>(p);ember.EnchantInternal(ModelDb.Enchantment<TezcatarasEmber>().ToMutable(),1);ember.Enchantment!.ModifyCard();
  CardModel[] targets=[ember,f.Run.CreateCard<AscendersBane>(p),f.Run.CreateCard<ForbiddenGrimoire>(p)];
  foreach(var card in targets){await CardPileCmd.Add(card,PileType.Deck);CardCmd.ApplyKeyword(card,CardKeyword.Eternal);CardCmd.ApplyKeyword(card,CardKeyword.Eternal);}
  var keywords=targets.ToDictionary(c=>c,c=>c.Keywords.Where(k=>k!=CardKeyword.Eternal).ToHashSet());
  Check("disgust actual ember bane grimoire start eternal level="+level,targets.All(Eternal)&&ember.Enchantment is TezcatarasEmber);
  var observer=new Harmony("Aikun020Probe.DisgustExistingEternal");foreach(var card in targets)disgustAdds[card]=0;
  observer.Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.AddKeyword)),prefix:new HarmonyMethod(typeof(Entry),nameof(DisgustObserveAdd)));
  CardModel source;
  try{source=await DisgustAdd(f.Run,p,level);Check("disgust existing eternal has one effective keyword level="+level,targets.All(c=>c.Keywords.Count(k=>k==CardKeyword.Eternal)==1));Check("disgust existing eternal never redundantly readded level="+level,targets.All(c=>disgustAdds[c]==0));}
  finally{observer.UnpatchAll(observer.Id);disgustAdds.Clear();}
  var selector=new DisgustFlowSelector{Clear=targets};using(CardSelectCmd.UseSelector(selector))await CardPileCmd.RemoveFromDeck(source,false);
  Check("disgust clears all native eternal sources in one departure level="+level,targets.All(c=>!Eternal(c)&&c.IsRemovable&&c.IsTransformable)&&selector.CleanupCalls==(level==0?1:0));
  foreach(var card in targets){var restored=card;for(int cycle=0;cycle<3;cycle++){restored=Restore(restored);Check($"disgust native eternal removal persists {level}/{card.Id}/{cycle}",!Eternal(restored)&&restored.Keywords.SetEquals(keywords[card])&&(card!=ember||restored.Enchantment is TezcatarasEmber));}}
  CardCmd.ApplyKeyword(ember,CardKeyword.Eternal);Check("disgust subsequent independent eternal may still be added level="+level,Eternal(ember));
 }
 static async Task DisgustActualFusion(int level,bool transform,bool other){
  using var f=NewDongniFixture(true);var p=other?f.Other:f.P;var foreign=other?f.P:f.Other;
  var source=await DisgustAdd(f.Run,p,level);var ordinary=p.Deck.Cards.Where(c=>c!=source).ToArray();var material=ordinary[0];var unlocked=ordinary[1];var locked=ordinary[2];CardCmd.RemoveKeyword(unlocked,CardKeyword.Eternal);
  var foreignBefore=foreign.Deck.Cards.Select(Eternal).ToArray();var selector=new DisgustFlowSelector();
  Check($"disgust actual rest fusion succeeds {level}/{transform}/{other}",await DisgustNativeFuse(p,selector,source,material));
  var fused=p.Deck.Cards.Single(c=>DisgustParts(c).OfType<Pack.DongniDisgust>().Any());
  Check($"disgust actual fusion does not retrigger either lifecycle {level}/{transform}/{other}",selector.FusionCalls==1&&selector.CleanupCalls==0&&!Eternal(unlocked)&&Eternal(locked));
  Check($"disgust actual fusion clears parent and all children eternal {level}/{transform}/{other}",DisgustParts(fused).All(c=>!Eternal(c))&&fused.IsRemovable&&fused.IsTransformable);
  Check($"disgust actual refusion succeeds {level}/{transform}/{other}",await DisgustNativeFuse(p,selector,fused,ordinary[3]));
  fused=p.Deck.Cards.Single(c=>DisgustParts(c).OfType<Pack.DongniDisgust>().Any());
  Check($"disgust refusion no entry or exit effects {level}/{transform}/{other}",selector.FusionCalls==2&&selector.CleanupCalls==0&&!Eternal(unlocked)&&Eternal(locked)&&DisgustParts(fused).All(c=>!Eternal(c)));
  var restored=fused;for(int cycle=0;cycle<3;cycle++){restored=Restore(restored);Check($"disgust refusion save restored unlocked {level}/{transform}/{other}/{cycle}",DisgustParts(restored).All(c=>!Eternal(c))&&DisgustParts(restored).OfType<Pack.DongniDisgust>().Single().CurrentUpgradeLevel==level);}
  var targets=p.Deck.Cards.Where(c=>c!=fused&&Eternal(c)).ToArray();selector.Clear=targets.Take(5).ToArray();int calls=selector.CleanupCalls;
  using(CardSelectCmd.UseSelector(selector)){
   if(transform)await CardCmd.Transform([new CardTransformation(fused,f.Run.CreateCard<DefendIronclad>(p))],null,CardPreviewStyle.None);
   else await CardPileCmd.RemoveFromDeck(fused,false);
  }
  Check($"disgust actual fused departure applies exactly once {level}/{transform}/{other}",selector.CleanupCalls-calls==(level==0?1:0)&&targets.Count(Eternal)==(level==0?Math.Max(0,targets.Length-5):0)&&!p.Deck.Cards.Contains(fused));
  Check($"disgust fusion departure isolated to owner {level}/{transform}/{other}",foreign.Deck.Cards.Select(Eternal).SequenceEqual(foreignBefore));
  selector.Pair=[];Check($"disgust cancelled actual fusion leaves deck untouched {level}/{transform}/{other}",!await DisgustNativeFuse(p,selector));
 }
 static async Task DisgustFusionImmunity(int level){
  using var f=NewDongniFixture();var p=f.P;var source=Up(f.Run.CreateCard<Pack.DongniDisgust>(p),level);var material=f.Run.CreateCard<Bash>(p);material.EnchantInternal(ModelDb.Enchantment<TezcatarasEmber>().ToMutable(),1);material.Enchantment!.ModifyCard();
  var fused=Fuse(source,material);Godot.GD.Print("[DisgustImmunity] "+JsonSerializer.Serialize(new{level,RootEternal=Eternal(fused),MaterialEternal=Eternal(material),Parts=DisgustParts(fused).Select(c=>c.GetType().FullName),Keywords=fused.Keywords.Select(k=>k.ToString())}));Check("disgust fusion rejects inherited eternal even before deck entry level="+level,!Eternal(fused)&&Eternal(material));
  f.Run.AddCard(fused,p);await CardPileCmd.Add(fused,PileType.Deck);await DisgustAdd(f.Run,p);
  Check("disgust fusion rejects later disgust eternal grant level="+level,!Eternal(fused)&&fused.IsRemovable&&fused.IsTransformable);
  CardCmd.ApplyKeyword(fused,CardKeyword.Eternal);Check("disgust fusion rejects later explicit eternal level="+level,!Eternal(fused));
  fused.EnchantInternal(ModelDb.Enchantment<TezcatarasEmber>().ToMutable(),1);fused.Enchantment!.ModifyCard();Check("disgust fusion rejects later native ember enchantment level="+level,!Eternal(fused)&&fused.Enchantment is TezcatarasEmber);
  var copy=(CardModel)fused.ClonePreservingMutability();Check("disgust fusion immunity survives clone level="+level,!Eternal(copy));
  copy=fused;for(int cycle=0;cycle<3;cycle++){copy=Restore(copy);CardCmd.ApplyKeyword(copy,CardKeyword.Eternal);Check($"disgust fusion immunity survives save and regrant {level}/{cycle}",!Eternal(copy)&&DisgustParts(copy).OfType<Pack.DongniDisgust>().Single().CurrentUpgradeLevel==level);}
  var ordinary=Fuse(f.Run.CreateCard<StrikeIronclad>(p),f.Run.CreateCard<DefendIronclad>(p));CardCmd.ApplyKeyword(ordinary,CardKeyword.Eternal);Check("disgust immunity does not block unrelated fusion level="+level,Eternal(ordinary));
 }
}
