using System.Text.Json;
using Godot;
using STS2RitsuLib.Settings;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public sealed class EngineTestRequest {
 public string Suite{get;set;}="";
 public string Profile{get;set;}="";
 public string Mode{get;set;}="";
 public string RunId{get;set;}="";
 public string RunnerSha256{get;set;}="";
 public string OutputDirectory{get;set;}="";
 public string[] RequestedSuites{get;set;}=[];
 public static EngineTestRequest Load(string versionOutput){
  var path=System.Environment.GetEnvironmentVariable("AIKUN_ENGINE_TEST_REQUEST")??throw new InvalidOperationException("请从 Run-EngineTests.ps1 启动测试，并明确测试请求。");
  var request=JsonSerializer.Deserialize<EngineTestRequest>(File.ReadAllText(path))??throw new InvalidDataException("缺少测试请求。");
  if(request.Suite is not ("targeted" or "regression")||request.Mode is not ("full" or "minimal")||request.RequestedSuites.Length==0)throw new InvalidDataException("测试范围无效。");
  string output=Path.GetFullPath(request.OutputDirectory),runs=Path.GetFullPath(Path.Combine(versionOutput,"runs"))+Path.DirectorySeparatorChar;
  if(!output.StartsWith(runs,StringComparison.OrdinalIgnoreCase)||Path.GetFullPath(path)!=Path.Combine(output,"request.json"))throw new InvalidDataException("测试输出越界。");
  if(request.Suite=="regression"&&(request.Profile!="all"||!request.RequestedSuites.SequenceEqual(new[]{"legacy-regression"})))throw new InvalidDataException("完整回归范围不完整。");
  return request;
 }
}

public static partial class Entry {
 static readonly List<string> executedSuites=new();
 static async Task RunTargeted(){
  // 这里只登记回调，不提前执行未选中的旧测试。
  var suites=new Dictionary<string,Func<Task>>{
   ["startup"]=RelatedStartupChecks,
   ["topbar-intents"]=TopBarIntentChecks,
   ["topbar-stability"]=TopBarStability,
   ["fusion-ember"]=FusionEmberChecks,
   ["imbued-apotheosis"]=ImbuedApotheosisChecks,
   ["qu-curse"]=QuCurseChecks,
   ["cockroach-form"]=CockroachFormChecks,
   ["upstream-audit"]=UpstreamAuditChecks,
   ["portrait-localization"]=PortraitLocalizationChecks,
   ["medo-compatibility"]=MedoCompatibilityChecks,
   ["orb-context"]=OrbChannelContextChecks,
   ["pink-tomorrow"]=PinkTomorrowChecks,
   ["power-labels"]=PowerCornerLabelChecks,
   ["curse-scroll"]=CurseScrollChecks,
   ["ring-boss-hp"]=RingBossHpChecks,
   ["double-vision-choice"]=DoubleVisionChoiceChecks,
   ["ignite"]=IgniteEnchantmentChecks,
   ["event-sl"]=EventSlChecks,
   ["enchant-icons"]=EnchantmentIconChecks,
   ["blood-initiation"]=BloodInitiationChecks,
   ["thunder-initiation"]=ThunderInitiationChecks,
   ["dongni-cards"]=DongniCardChecks,
   ["dongni-art"]=DongniScreenshots,
   ["dongni-office"]=DongniOfficeChecks,
   ["dongni-reward-gate"]=DongniRewardGateChecks,
   ["campfire-sl"]=CampfireSlChecks,
   ["blessed-finesse"]=BlessedFinesseChecks,
   ["yui-fusion-blessings"]=YuiFusionBlessingChecks,
   ["skip-animations"]=SkipStoryChecks,
   ["release-compatibility"]=ReleaseCompatibilityChecks,
   ["settings-fold"]=SettingsFoldChecks,
   ["settings-inline"]=SettingsFoldChecks,
   ["settings-extra-fold"]=SettingsExtraFoldChecks,
   ["settings-compact"]=SettingsCompactChecks,
   ["modform-art"]=ModFormArtChecks,
   ["upgrade-preview"]=UpgradePreviewChecks,
   ["fusion-upgrades"]=FusionUpgradeChecks,
   ["fake-dusty-tome"]=FakeDustyTomeChecks,
   ["blood-sect"]=BloodSectChecks,
   ["blood-block-sources"]=BloodBlockSourceChecks,
   ["blood-card-damage-art"]=BloodCardDamageChecks,
   ["blood-stacking"]=BloodStackingChecks,
   ["ghostly-anvil"]=GhostlyAnvilChecks,
   ["fusion-strike"]=FusionStrikeDisplayChecks,
   ["blood-cost-console"]=BloodCostConsoleChecks,
   ["blood-fusion"]=BloodFusionChecks,
   ["blood-fusion-leech"]=BloodFusionLeechChecks,
   ["stone-sword-quest"]=StoneSwordQuestChecks
  };
  foreach(string name in Request.RequestedSuites)if(!suites.ContainsKey(name))throw new InvalidDataException("未知专项检查："+name);
  foreach(string name in Request.RequestedSuites){
   executedSuites.Add(name);GD.Print("[Aikun020Probe] SUITE "+name);
   try{await suites[name]();}catch(Exception e){failures.Add(name+": "+e);GD.Print("[Aikun020Probe] FAIL "+name+" "+e);}
  }
 }
 static Task RelatedStartupChecks(){
  var pages=ModSettingsRegistry.GetPages().Where(p=>p.ModId==Pack.Settings.Id).ToArray();
  Check("related startup settings pages registered",pages.Any(p=>p.Id==Pack.Settings.Id+".misc")&&pages.Any(p=>p.Id==Pack.Settings.Id+".patches"));
  Check("related startup options initialized",Pack.Options.All.All(o=>Pack.Settings.Startup.ContainsKey(o.Key)));
  return Task.CompletedTask;
 }
}
