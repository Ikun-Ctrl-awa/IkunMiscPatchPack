using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 // 保留原生伤害、攻击与状态钩子；只隔离前端监听器及玩家数据。
 sealed class BloodFixture:IDisposable {
  readonly bool oldTest=TestMode.IsOn;readonly ulong? oldLocal=LocalContext.NetId;
  readonly System.Reflection.FieldInfo listeners=AccessTools.Field(typeof(CombatStateTracker),"CombatStateChanged");readonly object? frontend;
  public readonly Player P,Other;public readonly RunState Run;public readonly CombatState Combat;public readonly Creature Enemy;public readonly ThrowingPlayerChoiceContext Choice=new();
  public BloodFixture(MonsterModel? model=null,bool multiplayer=false){
   frontend=listeners.GetValue(CombatManager.Instance.StateTracker);listeners.SetValue(CombatManager.Instance.StateTracker,null);TestMode.IsOn=true;
   P=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);Other=Player.CreateForNewRun<Defect>(UnlockState.all,2);
   Run=RunState.CreateForTest(multiplayer?[P,Other]:[P],seed:"BLOOD-SECT");RunManager.Instance.SetUpTest(Run,new NetSingleplayerGameService());LocalContext.NetId=P.NetId;
   Combat=new CombatState(runState:Run);Combat.AddPlayer(P);if(multiplayer)Combat.AddPlayer(Other);
   Enemy=Combat.CreateCreature(model??ModelDb.Monster<CalcifiedCultist>().ToMutable(),CombatSide.Enemy,"blood-target");Combat.AddCreature(Enemy);Enemy.SetMaxHpInternal(10000);Enemy.SetCurrentHpInternal(10000);
   CombatManager.Instance.SetUpCombat(Combat);var turn=AccessTools.Field(typeof(CombatManager),"_turnState").GetValue(CombatManager.Instance)!;AccessTools.Property(turn.GetType(),"IsInProgress").SetValue(turn,true);
   P.Creature.SetMaxHpInternal(100);P.Creature.SetCurrentHpInternal(80);
  }
  public T Card<T>(Player? owner=null) where T:CardModel=>(T)Combat.CreateCard(ModelDb.Card<T>(),owner??P);
  public async Task Play(CardModel card,Creature? target=null){await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);await card.OnPlayWrapper(Choice,target,false,default,skipCardPileVisuals:true).WaitAsync(TimeSpan.FromSeconds(8));}
  public int Pool=>P.Creature.GetPower<Pack.TemporaryBloodPower>()?.Amount??0;
  public async Task Temp(int amount){await PowerCmd.Remove<Pack.TemporaryBloodPower>(P.Creature);if(amount>0)await PowerCmd.Apply<Pack.TemporaryBloodPower>(Choice,P.Creature,amount,P.Creature,null);}
  // 默认显式标记卡牌伤害；无来源命令的负例直接调用 CreatureCmd.Damage。
  public Task<IEnumerable<DamageResult>> Damage(Creature target,int n,Creature? dealer=null,CardModel? card=null)=>CreatureCmd.Damage(Choice,target,n,ValueProp.Move,dealer??P.Creature,card??(dealer==null||dealer==P.Creature?Card<StrikeIronclad>():null),null);
  public void Dispose(){try{CombatManager.Instance.Reset(false);RunManager.Instance.CleanUp();}finally{LocalContext.NetId=oldLocal;TestMode.IsOn=oldTest;listeners.SetValue(CombatManager.Instance.StateTracker,frontend);}}
 }
 static CardPlay BloodPlay(CardModel card,Creature target)=>new(){Card=card,Player=card.Owner,Target=target,ResultPile=PileType.Discard,Resources=default,IsAutoPlay=false,PlayIndex=0,PlayCount=1};
 static async Task BloodSectChecks(){
  int start=passes.Count;var evidence=new List<object>();
  Check("blood sect runtime installed",Pack.RuntimeLoader.States.GetValueOrDefault("blood_sect")=="已应用。");
  var canonical=ModelDb.Card<Pack.BloodSect>();
  Check("blood sect card is four energy colorless uncommon power",canonical.EnergyCost.Canonical==4&&canonical.Type==CardType.Power&&canonical.Rarity==CardRarity.Uncommon&&canonical.Pool is ColorlessCardPool);
  bool active=Pack.Settings.Startup["blood_sect"];
  try{Pack.Settings.Startup["blood_sect"]=false;Check("blood sect acquisition switch hides pool only",!canonical.CanBeGeneratedInCombat&&!canonical.Pool.GetUnlockedCards(UnlockState.all,CardMultiplayerConstraint.None).Any(c=>c is Pack.BloodSect)&&Restore(Up(canonical.ToMutable())) is Pack.BloodSect);}
  finally{Pack.Settings.Startup["blood_sect"]=active;}
  foreach(int n in Minimal?new[]{0,1}:new[]{0,1,2,3,5}){
   using var f=new BloodFixture(multiplayer:true);var card=f.Card<Pack.BloodSect>();if(n>1)Scorch(card);Up(card,n);
   int pct=100+20*Math.Max(0,n-1),heal=10*pct/100;string tag="blood level "+n;
   Check(tag+" title description and numeric multiplier agree",card.CurrentUpgradeLevel==n&&card.GetDescriptionForPile(PileType.None).Contains(pct+"%")&&card.DynamicVars["LeechPercent"].IntValue==pct);
   await f.Play(card);var power=f.P.Creature.GetPower<Pack.BloodSectPower>()!;
   Check(tag+" actual play stores upgrade",power!=null&&power.UpgradeLevel==n&&power.LeechPercent==pct);
   f.P.Creature.SetCurrentHpInternal(95);await f.Damage(f.Enemy,10);
   Check(tag+" real damage heals and overflow converts only after upgrade",f.P.Creature.CurrentHp==100&&f.Pool==(n>=1?heal-5:0)&&f.P.Creature.MaxHp==100);
   await f.Temp(0);f.P.Creature.SetCurrentHpInternal(30);await f.Play(f.Card<TwinStrike>(),f.Enemy);
   Check(tag+" every hit leeches once",f.P.Creature.CurrentHp==30+2*(5*pct/100));
   var second=f.Combat.CreateCreature(ModelDb.Monster<CalcifiedCultist>().ToMutable(),CombatSide.Enemy,"blood-second");f.Combat.AddCreature(second);second.SetMaxHpInternal(1000);second.SetCurrentHpInternal(1000);
   int hp=f.P.Creature.CurrentHp;await CreatureCmd.Damage(f.Choice,new[]{f.Enemy,second},4,ValueProp.Unpowered,f.P.Creature,f.Card<Whirlwind>(),null);
   Check(tag+" non attack and multiple enemies leech",f.P.Creature.CurrentHp==hp+2*(4*pct/100));
   hp=f.P.Creature.CurrentHp;await f.Damage(f.Enemy,10,f.Other.Creature);await f.Damage(f.Other.Creature,2);Check(tag+" ally damage does not heal owner",f.P.Creature.CurrentHp==hp);
   await f.Damage(f.P.Creature,3);Check(tag+" self damage never leeches",f.P.Creature.CurrentHp==hp-3);
   f.Enemy.GainBlockInternal(100);hp=f.P.Creature.CurrentHp;await f.Damage(f.Enemy,10);Check(tag+" blocked damage never leeches",f.P.Creature.CurrentHp==hp);f.Enemy.LoseBlockInternal(9999);
   await f.Play(f.Card<DefendIronclad>());Check(tag+" own defend gives no block",f.P.Creature.Block==0);
   var anchor=(Anchor)ModelDb.Relic<Anchor>().ToMutable();f.P.AddRelicInternal(anchor,silent:true);await anchor.BeforeCombatStart();Check(tag+" own relic gives no block",f.P.Creature.Block==0);
   var ownPlating=await PowerCmd.Apply<PlatingPower>(f.Choice,f.P.Creature,4,f.P.Creature,card);await ownPlating!.BeforeSideTurnEndEarly(f.Choice,CombatSide.Player,[f.P.Creature]);Check(tag+" delayed own card status allowed",f.P.Creature.Block==4);f.P.Creature.LoseBlockInternal(9999);await PowerCmd.Remove(ownPlating);
   var allyPlating=await PowerCmd.Apply<PlatingPower>(f.Choice,f.P.Creature,4,f.Other.Creature,null);await allyPlating!.BeforeSideTurnEndEarly(f.Choice,CombatSide.Player,[f.P.Creature]);Check(tag+" ally applied status follows level rule",f.P.Creature.Block>0);f.P.Creature.LoseBlockInternal(9999);await PowerCmd.Remove(allyPlating);
   var ally=f.Card<DefendIronclad>(f.Other);await CreatureCmd.GainBlock(f.P.Creature,7,ValueProp.Move,BloodPlay(ally,f.P.Creature));Check(tag+" ally card block follows level rule",f.P.Creature.Block==7);f.P.Creature.LoseBlockInternal(9999);
   var potion=ModelDb.Potion<BlockPotion>().ToMutable();await (Task)AccessTools.Method(typeof(BlockPotion),"OnUse").Invoke(potion,[f.Choice,f.P.Creature])!;Check(tag+" potion block follows level rule",f.P.Creature.Block==12);f.P.Creature.LoseBlockInternal(9999);
   var iron=ModelDb.Potion<HeartOfIron>().ToMutable();AccessTools.Property(typeof(PotionModel),nameof(PotionModel.Owner)).SetValue(iron,f.P);await (Task)AccessTools.Method(typeof(HeartOfIron),"OnUse").Invoke(iron,[f.Choice,f.P.Creature])!;
   var potionPlating=f.P.Creature.GetPower<PlatingPower>()!;await potionPlating.BeforeSideTurnEndEarly(f.Choice,CombatSide.Player,[f.P.Creature]);Check(tag+" potion delayed block is not mistaken for own card",f.P.Creature.Block>0);f.P.Creature.LoseBlockInternal(9999);await PowerCmd.Remove(potionPlating);
   await CreatureCmd.GainBlock(f.P.Creature,6,ValueProp.Unpowered,null);Check(tag+" source context does not leak",f.P.Creature.Block==6);f.P.Creature.LoseBlockInternal(9999);
   await f.Play(f.Card<Pack.BloodSect>());Check(tag+" playing lower copy adds percent and preserves upgrade",f.P.Creature.Powers.OfType<Pack.BloodSectPower>().Count()==1&&power!.UpgradeLevel==n&&power.LeechPercent==pct+100);
   f.P.Creature.SetCurrentHpInternal(40);await f.Damage(f.Enemy,10);Check(tag+" repeated power heals accumulated amount once",f.P.Creature.CurrentHp==40+heal+10);
   var tiny=f.Combat.CreateCreature(ModelDb.Monster<CalcifiedCultist>().ToMutable(),CombatSide.Enemy,"blood-overkill");f.Combat.AddCreature(tiny);tiny.SetMaxHpInternal(3);tiny.SetCurrentHpInternal(3);hp=f.P.Creature.CurrentHp;await f.Damage(tiny,50);
   Check(tag+" overkill excluded from healing",f.P.Creature.CurrentHp==hp+3*(pct+100)/100);
   evidence.Add(new{Level=n,LeechPercent=power!.LeechPercent,Description=card.GetDescriptionForPile(PileType.None)});
  }
  // 无第三方附魔环境也验证高等级状态本身的倍率，保证旧牌仍能继续战斗。
  using(var f=new BloodFixture()){
   var p=await ApplyBloodState(f,3);f.P.Creature.SetCurrentHpInternal(50);await f.Damage(f.Enemy,10);Check("blood upgraded state standalone leeches 140 percent",f.P.Creature.CurrentHp==64);
   await f.Temp(12);f.P.Creature.SetCurrentHpInternal(80);var first=(await f.Damage(f.P.Creature,8,f.Enemy)).Single();Check("temporary blood absorbs before hp without faking block",first.UnblockedDamage==0&&!first.WasFullyBlocked&&f.Pool==4&&f.P.Creature.CurrentHp==80);
   var second=(await f.Damage(f.P.Creature,7,f.Enemy)).Single();Check("temporary blood partial overflow reduces real hp",second.UnblockedDamage==3&&f.Pool==0&&f.P.Creature.CurrentHp==77&&f.P.Creature.MaxHp==100);
   await f.Temp(20);await Hook.AfterCombatEnd(f.Run,f.Combat,new CombatRoom(f.Run.Act.AllRegularEncounters.First().ToMutable(),f.Run));Check("temporary blood clears at combat end without max hp changes",f.Pool==0&&f.P.Creature.MaxHp==100);
  }
  await BloodMonsterChecks(evidence);
  if(!Minimal)foreach(string type in new[]{"MoreEnchant.Enchantments.ScorchingEnchantment","Plague.PlagueStackedShadowEnchantment"})foreach(int mode in new[]{0,1,2}){
   var card=canonical.ToMutable();var e=Enchant(Find(type),1);
   if(mode==1){card.EnchantInternal(ModelDb.Enchantment<Sharp>().ToMutable(),1);Extra(card,e);}else if(mode==2){var composite=Find("Plague.PlagueCompositeEnchantment").GetMethod("ConvertFrom",F)!.Invoke(null,[card,e])!;AccessTools.Field(typeof(CardModel),"<Enchantment>k__BackingField").SetValue(card,composite);}else{card.EnchantInternal(e,1);e.ModifyCard();}
   var preview=(CardModel)card.MutableClone();Up(card,5);string label="blood repeat "+type+" mode="+mode;Check(label+" effect at five upgrades",card.CurrentUpgradeLevel==5&&card.IsUpgradable&&card.DynamicVars["LeechPercent"].IntValue==180);
   card=(Pack.BloodSect)Restore((CardModel)card.MutableClone());Check(label+" clone and save preserve repeat effect",card.CurrentUpgradeLevel==5&&card.IsUpgradable&&card.DynamicVars["LeechPercent"].IntValue==180);Up(card);Check(label+" continues after load",card.CurrentUpgradeLevel==6&&card.DynamicVars["LeechPercent"].IntValue==200);
   using var context=new BloodFixture();var previousRun=run;var previousPlayer=player;
   try{run=context.Run;player=context.P;TestMode.IsOn=false;await PreviewSample(preview,label,false,false);}finally{TestMode.IsOn=true;run=previousRun;player=previousPlayer;}
  }
  await BloodArtChecks();
  File.WriteAllText(Output+"/blood-sect-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Failed=0,Checks=passes.Skip(start),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
 }
 static async Task BloodMonsterChecks(List<object> evidence){
  foreach(string kind in new[]{"paper","suck","imbalance"})foreach(var sample in new (int Pool,int Block,int Hits)[]{(10,0,1),(20,0,1),(6,0,1),(6,4,1),(5,4,1),(10,0,2),(0,10,1)}){
   MonsterModel model=kind=="paper"?ModelDb.Monster<ScrollOfBiting>().ToMutable():kind=="suck"?ModelDb.Monster<FossilStalker>().ToMutable():ModelDb.Monster<BowlbugRock>().ToMutable();
   using var f=new BloodFixture(model);await f.Temp(sample.Pool);f.P.Creature.GainBlockInternal(sample.Block);
   if(kind=="paper")await PowerCmd.Apply<PaperCutsPower>(f.Choice,f.Enemy,2,f.Enemy,null);
   if(kind=="suck")await PowerCmd.Apply<SuckPower>(f.Choice,f.Enemy,3,f.Enemy,null);
   if(kind=="imbalance")await PowerCmd.Apply<ImbalancedPower>(f.Choice,f.Enemy,1,f.Enemy,null);
   var command=await DamageCmd.Attack(10).FromMonster(model).WithHitCount(sample.Hits).WithNoAttackerAnim().Execute(f.Choice);
   var hits=command.Results.SelectMany(r=>r).ToArray();int pool=sample.Pool,block=sample.Block;var expected=new List<int>();
   for(int i=0;i<sample.Hits;i++){int blocked=Math.Min(block,10);block-=blocked;int absorbed=Math.Min(pool,10-blocked);pool-=absorbed;expected.Add(10-blocked-absorbed);}
   int healthHits=expected.Count(d=>d>0);bool full=expected.Any(d=>d==0);string label=$"blood {kind} pool={sample.Pool} block={sample.Block} hits={sample.Hits}";
   Check(label+" exact per hit damage and pool",hits.Select(r=>r.UnblockedDamage).SequenceEqual(expected)&&f.Pool==pool);
   if(kind=="paper")Check(label+" max hp changes only for actual hp hits",f.P.Creature.MaxHp==100-2*healthHits);
   if(kind=="suck")Check(label+" strength changes only for actual hp hits",(f.Enemy.GetPower<StrengthPower>()?.Amount??0)==3*healthHits);
   if(kind=="imbalance")Check(label+" off balance only when an attack hits no real hp",((BowlbugRock)model).IsOffBalance==full);
   Check(label+" actual health accounting",f.P.Creature.CurrentHp==80-hits.Sum(h=>h.UnblockedDamage));
   evidence.Add(new{Kind=kind,sample.Pool,sample.Block,sample.Hits,Hp=f.P.Creature.CurrentHp,MaxHp=f.P.Creature.MaxHp,RemainingPool=f.Pool,Damage=hits.Select(h=>new{h.UnblockedDamage,h.BlockedDamage,h.WasFullyBlocked})});
  }
  // 使用石虫真实头槌动作，确认标记进入原生眩晕流程，而不只改状态字段。
  using(var f=new BloodFixture(ModelDb.Monster<BowlbugRock>().ToMutable())){
   await f.Temp(100);await PowerCmd.Apply<ImbalancedPower>(f.Choice,f.Enemy,1,f.Enemy,null);
   await (Task)AccessTools.Method(typeof(BowlbugRock),"HeadbuttMove").Invoke(f.Enemy.Monster,[new List<Creature>{f.P.Creature}])!;
   Check("temporary blood fully absorbs native stone bowlbug headbutt and stuns",((BowlbugRock)f.Enemy.Monster!).IsOffBalance&&f.Enemy.IsStunned&&f.P.Creature.CurrentHp==80);
  }
 }
 static async Task BloodArtChecks(){
  var viewport=new SubViewport{Size=new(1500,900),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);
  try{var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("171320")});
   foreach(var item in new (int Level,float X,float Scale)[]{(0,200f,.65f),(1,600f,1.15f)}){var card=Up(ModelDb.Card<Pack.BloodSect>().ToMutable(),item.Level);var node=NCard.Create(card)!;host.AddChild(node);node.Position=new(item.X,420);node.Scale=Vector2.One*item.Scale;node.UpdateVisuals(PileType.None,CardPreviewMode.Normal);Check("blood card small large native art "+item.Level,node.GetNode<TextureRect>("%Portrait").Texture!=null&&card.PortraitPath==Pack.BloodSectArt.Path&&card.BetaPortraitPath==card.PortraitPath&&card.AllPortraitPaths.Contains(card.PortraitPath));}
   var power=ModelDb.Power<Pack.TemporaryBloodPower>().ToMutable();Check("temporary blood status icon loads",power.Icon!=null&&power.BigIcon!=null);
   await Wait(.2);await AfterDraw();Check("blood art screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/blood-sect-"+(Minimal?"minimal":"full")+".png")==Error.Ok);
  }finally{viewport.QueueFree();await Wait(.03);}
 }
}
