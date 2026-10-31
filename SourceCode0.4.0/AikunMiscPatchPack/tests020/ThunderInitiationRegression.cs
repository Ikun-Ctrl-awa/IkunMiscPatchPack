using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using STS2RitsuLib.Models.Capabilities;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static Creature SectSecondEnemy(BloodFixture f){var e=f.Combat.CreateCreature(ModelDb.Monster<CalcifiedCultist>().ToMutable(),CombatSide.Enemy,"sect-second");f.Combat.AddCreature(e);e.SetMaxHpInternal(10000);e.SetCurrentHpInternal(10000);return e;}
 static void AddArc(CardModel c)=>c.GetOrCreateCapability<Pack.BloodEnchantments>().Add<Pack.ThunderArc>();
 static async Task ThunderInitiationChecks(){
  var model=ModelDb.Event<Pack.ThunderSectInitiation>();
  Check("thunder separate miscellaneous option and cross-act event",Pack.Options.All.Single(o=>o.Key==Pack.ThunderContent.Key).Misc&&ModelDb.AllSharedEvents.Count(e=>e is Pack.ThunderSectInitiation)==1);
  Check("thunder initial title and text",model.Title.GetFormattedText()=="雷电宗招收弟子"&&model.InitialDescription.GetFormattedText()=="你路过了雷电宗的招生大典，这里真热闹啊。");
  Type[] memberTypes=[typeof(Zap),typeof(Tempest),typeof(LightningRod),typeof(BallLightning),typeof(Voltaic)];
  Check("thunder five native card names mapped exactly",memberTypes.Select(t=>ModelDb.GetById<CardModel>(ModelDb.GetId(t)).Title).SequenceEqual(new[]{"电击","暴风雨","引雷针","球状闪电","电流相生"}));
  using(var f=OfficeFixture()){
   foreach(var act in ModelDb.Acts.Where(a=>a.IsDefault||a.Id.Entry.Contains("ENDING")||a.GetType().FullName!.Contains("Act4Heart"))){var mutable=(ActModel)act.ToMutable();mutable.GenerateRooms(new Rng(123),UnlockState.all);var rooms=(RoomSet)AccessTools.Field(typeof(ActModel),"_rooms").GetValue(mutable)!;Check("thunder in chapter question event pool "+act.Id,rooms.events.Any(e=>e is Pack.ThunderSectInitiation));}
   foreach(var p in f.Run.Players){p.Gold=149;p.AddRelicInternal(ModelDb.Relic<Pack.BloodSectToken>().ToMutable(),silent:true);foreach(var c in p.Deck.Cards.Where(Pack.ThunderContent.IsMemberCard).ToArray())c.RemoveFromCurrentPile();}
   Check("thunder all first three invalid prevents event",!model.IsAllowed(f.Run));f.P.Gold=150;Check("thunder exact purchase threshold enables event",model.IsAllowed(f.Run));f.P.Gold=149;
   foreach(var type in memberTypes){var card=ModelDb.GetById<CardModel>(ModelDb.GetId(type)).ToMutable();f.Run.AddCard(card,f.P);await CardPileCmd.Add(card,PileType.Deck);Check("thunder membership individually recognized "+type.Name,model.IsAllowed(f.Run)&&Pack.ThunderContent.HasMemberCard(f.P));card.RemoveFromCurrentPile();}
   bool active=Pack.Settings.Startup[Pack.ThunderContent.Key];try{Pack.Settings.Startup[Pack.ThunderContent.Key]=false;f.P.Gold=200;Check("thunder switch disables future encounters only",!model.IsAllowed(f.Run)&&ModelDb.Event<Pack.BloodSectInitiation>().IsAllowed(f.Run));}finally{Pack.Settings.Startup[Pack.ThunderContent.Key]=active;}
  }
  string[] results=["你通过了资质测验，成为了雷电宗的弟子，后面会发生什么呢？","长老为你补发了宗门福利，你使用后变得更强了","真是把好刀。","你默默地离开了，还忙着要去找建筑师呢。"];
  for(int branch=0;branch<4;branch++)foreach(bool other in new[]{false,true}){
   using var f=OfficeFixture();var p=other?f.Other:f.P;var foreign=other?f.P:f.Other;p.Gold=200;if(!Pack.ThunderContent.HasMemberCard(p))await CardPileCmd.Add(f.Run.CreateCard<Zap>(p),PileType.Deck);
   var evt=(Pack.ThunderSectInitiation)model.ToMutable();await evt.BeginEvent(p,null,false);var options=evt.CurrentOptions.ToArray();int before=p.Deck.Cards.Count,foreignDeck=foreign.Deck.Cards.Count,foreignRelics=foreign.Relics.Count;
   Check($"thunder four ordered options P{p.NetId}",options.Select(o=>o.Title.GetFormattedText()).SequenceEqual(new[]{"加入雷电宗。","我早就是雷电宗的人了。","买点好装备。","不凑热闹。"}));
   using(CardSelectCmd.UseSelector(new SectSelector()))await options[branch].Chosen();
   Check($"thunder exact outcome P{p.NetId} B{branch}",evt.IsFinished&&evt.Description!.GetFormattedText()==results[branch]);
   Check("thunder scoped costs and no effects on other player",p.Gold==200-(branch==2?150:0)&&foreign.Deck.Cards.Count==foreignDeck&&foreign.Relics.Count==foreignRelics);
   Check("thunder exact permanent rewards",p.Deck.Cards.Count==before+(branch==1?2:0)&&p.Relics.Count(r=>r is Pack.ThunderSectToken)==(branch==0?1:0)&&p.Relics.Count(r=>r is Pack.ElectricBlade)==(branch==2?1:0));
   if(branch==0)Check("thunder token enchants selected three without adding cards",p.Deck.Cards.Count(c=>Pack.EnchantmentView.Read(c).Any(e=>e is Pack.ThunderArc))==3);
   if(branch==1)Check("thunder membership adds exact upgraded native pair",p.Deck.Cards.TakeLast(2).Select(c=>c.GetType()).SequenceEqual(new[]{typeof(Tempest),typeof(Voltaic)})&&p.Deck.Cards.TakeLast(2).All(c=>c.CurrentUpgradeLevel==1));
   await options[branch].Chosen();await options[(branch+1)%4].Chosen();Check("thunder stale choices never grant twice",p.Deck.Cards.Count==before+(branch==1?2:0)&&p.Gold==200-(branch==2?150:0));
  }
  using(var f=OfficeFixture()){
   f.P.Gold=149;f.P.AddRelicInternal(ModelDb.Relic<Pack.BloodSectToken>().ToMutable(),silent:true);var thunder=(Pack.ThunderSectInitiation)model.ToMutable();await thunder.BeginEvent(f.P,null,false);Check("blood token locks joining thunder sect",thunder.CurrentOptions[0].IsLocked&&thunder.CurrentOptions[1].IsLocked&&thunder.CurrentOptions[2].IsLocked);
   f.Other.AddRelicInternal(ModelDb.Relic<Pack.ThunderSectToken>().ToMutable(),silent:true);var blood=(Pack.BloodSectInitiation)ModelDb.Event<Pack.BloodSectInitiation>().ToMutable();await blood.BeginEvent(f.Other,null,false);Check("thunder token locks joining blood sect",blood.CurrentOptions[0].IsLocked);
  }
  foreach(int count in new[]{0,1,3}){
   using var f=OfficeFixture();var selector=new SectSelector{Count=count};var candidate=f.P.Deck.Cards.First(c=>c.Type==CardType.Attack);candidate.EnchantInternal(ModelDb.Enchantment<Sharp>().ToMutable(),1);
   using(CardSelectCmd.UseSelector(selector))await RelicCmd.Obtain<Pack.ThunderSectToken>(f.P);
   Check("thunder token 0 to 3 selection preserves other enchant N"+count,selector.Min==0&&selector.Max==3&&selector.Seen.All(c=>c.Type is CardType.Attack or CardType.Skill)&&f.P.Deck.Cards.Count(c=>Pack.EnchantmentView.Read(c).Any(e=>e is Pack.ThunderArc))==count&&candidate.Enchantment is Sharp);
   if(count>0)foreach(var copy in new[]{candidate,Restore(candidate),(CardModel)candidate.ClonePreservingMutability()})Check("arc real model persists separately beside sharp",copy.Enchantment is Sharp&&Pack.EnchantmentView.Read(copy).Count(e=>e is Pack.ThunderArc)==1);
   var token=f.P.Relics.OfType<Pack.ThunderSectToken>().Single();var saved=RelicModel.FromSerializable(JsonSerializer.Deserialize<SerializableRelic>(JsonSerializer.Serialize(token.ToSerializable()))!);Check("thunder token restores same stable placeholder id without pickup repeat",saved.Id==token.Id&&saved is Pack.ThunderSectToken&&token.Rarity==RelicRarity.Event&&token.Icon!=null);
  }
  await ThunderCombatChecks();await ThunderScreenshots();
 }
 static async Task ThunderCombatChecks(){
  using(var f=NewDongniFixture(true)){
   var second=SectSecondEnemy(f);var skill=f.Card<DefendIronclad>();AddArc(skill);int hp=f.Enemy.CurrentHp,hp2=second.CurrentHp;await f.Play(skill);
   Check("arc triggers on actual skill play hits all enemies once",f.Enemy.CurrentHp==hp-1&&second.CurrentHp==hp2-1&&f.P.Creature.CurrentHp==80);
   await f.Play(skill);Check("arc repeats on each play without one-time disable",f.Enemy.CurrentHp==hp-2&&second.CurrentHp==hp2-2);
   await PowerCmd.Apply<StrengthPower>(f.Choice,f.P.Creature,20,f.P.Creature,null);await f.Play(skill);Check("arc fixed one damage unaffected by strength",f.Enemy.CurrentHp==hp-3&&second.CurrentHp==hp2-3);
   Check("arc does not change permanent deck",f.P.Deck.Cards.All(c=>!Pack.EnchantmentView.Read(c).Any(e=>e is Pack.ThunderArc)));
  }
  using(var f=NewDongniFixture(true)){
   var second=SectSecondEnemy(f);await RelicCmd.Obtain<Pack.ElectricBlade>(f.P);var blade=f.P.Relics.OfType<Pack.ElectricBlade>().Single();
   int hp=f.Enemy.CurrentHp,hp2=second.CurrentHp;await f.Play(f.Card<StrikeIronclad>(),f.Enemy);Check("blade attack adds one hit and first counter",f.Enemy.CurrentHp==hp-7&&second.CurrentHp==hp2&&blade.AttacksPlayed==1);
   hp=f.Enemy.CurrentHp;await f.Play(f.Card<TwinStrike>(),f.Enemy);Check("blade multi-hit attack adds one extra hit only",f.Enemy.CurrentHp==hp-11&&blade.AttacksPlayed==2);
   await f.Play(f.Card<DefendIronclad>());await f.Play(f.Card<StrikeIronclad>(f.Other),f.Enemy);Check("blade ignores skills and other player attacks",blade.AttacksPlayed==2);
   hp=f.Enemy.CurrentHp;hp2=second.CurrentHp;await f.Play(f.Card<StrikeIronclad>(),f.Enemy);Check("blade third attack adds one plus three aoe once",f.Enemy.CurrentHp==hp-10&&second.CurrentHp==hp2-3&&blade.AttacksPlayed==0);
   hp=f.Enemy.CurrentHp;hp2=second.CurrentHp;await f.Play(f.Card<Whirlwind>());Check("blade untargeted aoe adds one per enemy and counts one card",f.Enemy.CurrentHp==hp-1&&second.CurrentHp==hp2-1&&blade.AttacksPlayed==1);
   blade.AttacksPlayed=2;var options=new JsonSerializerOptions{IncludeFields=true};var saved=(Pack.ElectricBlade)RelicModel.FromSerializable(JsonSerializer.Deserialize<SerializableRelic>(JsonSerializer.Serialize(blade.ToSerializable(),options),options)!);
   Check("blade saved counter survives restore without extra trigger",saved.AttacksPlayed==2&&saved.DisplayAmount==2&&saved.ShowCounter&&saved.Rarity==RelicRarity.Event&&saved.Icon!=null&&saved.BigIcon!=null);
   await blade.BeforeCombatStart();Check("blade counter carries across combats",blade.AttacksPlayed==2);
  }
  using(var f=NewDongniFixture()){
   await RelicCmd.Obtain<Pack.ElectricBlade>(f.P);await PowerCmd.Apply<Pack.BloodSectPower>(f.Choice,f.P.Creature,100,f.P.Creature,null);
   var attack=f.Card<StrikeIronclad>();AddArc(attack);Pack.BloodEnchantmentRules.AddWeeping(attack);f.P.Creature.SetCurrentHpInternal(40);await f.Play(attack,f.Enemy);
   Check("arc blade bonus damage never borrows card leech source",f.P.Creature.CurrentHp==47);
  }
  if(!Minimal)foreach(bool merged in new[]{false,true}){
   var mode=AccessTools.Field(Find("CardFusion.CardFusionSettings"),"_synthesizeMode");bool old=(bool)mode.GetValue(null)!;
   try{mode.SetValue(null,merged);using var f=OfficeFixture();var material=ModelDb.Card<StrikeIronclad>().ToMutable();AddArc(material);var fused=Fuse(material,ModelDb.Card<StrikeIronclad>().ToMutable());
    foreach(var copy in new[]{fused,Restore(fused),(CardModel)fused.ClonePreservingMutability()})Check("fusion arc real model survives save clone mode "+merged,FusionQuestBlessingFix.Entry.Descendants(copy).Count(c=>Pack.EnchantmentView.Read(c).Any(e=>e is Pack.ThunderArc))==1);
    f.Combat.AddCard(fused,f.P);int hp=f.Enemy.CurrentHp;await f.Play(fused,f.Enemy);Check("fusion arc triggers once for enchanted component mode "+merged,f.Enemy.CurrentHp==hp-13);
   }finally{mode.SetValue(null,old);}
  }
 }
 static async Task ThunderScreenshots(){
  using var f=OfficeFixture();f.P.Gold=200;await CardPileCmd.Add(f.Run.CreateCard<Zap>(f.P),PileType.Deck);var sync=RunManager.Instance.EventSynchronizer;sync.BeginEvent(ModelDb.Event<Pack.ThunderSectInitiation>());var model=(Pack.ThunderSectInitiation)sync.GetLocalEvent();
  var viewport=new SubViewport{Size=new(2560,1440),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("181c27")});bool old=TestMode.IsOn;
  try{TestMode.IsOn=false;var node=NEventRoom.Create(model,f.Run,false)!;host.AddChild(node);node.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);await Wait(2.2);await AfterDraw();
   Check("thunder native background four choices and opacity",node.Layout!.OptionButtons.Count()==4&&node.Layout.GetNode<TextureRect>("ThunderSectBackground").Modulate.A==.55f);Check("thunder event screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/thunder-sect-event.png")==Error.Ok);
   sync.ChooseLocalOption(3);await sync.AwaitPendingOptionTasks();await Wait(2.2);await AfterDraw();viewport.GetTexture().GetImage().SavePng(Output+"/thunder-sect-leave.png");
   node.QueueFree();await Wait(.1);TestMode.IsOn=true;var card=f.Card<StrikeIronclad>();AddArc(card);await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);TestMode.IsOn=false;var n=NCard.Create(card)!;host.AddChild(n);n.Position=new(800,700);n.Scale=Vector2.One*1.4f;n.UpdateVisuals(PileType.Hand,CardPreviewMode.Normal);Pack.MultiEnchantIcons.Refresh(n);await Wait(.3);await AfterDraw();Check("thunder actual arc card screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/thunder-arc-card.png")==Error.Ok);
  }finally{TestMode.IsOn=old;viewport.QueueFree();await Wait(.03);}
 }
}
