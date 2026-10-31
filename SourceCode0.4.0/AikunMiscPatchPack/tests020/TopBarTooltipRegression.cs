using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static HoverTip TooltipText(string id,string title,string description){object tip=new HoverTip{Id=id};AccessTools.Property(typeof(HoverTip),nameof(HoverTip.Title)).SetValue(tip,title);AccessTools.Property(typeof(HoverTip),nameof(HoverTip.Description)).SetValue(tip,description);return (HoverTip)tip;}
 static Rect2 TipVisualBounds(NHoverTipSet tip){
  var rects=tip.GetChildren().OfType<Control>().Where(c=>c.IsVisibleInTree()&&c.GetChildCount()>0).Select(c=>c.GetGlobalRect()).ToArray();
  return rects.Aggregate((a,b)=>a.Merge(b));
 }
 static async Task TopBarTooltipChecks(){
  var start=passes.Count;
  var viewport=new SubViewport{Size=new(1920,1080),RenderTargetUpdateMode=SubViewport.UpdateMode.Always,HandleInputLocally=true};Tree.Root.AddChild(viewport);
  var host=new Control{Size=viewport.Size};viewport.AddChild(host);
  host.AddChild(new ColorRect{Size=new(3000,1800),Color=new("20262b"),MouseFilter=Control.MouseFilterEnum.Ignore});
  var bar=new Control{Name="TopBar",Size=new(1920,80),MouseFilter=Control.MouseFilterEnum.Ignore};host.AddChild(bar);
  bar.AddChild(new TextureRect{Name="BgImage",Size=new(1920,100),Texture=ResourceLoader.Load<Texture2D>("res://images/atlases/ui_atlas.sprites/top_bar/top_bar.tres"),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter=Control.MouseFilterEnum.Ignore});
  var left=new HBoxContainer{Name="LeftAlignedStuff",Size=new(700,80),MouseFilter=Control.MouseFilterEnum.Ignore};bar.AddChild(left);
  var right=new HBoxContainer{Name="RightAlignedStuff",Size=new(600,80),MouseFilter=Control.MouseFilterEnum.Ignore};bar.AddChild(right);
  for(int i=0;i<28;i++)left.AddChild(new Button{Text="药水 "+i,CustomMinimumSize=new(80,80)});
  for(int i=0;i<7;i++)right.AddChild(new Button{Text="MOD "+i,CustomMinimumSize=new(80,80)});
  var owner=new Button{Text="遭遇",CustomMinimumSize=new(80,80)};right.AddChild(owner);
  var layout=AdaptiveTopBar.Entry.Attach(bar);
  var content=TooltipText("topbar-edge","遭遇","本幕已进入的遭遇数。达到指定次数遭遇即可解锁首领。\n\n打开 Gravity 外观设置");
  try{
   if(!Minimal){
   var state=RunState.CreateForTest(seed:"TOPBAR-GRAVITY-TIP");state.Act.GenerateRooms(state.Rng.UpFront,state.UnlockState);state.Map=state.Act.CreateMap(state,false);
   AccessTools.Method(Find("Gravity.GravityRunSettings"),"Initialize").Invoke(null,[state]);
   content=(HoverTip)AccessTools.Method(Find("Gravity.GravityTopBarProgress"),"Tooltip").Invoke(null,[state])!;
   Check("topbar tooltip uses actual Gravity encounter description",content.Id=="Gravity.Encounters"&&content.Description.Length>0);
  }
   foreach(var test in new[]{(2560,1440,1f),(1920,1080,1f),(1280,720,1f),(1280,720,.75f)}){
    var label=test.Item1+"x"+test.Item2+" scale="+test.Item3;
    viewport.Size=new(test.Item1,test.Item2);host.Scale=new(test.Item3,test.Item3);host.Size=(Vector2)viewport.Size/test.Item3;bar.Size=new(host.Size.X,80);
    Pack.Settings.Startup["better_topbar"]=false;await Wait(.3);await AfterDraw();
    var tip=NHoverTipSet.CreateAndShow(owner,content)!;tip.Reparent(host);tip.Scale=Vector2.One;
    // 复现 Gravity 在 CreateAndShow 完成原生边界校正后再设置根节点位置的路径。
    tip.GlobalPosition=owner.GlobalPosition+new Vector2(0,owner.Size.Y+20);
    await Wait(.1);await AfterDraw();var baseline=TipVisualBounds(tip);var nativePosition=tip.Position;
    Check("topbar tooltip original right overflow "+label,baseline.End.X>viewport.Size.X);
    if(test.Item1==1920)viewport.GetTexture().GetImage().SavePng(Output+"/topbar-tooltip-before.png");
    var mouse=owner.GetGlobalRect().GetCenter();viewport.PushInput(new InputEventMouseMotion{Position=mouse,GlobalPosition=mouse},true);
    GD.Print("[Aikun020Probe] tooltip pointer="+viewport.GetMousePosition()+" target="+mouse);
    Pack.Settings.Startup["better_topbar"]=true;await Wait(.1);await AfterDraw();
    var bounds=TipVisualBounds(tip);
    Check("topbar tooltip flips fully left of mouse "+label,bounds.End.X<=mouse.X-15);
    Check("topbar tooltip stays inside screen "+label,bounds.Position.X>=15&&bounds.Position.Y>=15&&bounds.End.X<=viewport.Size.X-15&&bounds.End.Y<=viewport.Size.Y-15);
    Check("topbar tooltip retains native scale "+label,tip.Scale==Vector2.One);
    var pos=tip.Position;var bottom=layout.BackgroundBottom;bool stable=true;
    for(int i=0;i<12;i++){await AfterDraw();stable&=tip.Position.DistanceTo(pos)<.1f&&Math.Abs(layout.BackgroundBottom-bottom)<.1f;}
    Check("topbar tooltip repeated frames do not drift "+label,stable);
    if(test.Item1==1920)viewport.GetTexture().GetImage().SavePng(Output+"/topbar-tooltip-left.png");
    Pack.Settings.Startup["better_topbar"]=false;await AfterDraw();
    Check("topbar tooltip disabled restores native position "+label,tip.Position.DistanceTo(nativePosition)<.1f);
    Pack.Settings.Startup["better_topbar"]=true;
    tip.GlobalPosition=new(100,300);await AfterDraw();
    Check("topbar tooltip ample space preserves right position "+label,tip.GlobalPosition.DistanceTo(new(100,300))<.1f);
    tip.GlobalPosition=owner.GlobalPosition+new Vector2(0,owner.Size.Y+20);tip.SetFollowOwner();await AfterDraw();
    pos=tip.Position;stable=true;for(int i=0;i<10;i++){await AfterDraw();stable&=tip.Position.DistanceTo(pos)<.1f;}
    Check("topbar tooltip native owner following remains stable "+label,stable);
    NHoverTipSet.Remove(owner);await AfterDraw();
   }
   viewport.Size=new(1280,720);host.Scale=Vector2.One;host.Size=viewport.Size;bar.Size=new(1280,80);await Wait(.2);
   var multiple=new IHoverTip[]{content,TooltipText("extra-tip","附加说明","同时显示的第二条原生说明。"),new CardHoverTip(ModelDb.Card<Bash>())};
   var group=NHoverTipSet.CreateAndShow(owner,multiple)!;group.Reparent(host);group.GlobalPosition=owner.GlobalPosition+new Vector2(0,100);await Wait(.2);await AfterDraw();
   var groupBounds=TipVisualBounds(group);
   Check("topbar tooltip group text and card preview fit screen",groupBounds.Position.X>=15&&groupBounds.End.X<=viewport.Size.X-15&&groupBounds.End.Y<=viewport.Size.Y-15);
   var text=group.GetNode<Control>("textHoverTipContainer");var card=group.GetNode<Control>("cardHoverTipContainer");
   var relative=card.GlobalPosition-text.GlobalPosition;await AfterDraw();
   Check("topbar tooltip group keeps relative preview positions",(card.GlobalPosition-text.GlobalPosition).DistanceTo(relative)<.1f&&text.GetChildCount()==2&&card.GetChildCount()>0);
   NHoverTipSet.Remove(owner);await AfterDraw();
   var outside=new Control{Position=new(1100,400),Size=new(80,80)};host.AddChild(outside);
   var unrelated=NHoverTipSet.CreateAndShow(outside,content)!;unrelated.Reparent(host);unrelated.GlobalPosition=outside.GlobalPosition;await Wait(.1);await AfterDraw();
   Check("topbar tooltip does not move unrelated screen tips",unrelated.GlobalPosition.DistanceTo(outside.GlobalPosition)<.1f);NHoverTipSet.Remove(outside);await AfterDraw();
   foreach(var c in left.GetChildren().OfType<Control>())c.Hide();foreach(var c in right.GetChildren().OfType<Control>().Where(c=>c!=owner))c.Hide();await Wait(.2);
   var single=NHoverTipSet.CreateAndShow(owner,content)!;single.Reparent(host);single.GlobalPosition=owner.GlobalPosition+new Vector2(0,100);await Wait(.1);await AfterDraw();
   Check("topbar tooltip single row also fits screen",layout.Rows==1&&TipVisualBounds(single).End.X<=viewport.Size.X-15);NHoverTipSet.Remove(owner);await AfterDraw();
  }finally{
   NHoverTipSet.Remove(owner);Pack.Settings.Startup["better_topbar"]=true;viewport.QueueFree();await Wait(.1);
   File.WriteAllText(Output+"/topbar-tooltip-"+(Minimal?"minimal":"full")+".json",System.Text.Json.JsonSerializer.Serialize(new{Passed=passes.Count-start,Checks=passes.Skip(start).ToArray()},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
  }
 }
}
