using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;

namespace AikunMiscPatchPack;
public static class EnchantmentLocalizationFix {
 static readonly Dictionary<string,string> Text=new(){
  ["VICTORY_PURSUIT_ENCHANTMENT.title"]="乘胜追击",
  ["VICTORY_PURSUIT_ENCHANTMENT.description"]="本场战斗首次打出这张牌时，将1张「转圈圈」加入手牌。附魔层数至少为2时，生成升级版。",
  ["VICTORY_PURSUIT_ENCHANTMENT.extraCardText"]="本场战斗首次打出时，获得1张「转圈圈」；附魔至少2层时获得升级版。",
  ["TEMPORARY_SOWN.title"]="临时播种",
  ["TEMPORARY_SOWN.description"]="本场战斗首次打出这张牌时，获得{Amount}点能量。战斗结束后移除此临时附魔。",
  ["TEMPORARY_SOWN.extraCardText"]="本场战斗首次打出时，获得{Amount}点能量。战斗结束后移除此临时附魔。",
  ["PLAGUE_COMPOSITE_ENCHANTMENT.title"]="复合附魔",
  ["PLAGUE_COMPOSITE_ENCHANTMENT.description"]="保留并同时结算其中各个附魔的效果。具体效果以各子附魔说明为准。"
 };
 public static void Install(){
  var h=new Harmony("AikunMiscPatchPack.EnchantmentLocalization");
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.SetLanguage)),postfix:new HarmonyMethod(typeof(EnchantmentLocalizationFix),nameof(Apply)));
  Apply();
 }
 public static void Apply(){
  if(!Settings.Active("enchantment_localization")||LocManager.Instance?.Language!="zhs")return;
  var table=LocManager.Instance.GetTable("enchantments");var missing=new Dictionary<string,string>();
  foreach(var pair in Text){
   string? old=table.HasEntry(pair.Key)?table.GetRawText(pair.Key):null;
   if(string.IsNullOrWhiteSpace(old)||!old.Any(c=>c is >= '\u4e00' and <= '\u9fff'))missing[pair.Key]=pair.Value;
  }
  table.MergeWith(missing);
 }
}
