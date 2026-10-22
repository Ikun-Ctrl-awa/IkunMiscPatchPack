using System.Reflection;
using Godot;
using HarmonyLib;
namespace AdaptiveTopBar;

// 可选兼容：无需引用/修改遗物整理 DLL。拖动坐标按单行基准保存，避免每次读档累加顶栏高度。
public static class DixieCompatibility {
 static FieldInfo? buttonField;
 static bool installed;static ulong nextAttempt;
 static Control? tracked;static float applied;
 public static void TryInstall(){
  if(installed||Time.GetTicksMsec()<nextAttempt)return;nextAttempt=Time.GetTicksMsec()+1000;
  var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("DixieRelicSort.RelicFoldPatch")).FirstOrDefault(t=>t!=null);if(type==null)return;
  buttonField=AccessTools.Field(type,"_foldButton");if(buttonField==null)return;
  var harmony=new Harmony("local.adaptive-top-bar.dixie");
  var save=AccessTools.Method(type,"SaveButtonPos");var reset=AccessTools.Method(type,"ResetButtonPos");
  if(save!=null)harmony.Patch(save,prefix:new HarmonyMethod(typeof(DixieCompatibility),nameof(BeforeSave)),finalizer:new HarmonyMethod(typeof(DixieCompatibility),nameof(AfterSave)));
  if(reset!=null)harmony.Patch(reset,postfix:new HarmonyMethod(typeof(DixieCompatibility),nameof(Reset)));
  installed=true;GD.Print("[AdaptiveTopBar] DixieRelicSort follow-bottom and relative drag coordinates enabled");
 }
 public static Control? Button=>buttonField?.GetValue(null) as Control;
 public static void Update(Control bar,float delta){
  var button=Button;if(button==null||!GodotObject.IsInstanceValid(button)||button.GetParent()!=bar.GetParent())return;
  if(tracked!=button){tracked=button;applied=0;}
  // 外部拖动直接成为新的相对偏移；只应用本次高度差，不覆盖用户 X 坐标。
  var target=button.Position+new Vector2(0,delta-applied);
  if(target.DistanceTo(button.Position)>.01f)button.Position=target;
  applied=delta;
 }
 static void BeforeSave(out Vector2? __state){
  __state=null;var button=Button;if(button==null||button!=tracked||!GodotObject.IsInstanceValid(button))return;
  __state=button.Position;button.Position-=new Vector2(0,applied);
 }
 static void AfterSave(Vector2? __state){if(__state is Vector2 pos&&tracked!=null&&GodotObject.IsInstanceValid(tracked))tracked.Position=pos;}
 static void Reset(){tracked=null;applied=0;}
}
