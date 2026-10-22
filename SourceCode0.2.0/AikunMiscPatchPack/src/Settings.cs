using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;
namespace AikunMiscPatchPack;

public sealed class PackConfig {
 public Dictionary<string,bool> Enabled{get;set;}=new();
 public Dictionary<string,int> PastChances{get;set;}=new();
 public bool ModFormIncludeSelf{get;set;}=false;
 public string SkipStoryKey{get;set;}="Space";
 public bool Get(string key)=>Enabled.GetValueOrDefault(key,true);
 public int Chance(string key)=>Math.Clamp(PastChances.GetValueOrDefault(key,30),0,100);
}
public static class Settings {
 public const string Id="AikunMiscPatchPack",Name="艾堃的杂项与补丁包",ConfigId="options";
 public static readonly Dictionary<string,bool> Startup=new();
 public static readonly Dictionary<string,int> StartupChances=new();
 public static bool StartupModFormIncludeSelf{get;set;}
 public static string StartupSkipStoryKey{get;set;}="Space";
 public static PackConfig Current=>ModDataStore.For(Id).Get<PackConfig>(ConfigId);
 public static bool Active(string key)=>Startup.GetValueOrDefault(key,true);
 static ModSettingsText T(string s)=>ModSettingsText.Literal(s);
 public static void Initialize(){
  ModDataStore.For(Id).Register<PackConfig>(ConfigId,"options.json",SaveScope.Global,()=>new PackConfig(),true,null,null);
  foreach(var option in Options.All)Startup[option.Key]=Current.Get(option.Key);
  StartupModFormIncludeSelf=Current.ModFormIncludeSelf;
  StartupSkipStoryKey=SkipStory.NormalizeKey(Current.SkipStoryKey);
  foreach(var key in new[]{"monster","elite","event","boss"})StartupChances[key]=Current.Chance(key);
 }
 public static string Status(PatchOption o){
  var missing=Options.Missing(o);if(missing.Length>0)return "未装载相关模组："+string.Join("、",missing)+"。补丁无效。";
  bool wanted=Current.Get(o.Key),running=Active(o.Key);if(wanted!=running)return (wanted?"已设置开启":"已设置关闭")+"，重启游戏后生效。";
  if(!running)return "已关闭。";
  if(o.Key=="skip_story"&&SkipStory.NormalizeKey(Current.SkipStoryKey)!=StartupSkipStoryKey)return "按键已修改，重启游戏后生效。";
  if(o.Key=="mod_form"&&Current.ModFormIncludeSelf!=StartupModFormIncludeSelf)return "自身参与候选已修改，重启游戏后生效。";
  if(o.Key=="past_rooms"&&StartupChances.Any(p=>Current.Chance(p.Key)!=p.Value))return "概率已修改，重启游戏后生效；已生成地图的 Boss 保持不变。";
  return RuntimeLoader.States.GetValueOrDefault(o.Key,"已启用；重启游戏后应用设置。");
 }
 public static void RegisterPages(){
  RitsuLibFramework.RegisterModSettings(Id,page=>page.WithTitle(T(Name)).WithModDisplayName(T(Name)).WithVisibleOnHostSurfaces((ModSettingsHostSurface)3).AddSection("categories",s=>s.WithTitle(T("分类")).AddParagraph("hint",T("各项功能首次加载默认开启；mod形态自身参与候选默认关闭。修改选项后重启游戏生效。缺少相关模组时，该补丁自动跳过。")).AddSubpage("misc",T("杂项"),Id+".misc",T("打开杂项")).AddSubpage("patches",T("补丁"),Id+".patches",T("打开补丁"))),Id);
  void Register(string pageId,string title,IEnumerable<PatchOption> options){
   RitsuLibFramework.RegisterModSettings(Id,page=>{
    page.AsChildOf(Id).WithTitle(T(title)).WithModDisplayName(T(Name)).WithVisibleOnHostSurfaces((ModSettingsHostSurface)3);
    foreach(var o in options){
     var binding=new ModSettingsValueBinding<PackConfig,bool>(Id,ConfigId,SaveScope.Global,c=>c.Get(o.Key),(c,v)=>c.Enabled[o.Key]=v);
     page.AddSection(o.Key,s=>{
      s.WithTitle(T(o.Title)).AddToggle("enabled",T("启用"),binding,T(o.Description)).WithEntryEnabledWhen("enabled",()=>Options.Missing(o).Length==0).AddParagraph("status",ModSettingsText.Dynamic(()=>Status(o)));
      if(o.Key=="mod_form"){
       var includeSelf=new ModSettingsValueBinding<PackConfig,bool>(Id,ConfigId,SaveScope.Global,c=>c.ModFormIncludeSelf,(c,v)=>c.ModFormIncludeSelf=v);
       s.AddToggle("include_self",T("mod形态自身参与生成候选"),includeSelf,T("默认关闭。开启后可以生成另一张mod形态；只有这张模组卡时也会进入无色卡池。重启游戏后生效。"));
      }
      if(o.Key=="skip_story"){
       var hotkey=new ModSettingsValueBinding<PackConfig,string>(Id,ConfigId,SaveScope.Global,c=>c.SkipStoryKey,(c,v)=>c.SkipStoryKey=SkipStory.NormalizeKey(v));
       s.AddKeyBinding("hotkey",T("跳过动画按键"),hotkey,allowModifierCombos:true,allowModifierOnly:false,distinguishModifierSides:false,description:T("默认 Space（空格），支持组合键；重启游戏后生效。按一次跳过当前结算链，完成后自动恢复。"));
       s.AddParagraph("conflicts",T("空格与原版默认快捷键重叠：鼠标键盘模式为查看战场（Peek），纯键盘模式为选择（Select）。本功能只在正在结算且没有选牌／菜单时接管该按键，其余时间保留原键位。"));
      }
      if(o.Key=="past_rooms")foreach(var (key,label) in new[]{("monster","普通战斗"),("elite","精英"),("event","事件"),("boss","Boss")}){
       var chance=new ModSettingsValueBinding<PackConfig,int>(Id,ConfigId,SaveScope.Global,c=>c.Chance(key),(c,v)=>c.PastChances[key]=Math.Clamp(v,0,100));
       s.AddIntSlider(key,T(label+"出现概率"),chance,0,100,1,v=>v+"%",T("重启后生效；已生成地图的 Boss 保持不变。"));
      }
     });
    }
   },Id+"."+pageId);
  }
  Register("misc","杂项",Options.All.Where(o=>o.Misc));Register("patches","补丁",Options.All.Where(o=>!o.Misc));
  Godot.GD.Print($"[AikunMiscPatchPack] settings registered: 杂项={Options.All.Count(o=>o.Misc)} 补丁={Options.All.Count(o=>!o.Misc)}");
 }
}
