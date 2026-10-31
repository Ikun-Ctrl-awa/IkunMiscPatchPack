using System.Text.Json;
using Godot;
using STS2RitsuLib.Settings;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static async Task SettingsExtraFoldChecks(){
  string before=JsonSerializer.Serialize(Pack.Settings.Current);
  foreach(var (key,children) in new[]{("skip_story",2),("mod_form",2),("past_rooms",5)}){
   string page=Pack.Settings.Id+(key=="past_rooms"?".patches":".misc");
   Check("extra fold native open "+key,ModSettingsNavigator.RequestOpenByIds(Pack.Settings.Id,page,key,null).Success);await Wait(2);
   var root=SettingsNodes(Tree.Root).OfType<Control>().Single(n=>n.Name=="AikunDescription_"+key&&n.IsVisibleInTree());
   var body=root.GetNode<Control>("Body");var button=(Button)root.GetMeta("expand_button").AsGodotObject();
   Check("extra controls initially hidden "+key,body.GetChildCount()==children&&!body.Visible&&body.GetChildren().OfType<Control>().All(n=>!n.IsVisibleInTree()));
   Check("extra enabled and expand stay visible "+key,button.IsVisibleInTree()&&button.GetParent().Name=="AikunEnabledLine_"+key&&((Control)button.GetParent().GetChild(0)).IsVisibleInTree());
   Tree.Root.GetTexture().GetImage().SavePng(Output+"/settings-"+key+"-collapsed.png");
   await ClickSettings(button);await Wait(.4);
   Check("extra controls expand on mouse click "+key,body.IsVisibleInTree()&&button.Text=="▼ 收起"&&body.GetChildren().OfType<Control>().All(n=>n.IsVisibleInTree()));
   Check("extra native controls retain layout "+key,body.GetChildren().Skip(1).OfType<Control>().All(n=>n.Size.X>0&&n.Size.Y>0));
   Tree.Root.GetTexture().GetImage().SavePng(Output+"/settings-"+key+"-expanded.png");
   await ClickSettings(button);await Wait(.3);
   Check("extra controls hide on second click "+key,!body.Visible&&button.Text=="▶ 展开"&&body.GetChildren().OfType<Control>().All(n=>!n.IsVisibleInTree()));
  }
  Check("extra folds preserve all config values",JsonSerializer.Serialize(Pack.Settings.Current)==before);
 }
 static IEnumerable<Node> SettingsNodes(Node root){
  yield return root;
  foreach(var child in root.GetChildren())foreach(var node in SettingsNodes(child))yield return node;
 }
 static async Task ClickSettings(Button button){
  // 长列表导航后的焦点滚动可能仍在移动目标；先让原生滚动容器露出控件并稳定。
  button.GrabFocus();
  for(Node? p=button.GetParent();p!=null;p=p.GetParent())if(p is ScrollContainer scroll)scroll.EnsureControlVisible(button);
  await Wait(.25);
  var pos=button.GetGlobalTransformWithCanvas()*(button.Size*.5f);
  // 使用视口坐标，避免窗口缩放使系统输入坐标偏移。
  var viewport=button.GetViewport();
  viewport.PushInput(new InputEventMouseMotion{Position=pos,GlobalPosition=pos},true);await Wait(.05);
  viewport.PushInput(new InputEventMouseButton{Position=pos,GlobalPosition=pos,ButtonIndex=MouseButton.Left,Pressed=true},true);await Wait(.05);
  viewport.PushInput(new InputEventMouseButton{Position=pos,GlobalPosition=pos,ButtonIndex=MouseButton.Left,Pressed=false},true);await Wait(.15);
 }
 static async Task SettingsFoldChecks(){
  string before=JsonSerializer.Serialize(Pack.Settings.Current);
  var pages=ModSettingsRegistry.GetPages().Where(p=>p.ModId==Pack.Settings.Id&&p.Id!=Pack.Settings.Id).ToArray();
  Check("fold keeps 8 misc and 40 patch sections",pages.Single(p=>p.Id.EndsWith(".misc")).Sections.Count==8&&pages.Single(p=>p.Id.EndsWith(".patches")).Sections.Count==40);
  foreach(var option in Pack.Options.All){
   var section=pages.SelectMany(p=>p.Sections).Single(s=>s.Id==option.Key);
   Check(option.Key+" toggle and status remain outside fold",!section.IsCollapsible&&section.Entries.Any(e=>e.Id=="enabled")&&section.Entries.Any(e=>e.Id=="status"));
   var description=(CustomModSettingsEntryDefinition)section.Entries.Single(e=>e.Id=="description");
   using var control=description.ControlFactory(null!);
   var body=control.GetNode<Control>("Body");var button=(Button)control.GetMeta("expand_button").AsGodotObject();
   Check(option.Key+" starts collapsed",!body.Visible&&button.Text=="▶ 展开");
   button.EmitSignal(BaseButton.SignalName.Pressed);
   Check(option.Key+" expands complete description",body.Visible&&button.Text=="▼ 收起"&&Pack.SettingsDescription.Text(option).StartsWith(option.Description));
   button.EmitSignal(BaseButton.SignalName.Pressed);
   Check(option.Key+" collapses again",!body.Visible&&button.Text=="▶ 展开");
   Check(option.Key+" old inline descriptions removed",section.Entries.All(e=>e.Description==null));
  }
  Check("fold preserves hotkey notes",Pack.SettingsDescription.Text(Pack.Options.All.Single(o=>o.Key=="skip_story")).Contains("Peek"));
  Check("fold preserves self candidate notes",Pack.SettingsDescription.Text(Pack.Options.All.Single(o=>o.Key=="mod_form")).Contains("默认关闭"));
  Check("fold preserves probability notes",Pack.SettingsDescription.Text(Pack.Options.All.Single(o=>o.Key=="past_rooms")).Contains("已生成地图的 Boss"));
  foreach(var page in pages){
   var opened=ModSettingsNavigator.RequestOpenByIds(Pack.Settings.Id,page.Id,null,null);
   Check("native open "+page.Id,opened.Success);await Wait(2);
   var roots=SettingsNodes(Tree.Root).OfType<Control>().Where(n=>n.Name.ToString().StartsWith("AikunDescription_")&&n.IsVisibleInTree()).ToArray();
   Check("native page has fold controls "+page.Id,roots.Length>0);
   var first=roots[0];var body=first.GetNode<Control>("Body");var button=(Button)first.GetMeta("expand_button").AsGodotObject();
   Check("native initial collapsed "+page.Id,!body.Visible);
   Tree.Root.GetTexture().GetImage().SavePng(Output+"/settings-"+(page.Id.EndsWith("misc")?"misc":"patches")+"-collapsed.png");
   Check("native expand shares enabled line "+page.Id,button.GetParent().Name.ToString().StartsWith("AikunEnabledLine_")&&Math.Abs(button.GetGlobalRect().GetCenter().Y-((Control)button.GetParent().GetChild(0)).GetGlobalRect().GetCenter().Y)<2);
   float height=first.Size.Y;
   await ClickSettings(button);await Wait(.4);
   Check("native mouse expands "+page.Id,body.IsVisibleInTree()&&button.Text=="▼ 收起");
   Check("native expands layout "+page.Id,first.Size.Y>height&&body.Size.Y>0&&body.Size.X>0);
   Check("native folds independent "+page.Id,roots.Skip(1).All(n=>!n.GetNode<Control>("Body").Visible));
   Tree.Root.GetTexture().GetImage().SavePng(Output+"/settings-"+(page.Id.EndsWith("misc")?"misc":"patches")+"-expanded.png");
   await ClickSettings(button);await Wait(.3);
   Check("native mouse collapses "+page.Id,!body.Visible&&first.Size.Y<=height+1);
   button.GrabFocus();
   button.EmitSignal(BaseButton.SignalName.Pressed);
   Check("native focused activation "+page.Id,button.HasFocus()&&body.Visible);
   button.EmitSignal(BaseButton.SignalName.Pressed);
  }
  var pastPage=pages.Single(p=>p.Sections.Any(s=>s.Id=="past_rooms"));
  Check("native open past rooms",ModSettingsNavigator.RequestOpenByIds(Pack.Settings.Id,pastPage.Id,"past_rooms",null).Success);await Wait(2);
  var past=SettingsNodes(Tree.Root).OfType<Control>().Single(n=>n.Name=="AikunDescription_past_rooms"&&n.IsVisibleInTree());
  var pastBody=past.GetNode<Control>("Body");var pastButton=(Button)past.GetMeta("expand_button").AsGodotObject();
  Check("past probabilities share collapsed body",pastBody.GetChildCount()==5&&!pastBody.Visible);
  Tree.Root.GetTexture().GetImage().SavePng(Output+"/settings-past-collapsed.png");
  await ClickSettings(pastButton);await Wait(.4);
  Check("past probabilities expand with description",pastBody.IsVisibleInTree()&&pastBody.GetChildren().OfType<Control>().All(n=>n.IsVisibleInTree()));
  Tree.Root.GetTexture().GetImage().SavePng(Output+"/settings-past-expanded.png");
  await ClickSettings(pastButton);await Wait(.3);
  Check("past probabilities collapse with description",!pastBody.Visible&&pastBody.GetChildren().OfType<Control>().All(n=>!n.IsVisibleInTree()));
  Check("fold never writes config",JsonSerializer.Serialize(Pack.Settings.Current)==before);
  File.WriteAllText(Output+"/settings-fold-checks.json",JsonSerializer.Serialize(new{Options=Pack.Options.All.Length,Pages=pages.Select(p=>p.Id),Checks=passes.Where(x=>!x.StartsWith("related ")).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
 }
}
