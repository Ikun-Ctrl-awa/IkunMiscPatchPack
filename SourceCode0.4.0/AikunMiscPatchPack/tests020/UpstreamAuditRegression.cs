using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static bool upstreamBypass;
 static bool BypassUpstreamBridge(ref bool __result){__result=false;return !upstreamBypass;}
 static Task UpstreamAuditChecks(){
  var evidence=new List<object>();
  Check("upstream audit retains the 38 patches from its recorded audit",Pack.Options.All.Count(o=>!o.Misc&&o.Key!=Pack.PlagueImbuedAutoPlayFix.Key&&o.Key!=Pack.FusionEmberCostFix.Key)==38);
  if(Minimal){Check("upstream optional raw fusion safely absent",FusionQuestBlessingFix.Entry.Find("CardFusion.FusedCard")==null);return Task.CompletedTask;}
  var inventory=JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(VersionOutput+"/upstream-audit/inventory.json"));
  foreach(var target in Pack.RuntimeLoader.Catalog.GroupBy(t=>t.Name)){
   string workshop=Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"");
   var path=workshop+target.First().WorkshopRelative;
   var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
   bool supported=target.Any(t=>t.OriginalHash==hash);
   evidence.Add(new{Assembly=target.Key,OriginalHash=hash,KnownBuggyOriginal=supported,Options=target.First().Options});
    Check("upstream still byte identical to known buggy original "+target.Key,supported);
    foreach(string key in target.First().Options)Check("upstream retained binary repair really applied "+key,Pack.RuntimeLoader.States.GetValueOrDefault(key)=="已应用。");
  }
  // 当前原件仅识别另一套 MultiEnchantmentMod API；真正的更多附魔额外槽不可被它枚举。
  var stack=Find("MoreEnchantmentsMod.MoreEnchantStack");
  EnchantmentModel[] Read(CardModel c)=>((IEnumerable<EnchantmentModel>)AccessTools.Method(stack,"AllEnchantments").Invoke(null,[c])!).ToArray();
  var a=ModelDb.Card<Bash>().ToMutable();a.EnchantInternal(Enchant(Find("MoreEnchantmentsMod.Spark"),3),3);Extra(a,Enchant(Find("MoreEnchantmentsMod.Warding"),2));
  var b=ModelDb.Card<Bash>().ToMutable();b.EnchantInternal(Enchant(Find("MoreEnchantmentsMod.Spark"),5),5);Extra(b,Enchant(Find("MoreEnchantmentsMod.Warding"),7));
  var result=ModelDb.Card<Bash>().ToMutable();
  var inherit=AccessTools.Method(Find("CardFusion.MultiEnchantmentCompat"),"InheritEnchantments");
  var bridge=AccessTools.Method(Find("CardFusion.LocalCompatibility.MoreEnchantFusionBridge"),"TryInherit");
  var control=new Harmony("Aikun020Probe.UpstreamFusionControl");
  control.Patch(bridge,prefix:new HarmonyMethod(typeof(Entry),nameof(BypassUpstreamBridge)));
  try{upstreamBypass=true;inherit.Invoke(null,[result,new CardModel[]{a,b},"upstream-audit"]);}
  finally{upstreamBypass=false;control.UnpatchAll(control.Id);}
  var inherited=Read(result);
  evidence.Add(new{Option="fusion_enchants",Sources=new[]{Read(a).Select(e=>e.GetType().Name+":"+e.Amount).ToArray(),Read(b).Select(e=>e.GetType().Name+":"+e.Amount).ToArray()},Actual=inherited.Select(e=>e.GetType().Name+":"+e.Amount).ToArray()});
  Check("upstream native inheritance fixture really has four enchants",Read(a).Length==2&&Read(b).Length==2);
  Check("upstream fusion 1.5.2 still loses three enchantments",inherited.Length==1&&inherited[0].Amount==3);
  Check("upstream obsolete API absent and real provider present",FusionQuestBlessingFix.Entry.Find("MultiEnchantmentMod.Api.MultiEnchantmentApi")==null&&stack!=null);
  var repaired=ModelDb.Card<Bash>().ToMutable();inherit.Invoke(null,[repaired,new CardModel[]{a,b},"upstream-repaired"]);
  Check("upstream new 1.5.2 variant inherits all four actual entries",Read(repaired).Length==4&&Read(repaired).Select(e=>e.Amount).SequenceEqual(new[]{3,2,5,7}));
  Check("upstream inherited models independently bound to new card",Read(repaired).All(e=>ReferenceEquals(e.Card,repaired))&&Read(a).All(e=>ReferenceEquals(e.Card,a))&&Read(b).All(e=>ReferenceEquals(e.Card,b)));
  for(int i=0;i<3;i++){repaired=Restore(repaired);Check("upstream restored new variant four enchantments "+i,Read(repaired).Length==4&&Read(repaired).Select(e=>e.Amount).SequenceEqual(new[]{3,2,5,7}));}
  var clone=(CardModel)repaired.ClonePreservingMutability();Check("upstream cloned entries do not share instances",Read(clone).Length==4&&Read(clone).Zip(Read(repaired)).All(p=>!ReferenceEquals(p.First,p.Second)));
  var fusedFour=Fuse(a,b);Check("upstream actual CreateFromList inherits all four",Read(fusedFour).Length==4);
  var c=ModelDb.Card<Bash>().ToMutable();c.EnchantInternal(Enchant(Find("MoreEnchantmentsMod.Warding"),9),9);
  var next=Fuse(fusedFour,c);Check("upstream re-fusion has five entries without double inheritance",Read(next).Length==5&&Read(next).Sum(e=>e.Amount)==26);
  var legacy=ModelDb.GetById<CardModel>(ModelDb.GetId(Find("CardFusion.FusedCard"))).ToMutable();
  AccessTools.Field(legacy.GetType(),"_sourceCards").SetValue(legacy,new List<CardModel>{a,b});legacy.EnchantInternal(Enchant(Find("MoreEnchantmentsMod.Spark"),3),3);
  var recovered=ModelDb.Card<Bash>().ToMutable();inherit.Invoke(null,[recovered,new CardModel[]{a,b,legacy},"upstream-legacy"]);
  Check("upstream legacy single slot restores all remaining child enchantments",Read(recovered).Length==4&&Read(recovered).Sum(e=>e.Amount)==17);
  var h=FusionQuestBlessingFix.Entry.Harmony;
  var keywords=AccessTools.Method(typeof(CardModel),"GetKeywordsWithSources");
  var blessing=AccessTools.Method(typeof(FusionQuestBlessingFix.Entry),nameof(FusionQuestBlessingFix.Entry.BlessingKeywords));
  var predicatePatch=AccessTools.Method(typeof(FusionQuestBlessingFix.Entry),nameof(FusionQuestBlessingFix.Entry.QuestPredicate));
  var predicates=Find("Medo.Scripts.Cards.Mem").Assembly.GetTypes().Where(t=>t.Namespace=="Medo.Scripts.Events").SelectMany(t=>t.GetMethods(F|BindingFlags.DeclaredOnly)).Where(m=>m.ReturnType==typeof(bool)&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType==typeof(CardModel)).ToArray();
  bool old=TestMode.IsOn;TestMode.IsOn=true;
  try{
   var owner=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);RunStateForAudit(owner);
   var speed=ModelDb.Card<Speedster>().ToMutable();speed.Owner=owner;
   var relic=ModelDb.GetById<RelicModel>(ModelDb.GetId(Find("YuiExtra.Relics.BlessedSpeedster"))).ToMutable();owner.AddRelicInternal(relic,silent:true);
   var fused=Fuse(speed,ModelDb.Card<Anger>().ToMutable());fused.Owner=owner;
   h.Unpatch(keywords,blessing);
   Check("upstream unfused blessing really grants Sly",speed.Keywords.Contains(CardKeyword.Sly));
   Check("upstream fusion still loses dynamic blessed Sly",!fused.Keywords.Contains(CardKeyword.Sly));
   h.Patch(keywords,postfix:new HarmonyMethod(blessing){priority=Priority.Last});
   Check("upstream retained blessing bridge restores Sly",fused.Keywords.Contains(CardKeyword.Sly));
   var mem=ModelDb.GetById<CardModel>(ModelDb.GetId(Find("Medo.Scripts.Cards.Mem"))).ToMutable();mem.Owner=owner;var quest=Fuse(mem,ModelDb.Card<Anger>().ToMutable());quest.Owner=owner;
   int tested=0;
   foreach(var method in predicates){
    object? receiver=method.IsStatic?null:System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(method.DeclaringType!);
    if(!(bool)method.Invoke(receiver,[mem])!)continue;
    h.Unpatch(method,predicatePatch);
    try{Check("upstream quest predicate rejects fused Mem "+method.Name,!(bool)method.Invoke(receiver,[quest])!);}
    finally{h.Patch(method,transpiler:new HarmonyMethod(predicatePatch));}
    Check("upstream retained quest bridge accepts fused Mem "+method.Name,(bool)method.Invoke(receiver,[quest])!);tested++;
   }
   Check("upstream Mem quest predicates exercised",tested>=2);
  }finally{
   if(!(Harmony.GetPatchInfo(keywords)?.Postfixes.Any(p=>p.PatchMethod==blessing)??false))h.Patch(keywords,postfix:new HarmonyMethod(blessing){priority=Priority.Last});
   TestMode.IsOn=old;
  }
  File.WriteAllText(Output+"/upstream-audit-controls.json",JsonSerializer.Serialize(new{Evidence=evidence,States=Pack.RuntimeLoader.States,Options=Pack.Options.All.Where(o=>!o.Misc).Select(o=>new{o.Key,o.Title,o.Required,o.AnyOf})},new JsonSerializerOptions{WriteIndented=true}));
  return Task.CompletedTask;
 }
 static void RunStateForAudit(Player owner)=>MegaCrit.Sts2.Core.Runs.RunState.CreateForTest([owner],seed:"UPSTREAM-AUDIT");
}
