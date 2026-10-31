using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Scaffolding.Content;

namespace AikunMiscPatchPack;

public sealed class BloodShield : ModCardTemplate {
 public BloodShield():base(1,CardType.Skill,CardRarity.Event,TargetType.Self){}
 public override CardPoolModel Pool=>ModelDb.CardPool<ColorlessCardPool>();
 public override bool CanBeGeneratedInCombat=>false;
 public override bool CanBeGeneratedByModifiers=>false;
 public override string? CustomPortraitPath=>BloodSectArt.Path;
 public override string? CustomBetaPortraitPath=>BloodSectArt.Path;
 public override string PortraitPath=>BloodSectArt.Path;
 public override string BetaPortraitPath=>PortraitPath;
 protected override string PortraitPngPath=>PortraitPath;
 public override IEnumerable<string> AllPortraitPaths=>[PortraitPath];
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new HpLossVar(5),new DynamicVar("TemporaryHp",15)];
 protected override void OnUpgrade(){DynamicVars.HpLoss.UpgradeValueBy(-2);DynamicVars["TemporaryHp"].UpgradeValueBy(1);}
 protected override async Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay){
  await CreatureCmd.Damage(choiceContext,Owner.Creature,DynamicVars.HpLoss.BaseValue,ValueProp.Unblockable|ValueProp.Unpowered|ValueProp.Move,this,cardPlay);
  if(Owner.Creature.IsAlive)await PowerCmd.Apply<TemporaryBloodPower>(choiceContext,Owner.Creature,DynamicVars["TemporaryHp"].BaseValue,Owner.Creature,this);
 }
}

public sealed class BloodSectInitiation : ModEventTemplate {
 bool resolving;
 static BloodSect? BloodIn(CardModel card)=>card as BloodSect??FusionQuestBlessingFix.Entry.Descendants(card).OfType<BloodSect>().FirstOrDefault();
 public override bool IsAllowed(IRunState runState)=>Settings.Active(BloodSectContent.Key)&&runState.Players.Any(p=>!p.Relics.Any(r=>r is ThunderSectToken)||p.Deck.Cards.Any(c=>BloodIn(c)!=null)||p.Gold>=150);
 public override string? CustomInitialPortraitPath=>BloodInitiationArt.BackgroundPath;
 public override IEnumerable<string> GetAssetPaths(IRunState runState)=>["res://scenes/events/default_event_layout.tscn",CustomInitialPortraitPath!];
 bool Allowed(string key)=>Owner is {} player&&key switch{"JOIN"=>!player.Relics.Any(r=>r is ThunderSectToken),"MEMBER"=>player.Deck.Cards.Any(c=>BloodIn(c)!=null),"BUY"=>player.Gold>=150,_=>true};
 protected override IReadOnlyList<EventOption> GenerateInitialOptions()=>[
  SectEventOptions.Create(this,InitialOptionKey("JOIN"),Allowed("JOIN"),()=>Choose("JOIN"),ModelDb.Relic<BloodSectToken>().HoverTips),
  SectEventOptions.Create(this,InitialOptionKey("MEMBER"),Allowed("MEMBER"),()=>Choose("MEMBER"),[new CardHoverTip(ModelDb.Card<BloodSect>())]),
  SectEventOptions.Create(this,InitialOptionKey("BUY"),Allowed("BUY"),()=>Choose("BUY"),ModelDb.Relic<VampiricBlade>().HoverTips),
  new EventOption(this,()=>Choose("LEAVE"),InitialOptionKey("LEAVE"))
 ];
 async Task Choose(string key){
  if(IsFinished||resolving||!Allowed(key))return;resolving=true;
  var player=Owner??throw new InvalidOperationException("血魔宗事件尚未指定玩家。");
  try{
   if(key=="JOIN")await RelicCmd.Obtain<BloodSectToken>(player);
   else if(key=="BUY"){await PlayerCmd.LoseGold(150,player,GoldLossType.Spent);await RelicCmd.Obtain<VampiricBlade>(player);}
   else if(key=="MEMBER"){
    var selected=await CardSelectCmd.FromDeckGeneric(player,new CardSelectorPrefs(new LocString("events",Id.Entry+".selectionScreenPrompt"),1),c=>BloodIn(c)!=null);
    var card=selected.FirstOrDefault();if(card==null||card.Owner!=player||card.Pile!=player.Deck||BloodIn(card) is not {} blood)return;
    BloodEnchantmentRules.AddInitiation(blood);
   }
   SetEventFinished(PageDescription(key));
  }finally{resolving=false;}
 }
}

public abstract class BloodEventRelic : ModRelicTemplate {
 public override RelicRarity Rarity=>RelicRarity.Event;
 public override bool IsAllowedInShops=>false;
 public override string? CustomIconPath=>BloodInitiationArt.IconPath(this switch{BloodSectToken=>BloodInitiationArt.BloodTokenPath,ThunderSectToken=>BloodInitiationArt.ThunderTokenPath,_=>BloodInitiationArt.BladePath});
 public override string? CustomIconOutlinePath=>CustomIconPath;
 public override string? CustomBigIconPath=>CustomIconPath;
}
public sealed class BloodSectToken : BloodEventRelic {
 public override bool HasUponPickupEffect=>true;
 protected override IEnumerable<IHoverTip> AdditionalHoverTips=>new CardModel[]{ModelDb.Card<BloodSect>(),ModelDb.Card<Bloodletting>(),ModelDb.Card<BloodShield>(),ModelDb.Card<Hemokinesis>()}.Select(c=>new CardHoverTip(c));
 public override async Task AfterObtained(){
  var run=Owner.RunState;await CardPileCmd.Add(new CardModel[]{run.CreateCard<BloodSect>(Owner),run.CreateCard<Bloodletting>(Owner),run.CreateCard<BloodShield>(Owner),run.CreateCard<Hemokinesis>(Owner)},PileType.Deck);
 }
}
public sealed class VampiricBlade : BloodEventRelic {
 public override bool HasUponPickupEffect=>true;
 public override async Task AfterObtained(){
  bool Eligible(CardModel c)=>c.Type==CardType.Attack&&!EnchantmentView.Read(c).Any(e=>e is WeepingBlood);
  int max=Math.Min(3,Owner.Deck.Cards.Count(Eligible));if(max==0)return;
  var selected=await CardSelectCmd.FromDeckGeneric(Owner,new CardSelectorPrefs(SelectionScreenPrompt,0,max){Cancelable=true},Eligible);
  foreach(var card in selected.Distinct().Take(3))if(card.Owner==Owner&&card.Pile==Owner.Deck&&Eligible(card))BloodEnchantmentRules.AddWeeping(card);
 }
}

public static class BloodInitiationContent {
 public static void Localize(){
  string id=ModelDb.GetId<BloodSectInitiation>().Entry;
  var events=new Dictionary<string,string>{[SectEventOptions.UnavailableKey]="不能选择该选项。",[id+".title"]="血魔宗招收弟子",[id+".pages.INITIAL.description"]="你路过了血魔宗的招生大典，这里真热闹啊。",[id+".selectionScreenPrompt"]="选择1张血魔宗，附魔播种与灼热。"};
  foreach(var (key,title,description,result) in new[]{
   ("JOIN","加入血魔宗。","获得遗物：[gold]血魔宗弟子令[/gold]。","你通过了资质测验，成为了血魔宗的弟子，后面会发生什么呢？"),
   ("MEMBER","我早就是血魔宗的人了。","为牌组中的1张血魔宗附魔[gold]播种[/gold]、[gold]灼热[/gold]。","长老为你补发了宗门福利，你使用后变得更强了"),
   ("BUY","买点好装备。","花费[gold]150金币[/gold]，获得遗物：[gold]吸血刀[/gold]。","真是把好刀。"),
   ("LEAVE","不凑热闹。","什么都不发生。","你默默地离开了，还忙着要去找建筑师呢。")}){
   events[id+".pages.INITIAL.options."+key+".title"]=title;events[id+".pages.INITIAL.options."+key+".description"]=description;events[id+".pages."+key+".description"]=result;
  }
  LocManager.Instance.GetTable("events").MergeWith(events);
  string shield=ModelDb.GetId<BloodShield>().Entry;
  LocManager.Instance.GetTable("cards").MergeWith(new(){[shield+".title"]="血盾",[shield+".description"]="失去[red]{HpLoss}[/red]点生命。\n获得[red]{TemporaryHp}[/red]点临时生命。"});
  var relics=new Dictionary<string,string>();
  foreach(var (type,title,description) in new[]{(typeof(BloodSectToken),"血魔宗弟子令","获得时，将[gold]血魔宗[/gold]、[gold]放血[/gold]、[gold]血盾[/gold]、[gold]御血术[/gold]各1张加入你的牌组。"),(typeof(VampiricBlade),"吸血刀","获得时，选择牌组中的至多3张攻击牌，为其附魔[gold]泣血[/gold]：该卡牌直接造成生命伤害时，回复伤害值的25%生命，不超过最大生命。")}){
   string rid=ModelDb.GetId(type).Entry;relics[rid+".title"]=title;relics[rid+".description"]=description;relics[rid+".eventDescription"]=description;relics[rid+".flavor"]=title;relics[rid+".selectionScreenPrompt"]="选择至多3张攻击牌，附魔泣血。";
  }
  LocManager.Instance.GetTable("relics").MergeWith(relics);
  var enchants=new Dictionary<string,string>();
  foreach(var (type,title,text) in new[]{(typeof(BloodScorching),"灼热","这张卡牌可以继续升级。与其他模组的灼热兼容，已有灼热不会重复附加。"),(typeof(WeepingBlood),"泣血","这张卡牌直接造成生命伤害时，回复伤害值的25%生命（向下取整），不超过最大生命。")}){
   string eid=ModelDb.GetId(type).Entry;enchants[eid+".title"]=title;enchants[eid+".description"]=text;enchants[eid+".extraCardText"]=type==typeof(BloodScorching)?"灼热：可以继续升级。":"泣血：回复实际生命伤害值的25%。";
  }
  LocManager.Instance.GetTable("enchantments").MergeWith(enchants);
 }
}
