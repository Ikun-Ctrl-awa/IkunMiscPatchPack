using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static IEnumerable<Control> UiChildren(Node node){foreach(var child in node.GetChildren()){if(child is Control control)yield return control;foreach(var nested in UiChildren(child))yield return nested;}}
 static Control CreateHextechSelection(){
  var type=Find("HextechRunes.HextechRuneSelectionScreen");var create=AccessTools.Method(type,"Create");
  var options=Activator.CreateInstance(Find("HextechRunes.HextechEnemyHexAdjustmentOptions"),true)!;
  var initial=AccessTools.Property(options.GetType(),"InitialHexes");var kind=initial.PropertyType.GetGenericArguments()[0];
  var values=Enum.GetValues(kind).Cast<object>().Where(v=>Convert.ToInt32(v)>0).Take(3).ToArray();var kinds=Array.CreateInstance(kind,values.Length);
  for(int i=0;i<values.Length;i++)kinds.SetValue(values[i],i);initial.SetValue(options,kinds);
  AccessTools.Property(options.GetType(),"ControlsEnabled").SetValue(options,true);
  var candidates=ModelDb.AllRelics.Where(r=>r.GetType().Namespace=="HextechRunes"&&r.GetType().Name.EndsWith("Rune")).ToArray();
  var relics=candidates.OrderBy(r=>r.GetType().Name is "GiantSlayerRune" or "InfernalConduitRune"?0:1).Take(3).Select(r=>r.ToMutable()).ToArray();
  var args=create.GetParameters().Select(p=>p.HasDefaultValue?p.DefaultValue:null).ToArray();args[0]=relics;args[1]=null;args[3]=options;
  args[2]=(Func<IReadOnlyList<RelicModel>,int,int,IReadOnlyList<RelicModel>>)((current,_,_)=>current);
  return (Control)create.Invoke(null,args)!;
 }
 static async Task HextechTopBarChecks(){
  AdaptiveTopBar.HextechCompatibility.TryInstall();
  if(Minimal){Check("Hextech compatibility skips absent optional mod",FusionQuestBlessingFix.Entry.Find("HextechRunes.HextechRuneSelectionScreen")==null);return;}
  var viewport=new SubViewport{Size=new(2560,1440),RenderTargetUpdateMode=SubViewport.UpdateMode.Always,HandleInputLocally=true};Tree.Root.AddChild(viewport);
  var host=new Control{Size=new(2560,1440)};viewport.AddChild(host);
  host.AddChild(new ColorRect{Size=host.Size,Color=new("17222d"),MouseFilter=Control.MouseFilterEnum.Ignore});
  var screen=CreateHextechSelection();host.AddChild(screen);
  var panel=screen.GetNode<Control>("ScreenCenter/ContentPanel");var originalParent=panel.GetParent();
  var bar=new Control{Name="TopBar",Size=new(2560,80),MouseFilter=Control.MouseFilterEnum.Ignore,ZIndex=100};host.AddChild(bar);
  bar.AddChild(new TextureRect{Name="BgImage",Size=new(2560,100),Texture=ResourceLoader.Load<Texture2D>("res://images/atlases/ui_atlas.sprites/top_bar/top_bar.tres"),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter=Control.MouseFilterEnum.Ignore});
  var left=new HBoxContainer{Name="LeftAlignedStuff",Size=new(700,80),MouseFilter=Control.MouseFilterEnum.Ignore};bar.AddChild(left);
  var right=new HBoxContainer{Name="RightAlignedStuff",Size=new(400,80),MouseFilter=Control.MouseFilterEnum.Ignore};bar.AddChild(right);
  for(int i=0;i<28;i++)left.AddChild(new Button{Text="药水 "+i,CustomMinimumSize=new(80,80)});
  for(int i=0;i<8;i++)right.AddChild(new Button{Text=i==7?"设置":"MOD "+i,CustomMinimumSize=new(80,80)});
  var layout=AdaptiveTopBar.Entry.Attach(bar);
  try{
   await Wait(.6);await AfterDraw();
   Pack.Settings.Startup["better_topbar"]=false;await Wait(.2);await AfterDraw();
   viewport.Size=new(1280,720);host.Size=new(1280,720);bar.Size=new(1280,80);await Wait(.3);await AfterDraw();
   GD.Print("[Aikun020Probe] Hextech original panel="+panel.GetGlobalRect()+" screen="+screen.Size+" barBottom="+layout.BackgroundBottom);
   viewport.GetTexture().GetImage().SavePng(Output+"/hextech-topbar-before.png");
   Check("Hextech original panel overlaps expanded topbar",panel.GetGlobalRect().Position.Y<layout.BackgroundBottom);
   Pack.Settings.Startup["better_topbar"]=true;
   foreach(var size in new[]{new Vector2I(2560,1440),new(1920,1080),new(1280,720),new(2560,1440)}){
    viewport.Size=size;host.Size=size;bar.Size=new(size.X,80);await Wait(.3);await AfterDraw();
    var rect=panel.GetGlobalRect();var label=size.X+"x"+size.Y;
    Check("Hextech multirow fixture "+label,layout.Rows>=2);
    Check("Hextech panel below complete topbar "+label,rect.Position.Y>=layout.BackgroundBottom+15);
    Check("Hextech panel inside viewport "+label,rect.Position.X>=15&&rect.End.X<=size.X-15&&rect.End.Y<=size.Y-15);
    Check("Hextech panel scales uniformly "+label,Math.Abs(panel.Scale.X-panel.Scale.Y)<.0001f&&panel.Scale.X>0&&panel.Scale.X<=1);
    Check("Hextech original UI parent preserved "+label,panel.GetParent()==originalParent);
    var buttons=UiChildren(panel).OfType<Button>().Where(b=>b.IsVisibleInTree()).ToArray();
    Check("Hextech all visible buttons fit "+label,buttons.Length>3&&buttons.All(b=>{var r=b.GetGlobalRect();return r.Position.Y>=layout.BackgroundBottom&&r.End.Y<=size.Y+1;}));
    var button=buttons.First(b=>!b.Disabled);var mouse=button.GetGlobalRect().GetCenter();
    viewport.PushInput(new InputEventMouseMotion{Position=mouse,GlobalPosition=mouse},true);await AfterDraw();
    var hovered=viewport.GuiGetHoveredControl();Check("Hextech native button receives mouse hover "+label,hovered==button||(hovered!=null&&button.IsAncestorOf(hovered)));
    var position=panel.Position;var scale=panel.Scale;var bottom=layout.BackgroundBottom;bool stable=true;
    for(int i=0;i<12;i++){await AfterDraw();stable&=panel.Position.DistanceTo(position)<.1f&&panel.Scale.DistanceTo(scale)<.0001f&&Math.Abs(layout.BackgroundBottom-bottom)<.1f;}
    Check("Hextech repeated draws do not drift "+label,stable);
    viewport.GetTexture().GetImage().SavePng(Output+"/hextech-topbar-"+label+".png");
   }
   screen.Hide();await Wait(.1);await AfterDraw();Check("Hextech hidden overlay restores panel scale",panel.Scale==Vector2.One);
   screen.Show();await Wait(.1);await AfterDraw();Check("Hextech reopened overlay avoids topbar",panel.GetGlobalRect().Position.Y>=layout.BackgroundBottom+15);
   foreach(var child in left.GetChildren().OfType<Control>())child.Hide();
   foreach(var child in right.GetChildren().OfType<Control>().Skip(1))child.Hide();
   await Wait(.3);await AfterDraw();Check("Hextech single row still avoids topbar",layout.Rows==1&&panel.GetGlobalRect().Position.Y>=100);
   bar.Hide();await Wait(.1);await AfterDraw();Check("Hextech hidden topbar restores original panel",panel.Scale==Vector2.One);
   bar.Show();await Wait(.1);await AfterDraw();Check("Hextech shown topbar restores safe area",panel.GetGlobalRect().Position.Y>=100);
  }finally{Pack.Settings.Startup["better_topbar"]=true;viewport.QueueFree();await Wait(.1);}
 }
}
