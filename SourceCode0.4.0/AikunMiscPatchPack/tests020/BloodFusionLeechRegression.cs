using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.ValueProps;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public sealed record FusionLeechHit(int Damage,int Percent,bool Overflow,bool IsCard);
// 仅观察原生伤害钩子，不发伤害、不改变玩家状态，不代替真实吸血结算。
public sealed class FusionLeechAuditPower:PowerModel {
 public override PowerType Type=>PowerType.Buff;
 public override PowerStackType StackType=>PowerStackType.Single;
 public List<FusionLeechHit> Hits{get;private set;}=new();
 protected override void AfterCloned(){base.AfterCloned();Hits=new();}
 public override Task AfterDamageGiven(PlayerChoiceContext choiceContext,Creature? dealer,DamageResult result,ValueProp props,Creature target,CardModel? cardSource){
  if(dealer==Owner&&target.Side!=Owner.Side){var blood=Owner.GetPower<Pack.BloodSectPower>();Hits.Add(new((int)result.UnblockedDamage,blood?.LeechPercent??0,blood?.OverflowEnabled??false,Pack.BloodSectRuntime.IsCardDamageFor(Owner,result)));}
  return Task.CompletedTask;
 }
}
public static partial class Entry {
 static async Task BloodFusionLeechChecks(){
  int start=passes.Count;var evidence=new List<object>();var errors=new List<string>();
  if(Minimal){
   using var f=new BloodFixture();await f.Play(Up(f.Card<Pack.BloodSect>()));await f.Play(f.Card<Pack.BloodSect>());f.P.Creature.SetCurrentHpInternal(98);await f.Play(f.Card<StrikeIronclad>(),f.Enemy);
   Check("fusion leech absent optional mod keeps standalone stacking and pool",f.P.Creature.GetPower<Pack.BloodSectPower>() is {LeechPercent:200,OverflowEnabled:true}&&f.P.Creature.CurrentHp==100&&f.Pool==10);
   Check("fusion leech absent CardFusion skips optional integration",Pack.Settings.Status(Pack.Options.All.Single(o=>o.Key=="fusion_upgrades")).Contains("未装载相关模组"));return;
  }
  var settings=Find("CardFusion.CardFusionSettings");_ = AccessTools.Property(settings,"SynthesizeMode").GetValue(null);var modeField=AccessTools.Field(settings,"_synthesizeMode");bool old=(bool)modeField.GetValue(null)!;
  string[][] sequences=[["B0","S"],["S","B0"],["B1","S"],["S","B1"],["B3","T"],["T","B3"],["B0","B0","S"],["B1","B0","S"],["B0","B1","S"],["B0","S","B1","T"],["B1","S","B0","T"],["S","B5","T","B0"],["B2","S","B5","T"],["S","B0","B1"]];
  try{
   Check("fusion leech per-card ordered mode compatibility installed",Pack.BloodSectFusionPlayFix.Available);
   foreach(bool merge in new[]{false,true})foreach(int hp in new[]{40,98})foreach(var sequence in sequences)await RunCase(sequence,hp,merge,"direct",false,1);
   foreach(bool merge in new[]{false,true})foreach(string path in new[]{"clone","save"}){
    await RunCase(["B0","S","B1","T"],98,merge,path,false,1);
    await RunCase(["S","B3","T","B0"],98,merge,path,false,1);
   }
   foreach(bool merge in new[]{false,true}){
    await RunCase(["S","B1"],98,merge,"existing state",true,1);
    await RunCase(["B1","S","B0","T"],98,merge,"replay",false,2);
    modeField.SetValue(null,merge);using var f=new BloodFixture();var audit=(FusionLeechAuditPower)(await PowerCmd.Apply<FusionLeechAuditPower>(f.Choice,f.P.Creature,1,f.P.Creature,null))!;
    var ordinary=Fuse(ModelDb.Card<StrikeIronclad>().ToMutable(),ModelDb.Card<StrikeIronclad>().ToMutable());f.Combat.AddCard(ordinary,f.P);int before=f.Enemy.CurrentHp;await f.Play(ordinary,f.Enemy);
    Check("fusion leech unrelated fusion keeps mode "+merge,before-f.Enemy.CurrentHp==12&&audit.Hits.Count==(merge?1:2)&&f.P.Creature.GetPower<Pack.BloodSectPower>()==null);
   }
  }finally{modeField.SetValue(null,old);}
  File.WriteAllText(Output+"/blood-fusion-leech-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Errors=errors,Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
  if(errors.Count>0)throw new Exception(string.Join("\n",errors));

  async Task RunCase(string[] tokens,int hp,bool merge,string path,bool prior,int repeats){
   string tag=string.Join("-",tokens)+" hp="+hp+" merge="+merge+" "+path;
   try{
    modeField.SetValue(null,merge);using var f=new BloodFixture();var audit=(FusionLeechAuditPower)(await PowerCmd.Apply<FusionLeechAuditPower>(f.Choice,f.P.Creature,1,f.P.Creature,null))!;
    if(prior)await f.Play(f.Card<Pack.BloodSect>());
    var children=tokens.Select(t=>{
     if(t=="S")return (CardModel)ModelDb.Card<StrikeIronclad>().ToMutable();if(t=="T")return ModelDb.Card<TwinStrike>().ToMutable();
     int level=int.Parse(t[1..]);var blood=ModelDb.Card<Pack.BloodSect>().ToMutable();if(level>1)Scorch(blood);return Up(blood,level);
    }).ToArray();
    var root=Fuse(children);if(path=="clone")root=(CardModel)root.ClonePreservingMutability();if(path=="save")root=Restore(root);f.Combat.AddCard(root,f.P);
    int expectedPct=prior?100:0;bool expectedOverflow=false;int expectedHp=hp,expectedPool=0;
    f.P.Creature.SetCurrentHpInternal(hp);int enemyBefore=f.Enemy.CurrentHp;
    var expectedHits=new List<(int Percent,bool Overflow)>();
    for(int replay=0;replay<repeats;replay++){
     foreach(string token in tokens){
      if(token[0]=='B'){int level=int.Parse(token[1..]);expectedPct+=BloodPct(level);expectedOverflow|=level>=1;}
      else for(int hit=0;hit<(token=="T"?2:1);hit++)expectedHits.Add((expectedPct,expectedOverflow));
     }
     await f.Play(root,f.Enemy);
    }
    var actual=f.P.Creature.GetPower<Pack.BloodSectPower>();
    evidence.Add(new{Case=tag,Tokens=tokens,HpBefore=hp,HpAfter=f.P.Creature.CurrentHp,Pool=f.Pool,Percent=actual?.LeechPercent??0,Overflow=actual?.OverflowEnabled??false,EnemyDamage=enemyBefore-f.Enemy.CurrentHp,Hits=audit.Hits.ToArray()});
    Check(tag+" actual attack hits occur exactly once",audit.Hits.Count==expectedHits.Count&&audit.Hits.All(h=>h.Damage>0)&&enemyBefore-f.Enemy.CurrentHp==audit.Hits.Sum(h=>h.Damage));
    Check(tag+" percent is active only after its material resolves",audit.Hits.Zip(expectedHits).All(p=>p.First.Percent==p.Second.Percent&&p.First.Overflow==p.Second.Overflow));
    Check(tag+" every fused attack has genuine card attribution",audit.Hits.All(h=>h.IsCard));
    foreach(var pair in audit.Hits.Zip(expectedHits)){
     int heal=pair.First.Damage*pair.Second.Percent/100;int room=100-expectedHp;
     expectedHp=Math.Min(100,expectedHp+heal);if(pair.Second.Overflow)expectedPool+=Math.Max(0,heal-room);
    }
    Check(tag+" exact health and temporary pool respect per-hit order",f.P.Creature.CurrentHp==expectedHp&&f.Pool==expectedPool);
    Check(tag+" all blood materials stack once",actual!=null&&actual.LeechPercent==expectedPct&&actual.Amount==expectedPct&&actual.DisplayAmount==expectedPct&&f.P.Creature.Powers.OfType<Pack.BloodSectPower>().Count()==1);
    Check(tag+" final overflow latch comes from upgraded material",actual!.OverflowEnabled==expectedOverflow&&actual.UpgradeLevel==tokens.Where(t=>t[0]=='B').Select(t=>int.Parse(t[1..])).Max());
    Check(tag+" global merge setting remains unchanged",(bool)AccessTools.Property(settings,"SynthesizeMode").GetValue(null)! ==merge);
    // 后置能力只影响之后的攻击，不能追溯前一段；已有状态仍按既有比例结算。
    int beforeHp=f.P.Creature.CurrentHp,beforePool=f.Pool;await f.Play(f.Card<StrikeIronclad>(),f.Enemy);int follow=6*expectedPct/100;
    Check(tag+" later ordinary attack uses accumulated final state",f.P.Creature.CurrentHp==Math.Min(100,beforeHp+follow)&&f.Pool==beforePool+(expectedOverflow?Math.Max(0,follow-(100-beforeHp)):0));
    if(path=="replay"){
     await f.Temp(0);f.P.Creature.SetCurrentHpInternal(40);int ehp=f.Enemy.CurrentHp;var proxy=ModelDb.Power<ForwardedDamageFixturePower>().ToMutable();proxy.ApplyInternal(f.P.Creature,1);
     await proxy.AfterCardPlayed(f.Choice,BloodPlay(f.Card<StrikeIronclad>(),f.Enemy));Check(tag+" forwarded power damage remains excluded",ehp-f.Enemy.CurrentHp==9&&f.P.Creature.CurrentHp==40&&f.Pool==0);await PowerCmd.Remove(proxy);
     await f.Temp(12);await f.Damage(f.P.Creature,12,f.Enemy);Check(tag+" temporary blood absorbs before real health",f.Pool==0&&f.P.Creature.CurrentHp==40);
     await f.Temp(12);await Hook.AfterCombatEnd(f.Run,f.Combat,new CombatRoom(f.Run.Act.AllRegularEncounters.First().ToMutable(),f.Run));Check(tag+" temporary blood clears after combat",f.Pool==0&&f.P.Creature.MaxHp==100);
    }
   }catch(Exception e){errors.Add(tag+": "+e.Message);GD.Print("[BloodFusionLeech] FAIL "+tag+": "+e);}
  }
 }
}
