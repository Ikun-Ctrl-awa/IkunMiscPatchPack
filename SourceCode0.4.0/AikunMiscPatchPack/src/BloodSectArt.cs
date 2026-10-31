using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace AikunMiscPatchPack;
public static class BloodSectArt {
 public const string Path="res://AikunMiscPatchPack/images/card_portraits/blood_sect.png";
 const string IconPath="res://AikunMiscPatchPack/images/powers/blood_sect.png";
 static Texture2D portrait=null!,icon=null!;
 public static Texture2D Portrait=>portrait;
 public static void Install(Harmony h){
  const string scene="<path d='M300 70 390 220 360 300 300 340 240 300 210 220Z' fill='#cc243d' stroke='#ffb0a0' stroke-width='7'/><path d='M170 300 300 370 430 300 404 360 300 412 196 360Z' fill='#491a2b' stroke='#d5a78b' stroke-width='9'/><path d='M230 70 300 25 370 70M145 160 100 220 145 280M455 160 500 220 455 280' fill='none' stroke='#944054' stroke-width='9'/>";
  using var stream=typeof(BloodSectArt).Assembly.GetManifestResourceStream("cardart.blood_sect.png")??throw new FileNotFoundException("cardart.blood_sect.png");
  using var bytes=new MemoryStream();stream.CopyTo(bytes);using var raster=new Image();
  if(raster.LoadPngFromBuffer(bytes.ToArray())!=Error.Ok)throw new InvalidDataException("血魔宗武器插画 PNG 无效");
  portrait=ImageTexture.CreateFromImage(raster);portrait.TakeOverPath(Path);
  icon=Load("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96' viewBox='0 0 600 440'>"+scene+"</svg>",IconPath);
  h.Patch(AccessTools.PropertyGetter(typeof(CardModel),"BetaPortraitPngPath"),prefix:new HarmonyMethod(typeof(BloodSectArt),nameof(Beta)));
  foreach(string name in new[]{nameof(PowerModel.Icon),nameof(PowerModel.BigIcon)})h.Patch(AccessTools.PropertyGetter(typeof(PowerModel),name),prefix:new HarmonyMethod(typeof(BloodSectArt),nameof(Icon)));
  h.Patch(AccessTools.PropertyGetter(typeof(PowerModel),nameof(PowerModel.PackedIconPath)),postfix:new HarmonyMethod(typeof(BloodSectArt),nameof(PowerPath)));
 }
 static Texture2D Load(string svg,string path){using var image=new Image();if(image.LoadSvgFromString(svg)!=Error.Ok)throw new InvalidDataException("血魔宗卡图无效");var texture=ImageTexture.CreateFromImage(image);texture.TakeOverPath(path);return texture;}
 static bool Beta(CardModel __instance,ref string __result){if(__instance is not BloodSect)return true;__result=Path;return false;}
 static bool Icon(PowerModel __instance,ref Texture2D __result){if(__instance is not(BloodSectPower or TemporaryBloodPower))return true;__result=icon;return false;}
 static void PowerPath(PowerModel __instance,ref string __result){if(__instance is BloodSectPower or TemporaryBloodPower)__result=IconPath;}
}
