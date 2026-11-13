using Godot;
using STS2RitsuLib.Settings;

namespace IkunMiscPatchPack;

/// <summary>每个设置项各自持有展开状态，仅影响当前界面。</summary>
public static class SettingsDescription {
 public static string Text(PatchOption option)=>option.Description+(option.Key switch {
  "mod_form"=>"\n\n自身参与生成候选：默认关闭。开启后可以生成另一张mod形态；只有这张模组卡时也会进入无色卡池。重启游戏后生效。\n\n生成衍生卡：默认开启，保持原有生成规则。关闭后排除衍生稀有度（Token）的卡牌；开启时仍须满足原有的可生成、可打出等条件。只影响mod形态的候选池，重启游戏后生效。",
  "past_rooms"=>"\n\n出现概率：重启后生效；已生成地图的 Boss 保持不变。",
  _=>""
 });
 public static Control Create(PatchOption option){
  var root=new VBoxContainer{Name="AikunDescription_"+option.Key,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
  root.AddThemeConstantOverride("separation",8);
  var body=new VBoxContainer{Name="Body",Visible=false,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
  var text=ModSettingsUiFactory.CreateInlineDescription(Text(option));body.AddChild(text);
  var button=new Button{Name="Expand",Text="▶ 展开",TooltipText="展开或收起此项说明",SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin,SizeFlagsVertical=Control.SizeFlags.ShrinkCenter,CustomMinimumSize=new Vector2(100,38)};
  button.AddThemeFontOverride("font",text.GetThemeFont("normal_font"));
  button.AddThemeFontSizeOverride("font_size",22);
  button.AddThemeStyleboxOverride("normal",ModSettingsUiFactory.CreatePillStyle());
  button.AddThemeStyleboxOverride("hover",ModSettingsUiFactory.CreatePillStyle(true));
  button.AddThemeStyleboxOverride("pressed",ModSettingsUiFactory.CreatePillStyle(true));
  button.Pressed+=()=>{
   body.Visible=!body.Visible;
   button.Text=body.Visible?"▼ 收起":"▶ 展开";
  };
  root.AddChild(button);root.AddChild(body);
  root.SetMeta("expand_button",button);
  // 原生条目仍由Ritsu创建、绑定、刷新和回收，保留其保存、重置及复制粘贴行为。
  // 只在本节挂载完成后调整布局，不复制开关或概率值，不修改其他模组。
  root.Ready+=()=>{
   var parent=root.GetParent();
   void Arrange(){
    if(!GodotObject.IsInstanceValid(root)||!root.IsInsideTree())return;
    if(button.GetParent()==root){
     int index=root.GetIndex()-2; // enabled、status、description
     if(index<0)return;
     var enabled=(Control)parent.GetChild(index);
     var line=new HBoxContainer{Name="AikunEnabledLine_"+option.Key,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
     line.AddThemeConstantOverride("separation",12);
     parent.AddChild(line);parent.MoveChild(line,index);
     enabled.Reparent(line);enabled.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
     button.Reparent(line);
     // 标题使用开关行原生标签，隐藏重复节标题；节菜单保留在同一行。
     var header=parent.GetChildren().Take(line.GetIndex()).OfType<HBoxContainer>().FirstOrDefault();
     if(header!=null){
      if(header.GetChildCount()>1){
       var menu=(Control)header.GetChild(header.GetChildCount()-1);
       menu.Reparent(line);menu.SizeFlagsVertical=Control.SizeFlags.ShrinkCenter;
       line.MoveChild(button,line.GetChildCount()-1);
      }
      header.Visible=false;
     }
    }
    if(option.Key is "past_rooms" or "mod_form"){
     // 这些节说明之后的控件均为附属设置；随说明折叠，保留原生控件及绑定。
     var rows=parent.GetChildren().Skip(root.GetIndex()+1).OfType<Control>().ToArray();
     foreach(var row in rows)row.Reparent(body);
    }
   }
   void Schedule(Node _)=>Callable.From(Arrange).CallDeferred();
   parent.ChildEnteredTree+=Schedule;
   root.TreeExiting+=()=>parent.ChildEnteredTree-=Schedule;
   Callable.From(Arrange).CallDeferred();
  };
  return root;
 }
}
