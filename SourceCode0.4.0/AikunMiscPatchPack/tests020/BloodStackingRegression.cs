using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.ValueProps;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static async Task<Pack.BloodSectPower> ApplyBloodState(BloodFixture f,int level){
  var power=f.P.Creature.GetPower<Pack.BloodSectPower>()??(Pack.BloodSectPower)ModelDb.Power<Pack.BloodSectPower>().ToMutable();power.RegisterUpgrade(level);
  await PowerCmd.Apply(f.Choice,power,f.P.Creature,100+20*Math.Max(0,level-1),f.P.Creature,null);return power;
 }
 static async Task BloodStackingChecks(){
  int start=passes.Count;var evidence=new List<object>();
  var viewport=new SubViewport{Size=new(1250,400),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);
  var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("191520")});
  bool corner=Pack.Settings.Startup["power_corner_labels"];
  try{
   int[][] sequences=[[0],[0,0],[0,0,0],[0,1],[1,0],[1,1],[0,1,0],[1,0,0],[2,5],[5,2],[0,2,1,5]];
   int column=0;
   foreach(var sequence in sequences){
    using var f=new BloodFixture(multiplayer:true);int total=0;bool overflow=false;Pack.BloodSectPower? power=null;NPower? node=null;
    var ordinaryPower=await PowerCmd.Apply<LethalityPower>(f.Choice,f.Other.Creature,3,f.Other.Creature,null);
    var ordinary=NPower.Create(ordinaryPower!);host.AddChild(ordinary);ordinary.Position=new(1100,60);await Wait(.02);
    string ordinaryBefore=JsonSerializer.Serialize(PowerLabelState(PowerBaseLabel(ordinary)));
    for(int i=0;i<sequence.Length;i++){
     int level=sequence[i];string tag="blood stacking "+string.Join("-",sequence)+" play="+i;
     if(Minimal&&level>=2)power=await ApplyBloodState(f,level);
     else{var card=f.Card<Pack.BloodSect>();if(level>=2)Scorch(card);Up(card,level);await f.Play(card);power=f.P.Creature.GetPower<Pack.BloodSectPower>()!;}
     total+=100+20*Math.Max(0,level-1);overflow|=level>=1;
     Check(tag+" cumulative percent uses every play",power!.LeechPercent==total&&power.Amount==total&&power.StackType==PowerStackType.Counter);
     Check(tag+" single status owns independent overflow latch",f.P.Creature.Powers.OfType<Pack.BloodSectPower>().Count()==1&&power.OverflowEnabled==overflow&&power.UpgradeLevel==sequence.Take(i+1).Max());
     string text=((StringVar)power.DynamicVars["EffectText"]).StringValue;
     Check(tag+" hover description shows total and overflow",text.Contains(total+"%")&&text.Contains("临时血量")==overflow);
     if(node==null){node=NPower.Create(power);host.AddChild(node);node.Position=new(80+column*180,140);node.Scale=Vector2.One*3;await Wait(.03);}
     // 检查原生 DisplayAmountChanged 自动刷新，不手动调用 RefreshAmount 掩盖漏更新。
     await Wait(.02);var label=PowerBaseLabel(node);
     GD.Print("[Aikun020Probe] stacking label "+tag+" "+JsonSerializer.Serialize(PowerLabelState(label)));
     Check(tag+" live native icon shows cumulative number without percent",label.Text==total.ToString()&&power.DisplayAmount==total);
     ordinaryPower!.SetAmount(total);await Wait(.02);
     Check(tag+" numeric badge follows native Lethality typography and layout",JsonSerializer.Serialize(PowerLabelState(label))==JsonSerializer.Serialize(PowerLabelState(PowerBaseLabel(ordinary))));
     await f.Temp(0);f.P.Creature.SetCurrentHpInternal(10);await f.Damage(f.Enemy,5);int heal=5*total/100;
     Check(tag+" exact cumulative healing once per hit",f.P.Creature.CurrentHp==10+heal&&f.Pool==0);
     f.P.Creature.SetCurrentHpInternal(98);await f.Damage(f.Enemy,5);
     Check(tag+" overflow is independent of play order",f.P.Creature.CurrentHp==100&&f.Pool==(overflow?heal-2:0));
     await f.Temp(0);
    }
    // 多次打出后仍只吸取卡牌本体的伤害，转传卡牌的模组能力伤害不计入。
    var proxy=ModelDb.Power<ForwardedDamageFixturePower>().ToMutable();proxy.ApplyInternal(f.P.Creature,1);f.P.Creature.SetCurrentHpInternal(40);int enemyHp=f.Enemy.CurrentHp;
    await proxy.AfterCardPlayed(f.Choice,BloodPlay(f.Card<StrikeIronclad>(),f.Enemy));
    Check("blood stacking forwarded proc excluded "+string.Join("-",sequence),enemyHp-f.Enemy.CurrentHp==9&&f.P.Creature.CurrentHp==40&&f.Pool==0);await PowerCmd.Remove(proxy);
    await f.Play(f.Card<DefendIronclad>());Check("blood stacking own direct block still denied "+total,f.P.Creature.Block==0);
    var potion=ModelDb.Potion<BlockPotion>().ToMutable();await (Task)AccessTools.Method(typeof(BlockPotion),"OnUse").Invoke(potion,[f.Choice,f.P.Creature])!;
    Check("blood stacking potion block still allowed "+total,f.P.Creature.Block==12);f.P.Creature.LoseBlockInternal(9999);
    ordinaryPower!.SetAmount(3);AccessTools.Method(typeof(NPower),"RefreshAmount").Invoke(ordinary,null);
    Check("blood stacking ordinary power label unchanged "+total,JsonSerializer.Serialize(PowerLabelState(PowerBaseLabel(ordinary)))==ordinaryBefore&&PowerBaseLabel(ordinary).Text=="3");
    foreach(bool enabled in new[]{false,true}){Pack.Settings.Startup["power_corner_labels"]=enabled;AccessTools.Method(typeof(NPower),"RefreshAmount").Invoke(node,null);Check("blood stacking label independent of optional corner fix "+total+enabled,PowerBaseLabel(node!).Text==total.ToString());}
    evidence.Add(new{Sequence=sequence,Percent=total,Overflow=overflow,MinimalHighLevelsUseStateFixture=Minimal&&sequence.Any(n=>n>=2),Label=PowerLabelState(PowerBaseLabel(node!))});
    // 三个实际能力图标并排保留截图，其他样例及时释放，避免不同战斗节点订阅残留。
    if(sequence.SequenceEqual(new[]{0,0})||sequence.SequenceEqual(new[]{1,0})||sequence.SequenceEqual(new[]{0,2,1,5})){column++;}
    else node!.QueueFree();ordinary.QueueFree();await Wait(.02);
   }
   using(var f=new BloodFixture()){
    var card=Up(f.Card<Pack.BloodSect>());await f.Play(card);await f.Play(card);
    Check("blood stacking same upgraded card replay contributes twice",f.P.Creature.GetPower<Pack.BloodSectPower>() is {LeechPercent:200,OverflowEnabled:true});
    // 真实卡牌多段伤害逐段取整，临时血池只在升级牌已生效后产生。
    f.P.Creature.SetCurrentHpInternal(90);await f.Play(f.Card<TwinStrike>(),f.Enemy);
    Check("blood stacking real multi hit card creates exact overflow",f.P.Creature.CurrentHp==100&&f.Pool==10);
    await f.Damage(f.P.Creature,10,f.Enemy);Check("blood stacking generated pool absorbs before real hp",f.Pool==0&&f.P.Creature.CurrentHp==100);
    await f.Temp(20);await Hook.AfterCombatEnd(f.Run,f.Combat,new CombatRoom(f.Run.Act.AllRegularEncounters.First().ToMutable(),f.Run));Check("blood stacking temporary pool clears after combat",f.Pool==0&&f.P.Creature.MaxHp==100);
   }
   using(var f=new BloodFixture())Check("blood stacking new combat starts without prior state",f.P.Creature.GetPower<Pack.BloodSectPower>()==null&&f.Pool==0);
   await Wait(.2);await AfterDraw();Check("blood stacking percentage icon screenshot saved",viewport.GetTexture().GetImage().SavePng(Output+"/blood-stacking-icons.png")==Error.Ok);
   File.WriteAllText(Output+"/blood-stacking-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Failed=0,Checks=passes.Skip(start),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
  }finally{Pack.Settings.Startup["power_corner_labels"]=corner;viewport.QueueFree();await Wait(.03);}
 }
}
