using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using STS2RitsuLib.Models.Capabilities;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 sealed class DisgustSelector(int take):ICardSelector {
  public int Calls,Min,Max;public CardModel[] Seen=[],Chosen=[];
  public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options,int minSelect,int maxSelect){Calls++;Min=minSelect;Max=maxSelect;Seen=options.ToArray();Chosen=Seen.Take(take<0?maxSelect:Math.Min(take,maxSelect)).ToArray();return Task.FromResult<IEnumerable<CardModel>>(Chosen);}
  public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,IReadOnlyList<CardRewardAlternative> alternatives)=>throw new NotSupportedException();
 }
 static bool Eternal(CardModel card)=>card.Keywords.Contains(CardKeyword.Eternal);
 static async Task<CardModel> DisgustAdd(RunState state,Player p,int level=0){var card=Up(state.CreateCard<Pack.DongniDisgust>(p),level);await CardPileCmd.Add(card,PileType.Deck);return card;}
 static async Task DongniDisgustChecks(){
  int start=passes.Count;var canonical=ModelDb.Card<Pack.DongniDisgust>();
  Check("disgust native zero-cost ethereal exhaust upgradable curse",canonical.Type==CardType.Curse&&canonical.Rarity==CardRarity.Curse&&canonical.Pool is CurseCardPool&&canonical.EnergyCost.Canonical==0&&canonical.MaxUpgradeLevel==1&&canonical.Keywords.SetEquals(new[]{CardKeyword.Ethereal,CardKeyword.Exhaust}));
  Check("disgust tower switch and curse catalog only",Pack.Options.All.Length==51&&ModelDb.CardPool<CurseCardPool>().AllCards.Contains(canonical)&&!Pack.ModFormContent.Groups.SelectMany(g=>g.Cards).Contains(canonical));
  foreach(int level in new[]{0,1})foreach(bool other in new[]{false,true}){
   using var f=NewDongniFixture(true);var p=other?f.Other:f.P;var foreign=other?f.P:f.Other;var deck=p.Deck.Cards.ToArray();var card=await DisgustAdd(f.Run,p,level);
   Check($"disgust entry grants every existing deck card except itself {level}/{p.NetId}",deck.All(Eternal)&&!Eternal(card)&&p.Deck.Cards.Count==deck.Length+1&&card.IsRemovable&&card.IsTransformable);
   Check($"disgust eternal actually locks native removal and transform {level}/{p.NetId}",deck.All(c=>!c.IsRemovable&&!c.IsTransformable)&&foreign.Deck.Cards.All(c=>!Eternal(c)));
   var unlocked=deck[0];CardCmd.RemoveKeyword(unlocked,CardKeyword.Eternal);int locked=p.Deck.Cards.Count(Eternal);
   if(level==0)CardCmd.Upgrade(card);
   Check($"disgust upgrade does not regrant keywords {level}/{p.NetId}",!Eternal(unlocked)&&p.Deck.Cards.Count(Eternal)==locked&&!Eternal(card));
   foreach(var sample in new[]{deck[1],unlocked,card}){
    var restored=sample;for(int i=0;i<3;i++)restored=Restore(restored);
    Check($"disgust eternal positive negative and self DTO persistence {level}/{p.NetId}/"+sample.Id,Eternal(restored)==Eternal(sample)&&restored.CurrentUpgradeLevel==sample.CurrentUpgradeLevel);
    var clone=(CardModel)sample.ClonePreservingMutability();Check($"disgust keyword clones independently {level}/{p.NetId}/"+sample.Id,Eternal(clone)==Eternal(sample));
   }
  }
  foreach(int level in new[]{0,1})foreach(int take in new[]{0,3,99})foreach(bool transform in new[]{false,true}){
   using var f=NewDongniFixture(true);var p=f.P;var card=await DisgustAdd(f.Run,p,level);var selector=new DisgustSelector(take);int before=p.Deck.Cards.Count(Eternal);var replacement=f.Run.CreateCard<DefendIronclad>(p);var foreign=f.Other.Deck.Cards[0];CardCmd.ApplyKeyword(foreign,CardKeyword.Eternal);
   using(CardSelectCmd.UseSelector(selector)){
    if(transform)await CardCmd.Transform([new CardTransformation(card,replacement)],null,CardPreviewStyle.None);
    else await CardPileCmd.RemoveFromDeck(card,false);
   }
   int max=Math.Min(5,before),chosen=level==0?Math.Min(take,max):before;
   Check($"disgust actual {(transform?"transform":"remove")} selector only normal {level}/{take}",level==0?selector.Calls==1&&selector.Min==0&&selector.Max==max&&selector.Seen.Length==before&&selector.Seen.All(c=>c.Owner==p&&c.Pile==p.Deck):selector.Calls==0);
   Check($"disgust exact eternal removal selected normal automatic all upgraded {level}/{take}/{transform}",p.Deck.Cards.Count(Eternal)==before-chosen&&selector.Chosen.All(c=>!Eternal(c)&&c.IsRemovable&&c.IsTransformable)&&selector.Seen.Except(selector.Chosen).All(Eternal)&&!p.Deck.Cards.Contains(card)&&Eternal(foreign));
   Check($"disgust transform replacement and unrelated keywords preserved {level}/{take}/{transform}",(!transform||p.Deck.Cards.Contains(replacement))&&p.Deck.Cards.All(c=>!c.Keywords.Contains(CardKeyword.Ethereal)&&!c.Keywords.Contains(CardKeyword.Exhaust)));
   foreach(var selected in level==0?selector.Chosen:p.Deck.Cards.ToArray()){var copy=selected;for(int i=0;i<3;i++)copy=Restore(copy);Check($"disgust removal survives three card readbacks {level}/{take}/{transform}/"+selected.Id,!Eternal(copy));}
  }
  using(var f=NewDongniFixture()){
   var original=f.P.Deck.Cards[0];var added=f.Run.CreateCard<Pack.DongniDisgust>(f.P);await CardCmd.Transform([new CardTransformation(original,added)],null,CardPreviewStyle.None);
   Check("disgust transformed-into acquisition also grants eternal once",f.P.Deck.Cards.Contains(added)&&!Eternal(added)&&f.P.Deck.Cards.Where(c=>c!=added).All(Eternal));
   var fresh=f.Run.CreateCard<StrikeIronclad>(f.P);await CardPileCmd.Add(fresh,PileType.Deck);Check("disgust later acquired cards are not passively made eternal",!Eternal(fresh));
   await Hook.AfterCardChangedPiles(f.Run,null,added,PileType.None,null);Check("disgust duplicate pile callback cannot regrant to later cards",!Eternal(fresh));
   var selector=new DisgustSelector(0);using(CardSelectCmd.UseSelector(selector))await CardPileCmd.RemoveFromDeck(added,false);
   var rejected=await CardPileCmd.Add(added,PileType.Deck);Check("disgust native rejected removed instance cannot grant again",!rejected.success&&!Eternal(fresh));
   var reacquired=f.Run.CloneCard(added);await CardPileCmd.Add(reacquired,PileType.Deck);Check("disgust valid reacquisition grants again excluding new self",Eternal(fresh)&&!Eternal(reacquired));
  }
  using(var f=NewDongniFixture()){
   var first=await DisgustAdd(f.Run,f.P);var second=await DisgustAdd(f.Run,f.P);
   Check("disgust second copy affects first but excludes only itself",Eternal(first)&&!Eternal(second));CardCmd.Upgrade(second);
   var selector=new DisgustSelector(-1);using(CardSelectCmd.UseSelector(selector))await CardPileCmd.RemoveFromDeck(second,false);
   Check("disgust upgraded automatically clears older curse without selector",!Eternal(first)&&selector.Calls==0);
  }
  foreach(int level in new[]{0,1}){
   using var f=NewDongniFixture();var card=Up(f.Card<Pack.DongniDisgust>(),level);for(int i=0;i<3;i++)await DongniPut(f,PileType.Draw);int hand=f.P.PlayerCombatState!.Hand.Cards.Count;var selector=new DisgustSelector(-1);
   using(CardSelectCmd.UseSelector(selector))await DongniManualPlay(f,card);
   Check("disgust combat play exhaust draws only upgraded level="+level,card.Pile?.Type==PileType.Exhaust&&f.P.PlayerCombatState.Hand.Cards.Count==hand+(level==1?1:0)&&f.P.Deck.Cards.All(c=>!Eternal(c))&&selector.Calls==0);
   var ethereal=Up(f.Card<Pack.DongniDisgust>(),level);await CardPileCmd.Add(ethereal,PileType.Hand,skipVisuals:true);using(CardSelectCmd.UseSelector(selector))await QuEnd(f);
   Check("disgust native ethereal exhaust is not permanent deck removal level="+level,ethereal.Pile?.Type==PileType.Exhaust&&selector.Calls==0&&f.P.Deck.Cards.All(c=>!Eternal(c)));
  }
  using(var f=NewDongniFixture()){
   await DongniCurseInHand(f);var card=Up(f.Card<Pack.DongniDisgust>());await DongniPut(f,PileType.Draw);await DongniManualPlay(f,card);
   Check("disgust playable curse participates in ordinary dongni delay",card.Pile?.Type==PileType.Exhaust&&f.P.PlayerCombatState!.Hand.Cards.Count==1&&Pack.DongniCurseRules.PendingCount(f.P)==1);
   await DongniNext(f);Check("disgust delayed upgraded draw executes next turn without deck side effects",f.P.PlayerCombatState!.Hand.Cards.Count==2&&Pack.DongniCurseRules.PendingCount(f.P)==0&&f.P.Deck.Cards.All(c=>!Eternal(c)));
  }
  if(!Minimal)await DisgustFusion();
  await DisgustRunSave();await DisgustScreenshot();
  bool enabled=Pack.Settings.Startup[Pack.TowerCurseContent.Key];try{
   Pack.Settings.Startup[Pack.TowerCurseContent.Key]=false;Check("disgust toggle stops only acquisition",!ModelDb.CardPool<CurseCardPool>().AllCards.Any(c=>c is Pack.DongniDisgust)&&Restore(canonical.ToMutable()) is Pack.DongniDisgust);
   using var f=NewDongniFixture();var card=await DisgustAdd(f.Run,f.P);Check("disgust existing explicit acquisition still follows card rules when disabled",f.P.Deck.Cards.Where(c=>c!=card).All(Eternal));
  }finally{Pack.Settings.Startup[Pack.TowerCurseContent.Key]=enabled;}
  File.WriteAllText(Output+"/dongni-disgust-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Failed=0,Checks=passes.Skip(start)},new JsonSerializerOptions{WriteIndented=true}));
 }
 static async Task DisgustFusion(){
  using var f=NewDongniFixture();var fused=Fuse(Up(ModelDb.Card<Pack.DongniDisgust>().ToMutable()),ModelDb.Card<StrikeIronclad>().ToMutable());f.Run.AddCard(fused,f.P);await CardPileCmd.Add(fused,PileType.Deck);
  Check("disgust fused permanent entry affects others excludes parent",f.P.Deck.Cards.Where(c=>c!=fused).All(Eternal)&&!Eternal(fused));
  var target=f.P.Deck.Cards.First(c=>c!=fused);var copy=Restore(target);Check("disgust full environment eternal survives native DTO",Eternal(copy));
  var selector=new DisgustSelector(-1);using(CardSelectCmd.UseSelector(selector))await CardPileCmd.RemoveFromDeck(fused,false);
  Check("disgust upgraded fused removal automatically clears all without selector",selector.Calls==0&&f.P.Deck.Cards.All(c=>!Eternal(c)));
  var enchantment=Enchant(Find("MoreEnchant.Enchantments.CalamityCurseEnchantment"),1);var targetWithEnchantment=f.P.Deck.Cards[0];targetWithEnchantment.EnchantInternal(enchantment,1);enchantment.ModifyCard();
  var extra=await DisgustAdd(f.Run,f.P,1);selector=new DisgustSelector(-1);using(CardSelectCmd.UseSelector(selector))await CardPileCmd.RemoveFromDeck(extra,false);
  for(int cycle=0;cycle<3;cycle++){targetWithEnchantment=Restore(targetWithEnchantment);Check("disgust removal persists without deleting original eternal enchantment cycle="+cycle,!Eternal(targetWithEnchantment)&&targetWithEnchantment.Enchantment?.GetType()==enchantment.GetType()&&targetWithEnchantment.Keywords.Contains(CardKeyword.Ethereal));}
 }
 static async Task DisgustRunSave(){
  using var session=new EventSlSession();session.State.PushRoom(new EventRoom(ModelDb.Event<Pack.DongniOffice>()));var card=await DisgustAdd(session.State,session.P);var cleared=session.P.Deck.Cards.First(c=>c!=card);CardCmd.RemoveKeyword(cleared,CardKeyword.Eternal);CardCmd.Upgrade(card);
  var expected=session.P.Deck.Cards.Select(c=>Eternal(c)).ToArray();await SaveManager.Instance.SaveRun(null,false);var result=SaveManager.Instance.LoadRunSave();Check("disgust actual permanent deck SaveManager checkpoint saved",result.Success&&result.SaveData!=null);
  string json=JsonSerializer.Serialize(result.SaveData,JsonSerializationUtility.Options);
  for(int i=0;i<3;i++){var saved=JsonSerializer.Deserialize<SerializableRun>(json,JsonSerializationUtility.Options)!;var restored=RunState.FromSerializable(saved);Check("disgust actual run readback preserves grant removal and no upgrade regrant cycle="+i,restored.Players[0].Deck.Cards.Select(Eternal).SequenceEqual(expected)&&restored.Players[0].Deck.Cards.OfType<Pack.DongniDisgust>().Single().CurrentUpgradeLevel==1);}
  var selector=new DisgustSelector(0);using(CardSelectCmd.UseSelector(selector))await CardPileCmd.RemoveFromDeck(card,false);
  await SaveManager.Instance.SaveRun(null,false);result=SaveManager.Instance.LoadRunSave();Check("disgust upgraded auto clear checkpoint saved without selector",selector.Calls==0&&result.Success&&result.SaveData!=null);
  json=JsonSerializer.Serialize(result.SaveData,JsonSerializationUtility.Options);
  for(int i=0;i<3;i++){var saved=JsonSerializer.Deserialize<SerializableRun>(json,JsonSerializationUtility.Options)!;var restored=RunState.FromSerializable(saved);Check("disgust upgraded auto clear survives full run readback cycle="+i,restored.Players[0].Deck.Cards.All(c=>!Eternal(c))&&!restored.Players[0].Deck.Cards.OfType<Pack.DongniDisgust>().Any());}
 }
 static async Task DisgustScreenshot(){
  using var f=NewDongniFixture();var viewport=new SubViewport{Size=new(1500,1100),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("17111e")});bool old=TestMode.IsOn;
  try{
   for(int level=0;level<=1;level++){
    var card=Up(f.Card<Pack.DongniDisgust>(),level);TestMode.IsOn=false;var node=NCard.Create(card)!;host.AddChild(node);node.Position=new(380+680*level,480);node.Scale=Vector2.One*1.05f;node.UpdateVisuals(PileType.None,CardPreviewMode.Normal);TestMode.IsOn=old;
    Check("disgust rendered 0 cost keywords and text level="+level,card.EnergyCost.Canonical==0&&card.GetDescriptionForPile(PileType.None).Contains(level==0?"至多5张":"移除牌组中所有卡牌的")&&!card.GetDescriptionForPile(PileType.None).Contains("任意张")&&card.Keywords.Contains(CardKeyword.Ethereal)&&card.Keywords.Contains(CardKeyword.Exhaust));
    Check("disgust embedded accusation art reloadable level="+level,ResourceLoader.Exists(card.PortraitPath)&&card.Portrait.GetSize()==new Vector2(1536,1024)&&card.BetaPortraitPath==card.PortraitPath);
   }
   await Wait(.2);await AfterDraw();Check("disgust both native card levels screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/dongni-disgust-native.png")==Error.Ok);
  }finally{TestMode.IsOn=old;viewport.QueueFree();await Wait(.05);}
 }
}
