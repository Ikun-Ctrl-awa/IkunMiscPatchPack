using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib;
using STS2RitsuLib.Scaffolding.Content;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe {
 // 模拟其他模组通过异步静态工具发伤害、且没有传 cardSource 的技能牌。
 public sealed class CardDamageHelperFixture:ModCardTemplate {
  public CardDamageHelperFixture():base(0,CardType.Skill,CardRarity.Common,TargetType.AnyEnemy){}
  public override CardPoolModel Pool=>ModelDb.CardPool<ColorlessCardPool>();
  public override bool CanBeGeneratedInCombat=>false;
  public override bool CanBeGeneratedByModifiers=>false;
  protected override Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay)=>DamageHelpers.Indirect.Dispatch(choiceContext,Owner.Creature,cardPlay.Target!);
 }
 // 故意把触发卡传给状态伤害命令，验证不能仅凭 cardSource 非空吸血。
 public sealed class ForwardedDamageFixturePower:PowerModel {
  public override PowerType Type=>PowerType.Buff;
  public override PowerStackType StackType=>PowerStackType.Single;
  public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext,CardPlay cardPlay){
   await Task.Yield();await CreatureCmd.Damage(choiceContext,cardPlay.Target!,9,ValueProp.Move,Owner,cardPlay.Card,cardPlay);
  }
  public async Task<AttackCommand> BuildDeferred(CardModel card,Creature target){
   await Task.Yield();return DamageCmd.Attack(9).FromCard(card,null).Targeting(target).WithNoAttackerAnim();
  }
 }
 public static partial class Entry {
  static async Task BloodCardDamageChecks(){
   int first=passes.Count;var evidence=new List<object>();
   Check("card only blood runtime installed",Pack.RuntimeLoader.States.GetValueOrDefault("blood_sect")=="已应用。");
   string id=ModelDb.GetId<CardDamageHelperFixture>().Entry;LocManager.Instance.GetTable("cards").MergeWith(new(){[id+".title"]="伤害来源测试牌",[id+".description"]="测试用异步伤害。"});
   foreach(int n in new[]{0,1,3}){
    using var f=new BloodFixture(multiplayer:true);var blood=await ApplyBloodState(f,n);
    int pct=100+20*Math.Max(0,n-1);int Heal(int damage)=>damage*pct/100;string label="card only blood n="+n;
    f.P.Creature.SetCurrentHpInternal(30);await f.Play(f.Card<StrikeIronclad>(),f.Enemy);Check(label+" ordinary strike leeches",f.P.Creature.CurrentHp==30+Heal(6));
    f.P.Creature.SetCurrentHpInternal(30);await f.Play(f.Card<TwinStrike>(),f.Enemy);Check(label+" multi hit leeches per actual hit",f.P.Creature.CurrentHp==30+2*Heal(5));
    var second=f.Combat.CreateCreature(ModelDb.Monster<CalcifiedCultist>().ToMutable(),CombatSide.Enemy,"card-damage-second");f.Combat.AddCreature(second);second.SetMaxHpInternal(1000);second.SetCurrentHpInternal(1000);
    f.P.Creature.SetCurrentHpInternal(30);var area=f.Card<DramaticEntrance>();int areaDamage=area.DynamicVars.Damage.IntValue;await f.Play(area);Check(label+" area card counts each enemy",f.P.Creature.CurrentHp==30+2*Heal(areaDamage));
    f.P.Creature.SetCurrentHpInternal(30);await f.Play(f.Card<CardDamageHelperFixture>(),f.Enemy);Check(label+" async mod skill helper with omitted card source leeches",f.P.Creature.CurrentHp==30+Heal(7));
    f.P.Creature.SetCurrentHpInternal(30);var noSource=(await CreatureCmd.Damage(f.Choice,f.Enemy,10,ValueProp.Move,f.P.Creature,null,null)).Single();Check(label+" bare dealer damage has no card attribution",f.P.Creature.CurrentHp==30&&!Pack.BloodSectRuntime.IsCardDamageFor(f.P.Creature,noSource));
    var card=f.Card<StrikeIronclad>(f.Other);f.P.Creature.SetCurrentHpInternal(30);await f.Play(card,f.Enemy);Check(label+" other player card does not leech for owner",f.P.Creature.CurrentHp==30);
    var own=f.Card<StrikeIronclad>();f.P.Creature.SetCurrentHpInternal(30);await CreatureCmd.Damage(f.Choice,f.Other.Creature,4,ValueProp.Move,f.P.Creature,own,null);Check(label+" own card against ally never leeches",f.P.Creature.CurrentHp==30);
    f.Enemy.GainBlockInternal(99);await f.Play(f.Card<StrikeIronclad>(),f.Enemy);Check(label+" blocked card damage never leeches",f.P.Creature.CurrentHp==30);f.Enemy.LoseBlockInternal(9999);
    var tiny=f.Combat.CreateCreature(ModelDb.Monster<CalcifiedCultist>().ToMutable(),CombatSide.Enemy,"card-overkill");f.Combat.AddCreature(tiny);tiny.SetMaxHpInternal(3);tiny.SetCurrentHpInternal(3);await f.Play(f.Card<StrikeIronclad>(),tiny);Check(label+" overkill remains excluded",f.P.Creature.CurrentHp==30+Heal(3));
    await PowerCmd.Apply<StrengthPower>(f.Choice,f.P.Creature,2,f.P.Creature,null);f.P.Creature.SetCurrentHpInternal(30);await f.Play(f.Card<StrikeIronclad>(),f.Enemy);Check(label+" modifiers to card damage remain included",f.P.Creature.CurrentHp==30+Heal(8));await PowerCmd.Remove<StrengthPower>(f.P.Creature);
    f.P.Creature.SetCurrentHpInternal(98);await f.Play(f.Card<StrikeIronclad>(),f.Enemy);Check(label+" qualifying card overflow follows upgrade",f.P.Creature.CurrentHp==100&&f.Pool==(n>=1?Heal(6)-2:0));await f.Temp(0);
    evidence.Add(new{Level=n,LeechPercent=pct});
   }
   // 使用原生回调与真实出牌触发追加伤害，验证这些伤害确实发生而没有吸血。
   using(var f=new BloodFixture()){
    var blood=await ApplyBloodState(f,1);
    async Task NoLeech(string label,Func<Task> action,int damage){f.P.Creature.SetCurrentHpInternal(40);int hp=f.Enemy.CurrentHp;await action();Check(label+" actually deals damage",hp-f.Enemy.CurrentHp==damage);Check(label+" gives no healing or temporary blood",f.P.Creature.CurrentHp==40&&f.Pool==0);evidence.Add(new{Effect=label,Damage=hp-f.Enemy.CurrentHp,Hp=f.P.Creature.CurrentHp});}
    var juggernaut=await PowerCmd.Apply<JuggernautPower>(f.Choice,f.P.Creature,4,f.P.Creature,null);
    await NoLeech("native Juggernaut",async()=>{await CreatureCmd.GainBlock(f.P.Creature,5,ValueProp.Unpowered,null);},4);f.P.Creature.LoseBlockInternal(9999);await PowerCmd.Remove(juggernaut);
    var serpent=await PowerCmd.Apply<SerpentFormPower>(f.Choice,f.P.Creature,3,f.P.Creature,null);
    await NoLeech("native SerpentForm skill trigger",()=>f.Play(f.Card<DefendIronclad>()),3);
    f.P.Creature.SetCurrentHpInternal(40);int ehp=f.Enemy.CurrentHp;await f.Play(f.Card<StrikeIronclad>(),f.Enemy);Check("native SerpentForm extra damage excluded beside attack",ehp-f.Enemy.CurrentHp==9&&f.P.Creature.CurrentHp==46);await PowerCmd.Remove(serpent);
    var flesh=await PowerCmd.Apply<SleightOfFleshPower>(f.Choice,f.P.Creature,4,f.P.Creature,null);
    await NoLeech("native SleightOfFlesh debuff trigger",async()=>{await PowerCmd.Apply<WeakPower>(f.Choice,f.Enemy,1,f.P.Creature,f.Card<Bash>());},4);await PowerCmd.Remove(flesh);
    var letter=(LetterOpener)ModelDb.Relic<LetterOpener>().ToMutable();f.P.AddRelicInternal(letter,silent:true);await letter.BeforeCombatStart();
    await NoLeech("native LetterOpener third skill",async()=>{for(int i=0;i<3;i++)await f.Play(f.Card<DefendIronclad>());},5);
    var kusa=(Kusarigama)ModelDb.Relic<Kusarigama>().ToMutable();f.P.AddRelicInternal(kusa,silent:true);await kusa.BeforeCombatStart();f.P.Creature.SetCurrentHpInternal(40);ehp=f.Enemy.CurrentHp;for(int i=0;i<3;i++)await f.Play(f.Card<StrikeIronclad>(),f.Enemy);Check("native Kusarigama only three cards leech and extra six excluded",ehp-f.Enemy.CurrentHp==24&&f.P.Creature.CurrentHp==58);
    var poison=await PowerCmd.Apply<PoisonPower>(f.Choice,f.Enemy,5,f.P.Creature,f.Card<Bash>());await NoLeech("native poison applied by own card",()=>poison!.Trigger(),5);await PowerCmd.Remove(poison);
    // 非虚异步 BuildDeferred 不在实际执行时的调用栈上，仍必须保留发起者是状态。
    var proxy=(ForwardedDamageFixturePower)ModelDb.Power<ForwardedDamageFixturePower>().ToMutable();proxy.ApplyInternal(f.P.Creature,1);var strike=f.Card<StrikeIronclad>();
    await NoLeech("mod power forwarding triggering card",()=>proxy.AfterCardPlayed(f.Choice,BloodPlay(strike,f.Enemy)),9);
    var pending=await proxy.BuildDeferred(strike,f.Enemy);await NoLeech("mod power defers attack command carrying a card",async()=>{await pending.Execute(f.Choice);},9);
    f.P.Creature.SetCurrentHpInternal(100);ehp=f.Enemy.CurrentHp;await proxy.AfterCardPlayed(f.Choice,BloodPlay(strike,f.Enemy));Check("excluded effect at full health never creates temporary blood",ehp-f.Enemy.CurrentHp==9&&f.Pool==0);await PowerCmd.Remove(proxy);
    f.P.Creature.SetCurrentHpInternal(40);await f.Play(f.Card<StrikeIronclad>(),f.Enemy);Check("effect source scope restores card leech afterward",f.P.Creature.CurrentHp==46);
    await PowerCmd.Apply<MonarchsGazePower>(f.Choice,f.P.Creature,1,f.P.Creature,null);f.P.Creature.SetCurrentHpInternal(40);await f.Play(f.Card<StrikeIronclad>(),f.Enemy);Check("non damaging power modifiers do not suppress genuine card leech",f.P.Creature.CurrentHp==46);
   }
   if(!Minimal){
    using var f=new BloodFixture();var blood=await ApplyBloodState(f,1);
    var fused=Fuse(ModelDb.Card<StrikeIronclad>().ToMutable(),ModelDb.Card<TwinStrike>().ToMutable());f.Combat.AddCard(fused,f.P);f.P.Creature.SetCurrentHpInternal(40);await f.Play(fused,f.Enemy);Check("CardFusion child attacks attributed to real card owner",f.P.Creature.CurrentHp==56);
   }
   await BloodWeaponArtChecks();
   File.WriteAllText(Output+"/blood-card-damage-art-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Failed=0,Checks=passes.Skip(first),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
  }
  static async Task BloodWeaponArtChecks(){
   using var stream=typeof(Pack.Entry).Assembly.GetManifestResourceStream("cardart.blood_sect.png")!;using var bytes=new MemoryStream();stream.CopyTo(bytes);
   Check("blood weapon embeds exact generated PNG",Convert.ToHexString(SHA256.HashData(bytes.ToArray()))=="6A40FC93BBAE42BFA652DD0BB06797126F558C83C896DCAFCD4E4BAB5B8EF8E2");
   using var expected=new Image();Check("blood weapon PNG decodes",expected.LoadPngFromBuffer(bytes.ToArray())==Error.Ok);expected.Convert(Image.Format.Rgba8);
   var upgraded=Up(ModelDb.Card<Pack.BloodSect>().ToMutable());
   foreach(var card in new CardModel[]{ModelDb.Card<Pack.BloodSect>(),upgraded,Restore(upgraded),(CardModel)upgraded.MutableClone()}){
    string[] paths=[card.PortraitPath,card.BetaPortraitPath,(string)AccessTools.Property(typeof(CardModel),"PortraitPngPath").GetValue(card)!,(string)AccessTools.Property(typeof(CardModel),"BetaPortraitPngPath").GetValue(card)!];
    Check("blood weapon all normal beta small large paths agree "+card.IsMutable+card.CurrentUpgradeLevel,paths.All(p=>p==Pack.BloodSectArt.Path)&&card.HasPortrait&&card.HasBetaPortrait&&card.AllPortraitPaths.SequenceEqual([Pack.BloodSectArt.Path]));
    var texture=ResourceLoader.Load<Texture2D>(paths[0]);using var actual=texture.GetImage();actual.Convert(Image.Format.Rgba8);Check("blood weapon loaded pixels match source "+card.IsMutable+card.CurrentUpgradeLevel,actual.GetSize()==expected.GetSize()&&actual.GetData().SequenceEqual(expected.GetData())&&card.Portrait==Pack.BloodSectArt.Portrait);
   }
   await BloodArtChecks();
  }
 }
}
namespace Aikun020Probe.DamageHelpers {
 public static class Indirect {
  public static async Task Dispatch(PlayerChoiceContext choice,Creature dealer,Creature target){await Task.Yield();await CreatureCmd.Damage(choice,target,7,ValueProp.Unpowered,dealer);}
 }
}
