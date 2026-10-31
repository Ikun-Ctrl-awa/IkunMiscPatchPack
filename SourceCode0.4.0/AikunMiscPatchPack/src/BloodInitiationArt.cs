using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;

namespace AikunMiscPatchPack;
public static class BloodInitiationArt {
 public static string BackgroundPath=>EmbeddedEventBackgrounds.PathFor("eventart.blood_sect_initiation.png");
 public const string BloodTokenPath="res://AikunMiscPatchPack/images/relics/blood_sect_token.png";
 public const string ThunderTokenPath="res://AikunMiscPatchPack/images/relics/thunder_sect_token.png";
 public const string BladePath="res://AikunMiscPatchPack/images/relics/vampiric_blade.png";
 public const string ScorchingPath="res://AikunMiscPatchPack/images/enchantments/blood_scorching.png";
 public const string WeepingPath="res://AikunMiscPatchPack/images/enchantments/weeping_blood.png";
 public static Texture2D Background=>PreloadManager.Cache.GetTexture2D(BackgroundPath);
 static readonly Dictionary<string,string> iconPaths=new();
 public static string IconPath(string path)=>iconPaths.GetValueOrDefault(path,path);
 public static Texture2D Icon(string path)=>PreloadManager.Cache.GetTexture2D(IconPath(path));
 public static void Initialize(Harmony h){
  _=BackgroundPath;
  Load(BloodTokenPath,"<path d='m27 7 42 0 9 68-30 15-30-15Z' fill='#682b32' stroke='#e6ba7a' stroke-width='4'/><path d='M48 20c-7 13-17 22-17 32a17 17 0 0 0 34 0c0-10-10-19-17-32Z' fill='#ea6964'/>");
  Load(ThunderTokenPath,"<path d='m27 7 42 0 9 68-30 15-30-15Z' fill='#303a6d' stroke='#e6cf8c' stroke-width='4'/><path d='m54 17-23 37h18l-7 25 25-37H49Z' fill='#ffe8a0'/>");
  Load(BladePath,"<path d='M73 7 34 54l10 9 42-48Z' fill='#e6a9a4' stroke='#6b3236' stroke-width='3'/><path d='m22 49 27 24' stroke='#e8c27b' stroke-width='7'/><path d='m33 61-18 22' stroke='#813b34' stroke-width='10'/><path d='M65 52c-5 8-10 13-10 19a10 10 0 0 0 20 0c0-6-5-11-10-19Z' fill='#b51f40'/>");
  Load(ScorchingPath,"<path d='M49 6C64 26 79 43 77 63a29 29 0 0 1-58-1c-1-14 9-26 18-36 0 15 6 18 12 20 8-13 3-23 0-40Z' fill='#ed713b' stroke='#ffe1a0' stroke-width='4'/><path d='M48 45c13 17 15 25 0 35-15-8-14-19 0-35Z' fill='#ffe7a2'/>");
  Load(WeepingPath,"<path d='M48 7C36 29 18 45 18 62a30 30 0 0 0 60 0C78 45 60 29 48 7Z' fill='#b32742' stroke='#f6b6ac' stroke-width='4'/><path d='M48 42v32M32 58h32' stroke='#ffe8bd' stroke-width='7'/>");
  h.Patch(AccessTools.Method(typeof(NEventLayout),nameof(NEventLayout.SetEvent)),postfix:new HarmonyMethod(typeof(BloodInitiationArt),nameof(Style)));
 }
 static void Load(string path,string drawing){
  using var image=new Image();if(image.LoadSvgFromString("<svg xmlns='http://www.w3.org/2000/svg' width='256' height='256' viewBox='0 0 96 96'>"+drawing+"</svg>")!=Error.Ok)throw new InvalidDataException(path);
  string file=EnchantmentIconTexture.Cache(image,path.Contains("/enchantments/")?"enchantment-icons":"relic-icons");
  iconPaths.Add(path,EnchantmentIconTexture.ResourcePath(file));
 }
 static void Style(NEventLayout __instance,EventModel eventModel){
  if(eventModel is not BloodSectInitiation)return;
  var portrait=__instance.GetNodeOrNull<TextureRect>("%Portrait");if(portrait!=null){portrait.Texture=null;portrait.ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize;portrait.Visible=false;}
  if(__instance.HasNode("BloodSectBackground"))return;
  var bg=new TextureRect{Name="BloodSectBackground",Texture=Background,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCovered,MouseFilter=Control.MouseFilterEnum.Ignore,Modulate=new Color(1,1,1,.55f)};
  __instance.AddChild(bg);__instance.MoveChild(bg,0);bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
 }
}
