using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.addons.mega_text;

namespace AikunMiscPatchPack;

public static class PowerCornerLabelFix {
 static readonly Harmony H=new("Aikun.MiscPatchPack.PowerCornerLabels");
 const string Prefix="RitsuExtraPowerCornerSlot_";
 static readonly MethodInfo Adjust=AccessTools.Method(typeof(MegaLabel),"AdjustFontSize");
 public static void Install(){
  var style=FusionQuestBlessingFix.Entry.Find("STS2RitsuLib.Combat.Ui.ExtraCornerAmountLabels.ExtraCornerPowerAmountStyle")??throw new MissingMemberException("当前 RitsuLib 无状态角标样式接口");
  var runtime=FusionQuestBlessingFix.Entry.Find("STS2RitsuLib.Combat.Ui.ExtraCornerAmountLabels.ExtraCornerAmountLabelsRuntime")??throw new MissingMemberException("当前 RitsuLib 无状态角标刷新接口");
  var apply=AccessTools.Method(style,"Apply",[typeof(MegaLabel),typeof(MegaLabel)])??throw new MissingMethodException("状态数字角标样式接口已改变");
  var sync=AccessTools.Method(runtime,"SyncPower",[typeof(NPower)])??throw new MissingMethodException("状态数字角标刷新接口已改变");
  try{
   H.Patch(apply,postfix:new HarmonyMethod(typeof(PowerCornerLabelFix),nameof(Style)){priority=Priority.Last});
   H.Patch(sync,postfix:new HarmonyMethod(typeof(PowerCornerLabelFix),nameof(Layout)){priority=Priority.Last});
  }catch{H.UnpatchAll(H.Id);throw;}
 }
 static void Style(MegaLabel __0,MegaLabel? __1){
  if(!Settings.Active("power_corner_labels")||__1==null)return;
  // 原角标样式复制了原生标签的自动字号上限（100），新 Label 的最小尺寸随之撑大。
  // 使用原生标签当前实际字号，在文本与布局首次计算前限制额外角标的上限。
  int size=__1.LabelSettings?.FontSize??__1.GetThemeFontSize("font_size");
  if(size<=0)return;
  __0.MaxFontSize=Math.Min(size,__1.MaxFontSize);__0.MinFontSize=Math.Min(__1.MinFontSize,__0.MaxFontSize);
  __0.AddThemeFontSizeOverride("font_size",__0.MaxFontSize);__0.ClipText=true;
  Adjust.Invoke(__0,null);
 }
 static void Layout(NPower node){
  if(!Settings.Active("power_corner_labels")||node.Model.GetType().FullName!="Medo.Scripts.Powers.FlyIntoAPinkTomorrowPower")return;
  // 剩余攻击次数与原生右下角的效果层数分别显示，避免两个含义不同的数字重叠。
  foreach(var label in node.GetChildren().OfType<MegaLabel>().Where(n=>n.Name.ToString().StartsWith(Prefix,StringComparison.Ordinal))){
   label.HorizontalAlignment=HorizontalAlignment.Left;label.VerticalAlignment=VerticalAlignment.Top;
   label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
   label.Position=Vector2.Zero;label.Size=new Vector2(44,23);Adjust.Invoke(label,null);
  }
 }
}
