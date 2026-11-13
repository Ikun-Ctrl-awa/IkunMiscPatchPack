using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static async Task SoulEnergyTextChecks(){
  foreach(int level in new[]{0,1}){
   using var f=XFixture(typeof(Pack.SoulRitual));var card=XCard(f,typeof(Pack.SoulRitual),level);var native=Up(ModelDb.Card<Bloodletting>().ToMutable(),level);
   string text=card.GetDescriptionForPile(PileType.None),reference=native.GetDescriptionForPile(PileType.None);
   var icons=Regex.Matches(text,@"\[img\][^\[]*_energy_icon\.png\[/img\]");
   Check("ritual energy uses native image tags level="+level,icons.Count==1+level&&Regex.IsMatch(reference,@"\[img\][^\[]*_energy_icon\.png\[/img\]")&&!QuText(text).Contains("点能量"));
   f.P.PlayerCombatState!.Energy=3;await DongniManualPlay(f,card);
   Check("ritual energy icon edit preserves immediate energy and X level="+level,f.P.PlayerCombatState.Energy==1+level&&RitualSouls(f).Length==3&&RitualSouls(f).All(c=>c.CurrentUpgradeLevel==level));
  }
  await XExpansionScreenshots();
 }
}
