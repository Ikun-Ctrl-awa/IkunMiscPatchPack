using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace AikunMiscPatchPack;

public static class HeartKeyTopBar {
 public static readonly string[] KeyNames=["RubyKey","EmeraldKey","SapphireKey"];
 // 一代原图的透明画布均为 64×64，裁出可见部分后仍保持原始拼合位置。
 public static readonly Vector2[] SlotPositions=[new(4,34),new(20,7),new(32,34)];
 static readonly string[] KeyTextures=["key_red.png","key_green.png","key_blue.png"];
 static readonly Dictionary<string,Texture2D> textures=new();
 static Texture2D Texture(string name){
  if(textures.TryGetValue(name,out var texture))return texture;
  using var stream=typeof(HeartKeyTopBar).Assembly.GetManifestResourceStream("heartkeys."+name)??throw new FileNotFoundException(name);
  using var bytes=new MemoryStream();stream.CopyTo(bytes);var image=new Image();
  if(image.LoadPngFromBuffer(bytes.ToArray())!=Error.Ok)throw new InvalidDataException(name);
  return textures[name]=ImageTexture.CreateFromImage(image);
 }
 static readonly ConditionalWeakTable<NTopBar,KeyDisplay> displays=new();
 static readonly List<WeakReference<NTopBar>> bars=new();
 static readonly Harmony H=new("Aikun.MiscPatchPack.HeartKeys");
 public static bool IsKey(RelicModel relic)=>relic.GetType().Namespace=="Act4Heart.Keys"&&KeyNames.Contains(relic.GetType().Name);
 public static void Install(){
  if(FusionQuestBlessingFix.Entry.Find("Act4Heart.Keys.RubyKey")==null)throw new MissingMemberException("Act4Heart 钥匙类型已改变");
  H.Patch(AccessTools.Method(typeof(NTopBar),nameof(NTopBar.Initialize)),postfix:new HarmonyMethod(typeof(HeartKeyTopBar),nameof(Initialize)));
  H.Patch(AccessTools.Method(typeof(NRelicInventory),"Add"),postfix:new HarmonyMethod(typeof(HeartKeyTopBar),nameof(HideKeys)));
  ((SceneTree)Engine.GetMainLoop()).ProcessFrame+=Tick;
 }
 public static void HideKeys(NRelicInventory __instance){
  foreach(var holder in __instance.RelicNodes.Where(n=>IsKey(n.Relic.Model))){holder.Visible=false;holder.FocusMode=Control.FocusModeEnum.None;}
 }
 static void Initialize(NTopBar __instance,IRunState runState){
  var player=LocalContext.GetMe(runState);if(player==null)return;
  if(!displays.TryGetValue(__instance,out var display)){
   display=new KeyDisplay(__instance,player);displays.Add(__instance,display);bars.Add(new(__instance));
  }
  display.Refresh();
 }
 static void Tick(){
  for(int i=bars.Count-1;i>=0;i--){
   if(!bars[i].TryGetTarget(out var bar)||!GodotObject.IsInstanceValid(bar)){bars.RemoveAt(i);continue;}
   if(bar.IsInsideTree())displays.GetValue(bar,_=>throw new InvalidOperationException()).Refresh();
  }
 }
 sealed class KeyDisplay {
  readonly Player player;readonly Control frame;readonly TextureRect[] icons=new TextureRect[3];
  public KeyDisplay(Control bar,Player player){
   this.player=player;
   frame=new Control{Name="AikunHeartKeyFrame",CustomMinimumSize=new(64,64),Size=new(64,64),MouseFilter=Control.MouseFilterEnum.Ignore,SizeFlagsVertical=Control.SizeFlags.ShrinkCenter};
   var left=bar.GetNode<Control>("LeftAlignedStuff");left.AddChild(frame);left.MoveChild(frame,0);
   frame.AddChild(new TextureRect{Name="KeySlots",Size=new(64,64),Texture=Texture("key_slots.png"),MouseFilter=Control.MouseFilterEnum.Ignore});
   for(int i=0;i<3;i++){
    var canonical=(RelicModel)ModelDb.GetById<RelicModel>(ModelDb.GetId(FusionQuestBlessingFix.Entry.Find("Act4Heart.Keys."+KeyNames[i])!));
    var texture=Texture(KeyTextures[i]);var region=texture.GetImage().GetUsedRect();
    var icon=new TextureRect{Name=KeyNames[i],Position=SlotPositions[i],Size=region.Size,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,Texture=new AtlasTexture{Atlas=texture,Region=region},MouseFilter=Control.MouseFilterEnum.Stop};
    frame.AddChild(icon);icons[i]=icon;
    icon.MouseEntered+=()=>NHoverTipSet.CreateAndShow(icon,canonical.HoverTips);icon.MouseExited+=NHoverTipSet.Clear;
   }
  }
  public void Refresh(){
   // 未获得时只留下原版深色凹槽；透明控件保留钥匙悬停说明。
   for(int i=0;i<3;i++)icons[i].SelfModulate=player.Relics.Any(r=>IsKey(r)&&r.GetType().Name==KeyNames[i])?Colors.White:Colors.Transparent;
   // 整理模组可能重新显示所有遗物；在布局前再次隐藏，钥匙数据不移动。
   if(frame.FindParent("TopBar")?.GetParent()?.FindChild("RelicInventory",true,false) is NRelicInventory inventory)HideKeys(inventory);
  }
 }
}
