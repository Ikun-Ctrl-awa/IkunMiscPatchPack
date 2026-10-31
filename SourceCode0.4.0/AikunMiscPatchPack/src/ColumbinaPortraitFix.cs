using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace AikunMiscPatchPack;
public static class ColumbinaPortraitFix {
 public static void Install(){
  var type=FusionQuestBlessingFix.Entry.Find("ColumbinaMod.ColumbinaModCode.Cards.ColumbinaModCard")??throw new TypeLoadException("未找到哥伦比娅卡牌基类");
  var h=new Harmony("AikunMiscPatchPack.ColumbinaPortraits");
  try{foreach(var name in new[]{"PortraitPath","BetaPortraitPath"})h.Patch(AccessTools.DeclaredProperty(type,name).GetMethod,
   postfix:new HarmonyMethod(typeof(ColumbinaPortraitFix),nameof(Resolve)));}
  catch{h.UnpatchAll(h.Id);throw;}
 }
 static void Resolve(CardModel __instance,ref string __result){
  if(!Settings.Active("columbina_portraits"))return;
  // 原包只有 big/ 专属图；小图 getter 会返回通用笑脸图。优先保留真实专属图，
  // 没有小图或测试图时，使用同一张牌已随模组提供的大图。
  string entry=__instance.Id.Entry;int split=entry.IndexOf('-');if(split<0)return;
  string file=entry[(split+1)..].ToLowerInvariant()+".png";
  string root="res://ColumbinaMod/images/card_portraits/";
  string normalized=__result.Replace('\\','/');
  if(normalized!=root+"card.png"&&normalized!=root+"big/card.png"&&ResourceLoader.Exists(normalized)){__result=normalized;return;}
  foreach(string path in new[]{root+file,root+"big/"+file})if(ResourceLoader.Exists(path)){__result=path;return;}
 }
}
