using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;

namespace AikunMiscPatchPack;

// 沿用本包的 SVG 图标方案：祝福是发光牌组，思考是带问号的思考气泡。
public static class DongniOfficeArt {
 public const string BlessingPath="res://AikunMiscPatchPack/images/relics/dongni_blessing.png";
 public const string ThinkingPath="res://AikunMiscPatchPack/images/relics/dongni_thinking.png";
 public static string BackgroundPath=>EmbeddedEventBackgrounds.PathFor("eventart.dongni_office.png");
 public const float BackgroundOpacity=.55f;
 public static Texture2D Background=>PreloadManager.Cache.GetTexture2D(BackgroundPath);
 static readonly Dictionary<string,string> iconPaths=new();
 public static string IconPath(string path)=>iconPaths.GetValueOrDefault(path,path);
 public static void Initialize(Harmony harmony){
  _=BackgroundPath;
  harmony.Patch(AccessTools.Method(typeof(NEventLayout),nameof(NEventLayout.SetEvent)),postfix:new HarmonyMethod(typeof(DongniOfficeArt),nameof(StyleOffice)));
  Load("<path d='M19 34 58 22 77 70 38 82Z' fill='#694b32' stroke='#ffdc80' stroke-width='4'/><path d='M28 25 69 26 67 79 27 78Z' fill='#f1d69c' stroke='#aa7837' stroke-width='4'/><path d='M38 39h19M38 48h19M38 57h15' stroke='#916c45' stroke-width='4'/><path d='m47 3 4 10 11 2-9 7 1 10-8-6-10 5 3-10-7-8 11-1Z' fill='#ffe597'/>",BlessingPath);
  Load("<path d='M17 21Q22 8 45 8h12Q83 10 83 35v15Q81 70 59 70H43L25 84l2-18Q11 60 11 44V32Q11 26 17 21Z' fill='#66508e' stroke='#d9c9ff' stroke-width='4'/><path d='M37 31q0-13 12-13 14 0 14 12 0 8-12 15v8' fill='none' stroke='#f7e8bd' stroke-width='7' stroke-linecap='round'/><circle cx='51' cy='62' r='4' fill='#f7e8bd'/>",ThinkingPath);
 }
 static void StyleOffice(NEventLayout __instance,EventModel eventModel){
  if(eventModel is not DongniOffice)return;
  // 背景按视口铺满，半透明显示；原生标题、描述、选项及其动画保持前景层次。
  var portrait=__instance.GetNodeOrNull<TextureRect>("%Portrait");
  if(portrait!=null){portrait.Texture=null;portrait.ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize;portrait.Visible=false;}
  if(__instance.HasNode("DongniOfficeBackground"))return;
  var background=new TextureRect{Name="DongniOfficeBackground",Texture=Background,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCovered,MouseFilter=Control.MouseFilterEnum.Ignore,Modulate=new Color(1,1,1,BackgroundOpacity)};
  __instance.AddChild(background);__instance.MoveChild(background,0);background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
 }
 static void Load(string content,string path){
  using var image=new Image();if(image.LoadSvgFromString("<svg xmlns='http://www.w3.org/2000/svg' width='256' height='256' viewBox='0 0 96 96'>"+content+"</svg>")!=Error.Ok)throw new InvalidDataException("东尼遗物图标无效。");
  string file=EnchantmentIconTexture.Cache(image,"relic-icons");iconPaths.Add(path,EnchantmentIconTexture.ResourcePath(file));
 }
}
