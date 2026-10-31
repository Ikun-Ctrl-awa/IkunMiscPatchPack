using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;

namespace AikunMiscPatchPack;
public static class ThunderInitiationArt {
 public static string BackgroundPath=>EmbeddedEventBackgrounds.PathFor("eventart.thunder_sect_initiation.png");
 public const string BladePath="res://AikunMiscPatchPack/images/relics/electric_blade.png";
 public const string ArcPath="res://AikunMiscPatchPack/images/enchantments/thunder_arc.png";
 public static Texture2D Background=>PreloadManager.Cache.GetTexture2D(BackgroundPath);
 static readonly Dictionary<string,string> iconPaths=new();
 public static string IconPath(string path)=>iconPaths.GetValueOrDefault(path,path);
 public static Texture2D Icon(string path)=>PreloadManager.Cache.GetTexture2D(IconPath(path));
 public static void Initialize(Harmony h){
  _=BackgroundPath;
  Load(BladePath,"<path d='M73 7 34 54l10 9 42-48Z' fill='#c3ddfc' stroke='#5b79b7' stroke-width='3'/><path d='m22 49 27 24' stroke='#e8d77b' stroke-width='7'/><path d='m33 61-18 22' stroke='#3c467f' stroke-width='10'/><path d='m68 27-17 19h12L48 69l30-28H64l15-14Z' fill='#f5df83'/>");
  Load(ArcPath,"<path d='m55 7-30 43h23l-9 38 34-50H51Z' fill='#ffe8a0' stroke='#86c5ff' stroke-width='4'/>");
  h.Patch(AccessTools.Method(typeof(NEventLayout),nameof(NEventLayout.SetEvent)),postfix:new HarmonyMethod(typeof(ThunderInitiationArt),nameof(Style)));
 }
 static void Load(string path,string drawing){
  using var image=new Image();if(image.LoadSvgFromString("<svg xmlns='http://www.w3.org/2000/svg' width='256' height='256' viewBox='0 0 96 96'>"+drawing+"</svg>")!=Error.Ok)throw new InvalidDataException(path);
  string file=EnchantmentIconTexture.Cache(image,path.Contains("/enchantments/")?"enchantment-icons":"relic-icons");
  iconPaths.Add(path,EnchantmentIconTexture.ResourcePath(file));
 }
 static void Style(NEventLayout __instance,EventModel eventModel){
  if(eventModel is not ThunderSectInitiation)return;
  var portrait=__instance.GetNodeOrNull<TextureRect>("%Portrait");if(portrait!=null){portrait.Texture=null;portrait.ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize;portrait.Visible=false;}
  if(__instance.HasNode("ThunderSectBackground"))return;
  var bg=new TextureRect{Name="ThunderSectBackground",Texture=Background,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCovered,MouseFilter=Control.MouseFilterEnum.Ignore,Modulate=new Color(1,1,1,.55f)};
  __instance.AddChild(bg);__instance.MoveChild(bg,0);bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
 }
}
