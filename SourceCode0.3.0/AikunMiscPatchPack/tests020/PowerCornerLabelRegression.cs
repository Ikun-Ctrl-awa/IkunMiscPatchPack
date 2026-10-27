using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.addons.mega_text;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static MegaLabel PowerBaseLabel(NPower node)=>(MegaLabel)AccessTools.Field(typeof(NPower),"_amountLabel").GetValue(node)!;
 static MegaLabel[] PowerExtraLabels(NPower node)=>node.GetChildren().OfType<MegaLabel>().Where(c=>c.Name.ToString().StartsWith("RitsuExtraPowerCornerSlot_",StringComparison.Ordinal)).ToArray();
 static object PowerLabelState(MegaLabel label)=>new{label.Text,Font=label.GetThemeFontSize("font_size"),label.MinFontSize,label.MaxFontSize,Width=label.Size.X,Height=label.Size.Y,X=label.Position.X,Y=label.Position.Y};
 static async Task PowerCornerLabelChecks(){
  int first=passes.Count;bool oldMode=TestMode.IsOn;var evidence=new List<object>();var nodes=new List<NPower>();
  var listeners=AccessTools.Field(typeof(CombatStateTracker),"CombatStateChanged");var frontend=listeners.GetValue(CombatManager.Instance.StateTracker);
  var viewport=new SubViewport{Size=new(1100,600),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);
  var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=host.Size,Color=new("20262b"),MouseFilter=Control.MouseFilterEnum.Ignore});
  try{
   listeners.SetValue(CombatManager.Instance.StateTracker,null);TestMode.IsOn=true;
   var p=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var state=RunState.CreateForTest([p],seed:"POWER-CORNER-LABEL");RunManager.Instance.SetUpTest(state,new NetSingleplayerGameService());
   var combat=new CombatState(runState:state);combat.AddPlayer(p);var enemy=combat.CreateCreature(ModelDb.Monster<CalcifiedCultist>().ToMutable(),CombatSide.Enemy,"label-target");combat.AddCreature(enemy);CombatManager.Instance.SetUpCombat(combat);
   var turn=AccessTools.Field(typeof(CombatManager),"_turnState").GetValue(CombatManager.Instance)!;AccessTools.Property(turn.GetType(),"IsInProgress").SetValue(turn,true);
   var strength=ModelDb.Power<StrengthPower>().ToMutable();strength.ApplyInternal(p.Creature,3);var ordinary=NPower.Create(strength);ordinary.Position=new(100,100);host.AddChild(ordinary);nodes.Add(ordinary);await Wait(.15);
   string ordinaryBefore=JsonSerializer.Serialize(PowerLabelState(PowerBaseLabel(ordinary)));
   Check("power badge ordinary counter remains small",PowerBaseLabel(ordinary).GetThemeFontSize("font_size")<=32&&PowerBaseLabel(ordinary).Text=="3");
   Check("power badge no slot created for ordinary power",PowerExtraLabels(ordinary).Length==0);
   if(Minimal){Check("power badge safe without Medo",Pack.RuntimeLoader.States["power_corner_labels"]=="已应用。");return;}
   var power=ModelDb.GetById<PowerModel>(ModelDb.GetId(Find("Medo.Scripts.Powers.FlyIntoAPinkTomorrowPower"))).ToMutable();power.ApplyInternal(p.Creature,2);
   NPower? fixedNode=null;
   foreach(bool enabled in new[]{false,true}){
    Pack.Settings.Startup["power_corner_labels"]=enabled;
    var node=NPower.Create(power);node.Position=enabled?new(700,200):new(260,200);node.Scale=Vector2.One*2;host.AddChild(node);nodes.Add(node);await Wait(.2);await AfterDraw();
    var native=PowerBaseLabel(node);var extra=PowerExtraLabels(node).Single();var info=new{Enabled=enabled,Native=PowerLabelState(native),Extra=PowerLabelState(extra)};evidence.Add(info);GD.Print("[Aikun020Probe] power label geometry "+JsonSerializer.Serialize(info));
    Check("power badge retains both counters enabled="+enabled,native.Text=="2"&&extra.Text=="2"&&power.Amount==2);
    if(!enabled)Check("power badge reproduces giant number when disabled",extra.GetThemeFontSize("font_size")>native.GetThemeFontSize("font_size")*2||extra.Size.Y>native.Size.Y*2);
    else{
     fixedNode=node;
     Check("power badge fixed font follows native counter",extra.GetThemeFontSize("font_size")<=native.GetThemeFontSize("font_size")&&extra.MaxFontSize<=native.GetThemeFontSize("font_size"));
     Check("power badge fixed bounds stay near icon",extra.Size.Y<44&&extra.Size.X<=44.1f&&extra.Position.Y>=-1&&extra.Position.Y<10);
     Check("power badge native total stays at bottom right",native.GetGlobalRect()!=extra.GetGlobalRect()&&native.Text=="2");
    }
   }
   await AfterDraw();viewport.GetTexture().GetImage().SavePng(Output+"/power-corner-label-before-after.png");
   // 使用真实战斗历史推进剩余次数，逐次对照原效果的增伤规则。
   var attack=combat.CreateCard(ModelDb.Card<StrikeIronclad>(),p);
   for(int count=0;count<=3;count++){
    if(count>0)CombatManager.Instance.History.CardPlayStarted(combat,new CardPlay{Card=attack,Player=p,Target=enemy,ResultPile=PileType.Discard,Resources=default,IsAutoPlay=false,PlayIndex=0,PlayCount=1});
    AccessTools.Method(typeof(NPower),"RefreshAmount").Invoke(fixedNode,null);await Wait(.03);
    var extra=PowerExtraLabels(fixedNode!).Single();decimal multiplier=power.ModifyDamageMultiplicative(enemy,6,ValueProp.Move,p.Creature,attack,null);
    Check("power badge remaining count follows history "+count,extra.Text==Math.Max(0,2-count).ToString());
    Pack.Settings.Startup["power_corner_labels"]=false;var nativeMultiplier=power.ModifyDamageMultiplicative(enemy,6,ValueProp.Move,p.Creature,attack,null);Pack.Settings.Startup["power_corner_labels"]=true;
    Check("power badge never changes damage rule "+count,multiplier==nativeMultiplier);
    Check("power badge stays small after refresh "+count,extra.GetThemeFontSize("font_size")<=32&&extra.Size.Y<44);
   }
   foreach(float scale in new[]{.75f,1f,1.5f}){
    fixedNode!.Scale=Vector2.One*scale;power.SetAmount(123);await Wait(.05);var extra=PowerExtraLabels(fixedNode).Single();
    Check("power badge multi digit and scale stays inside icon "+scale,extra.Text=="120"&&extra.Size.Y<44&&extra.MaxFontSize<=32);
   }
   AccessTools.Method(typeof(NPower),"RefreshAmount").Invoke(ordinary,null);
   Check("power badge ordinary label geometry unchanged",JsonSerializer.Serialize(PowerLabelState(PowerBaseLabel(ordinary)))==ordinaryBefore);
   File.WriteAllText(Output+"/power-corner-label-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Failed=0,Checks=passes.Skip(first),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
  }finally{
   Pack.Settings.Startup["power_corner_labels"]=true;viewport.QueueFree();await Wait(.05);
   try{if(RunManager.Instance.DebugOnlyGetState()!=null){CombatManager.Instance.Reset(false);RunManager.Instance.CleanUp();}}
   finally{TestMode.IsOn=oldMode;listeners.SetValue(CombatManager.Instance.StateTracker,frontend);}
  }
 }
}
