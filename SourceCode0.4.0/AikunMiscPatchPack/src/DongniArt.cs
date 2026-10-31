using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace AikunMiscPatchPack;

// 五张独立原图随 DLL 嵌入；向 Godot 缓存登记路径，兼容卡面、百科、预览及预加载。
public static class DongniArt {
 public const string Root="res://AikunMiscPatchPack/images/card_portraits/dongni/";
 static readonly Dictionary<string,Texture2D> portraits=new();
 public static string Key(CardModel card)=>card switch {
  DongniMeaning=>"meaning",NotDongniMeaning=>"not-meaning",WhoDongniMeaning=>"who-meaning",
  DongniStrike=>"strike",DongniDefend=>"defend",_=>throw new ArgumentException("不是东尼卡牌。",nameof(card))
 };
 public static string Path(CardModel card)=>Root+Key(card)+".png";
 public static Texture2D Portrait(CardModel card)=>portraits[Key(card)];
 public static void Install(Harmony harmony){
  foreach(string key in new[]{"meaning","not-meaning","who-meaning","strike","defend"}){
   using var stream=typeof(DongniArt).Assembly.GetManifestResourceStream("cardart.dongni."+key+".png")??throw new FileNotFoundException("东尼卡图资源缺失："+key);
   using var bytes=new MemoryStream();stream.CopyTo(bytes);using var image=new Image();
   if(image.LoadPngFromBuffer(bytes.ToArray())!=Error.Ok)throw new InvalidDataException("东尼卡图 PNG 无效："+key);
   var texture=ImageTexture.CreateFromImage(image);texture.TakeOverPath(Root+key+".png");portraits.Add(key,texture);
  }
  harmony.Patch(AccessTools.PropertyGetter(typeof(CardModel),"BetaPortraitPngPath"),prefix:new HarmonyMethod(typeof(DongniArt),nameof(BetaPortraitPath)));
 }
 static bool BetaPortraitPath(CardModel __instance,ref string __result){if(__instance is not DongniCard)return true;__result=Path(__instance);return false;}
}
