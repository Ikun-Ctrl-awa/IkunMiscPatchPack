using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace Aikun020Probe;
public static partial class Entry {
 static void CompatInventory(){
  if(Minimal)return;
  string Safe(Func<string> value){try{return value();}catch(Exception e){return "ERROR: "+e.GetType().Name+" "+e.Message;}}
  var enchants=ModelDb.AllAbstractModelSubtypes.Where(t=>!t.IsAbstract&&typeof(EnchantmentModel).IsAssignableFrom(t)).OrderBy(t=>t.FullName).Select(t=>{
   var e=Enchant(t,1);var card=ModelDb.Card<Bash>().ToMutable();card.EnchantInternal(e,1);
   return new{Type=t.FullName,Id=e.Id.ToString(),Title=Safe(()=>e.Title.GetFormattedText()),Description=Safe(()=>e.DynamicDescription.GetFormattedText())};
  }).ToArray();
  File.WriteAllText(Output+"/enchantment-localization-inventory.json",JsonSerializer.Serialize(enchants,new JsonSerializerOptions{WriteIndented=true}));
  var cards=ModelDb.AllCards.Where(c=>c.GetType().Assembly.GetName().Name=="ColumbinaMod").Select(c=>new{Type=c.GetType().FullName,Id=c.Id.ToString(),c.HasPortrait,c.HasBetaPortrait,PortraitPath=Safe(()=>c.PortraitPath),BetaPortraitPath=Safe(()=>c.BetaPortraitPath),TexturePath=Safe(()=>c.Portrait.ResourcePath)}).ToArray();
  File.WriteAllText(Output+"/columbina-portrait-inventory.json",JsonSerializer.Serialize(cards,new JsonSerializerOptions{WriteIndented=true}));
 }
}
