using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using STS2RitsuLib.Models.Capabilities;
using STS2RitsuLib.Settings;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
[ModInitializer(nameof(Initialize))]
public static partial class Entry {
 static readonly string VersionOutput=Path.Combine(Path.GetFullPath(System.Environment.GetEnvironmentVariable("AIKUN_TEST_OUTPUT_ROOT")??Path.Combine(AppContext.BaseDirectory,"results")),typeof(Pack.Entry).Assembly.GetName().Version!.ToString(3));
 static readonly EngineTestRequest Request=EngineTestRequest.Load(VersionOutput);
 static readonly string Output=Request.OutputDirectory;
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static SceneTree Tree=>(SceneTree)Engine.GetMainLoop();
 static readonly List<string> passes=new(),failures=new();
 static Player? player;static RunState? run;
 static bool Minimal=>Request.Mode=="minimal";
 public static void Initialize(){InstallNativeWaitFixture();STS2RitsuLib.RitsuLibFramework.CreateContentPack("Aikun020Probe").Card<ColorlessCardPool,RepeatMaterialFixture>().Card<ColorlessCardPool,CardDamageHelperFixture>().Power<ForwardedDamageFixturePower>().Apply();Tree.CreateTimer(4).Timeout+=Verify;GD.Print("[Aikun020Probe] scheduled isolated native verification user="+OS.GetUserDataDir());}
 static Task Wait(double seconds){var t=new TaskCompletionSource();Tree.CreateTimer(seconds).Timeout+=()=>t.SetResult();return t.Task;}
 static void Check(string label,bool ok){if(!ok)throw new Exception(label);passes.Add(label);GD.Print("[Aikun020Probe] PASS "+label);}
 static void Suite(string name,Action action){try{action();}catch(Exception e){failures.Add(name+": "+e);GD.Print("[Aikun020Probe] FAIL "+name+" "+e);}}
 static Type Find(string name)=>FusionQuestBlessingFix.Entry.Find(name)??throw new Exception("Missing "+name);
 static EnchantmentModel Enchant(Type type,int amount){var e=ModelDb.GetById<EnchantmentModel>(ModelDb.GetId(type)).ToMutable();e.Amount=amount;return e;}
 static CardModel Restore(CardModel card){var options=new JsonSerializerOptions{IncludeFields=true};return CardModel.FromSerializable(JsonSerializer.Deserialize<SerializableCard>(JsonSerializer.Serialize(card.ToSerializable(),options),options)!);}
 static void Extra(CardModel card,EnchantmentModel enchantment)=>Find("MoreEnchantmentsMod.MoreEnchantStack").GetMethod("RestoreExtra",F,null,[typeof(CardModel),typeof(EnchantmentModel),typeof(bool)],null)!.Invoke(null,[card,enchantment,true]);
 static async void Verify(){
  try{
   for(int i=0;i<90&&!ModSettingsRegistry.GetPages().Any(p=>p.ModId==Pack.Settings.Id);i++)await Wait(1);
   if(AccessTools.Field(typeof(NGame),"_gameStartupComplete").GetValue(NGame.Instance) is TaskCompletionSource startup)await startup.Task.WaitAsync(TimeSpan.FromSeconds(90));
   await Wait(2);
   Check("separate engine user data directory",OS.GetUserDataDir().EndsWith("SlayTheSpire2-Aikun020Test"));
   var io=new GodotFileIo("user://probe-local-only");var saves=new SaveManager(io,io,true);SaveManager.MockInstanceForTesting(saves);
   saves.InitProfileId(1);saves.InitSettingsDataForTest();saves.InitPrefsDataForTest();saves.InitProgressData();
   if(Request.Suite=="targeted"){
    player=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);run=RunState.CreateForTest([player],seed:"AIKUN-TARGETED");
    await RunTargeted();return;
   }
   await TopBarIntentChecks();
   await FusionEmberChecks();
   await ImbuedApotheosisChecks();
   await QuCurseChecks();
   await CockroachFormChecks();
   await EventSlChecks();
   await EnchantmentIconChecks();
   await BloodInitiationChecks();
   await ThunderInitiationChecks();
   await DongniCardChecks();
   await DongniOfficeChecks();
   await DongniRewardGateChecks();
   await YuiFusionBlessingChecks();
   await BlessedFinesseChecks();
   await CampfireSlChecks();
   executedSuites.Add("legacy-regression");
   GD.Print("[Aikun020Probe] SUITE legacy-regression (explicit)");
   Suite("compatibility inventory",CompatInventory);
   try{await ReleaseCompatibilityChecks();}catch(Exception e){failures.Add("release compatibility: "+e);GD.Print("[Aikun020Probe] FAIL release compatibility "+e);}
   try{await SettingsFoldChecks();}catch(Exception e){failures.Add("settings fold: "+e);GD.Print("[Aikun020Probe] FAIL settings fold "+e);}
   try{await SettingsExtraFoldChecks();}catch(Exception e){failures.Add("settings extra fold: "+e);GD.Print("[Aikun020Probe] FAIL settings extra fold "+e);}
   try{await SettingsCompactChecks();}catch(Exception e){failures.Add("settings compact: "+e);GD.Print("[Aikun020Probe] FAIL settings compact "+e);}
   Suite("settings",()=>{
    Check("48 options default on",Pack.Options.All.Length==48&&Pack.Options.All.All(o=>new Pack.PackConfig().Get(o.Key)));
    var pages=ModSettingsRegistry.GetPages().Where(p=>p.ModId==Pack.Settings.Id).ToArray();
    Check("eight misc options",pages.Single(p=>p.Id==Pack.Settings.Id+".misc").Sections.Count==8);
    Check("forty patch options",pages.Single(p=>p.Id==Pack.Settings.Id+".patches").Sections.Count==40);
    foreach(var key in new[]{"mod_form","multi_enchant_icons","act4_key_topbar","past_rooms"}){
     if(Minimal&&key is "act4_key_topbar" or "past_rooms")Check(key+" missing safely",Pack.Settings.Status(Pack.Options.All.Single(o=>o.Key==key)).Contains("未装载相关模组"));
     else Check(key+" installed",Pack.RuntimeLoader.States.GetValueOrDefault(key)=="已应用。");
    }
    foreach(var option in Pack.Options.All.Where(o=>Pack.Options.Missing(o).Length==0))Check("enabled option applied "+option.Key,Pack.RuntimeLoader.States.GetValueOrDefault(option.Key)=="已应用。");
    Check("all chances default 30",new[]{"monster","elite","event","boss"}.All(k=>new Pack.PackConfig().Chance(k)==30));
   });
   player=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);run=RunState.CreateForTest([player],seed:"AIKUN020");
   try{await ModFormArtChecks();}catch(Exception e){failures.Add("mod form artwork: "+e);GD.Print("[Aikun020Probe] FAIL mod form artwork "+e);}
   try{await FakeDustyTomeChecks();}catch(Exception e){failures.Add("fake dusty tome: "+e);GD.Print("[Aikun020Probe] FAIL fake dusty tome "+e);}
   try{await BloodSectChecks();}catch(Exception e){failures.Add("blood sect: "+e);GD.Print("[Aikun020Probe] FAIL blood sect "+e);}
   try{await BloodBlockSourceChecks();}catch(Exception e){failures.Add("blood block sources: "+e);GD.Print("[Aikun020Probe] FAIL blood block sources "+e);}
   try{await BloodCardDamageChecks();}catch(Exception e){failures.Add("blood card damage: "+e);GD.Print("[Aikun020Probe] FAIL blood card damage "+e);}
   try{await BloodStackingChecks();}catch(Exception e){failures.Add("blood stacking: "+e);GD.Print("[Aikun020Probe] FAIL blood stacking "+e);}
   try{await StoneSwordQuestChecks();}catch(Exception e){failures.Add("stone sword quest: "+e);GD.Print("[Aikun020Probe] FAIL stone sword quest "+e);}
   try{await GhostlyAnvilChecks();}catch(Exception e){failures.Add("ghostly anvil: "+e);GD.Print("[Aikun020Probe] FAIL ghostly anvil "+e);}
   try{await FusionStrikeDisplayChecks();}catch(Exception e){failures.Add("fusion strike: "+e);GD.Print("[Aikun020Probe] FAIL fusion strike "+e);}
   try{await BloodCostConsoleChecks();}catch(Exception e){failures.Add("blood cost console: "+e);GD.Print("[Aikun020Probe] FAIL blood cost console "+e);}
   try{await BloodFusionChecks();}catch(Exception e){failures.Add("blood fusion: "+e);GD.Print("[Aikun020Probe] FAIL blood fusion "+e);}
   try{await BloodFusionLeechChecks();}catch(Exception e){failures.Add("blood fusion leech: "+e);GD.Print("[Aikun020Probe] FAIL blood fusion leech "+e);}
   try{await IgniteEnchantmentChecks();}catch(Exception e){failures.Add("ignite enchantment: "+e);GD.Print("[Aikun020Probe] FAIL ignite enchantment "+e);}
   try{await MedoCompatibilityChecks();}catch(Exception e){failures.Add("Medo compatibility: "+e);GD.Print("[Aikun020Probe] FAIL Medo compatibility "+e);}
   try{await PortraitLocalizationChecks();}catch(Exception e){failures.Add("portrait localization: "+e);GD.Print("[Aikun020Probe] FAIL portrait localization "+e);}
   try{await OrbChannelContextChecks();}catch(Exception e){failures.Add("orb channel context: "+e);GD.Print("[Aikun020Probe] FAIL orb channel context "+e);}
   try{await PinkTomorrowChecks();}catch(Exception e){failures.Add("pink tomorrow damage: "+e);GD.Print("[Aikun020Probe] FAIL pink tomorrow damage "+e);}
   try{await PowerCornerLabelChecks();}catch(Exception e){failures.Add("power corner label: "+e);GD.Print("[Aikun020Probe] FAIL power corner label "+e);}
   try{await FusionUpgradeChecks();}catch(Exception e){failures.Add("fusion upgrades: "+e);GD.Print("[Aikun020Probe] FAIL fusion upgrades "+e);}
   try{await UpgradePreviewChecks();}catch(Exception e){failures.Add("upgrade preview: "+e);GD.Print("[Aikun020Probe] FAIL upgrade preview "+e);}
   try{await SkipStoryChecks();}catch(Exception e){failures.Add("skip animations: "+e);GD.Print("[Aikun020Probe] FAIL skip animations "+e);}
   Suite("mod form self candidate",SelfCandidateRules);
   try{await SelfGeneration();}catch(Exception e){failures.Add("self generation: "+e);GD.Print("[Aikun020Probe] FAIL self generation "+e);}
   try{await TopBarStability();}catch(Exception e){failures.Add("topbar hover: "+e);GD.Print("[Aikun020Probe] FAIL topbar hover "+e);}
   try{await TopBarTooltipChecks();}catch(Exception e){failures.Add("topbar tooltip: "+e);GD.Print("[Aikun020Probe] FAIL topbar tooltip "+e);}
   try{await HextechTopBarChecks();}catch(Exception e){failures.Add("Hextech topbar: "+e);GD.Print("[Aikun020Probe] FAIL Hextech topbar "+e);}
   try{await CurseScrollChecks();}catch(Exception e){failures.Add("curse scroll: "+e);GD.Print("[Aikun020Probe] FAIL curse scroll "+e);}
   try{await RingBossHpChecks();}catch(Exception e){failures.Add("ring boss HP: "+e);GD.Print("[Aikun020Probe] FAIL ring boss HP "+e);}
   try{await ModFormRepeatChecks();}catch(Exception e){failures.Add("mod form repeat: "+e);GD.Print("[Aikun020Probe] FAIL mod form repeat "+e);}
   try{await DoubleVisionChoiceChecks();}catch(Exception e){failures.Add("double vision choices: "+e);GD.Print("[Aikun020Probe] FAIL double vision choices "+e);}
   if(Minimal){
    var ownCandidates=new[]{typeof(Pack.BloodSect),typeof(Pack.DongniMeaning),typeof(Pack.NotDongniMeaning),typeof(Pack.WhoDongniMeaning),typeof(Pack.CockroachForm)};
    Check("minimal candidates exactly own five generatable cards",Pack.ModFormContent.Groups.SelectMany(g=>g.Cards).Select(c=>c.GetType()).ToHashSet().SetEquals(ownCandidates));
    Check("form enters complete pool with blood sect candidate",ModelDb.CardPool<ColorlessCardPool>().AllCards.Any(c=>c is Pack.ModForm));
    Check("form enters unlocked pool with blood sect candidate",ModelDb.CardPool<ColorlessCardPool>().GetUnlockedCards(UnlockState.all,CardMultiplayerConstraint.SingleplayerOnly).Any(c=>c is Pack.ModForm));
    Check("form can generate built-in blood sect",ModelDb.Card<Pack.ModForm>().CanBeGeneratedInCombat&&ModelDb.Card<Pack.ModForm>().CanBeGeneratedByModifiers);
    var generated=Pack.ModFormContent.PrepareGenerated(ModelDb.Card<Bash>().ToMutable(),3);generated=Restore(generated);
    Check("generated state loads with Ritsu alone",generated.CurrentUpgradeLevel==3&&generated.Keywords.Contains(CardKeyword.Exhaust)&&generated.Keywords.Contains(CardKeyword.Ethereal));
    return;
   }
   Suite("card rules",()=>{
    Check("mod cards available",Pack.ModFormContent.Groups.Length>0);
    Check("form enters colorless pool when candidates exist",ModelDb.CardPool<ColorlessCardPool>().AllCards.Any(c=>c is Pack.ModForm));
    Check("only playable candidate types",Pack.ModFormContent.Groups.SelectMany(g=>g.Cards).All(c=>c.Type is CardType.Attack or CardType.Skill or CardType.Power));
    Check("base game and self excluded",!Pack.ModFormContent.IsCandidate(ModelDb.Card<Bash>())&&!Pack.ModFormContent.IsCandidate(ModelDb.Card<Pack.ModForm>()));
    Check("empty pool returns safely",Pack.ModFormContent.Choose([],new Rng(1))==null);
    var card=ModelDb.Card<Pack.ModForm>().ToMutable();
    Check("three cost colorless rare power",card.EnergyCost.Canonical==3&&card.Type==CardType.Power&&card.Rarity==CardRarity.Rare&&card.Pool is ColorlessCardPool);
    card.UpgradeInternal();card.FinalizeUpgradeInternal();Check("normal upgrade generation level 1",card.CurrentUpgradeLevel==1&&card.GetDescriptionForPile(PileType.None).Contains("升级过的"));
    var scorch=Enchant(Find("MoreEnchant.Enchantments.ScorchingEnchantment"),1);card.EnchantInternal(scorch,1);scorch.ModifyCard();
    card.UpgradeInternal();card.UpgradeInternal();card.FinalizeUpgradeInternal();
    Check("scorching form reaches +3",card.CurrentUpgradeLevel==3&&card.GetDescriptionForPile(PileType.None).Contains("升级 +3"));
    var restored=Restore(card);Check("form upgrade level survives JSON",restored.CurrentUpgradeLevel==3&&restored.GetDescriptionForPile(PileType.None).Contains("升级 +3"));
    var generated=Pack.ModFormContent.PrepareGenerated(ModelDb.Card<Bash>().ToMutable(),3);
    Check("generated card upgraded three times",generated.CurrentUpgradeLevel==3);
    Check("generated card has ethereal and exhaust",generated.Keywords.Contains(CardKeyword.Ethereal)&&generated.Keywords.Contains(CardKeyword.Exhaust));
    for(int i=1;i<=3;i++){generated=Restore(generated);GD.Print($"[Aikun020Probe] generated restored n={generated.CurrentUpgradeLevel} cap={generated.Capability<Pack.GeneratedCardUpgrade>()?.UpgradeLimit} keywords={string.Join(',',generated.Keywords)}");Check("generated capability JSON "+i,generated.CurrentUpgradeLevel==3&&generated.Capability<Pack.GeneratedCardUpgrade>()?.UpgradeLimit==3&&generated.Keywords.Contains(CardKeyword.Ethereal)&&generated.Keywords.Contains(CardKeyword.Exhaust));}
    var clone=(CardModel)generated.ClonePreservingMutability();Check("generated capability clones independently",clone.Capability<Pack.GeneratedCardUpgrade>()?.UpgradeLimit==3&&!ReferenceEquals(clone.Capability<Pack.GeneratedCardUpgrade>(),generated.Capability<Pack.GeneratedCardUpgrade>()));
    Check("original Bash upgrade rules unchanged",ModelDb.Card<Bash>().MaxUpgradeLevel==1);
    var groups=new[]{new Pack.ModCardGroup("a",[ModelDb.Card<Pack.ModForm>()]),new Pack.ModCardGroup("b",[ModelDb.Card<Bash>(),ModelDb.Card<Anger>(),ModelDb.Card<DefendIronclad>()])};
    var rng=new Rng(1902);int first=Enumerable.Range(0,10000).Count(_=>Pack.ModFormContent.Choose(groups,rng) is Pack.ModForm);
    Check("mods selected uniformly despite pool size",first is >4700 and <5300);
   });
   CardModel? mixed=null;
   Suite("mixed enchantments",()=>{
    mixed=Mixed(false);var reverse=Mixed(true);
    Check("native more plague all visible",Pack.EnchantmentView.Read(mixed).Length==3);
    Check("reverse enchant order all visible",Pack.EnchantmentView.Read(reverse).Length==3);
    for(int i=1;i<=3;i++){mixed=Restore(mixed);Check("mixed enchantments JSON "+i,Pack.EnchantmentView.Read(mixed).Length==3);}
    Check("mixed enchantment clone",Pack.EnchantmentView.Read((CardModel)mixed.ClonePreservingMutability()).Length==3);
   });
   Suite("past rooms",()=>{
    Check("legacy chapters removed from new chapter candidates",ModelDb.ActsByIndex.SelectMany(x=>x).All(a=>!Pack.PastRooms.Legacy(a)));
    Check("legacy chapter catalog preserved",ModelDb.Acts.Any(Pack.PastRooms.Legacy));
    foreach(var act in run.Acts.Where(a=>a.Index<3)){
     typeof(RunState).GetProperty(nameof(RunState.CurrentActIndex))!.SetValue(run,act.Index);run.ActFloor=7;
     act.GenerateRooms(run.Rng.UpFront,run.UnlockState);var rooms=(RoomSet)AccessTools.Field(typeof(ActModel),"_rooms").GetValue(act)!;
     var original=rooms.normalEncounters[0];
     foreach(var percent in new[]{0,100}){
      Pack.Settings.StartupChances["monster"]=percent;Pack.Settings.StartupChances["elite"]=percent;Pack.Settings.StartupChances["boss"]=percent;
      Check(act.Id+" monster "+percent,Pack.PastRooms.Legacy(Pack.PastRooms.SelectEncounter(act,RoomType.Monster,original,run))==(percent==100));
      Check(act.Id+" elite "+percent,Pack.PastRooms.Legacy(Pack.PastRooms.SelectEncounter(act,RoomType.Elite,rooms.eliteEncounters[0],run))==(percent==100));
      Pack.PastRooms.ApplyBosses(act,run);var id=rooms.Boss.Id;Check(act.Id+" boss "+percent,Pack.PastRooms.Legacy(rooms.Boss)==(percent==100));
      Pack.PastRooms.ApplyBosses(act,run);Check(act.Id+" boss no reroll "+percent,rooms.Boss.Id==id);
      var loaded=ActModel.FromSave(act.ToSave());Check(act.Id+" boss save stable "+percent,((RoomSet)AccessTools.Field(typeof(ActModel),"_rooms").GetValue(loaded)!).Boss.Id==id);
      Pack.Settings.StartupChances["event"]=percent;
      ((HashSet<ModelId>)AccessTools.Field(typeof(RunState),"_visitedEventIds").GetValue(run)!).Clear();
      // 完成其他模组的保底任务事件后，再验证普通随机事件分支。
      foreach(var name in new[]{"Medo.Scripts.Events.HeroicSagaOfFlameChase","Medo.Scripts.Events.ElysianRealm"}){
       var eventType=FusionQuestBlessingFix.Entry.Find(name);if(eventType!=null)run.AddVisitedEvent(ModelDb.GetById<EventModel>(ModelDb.GetId(eventType)));
      }
      EventModel selected=null!;bool fallback=Pack.PastRooms.NextEvent(act,run,ref selected);
      Check(act.Id+" event handled "+percent,!fallback&&selected!=null);
      if(percent==0)Check(act.Id+" native event at 0",!Pack.PastRooms.Legacy(selected!));
      if(percent==100)Check(act.Id+" legacy event at 100",Pack.PastRooms.Legacy(selected!));
     }
    }
    var ending=run.Acts.FirstOrDefault(a=>a.Index==3);if(ending!=null){ending.GenerateRooms(run.Rng.UpFront,run.UnlockState);var rooms=(RoomSet)AccessTools.Field(typeof(ActModel),"_rooms").GetValue(ending)!;var original=rooms.normalEncounters[0];Pack.Settings.StartupChances["monster"]=100;Check("Act4 encounters preserved",ReferenceEquals(Pack.PastRooms.SelectEncounter(ending,RoomType.Monster,original,run),original));}
    var rng=new Rng(123);int hits=Enumerable.Range(0,10000).Count(_=>Pack.PastRooms.Roll(30,rng));Check("30 percent distribution",hits is >2700 and <3300);
    foreach(var key in new[]{"monster","elite","event","boss"})Pack.Settings.StartupChances[key]=30;
   });
   Suite("key invariants",()=>{
    foreach(var name in Pack.HeartKeyTopBar.KeyNames){var relic=ModelDb.GetById<RelicModel>(ModelDb.GetId(Find("Act4Heart.Keys."+name))).ToMutable();Check(name+" still original relic identity",Pack.HeartKeyTopBar.IsKey(relic));}
    Check("three fixed distinct slots",Pack.HeartKeyTopBar.SlotPositions.Distinct().Count()==3);
   });
   try{await Combat();}catch(Exception e){failures.Add("combat: "+e);GD.Print("[Aikun020Probe] FAIL combat "+e);}
   await Render(mixed);
   foreach(var pair in new[]{("misc",Pack.Settings.Id+".misc"),("patches",Pack.Settings.Id+".patches")}){
    var result=ModSettingsNavigator.RequestOpenByIds(Pack.Settings.Id,pair.Item2,null,null);Check("open "+pair.Item1,result.Success);await Wait(.8);Tree.Root.GetTexture().GetImage().SavePng(Output+"/settings-"+pair.Item1+".png");
   }
  }catch(Exception e){failures.Add(e.ToString());GD.Print("[Aikun020Probe] FATAL "+e);}
  finally{
   File.WriteAllText(Output+"/engine-result.json",JsonSerializer.Serialize(new{
    Request.Suite,Request.Profile,Request.Mode,Request.RunId,Request.RunnerSha256,Request.RequestedSuites,
    RunDirectory=Output,ExecutedSuites=executedSuites,RegressionComplete=Request.Suite=="regression"&&failures.Count==0,
    AssemblyHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(Pack.Entry).Assembly.Location))),
    ProbeHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(Entry).Assembly.Location))),
    Passed=passes.Count,Failed=failures.Count,Checks=passes,Failures=failures},new JsonSerializerOptions{WriteIndented=true}));
   GD.Print($"[Aikun020Probe] COMPLETE passed={passes.Count} failed={failures.Count}");await Wait(1);Tree.Quit();
  }
 }
 static async Task Combat(){
  var savedGroups=Pack.ModFormContent.Groups;bool oldMode=TestMode.IsOn;
  var listeners=AccessTools.Field(typeof(CombatStateTracker),"CombatStateChanged");var frontend=listeners.GetValue(CombatManager.Instance.StateTracker);
  try{
   listeners.SetValue(CombatManager.Instance.StateTracker,null);
   TestMode.IsOn=true;
   var p=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var state=RunState.CreateForTest([p],seed:"AIKUN020-COMBAT");
   RunManager.Instance.SetUpTest(state,new NetSingleplayerGameService());
   var combat=new CombatState(runState:state);combat.AddPlayer(p);
   combat.AddCreature(combat.CreateCreature(ModelDb.Monster<CalcifiedCultist>().ToMutable(),CombatSide.Enemy,"fixture"));
   CombatManager.Instance.SetUpCombat(combat);
   var turn=AccessTools.Field(typeof(CombatManager),"_turnState").GetValue(CombatManager.Instance)!;
   turn.GetType().GetProperty("IsInProgress")!.SetValue(turn,true);
   Check("combat fixture alive and running",CombatManager.Instance.IsInProgress&&!CombatManager.Instance.IsEnding);
   // 以痛击固定随机池，隔离出牌/回合钩子的生成次数、免费与升级效果；真实候选池另行验证。
   typeof(Pack.ModFormContent).GetProperty(nameof(Pack.ModFormContent.Groups))!.SetValue(null,new Pack.ModCardGroup[]{new("fixture",[ModelDb.Card<Bash>()])});
   foreach(var (n,enchantment,extra) in new[]{(0,"",false),(1,"",false),(3,"MoreEnchant.Enchantments.ScorchingEnchantment",false),(4,"Plague.PlagueStackedShadowEnchantment",false),(5,"Plague.PlagueStackedShadowEnchantment",true)}){
    var form=(Pack.ModForm)combat.CreateCard(ModelDb.Card<Pack.ModForm>(),p);
    if(n>1){var e=Enchant(Find(enchantment),1);if(extra){var sharp=ModelDb.Enchantment<Sharp>().ToMutable();form.EnchantInternal(sharp,1);Extra(form,e);}else{form.EnchantInternal(e,1);e.ModifyCard();}}
    for(int i=0;i<n;i++){form.UpgradeInternal();form.FinalizeUpgradeInternal();}
    int before=p.PlayerCombatState!.Hand.Cards.Count;
    await (Task)typeof(Pack.ModForm).GetMethod("OnPlay",F)!.Invoke(form,[new ThrowingPlayerChoiceContext(),null])!;
    var generated=p.PlayerCombatState.Hand.Cards.Last();
    Check("play immediately generates exactly one n="+n,p.PlayerCombatState.Hand.Cards.Count==before+1&&generated.CurrentUpgradeLevel==n);
    Check("generated free in combat n="+n,generated.EnergyCost.GetWithModifiers(CostModifiers.All)==0&&generated.Keywords.Contains(CardKeyword.Ethereal)&&generated.Keywords.Contains(CardKeyword.Exhaust));
   }
   var powers=p.Creature.Powers.OfType<Pack.ModFormPower>().ToArray();
   Check("five form instances retain distinct upgrade levels",powers.Select(x=>x.GenerationUpgradeLevel).Order().SequenceEqual(new[]{0,1,3,4,5}));
   int start=p.PlayerCombatState!.Hand.Cards.Count;
   foreach(var power in powers)await power.AfterSideTurnStart(CombatSide.Enemy,[],combat);
   Check("opponent turn does not generate",p.PlayerCombatState.Hand.Cards.Count==start);
   foreach(var power in powers)await power.AfterSideTurnStart(CombatSide.Player,[p.Creature],combat);
   Check("own turn triggers each form once",p.PlayerCombatState.Hand.Cards.Count==start+5);
   Check("turn generation preserves +0 +1 +3 +4 +5",p.PlayerCombatState.Hand.Cards.TakeLast(5).Select(c=>c.CurrentUpgradeLevel).Order().SequenceEqual(new[]{0,1,3,4,5}));
  }catch(Exception e){GD.Print("[Aikun020Probe] combat original error "+e);throw;}
  finally{
   typeof(Pack.ModFormContent).GetProperty(nameof(Pack.ModFormContent.Groups))!.SetValue(null,savedGroups);
   try{if(RunManager.Instance.DebugOnlyGetState()!=null){CombatManager.Instance.Reset(false);RunManager.Instance.CleanUp();}}
   finally{TestMode.IsOn=oldMode;listeners.SetValue(CombatManager.Instance.StateTracker,frontend);}
  }
 }
 static CardModel Mixed(bool reverse){
  var card=ModelDb.Card<Bash>().ToMutable();var spark=Enchant(Find("MoreEnchantmentsMod.Spark"),2);var sharp=ModelDb.Enchantment<Sharp>().ToMutable();sharp.Amount=3;
  var primary=reverse?spark:sharp;card.EnchantInternal(primary,primary.Amount);primary.ModifyCard();
  var composite=Find("Plague.PlagueCompositeEnchantment").GetMethod("ConvertFrom",F)!.Invoke(null,[card,primary])!;
  composite.GetType().GetMethod("AddOrStackEnchantment",F)!.Invoke(composite,[Enchant(Find("Plague.PlagueVoidEnchantment"),1),1m,false]);
  Extra(card,reverse?sharp:spark);return card;
 }
 static async Task Render(CardModel? mixed){
  var layer=new CanvasLayer{Layer=100};Tree.Root.AddChild(layer);
  var host=new Control{Size=Tree.Root.GetVisibleRect().Size};layer.AddChild(host);
  host.AddChild(new ColorRect{Size=host.Size,Color=new("141c2b"),MouseFilter=Control.MouseFilterEnum.Ignore});
  var normal=ModelDb.Card<Pack.ModForm>().ToMutable();var upgraded=ModelDb.Card<Pack.ModForm>().ToMutable();upgraded.UpgradeInternal();upgraded.FinalizeUpgradeInternal();
  var multi=ModelDb.Card<Pack.ModForm>().ToMutable();var scorch=Enchant(Find("MoreEnchant.Enchantments.ScorchingEnchantment"),1);multi.EnchantInternal(scorch,1);scorch.ModifyCard();for(int n=0;n<3;n++)multi.UpgradeInternal();multi.FinalizeUpgradeInternal();
  var cards=new[]{normal,upgraded,multi,mixed??ModelDb.Card<Bash>().ToMutable()};
  for(int i=0;i<cards.Length;i++){
   var card=NCard.Create(cards[i])!;host.AddChild(card);card.Position=new(host.Size.X*(i+.5f)/cards.Length,400);card.Scale=new(.85f,.85f);card.UpdateVisuals(PileType.None,CardPreviewMode.Normal);
   if(i==3&&mixed!=null)Check("three native enchantment tabs rendered",Enumerable.Range(0,3).All(n=>card.EnchantmentTab.GetParent().GetNodeOrNull<Control>("AikunEnchantment"+n)?.Visible==true));
  }
  var bar=new Control{Name="KeyPreview",Position=new(40,720),Size=new(300,80)};host.AddChild(bar);bar.AddChild(new HBoxContainer{Name="LeftAlignedStuff",Size=new(300,80)});bar.GetNode("LeftAlignedStuff").AddChild(new Control{Name="PortraitPlaceholder",CustomMinimumSize=new(80,80)});
  var type=typeof(Pack.HeartKeyTopBar).GetNestedType("KeyDisplay",F)!;var display=Activator.CreateInstance(type,F,null,[bar,player!],null)!;
  type.GetMethod("Refresh",F)!.Invoke(display,null);
  var frame=bar.GetNode<Control>("LeftAlignedStuff/AikunHeartKeyFrame");
  Check("key frame first in left group",frame.GetIndex()==0&&frame.Size==new Vector2(64,64));
  var slots=Pack.HeartKeyTopBar.KeyNames.Select(n=>frame.GetNode<TextureRect>(n)).ToArray();var positions=slots.Select(s=>s.Position).ToArray();
  Check("three cropped original key textures",slots.All(s=>s.Texture is AtlasTexture a&&a.Atlas.GetSize()==new Vector2(64,64)));
  Check("unowned keys only show dark slots",slots.All(s=>s.SelfModulate==Colors.Transparent)&&frame.GetNode<TextureRect>("KeySlots").Visible);
  Check("original layout green above ruby left sapphire right",positions[1].Y<positions[0].Y&&positions[0].Y==positions[2].Y&&positions[0].X<positions[2].X);
  var ruby=ModelDb.GetById<RelicModel>(ModelDb.GetId(Find("Act4Heart.Keys.RubyKey"))).ToMutable();ruby.Owner=player!;
  ((List<RelicModel>)AccessTools.Field(typeof(Player),"_relics").GetValue(player)!).Add(ruby);
  type.GetMethod("Refresh",F)!.Invoke(display,null);
  Check("owned ruby lights in its fixed slot",slots[0].SelfModulate==Colors.White&&slots[1].SelfModulate==Colors.Transparent&&slots.Select(s=>s.Position).SequenceEqual(positions));
  await KeyStates(host);
  var inventory=new NRelicInventory{Name="KeyInventoryPreview",Position=new(220,720),Size=new(400,80)};host.AddChild(inventory);
  var add=AccessTools.Method(typeof(NRelicInventory),"Add");
  add.Invoke(inventory,[player!.Relics.First(),true,-1]);add.Invoke(inventory,[ruby,true,-1]);
  Check("key hidden from real relic inventory holder",!inventory.RelicNodes.Single(n=>ReferenceEquals(n.Relic.Model,ruby)).Visible);
  Check("ordinary relic remains visible",inventory.RelicNodes.Single(n=>!Pack.HeartKeyTopBar.IsKey(n.Relic.Model)).Visible);
  await Wait(1);Tree.Root.GetTexture().GetImage().SavePng(Output+"/cards-and-keys.png");layer.QueueFree();await Wait(.2);
 }
}
