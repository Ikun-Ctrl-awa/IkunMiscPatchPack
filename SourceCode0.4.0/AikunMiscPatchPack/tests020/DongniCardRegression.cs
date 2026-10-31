using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.addons.mega_text;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static BloodFixture NewDongniFixture(bool multiplayer=false){
  var f=new BloodFixture(multiplayer:multiplayer);
  // 原生开战会复制十张起始牌到抽牌堆；清空战斗副本后再构造精确牌堆，保留永久牌组供对照。
  foreach(var p in new[]{f.P,f.Other})if(p.PlayerCombatState is {} state)foreach(var card in state.AllCards.ToArray())card.RemoveFromCurrentPile();
  return f;
 }
 sealed class DongniSelector(params CardModel[] wanted):ICardSelector {
  public CardModel[] Options=[];public int Min,Max,Calls;
  public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options,int minSelect,int maxSelect){
   Options=options.ToArray();Min=minSelect;Max=maxSelect;Calls++;
   var selected=wanted.Length>0?wanted:Options.Take(minSelect).ToArray();
   if(selected.Length!=minSelect||selected.Any(c=>!Options.Contains(c)))throw new Exception("东尼测试选牌不符合原生候选或数量。");
   return Task.FromResult<IEnumerable<CardModel>>(selected);
  }
  public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,IReadOnlyList<CardRewardAlternative> alternatives)=>throw new NotSupportedException();
 }
 static CardModel DongniCreate(BloodFixture f,Type type,Player? owner=null,int level=0){
  var card=f.Combat.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(type)),owner??f.P);if(level>0)CardCmd.Upgrade(card);return card;
 }
 static async Task<CardModel> DongniPut(BloodFixture f,PileType pile,Player? owner=null){
  var card=f.Card<StrikeIronclad>(owner);await CardPileCmd.Add(card,pile,skipVisuals:true);return card;
 }
 static readonly Type[] DongniTypes=[typeof(Pack.DongniMeaning),typeof(Pack.NotDongniMeaning),typeof(Pack.WhoDongniMeaning),typeof(Pack.DongniStrike),typeof(Pack.DongniDefend)];
 static async Task DongniCardChecks(){
  Check("dongni option defaults on without other mods",new Pack.PackConfig().Get(Pack.DongniContent.Key)&&Pack.Options.All.Single(o=>o.Key==Pack.DongniContent.Key) is {Misc:true,Required.Length:0});
  Check("dongni content registered",Pack.RuntimeLoader.States.GetValueOrDefault(Pack.DongniContent.Key)=="已应用。");
  var grouped=Pack.ModFormContent.Groups.SelectMany(g=>g.Cards).OfType<Pack.DongniCard>().ToArray();
  Check("mod form admits three main dongni cards excludes tokens",grouped.Length==3&&grouped.All(c=>c is Pack.DongniMeaningCard));
  foreach(var type in DongniTypes){
   var canonical=ModelDb.GetById<CardModel>(ModelDb.GetId(type));bool token=type==typeof(Pack.DongniStrike)||type==typeof(Pack.DongniDefend);
   Check(type.Name+" cost rarity pool exhaust",canonical.EnergyCost.Canonical==0&&canonical.Rarity==(token?CardRarity.Token:CardRarity.Uncommon)&&canonical.Pool is ColorlessCardPool&&canonical.Keywords.Contains(CardKeyword.Exhaust));
   Check(type.Name+" generation flags",canonical.CanBeGeneratedInCombat==!token&&canonical.CanBeGeneratedByModifiers==!token);
   foreach(int level in new[]{0,1}){
    var card=canonical.ToMutable();if(level>0)CardCmd.Upgrade(card);var restored=Restore(card);var cloned=(CardModel)card.ClonePreservingMutability();
    foreach(var copy in new[]{restored,cloned})Check(type.Name+" clone and DTO upgrade "+level,copy.GetType()==type&&copy.CurrentUpgradeLevel==level&&copy.Keywords.Contains(CardKeyword.Exhaust)&&copy.EnergyCost.Canonical==0&&copy.GetDescriptionForPile(PileType.None)==card.GetDescriptionForPile(PileType.None));
   }
  }
  bool active=Pack.Settings.Startup[Pack.DongniContent.Key];
  try{
   Pack.Settings.Startup[Pack.DongniContent.Key]=false;
   Check("dongni off removes every own card from acquisition",!ModelDb.CardPool<ColorlessCardPool>().GetUnlockedCards(UnlockState.all,CardMultiplayerConstraint.None).Any(c=>c is Pack.DongniCard)&&DongniTypes.All(t=>!ModelDb.GetById<CardModel>(ModelDb.GetId(t)).CanBeGeneratedInCombat));
   Check("dongni off still restores saved card",Restore(ModelDb.Card<Pack.DongniMeaning>().ToMutable()) is Pack.DongniMeaning);
  }finally{Pack.Settings.Startup[Pack.DongniContent.Key]=active;}
  var evidence=new List<object>();
  foreach(int level in new[]{0,1})foreach(bool other in new[]{false,true}){
   using var f=NewDongniFixture(true);var owner=other?f.Other:f.P;var card=DongniCreate(f,typeof(Pack.DongniMeaning),owner,level);var deck=owner.Deck.Cards.ToArray();
   await f.Play(card);var generated=owner.PlayerCombatState!.Hand.Cards;
   Check($"meaning L{level} P{owner.NetId} generates exact pair",generated.Count==2&&generated.Count(c=>c is Pack.DongniStrike)==1&&generated.Count(c=>c is Pack.DongniDefend)==1&&generated.All(c=>c.Owner==owner&&c.CurrentUpgradeLevel==level));
   Check($"meaning L{level} P{owner.NetId} source exhausts permanent deck unchanged",card.Pile?.Type==PileType.Exhaust&&owner.Deck.Cards.SequenceEqual(deck));
   evidence.Add(new{Card=card.Id.ToString(),level,owner.NetId,Result=generated.Select(c=>new{Id=c.Id.ToString(),c.CurrentUpgradeLevel})});
  }
  foreach(var type in new[]{typeof(Pack.NotDongniMeaning),typeof(Pack.WhoDongniMeaning)})foreach(int level in new[]{0,1})foreach(bool other in new[]{false,true}){
   using var f=NewDongniFixture(true);var owner=other?f.Other:f.P;var card=DongniCreate(f,type,owner,level);var state=owner.PlayerCombatState!;
   var hand1=await DongniPut(f,PileType.Hand,owner);var hand2=await DongniPut(f,PileType.Hand,owner);var hand3=await DongniPut(f,PileType.Hand,owner);
   var draw=await DongniPut(f,PileType.Draw,owner);var discard=await DongniPut(f,PileType.Discard,owner);var exhausted=await DongniPut(f,PileType.Exhaust,owner);
   var foreign=await DongniPut(f,PileType.Hand,other?f.P:f.Other);var permanent=owner.Deck.Cards.ToArray();
   CardModel[] selected=level==0?[hand2,hand1]:[draw,discard];var selector=new DongniSelector(selected);
   using(CardSelectCmd.UseSelector(selector))await f.Play(card);
   string tag=$"{type.Name} L{level} P{owner.NetId}";
   File.AppendAllText(Output+"/dongni-selection.jsonl",JsonSerializer.Serialize(new{tag,selector.Calls,selector.Min,selector.Max,Count=selector.Options.Length,SourceUpgrade=card.CurrentUpgradeLevel,AllOwned=selector.Options.All(c=>c.Owner==owner),HasSource=selector.Options.Contains(card),HasExhausted=selector.Options.Contains(exhausted),HasForeign=selector.Options.Contains(foreign)})+"\n");
   Check(tag+" native selector exact count and eligible range",selector.Calls==1&&selector.Min==2&&selector.Max==2&&selector.Options.Length==(level==0?3:5)&&selector.Options.All(c=>c.Owner==owner)&&!selector.Options.Contains(card)&&!selector.Options.Contains(exhausted)&&!selector.Options.Contains(foreign));
   Check(tag+" source exhausts other player and permanent deck unchanged",card.Pile?.Type==PileType.Exhaust&&foreign.Pile?.Type==PileType.Hand&&owner.Deck.Cards.SequenceEqual(permanent)&&hand3.Pile?.Type==PileType.Hand);
   var pair=state.AllCards.Where(c=>c is Pack.DongniStrike or Pack.DongniDefend).ToArray();
   Check(tag+" exact unupgraded pair",pair.Length==2&&pair.Count(c=>c is Pack.DongniStrike)==1&&pair.Count(c=>c is Pack.DongniDefend)==1&&pair.All(c=>c.Owner==owner&&!c.IsUpgraded));
   if(type==typeof(Pack.NotDongniMeaning))Check(tag+" selected cards exhausted pair in hand",selected.All(c=>c.Pile?.Type==PileType.Exhaust)&&pair.All(c=>c.Pile?.Type==PileType.Hand));
   else Check(tag+" transformed in original piles positions",selected.All(c=>c.Pile==null)&&pair.Single(c=>c is Pack.DongniStrike).Pile?.Type==(level==0?PileType.Hand:PileType.Draw)&&pair.Single(c=>c is Pack.DongniDefend).Pile?.Type==(level==0?PileType.Hand:PileType.Discard)&&state.AllCards.Count(c=>c==exhausted)==1);
   if(type==typeof(Pack.WhoDongniMeaning)&&level==0)Check(tag+" selection order maps to exact original positions",state.Hand.Cards[1] is Pack.DongniStrike&&state.Hand.Cards[0] is Pack.DongniDefend&&state.Hand.Cards[2]==hand3);
   evidence.Add(new{Card=type.Name,level,owner.NetId,Candidates=selector.Options.Length,Result=pair.Select(c=>new{Id=c.Id.ToString(),Pile=c.Pile!.Type.ToString(),c.CurrentUpgradeLevel})});
  }
  var singles=new HashSet<Type>();
  foreach(int count in new[]{0,1})foreach(int level in new[]{0,1})foreach(var type in new[]{typeof(Pack.NotDongniMeaning),typeof(Pack.WhoDongniMeaning)}){
   using var f=NewDongniFixture();var card=DongniCreate(f,type,level:level);if(count>0)await DongniPut(f,level==0?PileType.Hand:PileType.Discard);
   using(CardSelectCmd.UseSelector(new DongniSelector()))await f.Play(card);
   var pair=f.P.PlayerCombatState!.AllCards.Where(c=>c is Pack.DongniStrike or Pack.DongniDefend).ToArray();
   Check($"{type.Name} shortage {count} L{level}",card.Pile?.Type==PileType.Exhaust&&pair.Length==(type==typeof(Pack.NotDongniMeaning)?2:count));
  }
  for(int i=0;i<12;i++){
   using var f=NewDongniFixture();for(int j=0;j<i;j++)f.Run.Rng.CombatCardGeneration.NextInt(2);
   var original=await DongniPut(f,PileType.Hand);var source=f.Card<Pack.WhoDongniMeaning>();
   using(CardSelectCmd.UseSelector(new DongniSelector()))await f.Play(source);
   var result=f.P.PlayerCombatState!.Hand.Cards.Single();singles.Add(result.GetType());
   Check("who singleton seeded valid branch "+i,(result is Pack.DongniStrike or Pack.DongniDefend)&&original.Pile==null&&!result.IsUpgraded&&result.Owner==f.P);
  }
  Check("who singleton exercises both random branches",singles.Count==2);
  foreach(int level in new[]{0,1}){
   using var f=NewDongniFixture(true);var card=DongniCreate(f,typeof(Pack.DongniStrike),level:level);
   foreach(var pile in new[]{PileType.Hand,PileType.Draw,PileType.Discard,PileType.Exhaust})for(int i=0;i<2;i++)await DongniPut(f,pile);
   for(int i=0;i<3;i++)await DongniPut(f,PileType.Hand,f.Other);
   await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);card.UpdateDynamicVarPreview(CardPreviewMode.Normal,f.Enemy,card.DynamicVars);
   int expected=level==0?7:9;Check("strike live preview excludes played source and other player L"+level,card.DynamicVars.CalculatedDamage.PreviewValue==expected);
   int hp=f.Enemy.CurrentHp;await card.OnPlayWrapper(f.Choice,f.Enemy,false,default,skipCardPileVisuals:true);
   Check("strike actual total damage L"+level,hp-f.Enemy.CurrentHp==expected&&card.Pile?.Type==PileType.Exhaust);
   await PowerCmd.Apply<StrengthPower>(f.Choice,f.P.Creature,3,f.P.Creature,null);
   var strengthened=DongniCreate(f,typeof(Pack.DongniStrike),level:level);int expectedStrong=1+Pack.DongniContent.CountDamageCards(strengthened)+3;
   hp=f.Enemy.CurrentHp;await f.Play(strengthened,f.Enemy);Check("strike uses native strength exactly once L"+level,hp-f.Enemy.CurrentHp==expectedStrong);
  }
  foreach(int level in new[]{0,1}){
   using var f=NewDongniFixture(true);await CreatureCmd.GainBlock(f.P.Creature,37,ValueProp.Unpowered,null);
   await f.Play(DongniCreate(f,typeof(Pack.DongniDefend),level:level));
   Check("defend removes old block before gaining 15 L"+level,f.P.Creature.Block==15);
   await CreatureCmd.GainBlock(f.P.Creature,7,ValueProp.Unpowered,null);
   Check("defend unpowered potion-like block restriction L"+level,f.P.Creature.Block==(level==0?15:22));
   var potion=ModelDb.Potion<BlockPotion>().ToMutable();int beforePotion=f.P.Creature.Block;
   await (Task)AccessTools.Method(typeof(BlockPotion),"OnUse").Invoke(potion,[f.Choice,f.P.Creature])!;
   Check("defend actual block potion restriction L"+level,f.P.Creature.Block==(level==0?beforePotion:beforePotion+12));
   var plating=await PowerCmd.Apply<PlatingPower>(f.Choice,f.P.Creature,5,f.P.Creature,null);int beforeArmor=f.P.Creature.Block;
   await plating!.BeforeSideTurnEndEarly(f.Choice,CombatSide.Player,[f.P.Creature]);
   Check("defend actual armor power restriction L"+level,f.P.Creature.Block==(level==0?beforeArmor:beforeArmor+5));
   int before=f.P.Creature.Block;await f.Play(f.Card<DefendIronclad>());
   Check("defend later card restriction L"+level,level==0?f.P.Creature.Block==before:f.P.Creature.Block>before);
   var anchor=(Anchor)ModelDb.Relic<Anchor>().ToMutable();f.P.AddRelicInternal(anchor,silent:true);before=f.P.Creature.Block;await anchor.BeforeCombatStart();
   Check("defend later relic restriction L"+level,level==0?f.P.Creature.Block==before:f.P.Creature.Block>before);
   await CreatureCmd.GainBlock(f.Other.Creature,4,ValueProp.Unpowered,null);Check("defend does not block other player L"+level,f.Other.Creature.Block==4);
   var restriction=f.P.Creature.GetPower<Pack.DongniNoBlockPower>();
   Check("defend only base applies turn restriction L"+level,(restriction!=null)==(level==0));
   if(restriction!=null){
    await restriction.AfterSideTurnEnd(f.Choice,CombatSide.Enemy,[f.Enemy]);Check("defend lock stays until owners turn ends",f.P.Creature.HasPower<Pack.DongniNoBlockPower>());
    await restriction.AfterSideTurnEnd(f.Choice,CombatSide.Player,[f.P.Creature,f.Other.Creature]);Check("defend lock expires at owners turn end",!f.P.Creature.HasPower<Pack.DongniNoBlockPower>());
    before=f.P.Creature.Block;await CreatureCmd.GainBlock(f.P.Creature,6,ValueProp.Unpowered,null);Check("defend next turn can gain block",f.P.Creature.Block==before+6);
   }
  }
  using(var f=NewDongniFixture()){
   await f.Play(f.Card<Pack.DongniDefend>());await f.Play(DongniCreate(f,typeof(Pack.DongniDefend),level:1));
   Check("upgraded defend respects existing no block effect",f.P.Creature.Block==0&&f.P.Creature.HasPower<Pack.DongniNoBlockPower>());
  }
  using(var f=NewDongniFixture()){
   for(int i=0;i<10;i++)await DongniPut(f,PileType.Hand);
   await f.Play(f.Card<Pack.DongniMeaning>());
   var state=f.P.PlayerCombatState!;Check("meaning full hand native overflow preserves both tokens",state.Hand.Cards.Count==10&&state.AllCards.Count(c=>c is Pack.DongniStrike or Pack.DongniDefend)==2);
  }
  await DongniScreenshots();
  File.WriteAllText(Output+"/dongni-evidence.json",JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
 }
 static async Task DongniScreenshots(){
  using var f=NewDongniFixture();var viewport=new SubViewport{Size=new(2100,1300),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);
  var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("1b202c")});
  try{
   for(int level=0;level<=1;level++)for(int i=0;i<DongniTypes.Length;i++){
    var card=DongniCreate(f,DongniTypes[i],level:level);await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);
    bool old=TestMode.IsOn;NCard node;
    try{TestMode.IsOn=false;node=NCard.Create(card)!;host.AddChild(node);node.Position=new(210+420*i,335+640*level);node.Scale=Vector2.One*.85f;node.UpdateVisuals(PileType.Hand,CardPreviewMode.Normal);}finally{TestMode.IsOn=old;}
    string text=node.GetNode<MegaRichTextLabel>("%DescriptionLabel").Text;
    Check("dongni native card art and resolved description "+DongniTypes[i].Name+level,node.GetNode<TextureRect>("%Portrait").Texture==Pack.DongniArt.Portrait(card)&&!text.Contains("{EffectText}")&&!text.Contains("{PileText}")&&!text.Contains("{RestrictionText}")&&!text.Contains("{CalculatedDamage"));
    foreach(var copy in new[]{card,Restore(card),(CardModel)card.ClonePreservingMutability()}){
     string art=Pack.DongniArt.Path(copy);
     Check("dongni art native normal beta and preload paths "+copy.GetType().Name+level,
      copy.PortraitPath==art&&copy.BetaPortraitPath==art&&copy.AllPortraitPaths.SequenceEqual(new[]{art})&&copy.HasPortrait&&copy.HasBetaPortrait&&
      ((string)HarmonyLib.AccessTools.Property(typeof(CardModel),"BetaPortraitPngPath").GetValue(copy)!)==art&&ResourceLoader.Exists(art)&&ResourceLoader.HasCached(art));
     Check("dongni art native restored texture "+copy.GetType().Name+level,copy.Portrait==Pack.DongniArt.Portrait(copy));
    }
   }
   await Wait(.2);await AfterDraw();Check("dongni native normal upgrade screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/dongni-cards-native.png")==Error.Ok);
  }finally{viewport.QueueFree();await Wait(.03);}
 }
}
