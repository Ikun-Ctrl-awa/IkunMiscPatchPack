using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static void SwordNoPreview(ref bool showPreview)=>showPreview=false;
 static async Task StoneSwordQuestChecks(){
  int start=passes.Count;var evidence=new List<object>();var option=Pack.Options.All.Single(o=>o.Key==Pack.StoneSwordQuestFix.Key);
  if(Minimal){Check("stone sword absent optional mod safely skipped",Pack.Settings.Status(option).Contains("未装载相关模组"));return;}
  Check("stone sword independent patch installed",Pack.RuntimeLoader.States.GetValueOrDefault(option.Key)=="已应用。");
  Type type=Find("YuWanCard.Cards.Quest.StoneSword");var counter=AccessTools.Property(type,"ElitesDefeated");
  CardModel Quest(int progress=0){var q=ModelDb.GetById<CardModel>(ModelDb.GetId(type)).ToMutable();counter.SetValue(q,progress);return q;}
  int Progress(CardModel root)=>FusionQuestBlessingFix.Entry.Descendants(root).Prepend(root).Where(type.IsInstanceOfType).Select(q=>(int)counter.GetValue(q)!).Sum();
  var h=new Harmony("Aikun020Probe.Sword.NoPreview");bool enabled=Pack.Settings.Startup[option.Key],normalizer=Pack.Settings.Startup["fusion_quest"];
  // 测试夹具没有战后选牌 UI，只关闭原生命令的动画参数，业务方法完整执行。
  h.Patch(AccessTools.Method(typeof(CardPileCmd),nameof(CardPileCmd.RemoveFromDeck),[typeof(CardModel),typeof(bool)]),prefix:new HarmonyMethod(typeof(Entry),nameof(SwordNoPreview)));
  try{
   foreach(bool patch in new[]{false,true})foreach(bool fused in new[]{false,true}){
    using var f=new BloodFixture();Pack.Settings.Startup[option.Key]=patch;
    var root=fused?Fuse(Quest(),ModelDb.Card<StrikeIronclad>().ToMutable()):Quest();f.Run.AddCard(root,f.P);await CardPileCmd.Add(root,PileType.Deck,skipVisuals:true);
    var room=new CombatRoom(f.Run.Act.AllEliteEncounters.First().ToMutable(),f.Run);await root.AfterCombatVictory(room);
    Check($"stone sword baseline and repaired progress patch={patch} fused={fused}",Progress(root)==(fused&&!patch?0:1));
   }
   Pack.Settings.Startup[option.Key]=true;
   foreach(string shape in new[]{"single","fused","nested","duplicate-quests"})foreach(bool normalize in new[]{false,true}){
    using var f=new BloodFixture();Pack.Settings.Startup["fusion_quest"]=normalize;
    var root=shape switch{"single"=>Quest(),"fused"=>Fuse(Quest(),ModelDb.Card<StrikeIronclad>().ToMutable()),"nested"=>Fuse(Fuse(Quest(),ModelDb.Card<DefendIronclad>().ToMutable()),ModelDb.Card<StrikeIronclad>().ToMutable()),_=>Fuse(Quest(),Quest(),ModelDb.Card<StrikeIronclad>().ToMutable())};
    f.Run.AddCard(root,f.P);await CardPileCmd.Add(root,PileType.Deck,skipVisuals:true);string label="stone sword "+shape+" normalizer="+normalize;
    var regular=new CombatRoom(f.Run.Act.AllRegularEncounters.First().ToMutable(),f.Run);await root.AfterCombatVictory(regular);var boss=new CombatRoom(f.Run.Act.AllBossEncounters.First().ToMutable(),f.Run);await root.AfterCombatVictory(boss);Check(label+" ignores regular and boss wins",Progress(root)==0);
    int quests=shape=="duplicate-quests"?2:1;
    for(int win=1;win<=3;win++){
     f.Run.AppendToMapPointHistory(MapPointType.Elite,RoomType.Elite,null);var room=new CombatRoom(f.Run.Act.AllEliteEncounters.First().ToMutable(),f.Run);
     var clone=(CardModel)root.ClonePreservingMutability();if(clone.Owner==null)clone.Owner=f.P;clone.DeckVersion=root;
     // 同一战斗的永久实体、战斗副本和重复回调只能结算一次。
     await Hook.AfterCombatVictory(f.Run,f.Combat,room);
     Check(label+" native victory dispatcher advances permanent quest "+win,Progress(root)==quests*win);
     await Task.WhenAll(root.AfterCombatVictory(room),clone.AfterCombatVictory(room));await root.AfterCombatVictory(room);
     Check(label+" progress after win "+win,Progress(root)==quests*win);
     if(win<3){Check(label+" deck preserved before completion "+win,f.P.Deck.Cards.Contains(root)&&!f.P.Relics.OfType<SwordOfJade>().Any());var loaded=Restore(root);Check(label+" progress survives save "+win,Progress(loaded)==quests*win);}
     else{
      Check(label+" completion removes permanent card once",!f.P.Deck.Cards.Contains(root));
      Check(label+" completion grants reward per quest",f.P.Relics.OfType<SwordOfJade>().Count()==quests);
      var completed=f.Run.CurrentMapPointHistoryEntry!.GetEntry(f.P.NetId).CompletedQuests;Check(label+" completion logs quest id",completed.Count(id=>id==ModelDb.GetId(type))==quests&&!completed.Contains(root.Id)||shape=="single"&&completed.Count(id=>id==ModelDb.GetId(type))==1);
      await clone.AfterCombatVictory(new CombatRoom(f.Run.Act.AllEliteEncounters.First().ToMutable(),f.Run));Check(label+" removed quest cannot reward again",f.P.Relics.OfType<SwordOfJade>().Count()==quests);
     }
    }
    evidence.Add(new{Shape=shape,FusionNormalizer=normalize,Progress=Progress(root),Rewards=f.P.Relics.OfType<SwordOfJade>().Count(),Removed=!f.P.Deck.Cards.Contains(root)});
   }
   using(var f=new BloodFixture()){
    var old=Fuse(Quest(2),ModelDb.Card<StrikeIronclad>().ToMutable());old=Restore(old);f.Run.AddCard(old,f.P);await CardPileCmd.Add(old,PileType.Deck,skipVisuals:true);await old.AfterCombatVictory(new CombatRoom(f.Run.Act.AllEliteEncounters.First().ToMutable(),f.Run));Check("stone sword loaded old fusion completes from stored two elite wins",!f.P.Deck.Cards.Contains(old)&&f.P.Relics.OfType<SwordOfJade>().Count()==1);
    var generated=f.Combat.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(type)),f.P);counter.SetValue(generated,2);await generated.AfterCombatVictory(new CombatRoom(f.Run.Act.AllEliteEncounters.First().ToMutable(),f.Run));Check("stone sword combat generated quest cannot grant permanent reward",f.P.Relics.OfType<SwordOfJade>().Count()==1&&(int)counter.GetValue(generated)! ==2);
   }
  }finally{Pack.Settings.Startup[option.Key]=enabled;Pack.Settings.Startup["fusion_quest"]=normalizer;h.UnpatchAll(h.Id);}
  File.WriteAllText(Output+"/stone-sword-quest-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Failed=0,Checks=passes.Skip(start),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
 }
}
