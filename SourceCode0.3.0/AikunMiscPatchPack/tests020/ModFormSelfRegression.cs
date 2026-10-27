using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using STS2RitsuLib.Models.Capabilities;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Unlocks;
using STS2RitsuLib.Settings;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static async Task SelfGeneration(){
  var savedGroups=Pack.ModFormContent.Groups;bool savedSelf=Pack.Settings.StartupModFormIncludeSelf,oldMode=TestMode.IsOn;
  var listeners=AccessTools.Field(typeof(CombatStateTracker),"CombatStateChanged");var frontend=listeners.GetValue(CombatManager.Instance.StateTracker);
  try{
   listeners.SetValue(CombatManager.Instance.StateTracker,null);TestMode.IsOn=true;Pack.Settings.StartupModFormIncludeSelf=true;
   var p=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var state=RunState.CreateForTest([p],seed:"SELF-CANDIDATE");RunManager.Instance.SetUpTest(state,new NetSingleplayerGameService());
   var combat=new CombatState(runState:state);combat.AddPlayer(p);combat.AddCreature(combat.CreateCreature(ModelDb.Monster<CalcifiedCultist>().ToMutable(),CombatSide.Enemy,"self-fixture"));CombatManager.Instance.SetUpCombat(combat);
   var turn=AccessTools.Field(typeof(CombatManager),"_turnState").GetValue(CombatManager.Instance)!;turn.GetType().GetProperty("IsInProgress")!.SetValue(turn,true);
   typeof(Pack.ModFormContent).GetProperty(nameof(Pack.ModFormContent.Groups))!.SetValue(null,Pack.ModFormContent.GroupCards([ModelDb.Card<Pack.ModForm>()]));
   await Pack.ModFormContent.Generate(p,3);
   var hand=p.PlayerCombatState!.Hand;var generated=hand.Cards.Single();
   Check("self generation creates exactly one upgraded form",generated is Pack.ModForm&&generated.CurrentUpgradeLevel==3);
   Check("self generated free ethereal exhaust",generated.EnergyCost.GetWithModifiers(CostModifiers.All)==0&&generated.Keywords.Contains(CardKeyword.Ethereal)&&generated.Keywords.Contains(CardKeyword.Exhaust));
   var restored=Restore(generated);Check("self generated upgrade state survives save",restored is Pack.ModForm&&restored.CurrentUpgradeLevel==3&&restored.Capability<Pack.GeneratedCardUpgrade>()?.UpgradeLimit==3);
   Check("generating self does not automatically play it",p.Creature.Powers.OfType<Pack.ModFormPower>().Count()==0);
   await (Task)typeof(Pack.ModForm).GetMethod("OnPlay",F)!.Invoke(generated,[new ThrowingPlayerChoiceContext(),null])!;
   var power=p.Creature.Powers.OfType<Pack.ModFormPower>().Single();
   Check("playing generated form generates one more and retains level",hand.Cards.Count==2&&hand.Cards.Last() is Pack.ModForm&&hand.Cards.Last().CurrentUpgradeLevel==3&&power.GenerationUpgradeLevel==3);
   await power.AfterSideTurnStart(CombatSide.Player,[p.Creature],combat);
   Check("self generated form turn trigger creates one card",hand.Cards.Count==3&&hand.Cards.Last() is Pack.ModForm&&hand.Cards.Last().CurrentUpgradeLevel==3);
  }finally{
   Pack.Settings.StartupModFormIncludeSelf=savedSelf;typeof(Pack.ModFormContent).GetProperty(nameof(Pack.ModFormContent.Groups))!.SetValue(null,savedGroups);ClearPoolCache();
   try{if(RunManager.Instance.DebugOnlyGetState()!=null){CombatManager.Instance.Reset(false);RunManager.Instance.CleanUp();}}
   finally{TestMode.IsOn=oldMode;listeners.SetValue(CombatManager.Instance.StateTracker,frontend);}
  }
 }
 static void ClearPoolCache(){
  AccessTools.Field(typeof(CardPoolModel),"_allCardIds").SetValue(ModelDb.CardPool<ColorlessCardPool>(),null);
  AccessTools.Field(typeof(ModelDb),"_allCards").SetValue(null,null);
 }
 static void SelfCandidateRules(){
  var groups=Pack.ModFormContent.Groups;bool saved=Pack.Settings.StartupModFormIncludeSelf,enabled=Pack.Settings.Active("mod_form");
  var groupProperty=typeof(Pack.ModFormContent).GetProperty(nameof(Pack.ModFormContent.Groups))!;
  try{
   Check("self option defaults off including old config",!new Pack.PackConfig().ModFormIncludeSelf&&!JsonSerializer.Deserialize<Pack.PackConfig>("{\"Enabled\":{}}")!.ModFormIncludeSelf);
   var config=JsonSerializer.Deserialize<Pack.PackConfig>(JsonSerializer.Serialize(new Pack.PackConfig{ModFormIncludeSelf=true}))!;
   Check("self option survives JSON",config.ModFormIncludeSelf);
   var section=ModSettingsRegistry.GetPages().Single(p=>p.Id==Pack.Settings.Id+".misc").Sections.Single(s=>s.Id=="mod_form");
   Check("self toggle inside mod form settings",section.Entries.Any(e=>e.Id=="include_self"));
   var form=ModelDb.Card<Pack.ModForm>();
   Pack.Settings.StartupModFormIncludeSelf=false;
   Check("self excluded when switch off",!Pack.ModFormContent.IsCandidate(form)&&Pack.ModFormContent.GroupCards([form]).Length==0);
   groupProperty.SetValue(null,Array.Empty<Pack.ModCardGroup>());ClearPoolCache();
   Check("self-only pool hidden when switch off",!ModelDb.CardPool<ColorlessCardPool>().AllCards.Any(c=>c is Pack.ModForm)&&!form.CanBeGeneratedInCombat);
   Pack.Settings.StartupModFormIncludeSelf=true;
   // 空池状态下先建立候选，防止自依赖导致开关开启仍然无法进入卡池。
   Check("self eligible while group cache empty",Pack.ModFormContent.IsCandidate(form)&&form.CanBeGeneratedInCombat);
   var ownGroup=Pack.ModFormContent.GroupCards(ModelDb.AllCards.Append(form).Where(c=>c is Pack.ModForm));
   groupProperty.SetValue(null,ownGroup);ClearPoolCache();
   Check("self grouped once under pack mod",ownGroup.Length==1&&ownGroup[0].ModId==Pack.Settings.Id&&ownGroup[0].Cards.Length==1);
   Check("self-only complete and unlocked pool visible when on",ModelDb.CardPool<ColorlessCardPool>().AllCards.Any(c=>c is Pack.ModForm)&&ModelDb.CardPool<ColorlessCardPool>().GetUnlockedCards(UnlockState.all,CardMultiplayerConstraint.SingleplayerOnly).Any(c=>c is Pack.ModForm));
   Check("self can be selected when on",Pack.ModFormContent.Choose(ownGroup,new Rng(12)) is Pack.ModForm);
   Check("base card still excluded when self enabled",!Pack.ModFormContent.IsCandidate(ModelDb.Card<Bash>()));
   Pack.Settings.Startup["mod_form"]=false;ClearPoolCache();
   Check("master switch still blocks self and pool",!Pack.ModFormContent.IsCandidate(form)&&!form.CanBeGeneratedInCombat&&!ModelDb.CardPool<ColorlessCardPool>().AllCards.Any(c=>c is Pack.ModForm));
  }finally{
   Pack.Settings.StartupModFormIncludeSelf=saved;Pack.Settings.Startup["mod_form"]=enabled;groupProperty.SetValue(null,groups);ClearPoolCache();
  }
 }
}
