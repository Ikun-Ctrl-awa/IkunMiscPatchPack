using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static async Task AfterDraw(){
  var done=new TaskCompletionSource();void Finish(){RenderingServer.FramePostDraw-=Finish;done.SetResult();}
  RenderingServer.FramePostDraw+=Finish;await done.Task;
 }
 static async Task TopBarStability(){
  var viewport=new SubViewport{Size=new(1280,720),RenderTargetUpdateMode=SubViewport.UpdateMode.Always,HandleInputLocally=true};Tree.Root.AddChild(viewport);
  try{
   var host=new Control{Size=new(1280,720)};viewport.AddChild(host);
   host.AddChild(new ColorRect{Size=host.Size,Color=new("20262b"),MouseFilter=Control.MouseFilterEnum.Ignore});
   var relics=new Control{Name="RelicInventory",Position=new(16,88),Size=new(500,50)};host.AddChild(relics);
   var debug=new Control{Name="DebugInfo",Position=new(1000,93),Size=new(200,80)};host.AddChild(debug);
   var bar=new Control{Name="TopBar",Size=new(1280,80),MouseFilter=Control.MouseFilterEnum.Ignore};host.AddChild(bar);
   bar.AddChild(new TextureRect{Name="BgImage",Size=new(2560,100),Texture=ResourceLoader.Load<Texture2D>("res://images/atlases/ui_atlas.sprites/top_bar/top_bar.tres"),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter=Control.MouseFilterEnum.Ignore});
   var left=new HBoxContainer{Name="LeftAlignedStuff",Size=new(700,80),MouseFilter=Control.MouseFilterEnum.Ignore};bar.AddChild(left);
   var right=new HBoxContainer{Name="RightAlignedStuff",Size=new(400,80),MouseFilter=Control.MouseFilterEnum.Ignore};bar.AddChild(right);
   left.AddChild(new Label{Text="顶栏悬停回归检查",CustomMinimumSize=new(500,80),VerticalAlignment=VerticalAlignment.Center});
   Control? keyFrame=null;
   if(!Minimal){
    var type=typeof(Pack.HeartKeyTopBar).GetNestedType("KeyDisplay",F)!;
    var display=Activator.CreateInstance(type,F,null,[bar,player!],null)!;type.GetMethod("Refresh",F)!.Invoke(display,null);
    keyFrame=left.GetNode<Control>("AikunHeartKeyFrame");
    Check("key frame precedes portrait and other left controls",keyFrame.GetIndex()==0);
   }
   for(int i=0;i<12;i++)right.AddChild(new Button{Text="MOD "+i,CustomMinimumSize=new(80,80)});
   var pause=ResourceLoader.Load<PackedScene>("res://scenes/ui/top_bar/top_bar_settings_button.tscn").Instantiate<NTopBarPauseButton>();right.AddChild(pause);
   var layout=AdaptiveTopBar.Entry.Attach(bar);await Wait(.4);await AfterDraw();
   Check("hover test forces multiple rows",layout.Rows>=2);
   var icon=pause.GetNode<Control>("Control/Icon");var stone=bar.GetNode<NinePatchRect>("AdaptiveStoneBackground");
   foreach(float width in new[]{1280f,960f}){
    bar.Size=new(width,80);await Wait(.3);await AfterDraw();
    if(keyFrame!=null)Check("keys stay in upper left when wrapped width="+width,layout.Items[0]==keyFrame&&keyFrame.GlobalPosition.X<24&&keyFrame.GlobalPosition.Y<16);
    var bottom=layout.BackgroundBottom;var stoneSize=stone.Size;var relicPosition=relics.Position;var debugPosition=debug.Position;var buttonPosition=pause.GlobalPosition;
    // 调用真实设置按钮的悬停与移出回调，运行原生齿轮动画和提示框逻辑。
    AccessTools.Method(typeof(NTopBarPauseButton),"OnFocus").Invoke(pause,null);
    float minAngle=float.MaxValue,maxAngle=float.MinValue;bool stable=true;
    for(int i=0;i<45;i++){
     await AfterDraw();minAngle=Math.Min(minAngle,icon.Rotation);maxAngle=Math.Max(maxAngle,icon.Rotation);
     stable&=Math.Abs(layout.BackgroundBottom-bottom)<.1f&&stone.Size.DistanceTo(stoneSize)<.1f&&relics.Position.DistanceTo(relicPosition)<.1f&&debug.Position.DistanceTo(debugPosition)<.1f&&pause.GlobalPosition.DistanceTo(buttonPosition)<.1f;
    }
    Check("native settings gear animates width="+width,maxAngle-minAngle>.1f);
    Check("hover preserves background relics debug and button width="+width,stable);
    AccessTools.Method(typeof(NTopBarPauseButton),"OnUnfocus").Invoke(pause,null);
    for(int i=0;i<65;i++){
     await AfterDraw();stable&=Math.Abs(layout.BackgroundBottom-bottom)<.1f&&stone.Size.DistanceTo(stoneSize)<.1f&&relics.Position.DistanceTo(relicPosition)<.1f&&debug.Position.DistanceTo(debugPosition)<.1f;
    }
    Check("unhover preserves background relics and debug width="+width,stable);
   }
   viewport.GetTexture().GetImage().SavePng(Output+"/topbar-hover-regression.png");
  }finally{viewport.QueueFree();await Wait(.1);}
 }
 static async Task KeyStates(Control host){
  var type=typeof(Pack.HeartKeyTopBar).GetNestedType("KeyDisplay",F)!;
  var relics=(List<RelicModel>)AccessTools.Field(typeof(Player),"_relics").GetValue(player)!;
  var existing=relics.Where(Pack.HeartKeyTopBar.IsKey).ToArray();
  try{
   for(int mask=0;mask<8;mask++){
    relics.RemoveAll(Pack.HeartKeyTopBar.IsKey);
    for(int i=0;i<3;i++)if((mask&(1<<i))!=0){var key=ModelDb.GetById<RelicModel>(ModelDb.GetId(Find("Act4Heart.Keys."+Pack.HeartKeyTopBar.KeyNames[i]))).ToMutable();key.Owner=player!;relics.Add(key);}
    var preview=new Control{Name="KeyState"+mask,Position=new(40+mask*120,620),Size=new(100,80)};host.AddChild(preview);
    preview.AddChild(new HBoxContainer{Name="LeftAlignedStuff",Size=new(100,80)});
    var display=Activator.CreateInstance(type,F,null,[preview,player!],null)!;type.GetMethod("Refresh",F)!.Invoke(display,null);
    var frame=preview.GetNode<Control>("LeftAlignedStuff/AikunHeartKeyFrame");
    Check("key ownership combination "+mask,Enumerable.Range(0,3).All(i=>frame.GetNode<TextureRect>(Pack.HeartKeyTopBar.KeyNames[i]).SelfModulate==((mask&(1<<i))!=0?Colors.White:Colors.Transparent)));
   }
   await Wait(.3);await AfterDraw();Tree.Root.GetTexture().GetImage().SavePng(Output+"/key-states-original-style.png");
  }finally{relics.RemoveAll(Pack.HeartKeyTopBar.IsKey);relics.AddRange(existing);}
 }
}
