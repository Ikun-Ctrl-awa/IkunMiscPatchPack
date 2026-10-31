using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.TestSupport;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static Rect2 IntentEnvelope(Control group)=>(Rect2)IntentBoundsMethod(group)[1]!;
 static object?[] IntentBoundsMethod(Control group){object?[] args=[group,null];if(!(bool)AccessTools.Method(typeof(AdaptiveTopBar.MonsterIntents),"Bounds").Invoke(null,args)!)throw new Exception("没有意图范围");return args;}
 static async Task TopBarIntentChecks(){
  int first=passes.Count;var evidence=new List<object>();bool active=Pack.Settings.Startup["better_topbar"];
  MonsterModel? model=Minimal?null:ModelDb.GetById<MonsterModel>(ModelDb.GetId(Find("YuWanCard.Monsters.FerrousWroughtnaut"))).ToMutable();
  using var f=new BloodFixture(model);f.Enemy.SetMaxHpInternal(500);f.Enemy.SetCurrentHpInternal(500);
  var viewport=new SubViewport{Size=new(1920,1080),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Tree.Root.AddChild(viewport);
  var host=new Control{Size=viewport.Size};viewport.AddChild(host);host.AddChild(new ColorRect{Size=new(3000,1800),Color=new("252c32"),MouseFilter=Control.MouseFilterEnum.Ignore});
  bool test=TestMode.IsOn;
  try{
   TestMode.IsOn=false;var creature=NCreature.Create(f.Enemy)!;host.AddChild(creature);TestMode.IsOn=test;
   Check("intent actual creature created with original visuals",creature.Entity.Monster!=null&&creature.Visuals!=null&&creature.IntentContainer!=null);
   if(!Minimal)Check("intent actual steel Guardian from YuWanCard",creature.Entity.Monster!.GetType().FullName=="YuWanCard.Monsters.FerrousWroughtnaut"&&creature.Visuals!.Name.ToString().Contains("Ferrous"));
   var group=creature.IntentContainer!;
   foreach(var intent in new AbstractIntent[]{new MultiAttackIntent(8,2),new BuffIntent()}){
    var node=ResourceLoader.Load<PackedScene>("res://scenes/combat/intent.tscn").Instantiate<NIntent>();group.AddChild(node);node.UpdateIntent(intent,[f.P.Creature],f.Enemy);
   }
   group.Modulate=Colors.White;
   var bar=new Control{Name="TopBar",Size=new(1920,80),MouseFilter=Control.MouseFilterEnum.Ignore};host.AddChild(bar);
   bar.AddChild(new TextureRect{Name="BgImage",Size=new(2560,100),Texture=ResourceLoader.Load<Texture2D>("res://images/atlases/ui_atlas.sprites/top_bar/top_bar.tres"),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter=Control.MouseFilterEnum.Ignore});
   var left=new HBoxContainer{Name="LeftAlignedStuff",Size=new(700,80),MouseFilter=Control.MouseFilterEnum.Ignore};bar.AddChild(left);
   var right=new HBoxContainer{Name="RightAlignedStuff",Size=new(400,80),MouseFilter=Control.MouseFilterEnum.Ignore};bar.AddChild(right);
   for(int i=0;i<30;i++)left.AddChild(new Button{Text="MOD "+i,CustomMinimumSize=new(110,80)});
   right.AddChild(new Button{Text="地图",CustomMinimumSize=new(80,80)});var layout=AdaptiveTopBar.Entry.Attach(bar);
   await Wait(.3);await AfterDraw();
   var sprite=creature.Visuals!.Position;var hitbox=creature.Hitbox.Position;
   foreach(var sample in new[]{(1920,1080,1f),(1280,720,1f),(1280,720,.75f)}){
    Pack.Settings.Startup["better_topbar"]=false;viewport.Size=new(sample.Item1,sample.Item2);host.Scale=Vector2.One*sample.Item3;host.Size=(Vector2)viewport.Size/sample.Item3;bar.Size=new(host.Size.X,80);
    creature.Position=new(host.Size.X*.72f,host.Size.Y*.85f);await Wait(.3);await AfterDraw();
    // 保持真实怪物及原生意图标记，只调战场站位，使其覆盖顶栏下沿。
    var baseline=IntentEnvelope(group);creature.Position+=new Vector2(0,(60-baseline.Position.Y)/sample.Item3);await AfterDraw();baseline=IntentEnvelope(group);
    var original=group.Position;var originalCreature=creature.Position;var originalScale=group.Scale;
    float bottom=bar.GetGlobalTransformWithCanvas().Origin.Y+layout.BackgroundBottom*sample.Item3;
    string tag=$"intent {sample.Item1}x{sample.Item2} scale={sample.Item3}";
    Check(tag+" original intent overlaps wrapped topbar",layout.Rows>1&&baseline.Position.Y<bottom);
    if(sample.Item1==1920)viewport.GetTexture().GetImage().SavePng(Output+"/topbar-intents-before.png");
    Pack.Settings.Startup["better_topbar"]=true;await AfterDraw();var moved=IntentEnvelope(group);
    Check(tag+" entire animation envelope below topbar",moved.Position.Y>=bottom+11.9f&&group.Position.Y>original.Y);
    Check(tag+" only intent moves preserving monster and hitbox",creature.Position.DistanceTo(originalCreature)<.01f&&creature.Visuals.Position.DistanceTo(sprite)<.01f&&creature.Hitbox.Position.DistanceTo(hitbox)<.01f&&group.Scale==originalScale);
    Check(tag+" horizontal alignment preserved",Math.Abs(moved.Position.X-baseline.Position.X)<.1f);
    var fixedPosition=group.Position;float low=float.MaxValue,high=float.MinValue;bool stable=true;
    for(int i=0;i<32;i++){
     await AfterDraw();stable&=group.Position.DistanceTo(fixedPosition)<.1f&&IntentEnvelope(group).Position.Y>=bottom+11.9f;
     float y=group.GetChild<NIntent>(0).GetNode<Control>("%IntentHolder").Position.Y;low=Math.Min(low,y);high=Math.Max(high,y);
    }
    Check(tag+" repeated paint never drifts or overlaps",stable);
    Check(tag+" native intention bob animation retained",high-low>.1f);
    if(sample.Item1==1920)viewport.GetTexture().GetImage().SavePng(Output+"/topbar-intents-after.png");
    var values=group.GetChildren().OfType<NIntent>().Select(n=>n.GetNode<Godot.RichTextLabel>("%Value").Text).ToArray();
    Check(tag+" original multi-attack label and second intent intact",group.GetChildCount()==2&&values[0].Contains("8")&&values[0].Contains("2"));
    bar.Hide();await AfterDraw();Check(tag+" hidden topbar restores natural intent",group.Position.DistanceTo(original)<.1f);bar.Show();await AfterDraw();
    Pack.Settings.Startup["better_topbar"]=false;await AfterDraw();Check(tag+" switch off restores natural intent",group.Position.DistanceTo(original)<.1f);
    Pack.Settings.Startup["better_topbar"]=true;creature.Position+=new Vector2(0,600/sample.Item3);await AfterDraw();
    Check(tag+" ordinary unobstructed intent stays at native offset",group.Position.DistanceTo(original)<.1f);
    evidence.Add(new{Width=sample.Item1,Height=sample.Item2,Scale=sample.Item3,Rows=layout.Rows,TopBarBottom=bottom,Before=baseline.Position.Y,After=moved.Position.Y});
   }
   // 原生体型重算会覆盖意图标记位置；应采纳新位置而非累加旧的顶栏偏移。
   Pack.Settings.Startup["better_topbar"]=false;creature.Position=new(900,350);host.Scale=Vector2.One;host.Size=viewport.Size;bar.Size=new(host.Size.X,80);await Wait(.2);
   var old=creature.Visuals.Scale;creature.Visuals.Scale=old*1.4f;AccessTools.Method(typeof(NCreature),"UpdateBounds",[typeof(Node)]).Invoke(creature,[creature.Visuals]);await AfterDraw();var resized=group.Position;
   Pack.Settings.Startup["better_topbar"]=true;await AfterDraw();Pack.Settings.Startup["better_topbar"]=false;await AfterDraw();
   Check("intent native size-change marker restored without old offset",group.Position.DistanceTo(resized)<.1f);
   foreach(var c in left.GetChildren().OfType<Control>())c.Hide();await Wait(.2);creature.Position=new(900,350);var natural=IntentEnvelope(group);creature.Position+=new Vector2(0,50-natural.Position.Y);await AfterDraw();
   var single=group.Position;Pack.Settings.Startup["better_topbar"]=true;await AfterDraw();
   Check("intent single-row topbar also protects very tall monsters",layout.Rows==1&&IntentEnvelope(group).Position.Y>=111.9f&&group.Position.Y>single.Y);
   bar.Position=new(0,-500);await AfterDraw();Check("intent animated-away topbar restores natural marker",group.Position.DistanceTo(single)<.1f);
  }finally{
   TestMode.IsOn=test;Pack.Settings.Startup["better_topbar"]=active;viewport.QueueFree();await Wait(.1);
   File.WriteAllText(Output+"/topbar-intents-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Checks=passes.Skip(first).ToArray(),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
  }
 }
}
