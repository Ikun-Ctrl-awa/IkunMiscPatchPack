using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib;
using STS2RitsuLib.Models.Capabilities;
using STS2RitsuLib.Scaffolding.Content;

namespace AikunMiscPatchPack;
public sealed class ThunderSectInitiation : ModEventTemplate {
 bool resolving;
 public override bool IsAllowed(IRunState runState)=>Settings.Active(ThunderContent.Key)&&runState.Players.Any(p=>!p.Relics.Any(r=>r is BloodSectToken)||ThunderContent.HasMemberCard(p)||p.Gold>=150);
 public override string? CustomInitialPortraitPath=>ThunderInitiationArt.BackgroundPath;
 public override IEnumerable<string> GetAssetPaths(IRunState runState)=>["res://scenes/events/default_event_layout.tscn",CustomInitialPortraitPath!];
 bool Allowed(string key)=>Owner is {} p&&key switch{"JOIN"=>!p.Relics.Any(r=>r is BloodSectToken),"MEMBER"=>ThunderContent.HasMemberCard(p),"BUY"=>p.Gold>=150,_=>true};
 protected override IReadOnlyList<EventOption> GenerateInitialOptions()=>[
  SectEventOptions.Create(this,InitialOptionKey("JOIN"),Allowed("JOIN"),()=>Choose("JOIN"),ModelDb.Relic<ThunderSectToken>().HoverTips),
  SectEventOptions.Create(this,InitialOptionKey("MEMBER"),Allowed("MEMBER"),()=>Choose("MEMBER"),[SectEventOptions.UpgradedTip<Tempest>(),SectEventOptions.UpgradedTip<Voltaic>()]),
  SectEventOptions.Create(this,InitialOptionKey("BUY"),Allowed("BUY"),()=>Choose("BUY"),ModelDb.Relic<ElectricBlade>().HoverTips),
  new EventOption(this,()=>Choose("LEAVE"),InitialOptionKey("LEAVE"))
 ];
 async Task Choose(string key){
  if(IsFinished||resolving||!Allowed(key))return;resolving=true;
  var p=Owner??throw new InvalidOperationException("雷电宗事件尚未指定玩家。");
  try{
   if(key=="JOIN")await RelicCmd.Obtain<ThunderSectToken>(p);
   else if(key=="MEMBER"){
    var tempest=p.RunState.CreateCard<Tempest>(p);var voltaic=p.RunState.CreateCard<Voltaic>(p);
    CardCmd.Upgrade(tempest);CardCmd.Upgrade(voltaic);await CardPileCmd.Add(new CardModel[]{tempest,voltaic},PileType.Deck);
   }else if(key=="BUY"){await PlayerCmd.LoseGold(150,p,GoldLossType.Spent);await RelicCmd.Obtain<ElectricBlade>(p);}
   SetEventFinished(PageDescription(key));
  }finally{resolving=false;}
 }
}

// 保留原占位遗物的类型与注册位置，已有存档中的弟子令继续使用原来的 ID。
public sealed class ThunderSectToken : BloodEventRelic {
 public override bool HasUponPickupEffect=>true;
 public override async Task AfterObtained(){
  bool Eligible(CardModel c)=>c.Type is CardType.Attack or CardType.Skill&&!EnchantmentView.Read(c).Any(e=>e is ThunderArc);
  int max=Math.Min(3,Owner.Deck.Cards.Count(Eligible));if(max==0)return;
  var selected=await CardSelectCmd.FromDeckGeneric(Owner,new CardSelectorPrefs(SelectionScreenPrompt,0,max){Cancelable=true},Eligible);
  foreach(var card in selected.Distinct().Take(3))if(card.Owner==Owner&&card.Pile==Owner.Deck&&Eligible(card))card.GetOrCreateCapability<BloodEnchantments>().Add<ThunderArc>();
 }
}

public sealed class ThunderArc : ModEnchantmentTemplate {
 public override bool HasExtraCardText=>true;
 public override bool ShowAmount=>false;
 public override string? CustomIconPath=>ThunderInitiationArt.IconPath(ThunderInitiationArt.ArcPath);
 public override bool CanEnchantCardType(CardType type)=>type is CardType.Attack or CardType.Skill;
 public override bool CanEnchant(CardModel card)=>base.CanEnchant(card)&&!EnchantmentView.Read(card).Any(e=>e is ThunderArc);
 public override async Task OnPlay(PlayerChoiceContext choiceContext,CardPlay? cardPlay){
  var owner=Card.Owner;if(!owner.Creature.IsAlive)return;var enemies=owner.Creature.CombatState?.HittableEnemies.ToArray()??[];
  if(enemies.Length>0)await CreatureCmd.Damage(choiceContext,enemies,1,ValueProp.Unpowered,owner.Creature,Card,cardPlay);
 }
}

public sealed class ElectricBlade : BloodEventRelic {
 int attacksPlayed;
 public override string? CustomIconPath=>ThunderInitiationArt.IconPath(ThunderInitiationArt.BladePath);
 public override bool ShowCounter=>true;
 public override int DisplayAmount=>AttacksPlayed;
 [SavedProperty] public int AttacksPlayed{get=>attacksPlayed;set{AssertMutable();attacksPlayed=Math.Clamp(value,0,2);InvokeDisplayAmountChanged();}}
 public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext,CardPlay cardPlay){
  if(cardPlay.Player!=Owner||cardPlay.Card.Type!=CardType.Attack||!Owner.Creature.IsAlive)return;
  bool discharge=AttacksPlayed==2;AttacksPlayed=(AttacksPlayed+1)%3;Flash();
  var enemies=Owner.Creature.CombatState?.HittableEnemies.ToArray()??[];
  var targets=cardPlay.Target is {} target&&enemies.Contains(target)?new[]{target}:cardPlay.Card.TargetType==TargetType.AllEnemies?enemies:[];
  if(targets.Length>0)await CreatureCmd.Damage(choiceContext,targets,1,ValueProp.Unpowered,Owner.Creature);
  if(discharge){enemies=Owner.Creature.CombatState?.HittableEnemies.ToArray()??[];if(enemies.Length>0)await CreatureCmd.Damage(choiceContext,enemies,3,ValueProp.Unpowered,Owner.Creature);}
 }
}

public static class ThunderContent {
 public const string Key="thunder_sect";
 public static bool IsMemberCard(CardModel c)=>c is Zap or Tempest or LightningRod or BallLightning or Voltaic;
 public static bool HasMemberCard(Player p)=>p.Deck.Cards.Any(c=>IsMemberCard(c)||FusionQuestBlessingFix.Entry.Descendants(c).Any(IsMemberCard));
 public static void Initialize(){
  RitsuLibFramework.CreateContentPack(Settings.Id+".ThunderSect").SharedEvent<ThunderSectInitiation>().Relic<EventRelicPool,ElectricBlade>().Enchantment<ThunderArc>().Apply();
  var h=new Harmony("AikunMiscPatchPack.ThunderSect.Content");ThunderInitiationArt.Initialize(h);
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.Initialize)),postfix:new HarmonyMethod(typeof(ThunderContent),nameof(Localize)));
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.SetLanguage)),postfix:new HarmonyMethod(typeof(ThunderContent),nameof(Localize)));
  if(LocManager.Instance!=null)Localize();
  if(Settings.Active(Key))RuntimeLoader.States[Key]="已应用。";
 }
 public static void Localize(){
  if(LocManager.Instance==null)return;string id=ModelDb.GetId<ThunderSectInitiation>().Entry;
  var events=new Dictionary<string,string>{[id+".title"]="雷电宗招收弟子",[id+".pages.INITIAL.description"]="你路过了雷电宗的招生大典，这里真热闹啊。"};
  foreach(var (key,title,description,result) in new[]{
   ("JOIN","加入雷电宗。","获得遗物：[gold]雷电宗弟子令[/gold]。","你通过了资质测验，成为了雷电宗的弟子，后面会发生什么呢？"),
   ("MEMBER","我早就是雷电宗的人了。","向牌组中加入[gold]暴风雨+[/gold]、[gold]电流相生+[/gold]。","长老为你补发了宗门福利，你使用后变得更强了"),
   ("BUY","买点好装备。","花费[gold]150金币[/gold]，获得遗物：[gold]电刀[/gold]。","真是把好刀。"),
   ("LEAVE","不凑热闹。","什么都不发生。","你默默地离开了，还忙着要去找建筑师呢。")}){
   events[id+".pages.INITIAL.options."+key+".title"]=title;events[id+".pages.INITIAL.options."+key+".description"]=description;events[id+".pages."+key+".description"]=result;
  }
  LocManager.Instance.GetTable("events").MergeWith(events);
  var relics=new Dictionary<string,string>();
  foreach(var (type,title,description) in new[]{(typeof(ThunderSectToken),"雷电宗弟子令","获得时，选择至多3张攻击牌或技能牌，为其附魔[gold]电弧[/gold]：打出该牌时，对所有敌人造成1点伤害。"),(typeof(ElectricBlade),"电刀","打出攻击牌时，对目标额外造成1点伤害1次。每打出3张攻击牌，对所有敌人造成3点伤害。")}){
   string rid=ModelDb.GetId(type).Entry;relics[rid+".title"]=title;relics[rid+".description"]=description;relics[rid+".eventDescription"]=description;relics[rid+".flavor"]=title=="电刀"?"电光流转。":"雷光为证。";relics[rid+".selectionScreenPrompt"]="选择至多3张攻击牌或技能牌，附魔电弧。";
  }
  LocManager.Instance.GetTable("relics").MergeWith(relics);
  string eid=ModelDb.GetId<ThunderArc>().Entry;LocManager.Instance.GetTable("enchantments").MergeWith(new(){[eid+".title"]="电弧",[eid+".description"]="打出这张卡时，对所有敌人造成1点伤害。",[eid+".extraCardText"]="电弧：对所有敌人造成1点伤害。"});
 }
}
