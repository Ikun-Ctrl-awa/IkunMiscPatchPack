using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.addons.mega_text;

namespace AikunMiscPatchPack;

public static class EnchantmentView {
 const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static readonly List<MethodInfo> readers=new();
 public static void Discover(){
  readers.Clear();
  foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies()){
   var stack=assembly.GetType("MoreEnchantmentsMod.MoreEnchantStack");
   var read=stack?.GetMethod("AllEnchantments",Flags,null,[typeof(CardModel)],null);if(read!=null)readers.Add(read);
   // 支持多重附魔提供方的只读枚举接口；不调用添加、移除或执行效果的接口。
   if(assembly.GetName().Name=="MultiEnchantmentMod")foreach(var type in assembly.GetTypes())
    readers.AddRange(type.GetMethods(Flags).Where(m=>m.IsStatic&&m.Name is "GetAllEnchantments" or "GetEnchantments" or "AllEnchantments"
     &&m.GetParameters() is [{ParameterType:var p}]&&p==typeof(CardModel)&&typeof(IEnumerable<EnchantmentModel>).IsAssignableFrom(m.ReturnType)));
  }
 }
 public static EnchantmentModel[] Read(CardModel card){
  var roots=new List<EnchantmentModel>();if(card.Enchantment!=null)roots.Add(card.Enchantment);
  roots.AddRange(BloodEnchantmentRules.Extra(card));
  foreach(var reader in readers)if(reader.Invoke(null,[card]) is IEnumerable<EnchantmentModel> extras)roots.AddRange(extras);
  var seen=new HashSet<EnchantmentModel>(ReferenceEqualityComparer.Instance);var result=new List<EnchantmentModel>();
  void Visit(EnchantmentModel enchantment){
   if(!seen.Add(enchantment))return;
   var children=enchantment.GetType().GetProperties(Flags).Where(p=>p.GetIndexParameters().Length==0&&typeof(IEnumerable<EnchantmentModel>).IsAssignableFrom(p.PropertyType))
    .SelectMany(p=>(IEnumerable<EnchantmentModel>?)p.GetValue(enchantment)??[]).ToArray();
   if(children.Length>0){foreach(var child in children)Visit(child);return;}
   if(enchantment.GetType().Name!="ExtraEnchantCarrier")result.Add(enchantment);
  }
  foreach(var root in roots)Visit(root);return result.ToArray();
 }
 public static Texture2D ResolveIcon(EnchantmentModel enchantment){
  if(enchantment is BloodScorching)return BloodInitiationArt.Icon(BloodInitiationArt.ScorchingPath);
  if(enchantment is WeepingBlood)return BloodInitiationArt.Icon(BloodInitiationArt.WeepingPath);
  if(enchantment is ThunderArc)return ThunderInitiationArt.Icon(ThunderInitiationArt.ArcPath);
  var resolver=enchantment.GetType().Assembly.GetType("Plague.PlagueCustomEnchantIconResolver");
  return resolver?.GetMethod("TryResolve",Flags)?.Invoke(null,[enchantment]) as Texture2D??enchantment.Icon;
 }
}

public static class MultiEnchantIcons {
 static readonly ConditionalWeakTable<NCard,Display> displays=new();
 static readonly List<WeakReference<NCard>> cards=new();
 static readonly Harmony H=new("Aikun.MiscPatchPack.MultiEnchantIcons");
 static bool ticking;
 static void EnsureTicker(){if(ticking)return;ticking=true;((SceneTree)Engine.GetMainLoop()).ProcessFrame+=Tick;}
 public static void InstallOwn(Harmony h){
  h.Patch(AccessTools.Method(typeof(NCard),"UpdateEnchantmentVisuals"),postfix:new HarmonyMethod(typeof(MultiEnchantIcons),nameof(RefreshOwn)){priority=Priority.Last});EnsureTicker();
 }
 static void RefreshOwn(NCard __instance){if(__instance.Model!=null&&BloodEnchantmentRules.Extra(__instance.Model).Any())Refresh(__instance);}
 public static void Install(){
  EnchantmentView.Discover();var method=AccessTools.Method(typeof(NCard),"UpdateEnchantmentVisuals");
  H.Patch(method,postfix:new HarmonyMethod(typeof(MultiEnchantIcons),nameof(Refresh)){priority=Priority.Last,after=Harmony.GetPatchInfo(method)?.Owners.ToArray()??[]});
  EnsureTicker();
 }
 static void Tick(){
  for(int i=cards.Count-1;i>=0;i--){
   if(!cards[i].TryGetTarget(out var card)||!GodotObject.IsInstanceValid(card)){cards.RemoveAt(i);continue;}
   if(card.IsInsideTree())displays.GetValue(card,_=>throw new InvalidOperationException()).Update(card,false);
  }
 }
 public static void Refresh(NCard __instance){
  if(!displays.TryGetValue(__instance,out var display)){display=new();displays.Add(__instance,display);cards.Add(new(__instance));}
  display.Update(__instance,true);
 }
 sealed class Display {
  readonly List<Control> tabs=new();
  EnchantmentModel[] previous=[];string signature="";
  public void Update(NCard card,bool force){
   if(card.Model==null||card.EnchantmentTab==null)return;
   var enchantments=EnchantmentView.Read(card.Model);
   var state=string.Join(';',enchantments.Select(e=>$"{e.Amount}:{e.DisplayAmount}:{e.Status}:{e.ShowAmount}"))+"/"+card.Visibility;
   if(!force&&previous.SequenceEqual(enchantments)&&signature==state)return;
   signature=state;previous=enchantments;
   bool expanded=(enchantments.Length>1||BloodEnchantmentRules.Extra(card.Model).Any())&&card.Visibility==ModelVisibility.Visible;
   foreach(var tab in tabs)tab.Visible=false;
   if(!expanded){if(enchantments.Length==0)card.EnchantmentTab.Visible=false;else if(card.Visibility==ModelVisibility.Visible)card.EnchantmentTab.Visible=true;return;}
   var source=card.EnchantmentTab;
   int columns=Math.Max(1,(enchantments.Length+5)/6),rows=(int)Math.Ceiling((double)enchantments.Length/columns);
   float step=Math.Min(44,220f/Math.Max(1,rows-1));
   for(int i=0;i<enchantments.Length;i++){
    Control tab;
    if(i<tabs.Count)tab=tabs[i];else{
     tab=(Control)source.Duplicate();tab.Name="AikunEnchantment"+i;tab.UniqueNameInOwner=false;tab.MouseFilter=Control.MouseFilterEnum.Ignore;
     if(tab.Material!=null)tab.Material=(Material)tab.Material.Duplicate();
     source.GetParent().AddChild(tab);tabs.Add(tab);
    }
    var enchantment=enchantments[i];var image=tab.GetNode<TextureRect>("Icon");var label=tab.GetNode<MegaLabel>("Label");
    tab.Position=source.Position+new Vector2(-(i/rows)*54,(i%rows)*step);tab.Scale=source.Scale;tab.Rotation=source.Rotation;
    image.Texture=EnchantmentView.ResolveIcon(enchantment);label.SetTextAutoSize(enchantment.DisplayAmount.ToString());label.Visible=enchantment.ShowAmount;
    bool disabled=enchantment.Status==EnchantmentStatus.Disabled;
    tab.Modulate=new Color(1,1,1,disabled?.55f:1);image.UseParentMaterial=false;label.SelfModulate=disabled?Colors.Gray:Colors.White;
    tab.Visible=true;
   }
   source.Visible=false;
  }
 }
}
