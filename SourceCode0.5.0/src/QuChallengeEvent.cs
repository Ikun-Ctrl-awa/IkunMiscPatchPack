using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;

namespace IkunMiscPatchPack;

public sealed class QuChallengeEvent : EventModel {
 bool choiceCommitted;
 // 进入战斗的事件使用原生共同投票，每名玩家各自结算回复或撤离奖励。
 public override bool IsShared=>true;
 public override EventLayoutType LayoutType=>EventLayoutType.Combat;
 public override EncounterModel CanonicalEncounter=>ModelDb.Encounter<PhrogParasiteElite>();
 public override bool IsAllowed(IRunState runState)=>Settings.Active(QuChallengeContent.Key)&&runState.CurrentActIndex==0;
 protected override IReadOnlyList<EventOption> GenerateInitialOptions()=>[
  new EventOption(this,Fight,Id.Entry+".pages.INITIAL.options.FIGHT"),
  new EventOption(this,Retreat,Id.Entry+".pages.INITIAL.options.RETREAT",new CardHoverTip(ModelDb.Card<QuCurse>()))
 ];
 async Task Fight(){
  if(IsFinished||choiceCommitted)return;choiceCommitted=true;
  var player=Owner??throw new InvalidOperationException("区事件尚未指定玩家。");
  await CreatureCmd.Heal(player.Creature,player.Creature.MaxHp*0.30m);
  // 原生精英遭遇负责怪物、难度、金币、遗物、卡牌与药水概率，不追加重复奖励。
  EnterCombatWithoutExitingEvent<PhrogParasiteElite>([],shouldResumeAfterCombat:false);
 }
 async Task Retreat(){
  if(IsFinished||choiceCommitted)return;choiceCommitted=true;
  var player=Owner??throw new InvalidOperationException("区事件尚未指定玩家。");
  var relic=RelicFactory.PullNextRelicFromFront(player,Rng).ToMutable();
  await RelicCmd.Obtain(relic,player);
  // 明确指定事件代价，即使关闭“区”的随机获取开关也保留该分支的诅咒。
  await CardPileCmd.AddCurseToDeck<QuCurse>(player);
  SetEventFinished(L10NLookup(Id.Entry+".pages.RETREAT.description"));
 }
}

public static class QuChallengeContent {
 public const string Key="qu_challenge_event";
 public static void Initialize(){
  // 总是登记模型；开关仅限制后续遇到，既有事件和嵌套战斗仍可读档。
  RitsuLibFramework.CreateContentPack(Settings.Id+".QuChallenge").SharedEvent<QuChallengeEvent>().Apply();
  var h=new Harmony(Settings.Id+".QuChallenge");
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.Initialize)),postfix:new HarmonyMethod(typeof(QuChallengeContent),nameof(Localize)));
  h.Patch(AccessTools.Method(typeof(LocManager),nameof(LocManager.SetLanguage)),postfix:new HarmonyMethod(typeof(QuChallengeContent),nameof(Localize)));
  if(LocManager.Instance!=null)Localize();
  if(Settings.Active(Key))RuntimeLoader.States[Key]="已应用。";
 }
 public static void Localize(){
  if(LocManager.Instance==null)return;string id=ModelDb.GetId<QuChallengeEvent>().Entry;
  LocManager.Instance.GetTable("events").MergeWith(new(){
   [id+".title"]="！？区区？！",
   [id+".pages.INITIAL.description"]="你惊扰了区！！！",
   [id+".pages.INITIAL.options.FIGHT.title"]="迎战！",
   [id+".pages.INITIAL.options.FIGHT.description"]="回复[green]30%[/green]最大生命值，进入一场[red]艰难的战斗[/red]。胜利后获得精英战斗奖励。",
   [id+".pages.INITIAL.options.RETREAT.title"]="搜、撤，不打。",
   [id+".pages.INITIAL.options.RETREAT.description"]="获得[gold]1件随机遗物[/gold]，将1张[red]区[/red]加入你的牌组。",
   [id+".pages.RETREAT.description"]="你带着搜到的遗物撤离了。区也跟上来了。"
  });
 }
}
