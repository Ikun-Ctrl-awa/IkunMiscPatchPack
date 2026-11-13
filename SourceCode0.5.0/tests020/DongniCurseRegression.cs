using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.addons.mega_text;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static readonly List<(Type Card,ulong Player,bool Auto,int Spent)> dongniCursePlays=new();
 static void ObserveDongniCursePlay(CardPlay cardPlay)=>dongniCursePlays.Add((cardPlay.Card.GetType(),cardPlay.Player.NetId,cardPlay.IsAutoPlay,cardPlay.Resources.EnergySpent));
 static async Task<CardModel> DongniCurseInHand(BloodFixture f,int level=0,Player? owner=null){var card=Up(f.Card<Pack.DongniCurse>(owner),level);await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);return card;}
 static async Task DongniManualPlay(BloodFixture f,CardModel card,Creature? target=null){
  await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);int cost=card.EnergyCost.GetAmountToSpend(),star=card.GetStarCostWithModifiers();
  var paid=await card.SpendResources();
  await card.OnPlayWrapper(f.Choice,target,false,new ResourceInfo{EnergySpent=paid.Item1,EnergyValue=cost,StarsSpent=paid.Item2,StarValue=star},true).WaitAsync(TimeSpan.FromSeconds(8));
 }
 static async Task DongniNext(BloodFixture f,Player? owner=null){var p=owner??f.P;p.PlayerCombatState!.IncrementTurnNumber();await ImbuedPhase(f,p);}
 static async Task DongniCurseChecks(){
  int start=passes.Count;var canonical=ModelDb.Card<Pack.DongniCurse>();var evidence=new List<object>();
  var observer=new Harmony("Aikun020Probe.DongniCurseObservation");observer.Patch(AccessTools.Method(typeof(Hook),nameof(Hook.BeforeCardPlayed)),prefix:new HarmonyMethod(typeof(Entry),nameof(ObserveDongniCursePlay)));
  try{
   Check("dongni curse belongs to tower curse miscellaneous group",Pack.Options.All.Length==51&&Pack.Options.All.Single(o=>o.Key==Pack.TowerCurseContent.Key).Misc);
   Check("dongni curse native three-cost exhaust once-upgradable curse pool",canonical.Pool is CurseCardPool&&canonical.Type==CardType.Curse&&canonical.Rarity==CardRarity.Curse&&canonical.Keywords.Contains(CardKeyword.Exhaust)&&!canonical.Keywords.Contains(CardKeyword.Unplayable)&&canonical.EnergyCost.Canonical==3&&canonical.MaxUpgradeLevel==1&&canonical.IsUpgradable);
   Check("dongni curse random curse acquisition only",ModelDb.CardPool<CurseCardPool>().AllCards.Contains(canonical)&&!ModelDb.CardPool<ColorlessCardPool>().AllCards.Contains(canonical)&&!Pack.ModFormContent.Groups.SelectMany(g=>g.Cards).Contains(canonical));
   foreach(int level in new[]{0,1}){
    var card=Up(canonical.ToMutable(),level);for(int i=0;i<3;i++)card=Restore(card);
    Check("dongni curse save and clone level="+level,card.CurrentUpgradeLevel==level&&card.IsUpgradable==(level==0)&&card.GetDescriptionForPile(PileType.None).Contains(level==0?"出牌顺序":"下回合生效")&&card.Keywords.Contains(CardKeyword.Exhaust)&&card.EnergyCost.Canonical==3);
    using var f=NewDongniFixture();f.P.PlayerCombatState!.Energy=3;var curse=await DongniCurseInHand(f,level);Check("dongni curse can be manually played level="+level,curse.CanPlay());
    await DongniManualPlay(f,curse);
    Check("dongni curse pays three exhausts itself immediately level="+level,f.P.PlayerCombatState.Energy==0&&curse.Pile?.Type==PileType.Exhaust&&Pack.DongniCurseRules.PendingCount(f.P)==0);
   }
   foreach(var pile in new[]{PileType.Draw,PileType.Discard,PileType.Exhaust}){
    using var f=NewDongniFixture();var curse=f.Card<Pack.DongniCurse>();await CardPileCmd.Add(curse,pile,skipVisuals:true);int hp=f.Enemy.CurrentHp;var strike=f.Card<StrikeIronclad>();await DongniManualPlay(f,strike,f.Enemy);
    Check("dongni normal curse inactive outside hand pile="+pile,hp-f.Enemy.CurrentHp==6&&Pack.DongniCurseRules.PendingCount(f.P)==0&&strike.Pile?.Type==PileType.Discard);
   }
   foreach(bool other in new[]{false,true}){
    using var f=NewDongniFixture(true);var owner=other?f.Other:f.P;var foreign=other?f.P:f.Other;await DongniCurseInHand(f,owner:owner);owner.PlayerCombatState!.Energy=9;
    var bash=f.Card<Bash>(owner);var strike=f.Card<StrikeIronclad>(owner);var defend=f.Card<DefendIronclad>(owner);int hp=f.Enemy.CurrentHp,count=((System.Collections.IEnumerable)AccessTools.Field(typeof(CombatState),"_allCards").GetValue(f.Combat)!).Cast<CardModel>().Count();dongniCursePlays.Clear();
    await DongniManualPlay(f,bash,f.Enemy);await DongniManualPlay(f,strike,f.Enemy);await DongniManualPlay(f,defend);
    Check("dongni normal defers all effects and native played hooks owner="+owner.NetId,f.Enemy.CurrentHp==hp&&owner.Creature.Block==0&&!f.Enemy.HasPower<VulnerablePower>()&&dongniCursePlays.Count==0&&Pack.DongniCurseRules.PendingCount(owner)==3);
    Check("dongni original cards discard now once and costs paid now owner="+owner.NetId,new CardModel[]{bash,strike,defend}.All(c=>c.Pile?.Type==PileType.Discard)&&owner.PlayerCombatState.Energy==5&&((System.Collections.IEnumerable)AccessTools.Field(typeof(CombatState),"_allCards").GetValue(f.Combat)!).Cast<CardModel>().Count()==count);
    await ImbuedPhase(f,owner);Check("dongni same turn phase does not flush future queue owner="+owner.NetId,Pack.DongniCurseRules.PendingCount(owner)==3&&f.Enemy.CurrentHp==hp);
    var independent=f.Card<StrikeIronclad>(foreign);foreign.PlayerCombatState!.Energy=3;await DongniManualPlay(f,independent,f.Enemy);hp=f.Enemy.CurrentHp;
    Check("dongni other player acts immediately owner="+owner.NetId,Pack.DongniCurseRules.PendingCount(foreign)==0&&independent.Pile?.Type==PileType.Discard);
    dongniCursePlays.Clear();int all=((System.Collections.IEnumerable)AccessTools.Field(typeof(CombatState),"_allCards").GetValue(f.Combat)!).Cast<CardModel>().Count();await DongniNext(f,owner);
    Check("dongni next turn original order vulnerability then strike then block owner="+owner.NetId,hp-f.Enemy.CurrentHp==17&&owner.Creature.Block==5&&f.Enemy.GetPowerAmount<VulnerablePower>()==2);
    Check("dongni next turn actual native play history ordered owner="+owner.NetId,dongniCursePlays.Select(p=>p.Card).SequenceEqual(new[]{typeof(Bash),typeof(StrikeIronclad),typeof(DefendIronclad)})&&dongniCursePlays.All(p=>p.Auto&&p.Player==owner.NetId));
    Check("dongni originals stay discarded no duplicates or second payment owner="+owner.NetId,new CardModel[]{bash,strike,defend}.All(c=>c.Pile?.Type==PileType.Discard)&&owner.PlayerCombatState.Energy==5&&((System.Collections.IEnumerable)AccessTools.Field(typeof(CombatState),"_allCards").GetValue(f.Combat)!).Cast<CardModel>().Count()==all&&Pack.DongniCurseRules.PendingCount(owner)==0);
    await DongniNext(f,owner);Check("dongni queued plays execute only once owner="+owner.NetId,dongniCursePlays.Count==3);
    evidence.Add(new{Owner=owner.NetId,Order=dongniCursePlays.Select(p=>p.Card.Name),Energy=owner.PlayerCombatState.Energy});
   }
   using(var f=NewDongniFixture()){
    await DongniCurseInHand(f);await DongniCurseInHand(f);f.P.PlayerCombatState!.Energy=3;var offering=f.Card<Offering>();int hp=f.P.Creature.CurrentHp;
    for(int i=0;i<8;i++)await DongniPut(f,PileType.Draw);await DongniManualPlay(f,offering);
    Check("dongni multiple curses do not multiply queue and exhaust is normal now",offering.Pile?.Type==PileType.Exhaust&&f.P.Creature.CurrentHp==hp&&Pack.DongniCurseRules.PendingCount(f.P)==1);
    int all=((System.Collections.IEnumerable)AccessTools.Field(typeof(CombatState),"_allCards").GetValue(f.Combat)!).Cast<CardModel>().Count();await DongniNext(f);
    Check("dongni delayed exhaust card effects execute once without second exhaust copy",hp-f.P.Creature.CurrentHp==6&&f.P.PlayerCombatState.Energy==5&&f.P.PlayerCombatState.Hand.Cards.Count==5&&offering.Pile?.Type==PileType.Exhaust&&((System.Collections.IEnumerable)AccessTools.Field(typeof(CombatState),"_allCards").GetValue(f.Combat)!).Cast<CardModel>().Count()==all);
   }
   using(var f=NewDongniFixture()){
    await DongniCurseInHand(f);f.P.PlayerCombatState!.Energy=3;var x=f.Card<Whirlwind>();int hp=f.Enemy.CurrentHp;await DongniManualPlay(f,x);f.P.PlayerCombatState.Energy=1;await DongniNext(f);
    Check("dongni X cost captures original payment and no next-turn repayment",hp-f.Enemy.CurrentHp==15&&f.P.PlayerCombatState.Energy==1&&x.Pile?.Type==PileType.Discard);
   }
   using(var f=NewDongniFixture()){
    var curse=await DongniCurseInHand(f);var card=f.Card<StrikeIronclad>();card.EnchantInternal(ModelDb.Enchantment<Sharp>().ToMutable(),1);card.Enchantment!.ModifyCard();f.P.PlayerCombatState!.Energy=4;int hp=f.Enemy.CurrentHp;await DongniManualPlay(f,card,f.Enemy);
    await DongniManualPlay(f,curse);Check("dongni paying to exhaust curse does not cancel already queued plays",curse.Pile?.Type==PileType.Exhaust&&f.P.PlayerCombatState.Energy==0&&Pack.DongniCurseRules.PendingCount(f.P)==1);await DongniNext(f);
    Check("dongni due plays survive curse leaving hand and preserve enchantment snapshot",hp-f.Enemy.CurrentHp>6&&card.Pile?.Type==PileType.Discard&&Pack.DongniCurseRules.PendingCount(f.P)==0);
   }
   using(var f=NewDongniFixture()){
    await DongniCurseInHand(f);var card=f.Card<StrikeIronclad>();await DongniManualPlay(f,card,f.Enemy);
    var next=f.Combat.CreateCreature(ModelDb.Monster<CalcifiedCultist>().ToMutable(),CombatSide.Enemy,"dongni-retarget");f.Combat.AddCreature(next);next.SetMaxHpInternal(100);next.SetCurrentHpInternal(100);f.Enemy.SetCurrentHpInternal(0);await DongniNext(f);
    Check("dongni dead original target uses native live target selection",next.CurrentHp==94&&card.Pile?.Type==PileType.Discard&&Pack.DongniCurseRules.PendingCount(f.P)==0);
   }
   foreach(bool other in new[]{false,true}){
    using var f=NewDongniFixture(true);var owner=other?f.Other:f.P;var foreign=other?f.P:f.Other;var curse=await DongniCurseInHand(f,1,owner);owner.PlayerCombatState!.Energy=3;
    for(int i=0;i<12;i++)await DongniPut(f,PileType.Draw,owner);
    await PlayerCmd.GainEnergy(2,owner);var drawn=await CardPileCmd.Draw(f.Choice,2,owner);
    Check("dongni upgraded defers resources with exact native powers owner="+owner.NetId,owner.PlayerCombatState.Energy==3&&!drawn.Any()&&owner.PlayerCombatState.Hand.Cards.Count==1&&owner.Creature.GetPowerAmount<EnergyNextTurnPower>()==2&&owner.Creature.GetPowerAmount<DrawCardsNextTurnPower>()==2);
    int hp=f.Enemy.CurrentHp;await DongniManualPlay(f,f.Card<StrikeIronclad>(owner),f.Enemy);Check("dongni upgraded keeps damage immediate owner="+owner.NetId,hp-f.Enemy.CurrentHp==6&&Pack.DongniCurseRules.PendingCount(owner)==0);
    foreign.PlayerCombatState!.Energy=3;await PlayerCmd.GainEnergy(1,foreign);Check("dongni upgraded resource ownership isolated owner="+owner.NetId,foreign.PlayerCombatState.Energy==4&&!foreign.Creature.HasPower<EnergyNextTurnPower>());
    owner.PlayerCombatState.IncrementTurnNumber();owner.PlayerCombatState.ResetEnergy();await Hook.AfterEnergyReset(f.Combat,owner);
    Check("dongni native future energy delivers despite retained curse owner="+owner.NetId,owner.PlayerCombatState.Energy==5&&!owner.Creature.HasPower<EnergyNextTurnPower>());
    var drawPower=owner.Creature.GetPower<DrawCardsNextTurnPower>()!;AccessTools.Property(typeof(PowerModel),nameof(PowerModel.AmountOnTurnStart)).SetValue(drawPower,drawPower.Amount);
    decimal draw=Hook.ModifyHandDraw(f.Combat,owner,5,out _);var cards=await CardPileCmd.Draw(f.Choice,draw,owner,fromHandDraw:true);await Hook.AfterSideTurnStart(f.Combat,CombatSide.Player,[owner.Creature]);
    Check("dongni native initial draw plus deferred cards no redelay owner="+owner.NetId,draw==7&&cards.Count()==7&&!owner.Creature.HasPower<DrawCardsNextTurnPower>()&&owner.PlayerCombatState.Hand.Cards.Contains(curse));
    await PlayerCmd.GainEnergy(1,owner);Check("dongni delivery bypass ends and new gains defer again owner="+owner.NetId,owner.PlayerCombatState.Energy==5&&owner.Creature.GetPowerAmount<EnergyNextTurnPower>()==1);
   }
   using(var f=NewDongniFixture()){
    await DongniCurseInHand(f,1);await PowerCmd.Apply<NoDrawPower>(f.Choice,f.P.Creature,1,f.P.Creature,null);await DongniPut(f,PileType.Draw);await CardPileCmd.Draw(f.Choice,1,f.P);
    Check("dongni native no-draw blocker takes precedence",!f.P.Creature.HasPower<DrawCardsNextTurnPower>());
    await PowerCmd.Apply<NoEnergyGainPower>(f.Choice,f.P.Creature,1,f.P.Creature,null);int energy=f.P.PlayerCombatState!.Energy;await PlayerCmd.GainEnergy(2,f.P);
    Check("dongni native no-energy blocker takes precedence",!f.P.Creature.HasPower<EnergyNextTurnPower>()&&f.P.PlayerCombatState.Energy==energy);
   }
   if(!Minimal)await DongniCurseHextechChecks();
   bool active=Pack.Settings.Startup[Pack.TowerCurseContent.Key];try{
    Pack.Settings.Startup[Pack.TowerCurseContent.Key]=false;
    Check("dongni curse disabled acquisition retains registered model",!ModelDb.CardPool<CurseCardPool>().AllCards.Any(c=>c is Pack.DongniCurse)&&Restore(canonical.ToMutable()) is Pack.DongniCurse);
    using var f=NewDongniFixture();await DongniCurseInHand(f,1);await PlayerCmd.GainEnergy(1,f.P);Check("dongni curse existing card works with miscellaneous off",f.P.Creature.GetPowerAmount<EnergyNextTurnPower>()==1);
   }finally{Pack.Settings.Startup[Pack.TowerCurseContent.Key]=active;}
   using(var f=NewDongniFixture()){
    await DongniCurseInHand(f);await DongniManualPlay(f,f.Card<StrikeIronclad>(),f.Enemy);await Hook.AfterCombatEnd(f.Run,f.Combat,new CombatRoom(f.Run.Act.AllRegularEncounters.First().ToMutable(),f.Run));
    Check("dongni pending plays cleared at combat end",Pack.DongniCurseRules.PendingCount(f.P)==0);
   }
   await DongniCurseScreenshot();
  }finally{observer.UnpatchAll(observer.Id);dongniCursePlays.Clear();}
  File.WriteAllText(Output+"/dongni-curse-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Failed=0,Checks=passes.Skip(start),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
 }
 static async Task DongniCurseHextechChecks(){
  using(var fusionFixture=new BloodFixture()){
   foreach(var c in fusionFixture.P.PlayerCombatState!.AllCards.ToArray())c.RemoveFromCurrentPile();await DongniCurseInHand(fusionFixture);
   var fused=Fuse(ModelDb.Card<StrikeIronclad>().ToMutable(),ModelDb.Card<DefendIronclad>().ToMutable());fusionFixture.Combat.AddCard(fused,fusionFixture.P);fusionFixture.P.PlayerCombatState.Energy=10;int hp=fusionFixture.Enemy.CurrentHp;
   await DongniManualPlay(fusionFixture,fused,fusionFixture.Enemy);Check("dongni actual fused card defers as one ordered play",fused.Pile?.Type==PileType.Discard&&fusionFixture.Enemy.CurrentHp==hp&&fusionFixture.P.Creature.Block==0&&Pack.DongniCurseRules.PendingCount(fusionFixture.P)==1);
   await DongniNext(fusionFixture);Check("dongni actual fused delayed effects preserve original discard",hp-fusionFixture.Enemy.CurrentHp==6&&fusionFixture.P.Creature.Block==5&&fused.Pile?.Type==PileType.Discard&&Pack.DongniCurseRules.PendingCount(fusionFixture.P)==0);
  }
  using var f=NewDongniFixture(true);var type=Find("HextechRunes.SpinToWinRune");var rune=ModelDb.GetById<RelicModel>(ModelDb.GetId(type)).ToMutable();f.P.AddRelicInternal(rune,silent:true);
  await DongniCurseInHand(f,1);f.P.PlayerCombatState!.Energy=3;for(int i=0;i<8;i++)await DongniPut(f,PileType.Draw);
  await PlayerCmd.GainEnergy(2,f.P);var cards=await CardPileCmd.Draw(f.Choice,2,f.P);
  Check("dongni actual Hextech SpinToWin energy beats upgraded curse",f.P.PlayerCombatState.Energy==5&&!f.P.Creature.HasPower<EnergyNextTurnPower>());
  Check("dongni actual Hextech SpinToWin draw beats upgraded curse",f.P.PlayerCombatState.Hand.Cards.Count==3&&!f.P.Creature.HasPower<DrawCardsNextTurnPower>()&&cards.Count()==2&&cards.All(c=>c.Pile?.Type==PileType.Hand));
  await PowerCmd.Apply<EnergyNextTurnPower>(f.Choice,f.P.Creature,1,f.P.Creature,null);await PowerCmd.Apply<DrawCardsNextTurnPower>(f.Choice,f.P.Creature,1,f.P.Creature,null);
  Check("dongni actual existing next-turn powers still convert immediately",f.P.PlayerCombatState.Energy==6&&f.P.PlayerCombatState.Hand.Cards.Count==4&&!f.P.Creature.HasPower<EnergyNextTurnPower>()&&!f.P.Creature.HasPower<DrawCardsNextTurnPower>());
  await DongniCurseInHand(f,1,f.Other);f.Other.PlayerCombatState!.Energy=3;await PlayerCmd.GainEnergy(1,f.Other);
  Check("dongni Hextech owner does not exempt other player",f.Other.PlayerCombatState.Energy==3&&f.Other.Creature.GetPowerAmount<EnergyNextTurnPower>()==1);
 }
 static async Task DongniCurseScreenshot(){
  using var f=NewDongniFixture();var viewport=new SubViewport{Size=new(1500,1100),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("17111d")});bool old=TestMode.IsOn;
  try{
   for(int level=0;level<=1;level++){
    var card=Up(f.Card<Pack.DongniCurse>(),level);TestMode.IsOn=false;var node=NCard.Create(card)!;host.AddChild(node);node.Position=new(380+680*level,480);node.Scale=Vector2.One*1.05f;node.UpdateVisuals(PileType.None,CardPreviewMode.Normal);TestMode.IsOn=old;
    string text=QuText(node.GetNode<MegaRichTextLabel>("%DescriptionLabel").Text);Check("dongni rendered curse exact upgraded text level="+level,!text.Contains("不能被打出")&&!text.Contains("原牌照常离开手牌")&&!text.Contains("下回合不再支付费用")&&!text.Contains("提前生效的效果优先")&&text.Contains("消耗")&&text.Contains(level==0?"出牌顺序":"下回合生效")&&card.EnergyCost.Canonical==3);
    Check("dongni embedded art reloadable original and beta level="+level,ResourceLoader.Exists(card.PortraitPath)&&card.Portrait.GetSize()==new Vector2(1536,1024)&&card.BetaPortraitPath==card.PortraitPath);
   }
   await Wait(.2);await AfterDraw();Check("dongni native both levels screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/dongni-curse-native.png")==Error.Ok);
  }finally{TestMode.IsOn=old;viewport.QueueFree();await Wait(.05);}
 }
}
