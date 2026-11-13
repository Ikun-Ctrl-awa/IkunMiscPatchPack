using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Random;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Settings;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
// 仅隔离探针登记，用于稳定覆盖稀有度与原有可生成标志的交集。
public class ModFormTokenFixture:ModCardTemplate {
 public ModFormTokenFixture():base(1,CardType.Skill,CardRarity.Token,TargetType.Self){}
 public override CardPoolModel Pool=>ModelDb.CardPool<ColorlessCardPool>();
 public override bool CanBeGeneratedInCombat=>true;
 public override bool CanBeGeneratedByModifiers=>true;
}
public sealed class ModFormBlockedTokenFixture:ModFormTokenFixture {
 public override bool CanBeGeneratedInCombat=>false;
 public override bool CanBeGeneratedByModifiers=>false;
}
public static partial class Entry {
 static async Task ModFormTokenChecks(){
  int start=passes.Count;bool saved=Pack.Settings.StartupModFormIncludeTokens,self=Pack.Settings.StartupModFormIncludeSelf,current=Pack.Settings.Current.ModFormIncludeTokens;
  var groups=Pack.ModFormContent.Groups;var property=AccessTools.Property(typeof(Pack.ModFormContent),nameof(Pack.ModFormContent.Groups));
  try{
   Check("token setting default and old config preserve enabled behavior",new Pack.PackConfig().ModFormIncludeTokens&&JsonSerializer.Deserialize<Pack.PackConfig>("{\"Enabled\":{}}")!.ModFormIncludeTokens);
   var section=ModSettingsRegistry.GetPages().Single(p=>p.Id==Pack.Settings.Id+".misc").Sections.Single(s=>s.Id=="mod_form");
   Check("token native child toggle keeps main option count",section.Entries.Any(e=>e.Id=="include_tokens")&&section.Entries.Any(e=>e.Id=="include_self")&&Pack.Options.All.Length==51);
   var token=ModelDb.Card<ModFormTokenFixture>();var blocked=ModelDb.Card<ModFormBlockedTokenFixture>();var ordinary=ModelDb.Card<Pack.DongniMeaning>();
   foreach(bool enabled in new[]{false,true}){
    Check("token option JSON roundtrip "+enabled,JsonSerializer.Deserialize<Pack.PackConfig>(JsonSerializer.Serialize(new Pack.PackConfig{ModFormIncludeTokens=enabled}))!.ModFormIncludeTokens==enabled);
    Pack.Settings.StartupModFormIncludeTokens=enabled;
    Check("token candidate follows switch "+enabled,Pack.ModFormContent.IsCandidate(token)==enabled&&!Pack.ModFormContent.IsCandidate(blocked)&&Pack.ModFormContent.IsCandidate(ordinary)&&!Pack.ModFormContent.IsCandidate(ModelDb.Card<Bash>()));
    var pool=Pack.ModFormContent.GroupCards([token,ordinary,blocked]);Check("token grouped and sampled candidate pool "+enabled,pool.SelectMany(g=>g.Cards).Contains(token)==enabled&&Enumerable.Range(0,100).All(i=>Pack.ModFormContent.Choose(pool,new Rng((uint)i))!.Rarity!=CardRarity.Token||enabled));
    var actual=Pack.ModFormContent.GroupCards(ModelDb.AllCards);Check("token all loaded models obey switch "+enabled,enabled||actual.SelectMany(g=>g.Cards).All(c=>c.Rarity!=CardRarity.Token));
    property.SetValue(null,Pack.ModFormContent.GroupCards([token]));ClearPoolCache();
    using var f=NewDongniFixture();int before=f.P.PlayerCombatState!.Hand.Cards.Count;await Pack.ModFormContent.Generate(f.P,2);
    Check("token actual combat generation count "+enabled,f.P.PlayerCombatState.Hand.Cards.Count==before+(enabled?1:0));
    if(enabled){var made=f.P.PlayerCombatState.Hand.Cards.Last();Check("token actual generated upgrade free ethereal exhaust",made is ModFormTokenFixture&&made.CurrentUpgradeLevel==2&&made.EnergyCost.GetWithModifiers(CostModifiers.All)==0&&made.Keywords.Contains(CardKeyword.Ethereal)&&made.Keywords.Contains(CardKeyword.Exhaust));Check("token generated save readback",Restore(made).CurrentUpgradeLevel==2);}
   }
   Pack.Settings.StartupModFormIncludeTokens=false;Pack.Settings.StartupModFormIncludeSelf=true;Check("token off does not block enabled self",Pack.ModFormContent.IsCandidate(ModelDb.Card<Pack.ModForm>()));
   Pack.Settings.Current.ModFormIncludeTokens=true;Check("token status explains restart",Pack.Settings.Status(Pack.Options.All.Single(o=>o.Key=="mod_form")).Contains("重启"));
   SelfCandidateRules();await SelfGeneration();
  }finally{Pack.Settings.StartupModFormIncludeTokens=saved;Pack.Settings.StartupModFormIncludeSelf=self;Pack.Settings.Current.ModFormIncludeTokens=current;property.SetValue(null,groups);ClearPoolCache();}
  File.WriteAllText(Output+"/modform-token-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Checks=passes.Skip(start)},new JsonSerializerOptions{WriteIndented=true}));
 }
}
