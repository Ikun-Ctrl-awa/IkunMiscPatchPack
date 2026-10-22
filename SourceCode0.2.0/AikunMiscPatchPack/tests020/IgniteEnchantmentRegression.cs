using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using Pack=AikunMiscPatchPack;
namespace Aikun020Probe;
public static partial class Entry {
 static async Task IgniteEnchantmentChecks(){
  if(Minimal){Check("ignite missing YUI safely skipped",Pack.Settings.Status(Pack.Options.All.Single(o=>o.Key=="yui_ignite")).Contains("未装载相关模组"));return;}
  int first=passes.Count;var evidence=new List<object>();bool old=TestMode.IsOn;ulong? oldLocal=LocalContext.NetId;
  var listeners=AccessTools.Field(typeof(CombatStateTracker),"CombatStateChanged");var frontend=listeners.GetValue(CombatManager.Instance.StateTracker);
  try{
   listeners.SetValue(CombatManager.Instance.StateTracker,null);TestMode.IsOn=true;
   var type=Find("YuiExtra.Enhancement.IgniteEnchantment");var soulType=Find("YUIEXTRA.Necrobinder.Cards.SoulFlame");
   var registry=Find("YuiExtra.Enhancement.GeneratedCardEnchantmentRegistry");var registrations=(System.Collections.IDictionary)AccessTools.Field(registry,"CardTypes").GetValue(null)!;
   const string key="YuiExtra.Enhancement.IgniteEnchantment.GeneratedCard";
   Check("ignite original Cards extension registers SoulFlame",ReferenceEquals(registrations[key],soulType));
   registrations.Remove(key);
   Check("ignite missing Cards registration reproduces null generated card",AccessTools.Method(registry,"CreateCard").Invoke(null,[key,new object(),null])==null);
   registrations[key]=soulType;
   Check("ignite patch installed",Pack.RuntimeLoader.States.GetValueOrDefault("yui_ignite")=="已应用。");
   var p=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var s=RunState.CreateForTest([p],seed:"IGNITE-PERSISTENCE");RunManager.Instance.SetUpTest(s,new NetSingleplayerGameService());LocalContext.NetId=p.NetId;
   var room=new CombatRoom(s.Act.AllRegularEncounters.First().ToMutable(),s);s.PushRoom(room);
   async Task<CardModel> Deck(CardModel canonical){var c=s.CreateCard(canonical,p);await CardPileCmd.Add(c,PileType.Deck,skipVisuals:true);return c;}
   var victim=await Deck(ModelDb.Card<Bash>());victim.EnchantInternal(Enchant(type,1),1);
   Pack.Settings.Startup["yui_ignite"]=false;await victim.Enchantment!.AfterCombatEnd(room);Check("ignite disabled reproduces permanent enchantment deletion",victim.Enchantment==null);Pack.Settings.Startup["yui_ignite"]=true;
   var permanent=await Deck(ModelDb.Card<StrikeIronclad>());permanent.EnchantInternal(Enchant(type,1),1);
   var multiple=await Deck(ModelDb.Card<DefendIronclad>());multiple.EnchantInternal(ModelDb.Enchantment<Sharp>().ToMutable(),2);Extra(multiple,Enchant(type,1));
   Pack.Settings.Startup["yui_ignite"]=false;
   Check("ignite old zero-parameter reflection loses card preview",permanent.Enchantment!.HoverTips.Count()==1);
   Pack.Settings.Startup["yui_ignite"]=true;
   Check("ignite hover tip resolves original generated card",permanent.Enchantment!.HoverTips.Count()>1);
   for(int battle=1;battle<=2;battle++){
    var combat=new CombatState(runState:s);combat.AddPlayer(p);var enemy=combat.CreateCreature(ModelDb.Monster<CalcifiedCultist>().ToMutable(),CombatSide.Enemy,"ignite-target");combat.AddCreature(enemy);enemy.SetMaxHpInternal(10000);enemy.SetCurrentHpInternal(10000);
    CombatManager.Instance.SetUpCombat(combat);var turn=AccessTools.Field(typeof(CombatManager),"_turnState").GetValue(CombatManager.Instance)!;AccessTools.Property(turn.GetType(),"IsInProgress").SetValue(turn,true);
    var choice=new ThrowingPlayerChoiceContext();
    var card=PileType.Draw.GetPile(p).Cards.Single(c=>ReferenceEquals(c.DeckVersion,permanent));var extra=PileType.Draw.GetPile(p).Cards.Single(c=>ReferenceEquals(c.DeckVersion,multiple));
    Check("ignite new combat starts enabled "+battle,card.Enchantment?.Status==EnchantmentStatus.Normal);
    await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);int energy=p.PlayerCombatState!.Energy;
    await card.OnPlayWrapper(choice,enemy,false,default,skipCardPileVisuals:true).WaitAsync(TimeSpan.FromSeconds(5));
    int Count(PileType pile)=>pile.GetPile(p).Cards.Count(c=>c.GetType()==soulType);
    Check("ignite actual card play generates one SoulFlame in each pile "+battle,Count(PileType.Hand)==1&&Count(PileType.Draw)==1&&Count(PileType.Discard)==1);
    Check("ignite direct hand generation does not trigger draw energy "+battle,p.PlayerCombatState.Energy==energy);
    Check("ignite marks first trigger consumed "+battle,card.Enchantment!.Status==EnchantmentStatus.Disabled);
    await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);await card.OnPlayWrapper(choice,enemy,false,default,skipCardPileVisuals:true).WaitAsync(TimeSpan.FromSeconds(5));
    Check("ignite repeated card play does not generate again "+battle,Count(PileType.Hand)+Count(PileType.Draw)+Count(PileType.Discard)==3);
    var flame=PileType.Draw.GetPile(p).Cards.Single(c=>c.GetType()==soulType);await CardPileCmd.Add(flame,PileType.Draw,CardPilePosition.Top,skipVisuals:true);await CardPileCmd.Draw(choice,1,p);
    Check("ignite original SoulFlame grants one energy when drawn "+battle,p.PlayerCombatState.Energy==energy+1);
    Check("ignite original SoulFlame retains ethereal and skill type "+battle,flame.Keywords.Contains(CardKeyword.Ethereal)&&flame.Type==CardType.Skill);
    await CardPileCmd.Add(extra,PileType.Hand,skipVisuals:true);await extra.OnPlayWrapper(choice,null,false,default,skipCardPileVisuals:true).WaitAsync(TimeSpan.FromSeconds(5));
    Check("ignite extra enchantment triggers with another primary "+battle,Count(PileType.Hand)+Count(PileType.Draw)+Count(PileType.Discard)==6);
    var temporary=combat.CreateCard(ModelDb.Card<DefendIronclad>(),p);temporary.EnchantInternal(Enchant(type,1),1);await CardPileCmd.Add(temporary,PileType.Hand,skipVisuals:true);
    await Hook.AfterCombatEnd(s,combat,room);
    Check("ignite permanent primary survives native combat end "+battle,permanent.Enchantment?.GetType()==type&&permanent.Enchantment.Status==EnchantmentStatus.Normal);
    Check("ignite permanent extra and other primary survive native combat end "+battle,multiple.Enchantment is Sharp&&Pack.EnchantmentView.Read(multiple).Any(e=>e.GetType()==type));
    Check("ignite combat-only temporary enchantment still expires "+battle,temporary.Enchantment==null);
    Check("ignite primary survives actual JSON save roundtrip "+battle,Restore(permanent).Enchantment?.GetType()==type);
    Check("ignite extra survives actual JSON save roundtrip "+battle,Pack.EnchantmentView.Read(Restore(multiple)).Any(e=>e.GetType()==type));
    evidence.Add(new{Battle=battle,SoulFlames=Count(PileType.Hand)+Count(PileType.Draw)+Count(PileType.Discard),EnergyGain=p.PlayerCombatState.Energy-energy,PermanentRetained=permanent.Enchantment!=null,TemporaryRemoved=temporary.Enchantment==null});
    CombatManager.Instance.Reset(false);
   }
   File.WriteAllText(Output+"/ignite-enchantment-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Failed=0,Checks=passes.Skip(first),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
  }finally{
   Pack.Settings.Startup["yui_ignite"]=true;
   try{if(RunManager.Instance.DebugOnlyGetState()!=null){CombatManager.Instance.Reset(false);RunManager.Instance.CleanUp();}}
   finally{LocalContext.NetId=oldLocal;TestMode.IsOn=old;listeners.SetValue(CombatManager.Instance.StateTracker,frontend);}
  }
 }
}
