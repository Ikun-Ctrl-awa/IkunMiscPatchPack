using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Models.Capabilities;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 sealed class SectSelector : ICardSelector {
  public int Count=3,Min,Max;public CardModel[] Seen=[];
  public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options,int minSelect,int maxSelect){Seen=options.ToArray();Min=minSelect;Max=maxSelect;return Task.FromResult<IEnumerable<CardModel>>(Seen.Take(Math.Min(Count,maxSelect)).ToArray());}
  public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,IReadOnlyList<CardRewardAlternative> alternatives)=>throw new InvalidOperationException("本专项不应生成卡牌奖励。");
 }
 static async Task BloodInitiationChecks(){
  var model=ModelDb.Event<Pack.BloodSectInitiation>();var evidence=new List<object>();
  Check("sect shared cross-act event registered once",ModelDb.AllSharedEvents.Count(e=>e is Pack.BloodSectInitiation)==1);
  Check("sect miscellaneous option renamed keeps saved key",Pack.Options.All.Single(o=>o.Key=="blood_sect") is {Title:"血魔宗卡牌、遗物与事件",Misc:true});
  Check("sect localized title and initial text",model.Title.GetFormattedText()=="血魔宗招收弟子"&&model.InitialDescription.GetFormattedText()=="你路过了血魔宗的招生大典，这里真热闹啊。");
  using(var f=OfficeFixture()){
   foreach(var act in ModelDb.Acts.Where(a=>a.IsDefault||a.Id.Entry.Contains("ENDING")||a.GetType().FullName!.Contains("Act4Heart"))){
    var mutable=(ActModel)act.ToMutable();mutable.GenerateRooms(new Rng(123),UnlockState.all);var rooms=(RoomSet)AccessTools.Field(typeof(ActModel),"_rooms").GetValue(mutable)!;
    Check("sect in chapter question event pool "+act.Id,rooms.events.Any(e=>e is Pack.BloodSectInitiation));
   }
   foreach(var p in f.Run.Players){p.Gold=149;p.AddRelicInternal(ModelDb.Relic<Pack.ThunderSectToken>().ToMutable(),silent:true);}
   Check("sect omitted when first three conditions all fail",!model.IsAllowed(f.Run));
   f.P.Gold=150;Check("sect exact gold threshold enables event",model.IsAllowed(f.Run));f.P.Gold=149;
   await CardPileCmd.Add(f.Run.CreateCard<Pack.BloodSect>(f.P),PileType.Deck);Check("sect blood card alone enables event",model.IsAllowed(f.Run));
   bool active=Pack.Settings.Startup["blood_sect"];try{Pack.Settings.Startup["blood_sect"]=false;
    Check("sect off stops encounters and card acquisition",!model.IsAllowed(f.Run)&&!ModelDb.CardPool<ColorlessCardPool>().GetUnlockedCards(UnlockState.all,CardMultiplayerConstraint.None).Any(c=>c is Pack.BloodSect or Pack.BloodShield));
    Check("sect off still restores saved content",Restore(ModelDb.Card<Pack.BloodShield>().ToMutable()) is Pack.BloodShield);
   }finally{Pack.Settings.Startup["blood_sect"]=active;}
  }
  string[] results=["你通过了资质测验，成为了血魔宗的弟子，后面会发生什么呢？","长老为你补发了宗门福利，你使用后变得更强了","真是把好刀。","你默默地离开了，还忙着要去找建筑师呢。"];
  foreach(bool foreign in new[]{false,true})for(int branch=0;branch<4;branch++){
   using var f=OfficeFixture();var p=foreign?f.Other:f.P;var other=foreign?f.P:f.Other;p.Gold=200;
   var card=f.Run.CreateCard<Pack.BloodSect>(p);await CardPileCmd.Add(card,PileType.Deck);
   var evt=(Pack.BloodSectInitiation)model.ToMutable();await evt.BeginEvent(p,null,false);var options=evt.CurrentOptions.ToArray();int before=p.Deck.Cards.Count,gold=p.Gold,otherDeck=other.Deck.Cards.Count,otherRelics=other.Relics.Count;
   Check($"sect four correct options P{p.NetId}",options.Select(o=>o.Title.GetFormattedText()).SequenceEqual(new[]{"加入血魔宗。","我早就是血魔宗的人了。","买点好装备。","不凑热闹。"}));
   using(CardSelectCmd.UseSelector(new SectSelector()))await options[branch].Chosen();
   Check($"sect result P{p.NetId} B{branch}",evt.IsFinished&&evt.Description!.GetFormattedText()==results[branch]);
   Check($"sect isolated costs P{p.NetId} B{branch}",p.Gold==gold-(branch==2?150:0)&&other.Deck.Cards.Count==otherDeck&&other.Relics.Count==otherRelics);
   Check($"sect permanent branch rewards P{p.NetId} B{branch}",p.Deck.Cards.Count==before+(branch==0?4:0)&&p.Relics.Count(r=>r is Pack.BloodSectToken)==(branch==0?1:0)&&p.Relics.Count(r=>r is Pack.VampiricBlade)==(branch==2?1:0));
   if(branch==0)Check("sect token exact four permanent cards",p.Deck.Cards.TakeLast(4).Select(c=>c.GetType()).SequenceEqual(new[]{typeof(Pack.BloodSect),typeof(Bloodletting),typeof(Pack.BloodShield),typeof(Hemokinesis)}));
   if(branch==1)Check("sect membership grants sown and independent scorching",Pack.EnchantmentView.Read(card).Count(e=>e is Sown)==1&&Pack.EnchantmentView.Read(card).Count(e=>e is Pack.BloodScorching)==1);
   int relicCount=p.Relics.Count;await options[branch].Chosen();await options[(branch+1)%4].Chosen();
   Check("sect repeated and stale options do not grant twice",p.Relics.Count==relicCount&&p.Deck.Cards.Count==before+(branch==0?4:0)&&p.Gold==gold-(branch==2?150:0));
   foreach(var relic in p.Relics.Where(r=>r is Pack.BloodEventRelic)){var saved=RelicModel.FromSerializable(JsonSerializer.Deserialize<SerializableRelic>(JsonSerializer.Serialize(relic.ToSerializable()))!);
    Check("sect relic saves own type without rerunning pickup "+relic.Id,saved.GetType()==relic.GetType()&&saved.Rarity==RelicRarity.Event&&saved.Icon!=null&&saved.BigIcon!=null&&p.Deck.Cards.Count==before+(branch==0?4:0));}
  }
  using(var f=OfficeFixture()){
   f.P.Gold=149;f.P.AddRelicInternal(ModelDb.Relic<Pack.ThunderSectToken>().ToMutable(),silent:true);
   var evt=(Pack.BloodSectInitiation)model.ToMutable();await evt.BeginEvent(f.P,null,false);
   Check("sect disallowed choices remain visibly locked",evt.CurrentOptions.Take(3).All(o=>o.IsLocked)&&!evt.CurrentOptions[3].IsLocked);
  }
  foreach(int count in new[]{0,1,2,3}){
   using var f=OfficeFixture();var selector=new SectSelector{Count=count};var candidates=f.P.Deck.Cards.Where(c=>c.Type==CardType.Attack).ToArray();
   var native=ModelDb.Enchantment<Sharp>().ToMutable();candidates[0].EnchantInternal(native,2);native.ModifyCard();
   using(CardSelectCmd.UseSelector(selector))await RelicCmd.Obtain<Pack.VampiricBlade>(f.P);
   Check("blade native selection min zero max three N"+count,selector.Min==0&&selector.Max==3&&selector.Seen.All(c=>c.Type==CardType.Attack)&&selector.Seen.Length==candidates.Length);
   Check("blade exact selected count keeps existing enchant N"+count,candidates.Count(c=>Pack.EnchantmentView.Read(c).Any(e=>e is Pack.WeepingBlood))==count&&candidates[0].Enchantment==native&&native.Amount==2);
   if(count>0)foreach(var copy in new[]{Restore(candidates[0]),(CardModel)candidates[0].ClonePreservingMutability()})Check("blade save and clone retain both enchantments",copy.Enchantment is Sharp {Amount:2}&&Pack.EnchantmentView.Read(copy).Count(e=>e is Pack.WeepingBlood)==1);
  }
  await SectEnchantChecks(evidence);await SectShieldChecks();await SectScreenshots();
  File.WriteAllText(Output+"/blood-initiation-evidence.json",JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
 }
 static async Task SectEnchantChecks(List<object> evidence){
  using(var f=OfficeFixture()){
   var deck=f.Run.CreateCard<Pack.BloodSect>(f.P);await CardPileCmd.Add(deck,PileType.Deck);
   Pack.BloodEnchantmentRules.AddInitiation(deck);Pack.BloodEnchantmentRules.AddInitiation(deck);Up(deck,5);
   Check("own scorching independent allows five upgrades without duplicate entries",deck.CurrentUpgradeLevel==5&&deck.IsUpgradable&&deck.DynamicVars["LeechPercent"].IntValue==180&&Pack.EnchantmentView.Read(deck).Length==2);
   foreach(var copy in new[]{Restore(deck),(CardModel)deck.ClonePreservingMutability(),Up((CardModel)deck.ClonePreservingMutability())}){
    Check("own scorching saved cloned preview retains levels and two effects",copy.CurrentUpgradeLevel>=5&&copy.IsUpgradable&&Pack.EnchantmentView.Read(copy).Count(e=>e is Sown)==1&&Pack.EnchantmentView.Read(copy).Count(e=>e is Pack.BloodScorching)==1);
    Check("own enchant visible description and hover tips",copy.GetDescriptionForPile(PileType.None).Contains("灼热")&&copy.HoverTips.Count()>=2);
   }
   var played=(CardModel)deck.ClonePreservingMutability();AccessTools.Field(typeof(CardModel),"_owner").SetValue(played,null);f.Combat.AddCard(played,f.P);int energy=f.P.PlayerCombatState!.Energy;
   await f.Play(played);Check("added native sown grants energy once on actual play",f.P.PlayerCombatState.Energy==energy+1);
   await f.Play(played);Check("sown cannot grant energy twice in one combat",f.P.PlayerCombatState.Energy==energy+1);
   Check("combat sown status never mutates permanent card",Pack.EnchantmentView.Read(deck).OfType<Sown>().Single().Status==MegaCrit.Sts2.Core.Entities.Enchantments.EnchantmentStatus.Normal);
   evidence.Add(new{Card=deck.ToSerializable(),Enchantments=Pack.EnchantmentView.Read(deck).Select(e=>e.Id.ToString()).ToArray()});
  }
  using(var f=NewDongniFixture(true)){
   var card=f.Card<TwinStrike>();Pack.BloodEnchantmentRules.AddWeeping(card);f.P.Creature.SetCurrentHpInternal(40);await f.Play(card,f.Enemy);
   Check("weeping actual multi-hit rounds each hit independently",f.P.Creature.CurrentHp==42);
   int hp=f.P.Creature.CurrentHp;await f.Play(f.Card<StrikeIronclad>(),f.Enemy);Check("weeping ignores other cards",f.P.Creature.CurrentHp==hp);
   f.Enemy.GainBlockInternal(100);await f.Play(card,f.Enemy);Check("weeping blocked hits do not heal",f.P.Creature.CurrentHp==hp);f.Enemy.LoseBlockInternal(9999);
   await CreatureCmd.Damage(f.Choice,f.Other.Creature,12,ValueProp.Move,f.P.Creature,card,null);Check("weeping ignores friendly targets",f.P.Creature.CurrentHp==hp);
   await CreatureCmd.Damage(f.Choice,f.Enemy,12,ValueProp.Unpowered,f.Other.Creature,card,null);Check("weeping ignores another players damage",f.P.Creature.CurrentHp==hp);
   f.P.Creature.SetCurrentHpInternal(99);await f.Play(card,f.Enemy);Check("weeping heals only to max without temporary overflow",f.P.Creature.CurrentHp==100&&f.Pool==0);
   var p=await PowerCmd.Apply<Pack.BloodSectPower>(f.Choice,f.P.Creature,100,f.P.Creature,null);f.P.Creature.SetCurrentHpInternal(40);await f.Play(card,f.Enemy);
   Check("weeping coexists with blood sect without losing either effect",f.P.Creature.CurrentHp==52);
  }
  var own=ModelDb.Enchantment<Pack.BloodScorching>();Check("own scorching registered stable distinct model id",own.Id.Entry.Contains("BLOOD_SCORCHING"));
  if(!Minimal){
   var foreign=Enchant(Find("MoreEnchant.Enchantments.ScorchingEnchantment"),1);
   Check("same display name scorching uses distinct registered ids",own.Title.GetFormattedText()==foreign.Title.GetFormattedText()&&own.Id!=foreign.Id&&ModelDb.GetById<EnchantmentModel>(own.Id).GetType()==typeof(Pack.BloodScorching)&&ModelDb.GetById<EnchantmentModel>(foreign.Id).GetType()==foreign.GetType());
   for(int mode=0;mode<3;mode++){
    var card=ModelDb.Card<Pack.BloodSect>().ToMutable();var old=Enchant(foreign.GetType(),1);
    if(mode==0)card.EnchantInternal(old,1);
    else if(mode==1){card.EnchantInternal(ModelDb.Enchantment<Sharp>().ToMutable(),2);Extra(card,old);}
    else{var composite=Find("Plague.PlagueCompositeEnchantment").GetMethod("ConvertFrom",F)!.Invoke(null,[card,old]);AccessTools.Field(typeof(CardModel),"<Enchantment>k__BackingField").SetValue(card,composite);}
    Pack.BloodEnchantmentRules.AddInitiation(card);Up(card,5);
    foreach(var copy in new[]{card,Restore(card),(CardModel)card.ClonePreservingMutability()})
     Check("existing scorching original id persists no duplicate mode "+mode,copy.CurrentUpgradeLevel==5&&copy.IsUpgradable&&Pack.EnchantmentView.Read(copy).Count(Pack.BloodEnchantmentRules.IsScorching)==1&&Pack.EnchantmentView.Read(copy).Any(e=>e.GetType()==foreign.GetType())&&!Pack.EnchantmentView.Read(copy).Any(e=>e is Pack.BloodScorching));
   }
   var ownCard=ModelDb.Card<Pack.BloodSect>().ToMutable();Pack.BloodEnchantmentRules.AddInitiation(ownCard);
   Check("foreign scorching cannot attach a duplicate to our extra slot",!ModelDb.GetById<EnchantmentModel>(foreign.Id).CanEnchant(ownCard)&&!own.CanEnchant(ownCard));
   var both=ModelDb.Card<Pack.BloodSect>().ToMutable();Pack.BloodEnchantmentRules.AddInitiation(both);both.EnchantInternal(Enchant(foreign.GetType(),1),1);Up(both,5);
   Check("already double scorching upgrades once per action",both.CurrentUpgradeLevel==5&&both.DynamicVars["LeechPercent"].IntValue==180&&Restore(both).CurrentUpgradeLevel==5);
   foreach(bool merged in new[]{false,true}){
    var modeField=AccessTools.Field(Find("CardFusion.CardFusionSettings"),"_synthesizeMode");bool old=(bool)modeField.GetValue(null)!;
    try{
     modeField.SetValue(null,merged);
     using var f=OfficeFixture();var material=ModelDb.Card<Pack.BloodSect>().ToMutable();Pack.BloodEnchantmentRules.AddInitiation(material);
     var fused=Fuse(material,ModelDb.Card<StrikeIronclad>().ToMutable());Up(fused,5);
     foreach(var copy in new[]{fused,Restore(fused),(CardModel)fused.ClonePreservingMutability()}){
      var blood=FusionQuestBlessingFix.Entry.Descendants(copy).OfType<Pack.BloodSect>().Single();
      Check("fusion own scorching and sown survive clone save mode "+merged,blood.CurrentUpgradeLevel==5&&blood.IsUpgradable&&Pack.EnchantmentView.Read(blood).Count(e=>e is Pack.BloodScorching)==1&&Pack.EnchantmentView.Read(blood).Count(e=>e is Sown)==1);
     }
     f.Combat.AddCard(fused,f.P);int energy=f.P.PlayerCombatState!.Energy;await f.Play(fused,f.Enemy);
     Check("fusion child sown executes once mode "+merged,f.P.PlayerCombatState.Energy==energy+1);
     var attack=ModelDb.Card<TwinStrike>().ToMutable();Pack.BloodEnchantmentRules.AddWeeping(attack);var attacking=Fuse(attack,ModelDb.Card<StrikeIronclad>().ToMutable());f.Combat.AddCard(attacking,f.P);
     await PowerCmd.Remove<Pack.BloodSectPower>(f.P.Creature);f.P.Creature.SetCurrentHpInternal(40);await f.Play(attacking,f.Enemy);
     Check("fusion child weeping applies only its two hits mode "+merged,f.P.Creature.CurrentHp==42);
     var deckFused=Fuse(ModelDb.Card<Pack.BloodSect>().ToMutable(),ModelDb.Card<DefendIronclad>().ToMutable());f.Run.AddCard(deckFused,f.P);await CardPileCmd.Add(deckFused,PileType.Deck);
     var evt=(Pack.BloodSectInitiation)ModelDb.Event<Pack.BloodSectInitiation>().ToMutable();await evt.BeginEvent(f.P,null,false);
     Check("sect event recognizes blood sect fusion material",!evt.CurrentOptions[1].IsLocked);
     using(CardSelectCmd.UseSelector(new SectSelector{Count=1}))await evt.CurrentOptions[1].Chosen();
     Check("sect fused membership enchants blood material",FusionQuestBlessingFix.Entry.Descendants(deckFused).OfType<Pack.BloodSect>().All(c=>Pack.BloodEnchantmentRules.HasScorching(c)));
    }finally{modeField.SetValue(null,old);}
   }
  }
 }
 static async Task SectShieldChecks(){
  foreach(int level in new[]{0,1})foreach(bool other in new[]{false,true}){
   using var f=NewDongniFixture(true);var p=other?f.Other:f.P;p.Creature.SetMaxHpInternal(100);p.Creature.SetCurrentHpInternal(80);
   var card=f.Card<Pack.BloodShield>(p);if(level==1)Up(card);await f.Play(card);
   Check($"shield L{level} P{p.NetId} exact hp loss temporary life cost",p.Creature.CurrentHp==80-(level==0?5:3)&&p.Creature.GetPower<Pack.TemporaryBloodPower>()?.Amount==(level==0?15:16)&&card.EnergyCost.Canonical==1);
   foreach(var copy in new[]{Restore(card),(CardModel)card.ClonePreservingMutability()})Check("shield upgrade saves exact values L"+level,copy.DynamicVars.HpLoss.IntValue==(level==0?5:3)&&copy.DynamicVars["TemporaryHp"].IntValue==(level==0?15:16));
   int hp=p.Creature.CurrentHp;await CreatureCmd.Damage(f.Choice,p.Creature,7,ValueProp.Unblockable|ValueProp.Unpowered,f.Enemy,null,null);
   Check("shield pool absorbs damage before real life L"+level,p.Creature.CurrentHp==hp&&p.Creature.GetPower<Pack.TemporaryBloodPower>()!.Amount==(level==0?8:9)&&p.Creature.MaxHp==100);
   await Hook.AfterCombatEnd(f.Run,f.Combat,new CombatRoom(f.Run.Act.AllRegularEncounters.First().ToMutable(),f.Run));
   Check("shield temporary pool clears after combat L"+level,(p.Creature.GetPower<Pack.TemporaryBloodPower>()?.Amount??0)==0);
  }
 }
 static async Task SectScreenshots(){
  using var f=OfficeFixture();var sync=RunManager.Instance.EventSynchronizer;sync.BeginEvent(ModelDb.Event<Pack.BloodSectInitiation>());var model=(Pack.BloodSectInitiation)sync.GetLocalEvent();
  var viewport=new SubViewport{Size=new(2560,1440),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("181c27")});bool old=TestMode.IsOn;
  try{
   TestMode.IsOn=false;var node=NEventRoom.Create(model,f.Run,false)!;host.AddChild(node);node.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);await Wait(2.2);await AfterDraw();
   Check("sect native event four visible choices and semitransparent background",node.Layout!.OptionButtons.Count()==4&&node.Layout.GetNode<TextureRect>("BloodSectBackground").Modulate.A==.55f);
   Check("sect event screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/blood-sect-event.png")==Error.Ok);
   sync.ChooseLocalOption(3);await sync.AwaitPendingOptionTasks();await Wait(2.2);await AfterDraw();viewport.GetTexture().GetImage().SavePng(Output+"/blood-sect-leave.png");
   node.QueueFree();await Wait(.1);TestMode.IsOn=true;
   var samples=new List<CardModel>{f.Card<Pack.BloodShield>(),Up(f.Card<Pack.BloodShield>()),f.Card<Pack.BloodSect>(),f.Card<TwinStrike>()};Pack.BloodEnchantmentRules.AddInitiation(samples[2]);Up(samples[2],5);Pack.BloodEnchantmentRules.AddWeeping(samples[3]);
   for(int i=0;i<samples.Count;i++){await CardPileCmd.Add(samples[i],PileType.Hand,skipVisuals:true);TestMode.IsOn=false;var n=NCard.Create(samples[i])!;host.AddChild(n);n.Position=new(300+600*i,650);n.Scale=Vector2.One*1.2f;n.UpdateVisuals(PileType.Hand,CardPreviewMode.Normal);TestMode.IsOn=true;Pack.MultiEnchantIcons.Refresh(n);}
   await Wait(.3);await AfterDraw();Check("sect native shield and enchant card screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/blood-sect-cards.png")==Error.Ok);
  }finally{TestMode.IsOn=old;viewport.QueueFree();await Wait(.03);}
 }
}
