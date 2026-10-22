using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using FusionQuestBlessingFix;
var dirs=new[]{@"D:\Games\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64",@"D:\Games\Steam\steamapps\common\Slay the Spire 2\mods\CardFusion_LocalFix",@"D:\Games\Steam\steamapps\workshop\content\2868840\3747563416",@"D:\Games\Steam\steamapps\workshop\content\2868840\3766908612",@"D:\Games\Steam\steamapps\workshop\content\2868840\3737335127\BaseLib",@"D:\Games\Steam\steamapps\workshop\content\2868840\3747602295\compat\0.111.0",@"D:\Games\Steam\steamapps\workshop\content\2868840\3747602295\shared",@"D:\Games\Steam\steamapps\common\Slay the Spire 2\mods\YUICore_LocalFix"};
AssemblyLoadContext.Default.Resolving+=(ctx,name)=>{var p=dirs.Select(d=>Path.Combine(d,name.Name+".dll")).FirstOrDefault(File.Exists);return p==null?null:ctx.LoadFromAssemblyPath(p);};
var fusion=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("CardFusion"));var yui=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("YUISpireExpansion"));var medo=AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("Medo"));Run(fusion,yui,medo,args.Contains("baseline"));
static void Run(Assembly fusion,Assembly yui,Assembly medo,bool baseline){
 const BindingFlags F=Entry.Flags;MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn=true;int pass=0,fail=0;
 void Check(string n,bool ok){Console.WriteLine($"{(ok?"PASS":"FAIL")} {n}");if(ok)pass++;else fail++;}
 var quiet=new Harmony("local.fusion-quest.regression.quiet");foreach(var n in new[]{"LogInfo","LogWarn"})quiet.Patch(fusion.GetType("CardFusion.MainFile")!.GetMethod(n,F)!,prefix:new HarmonyMethod(typeof(Support).GetMethod(nameof(Support.Quiet))!));
 quiet.CreateClassProcessor(yui.GetType("YuiExtra.Relics.CardBlessingKeywordsPatch")!).Patch();
 var fusedType=fusion.GetType("CardFusion.FusedCard")!;var blessed=yui.GetType("YuiExtra.Relics.BlessedSpeedster")!;var mem=medo.GetType("Medo.Scripts.Cards.Mem")!;var cyrene=medo.GetType("Medo.Scripts.Cards.Cyrene")!;
 var blessedDance=yui.GetType("YuiExtra.Relics.BlessedBladeDance")!;
 foreach(var t in new[]{typeof(Speedster),typeof(Anger),typeof(BladeDance),typeof(Adrenaline),fusedType,blessed,blessedDance,mem,cyrene})ModelDb.Inject(t);
 CardModel Card(Type t)=>ModelDb.GetById<CardModel>(ModelDb.GetId(t)).ToMutable();
 CardModel Fuse(params CardModel[] children){var result=Card(fusedType);fusedType.GetField("_sourceCards",F)!.SetValue(result,children.ToList());return result;}
 var p=(Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));typeof(Player).GetField("_relics",F)!.SetValue(p,new List<RelicModel>());typeof(Player).GetField("<Deck>k__BackingField",F)!.SetValue(p,new CardPile(PileType.Deck));
 var relic=ModelDb.GetById<RelicModel>(ModelDb.GetId(blessed)).ToMutable();relic.Owner=p;((List<RelicModel>)p.Relics).Add(relic);
 if(!baseline)Entry.Install();
 var speed=Card(typeof(Speedster));speed.Owner=p;Check("Unfused blessed Speedster retains Sly",speed.Keywords.Contains(CardKeyword.Sly));
 var fused=Fuse(speed,Card(typeof(Anger)));fused.Owner=p;
 Check("Blessed Speedster fusion inherits dynamic Sly",fused.Keywords.Contains(CardKeyword.Sly));
 Check("Blessing does not contaminate permanent local keywords",!fused.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Sly));
 ((List<RelicModel>)p.Relics).Clear();Check("Losing blessing removes dynamic Sly",!fused.Keywords.Contains(CardKeyword.Sly));
 ((List<RelicModel>)p.Relics).Add(relic);Check("Regaining blessing restores Sly without refusion",fused.Keywords.Contains(CardKeyword.Sly));
 var childOwner=speed.Owner;fused.Keywords.Contains(CardKeyword.Sly);Check("Keyword query preserves child owner",ReferenceEquals(speed.Owner,childOwner));
 var nested=Fuse(fused,Card(typeof(Anger)));nested.Owner=p;Check("Repeated/nested fusion retains blessing",nested.Keywords.Contains(CardKeyword.Sly));
 var normal=Fuse(Card(typeof(Anger)),Card(typeof(Anger)));normal.Owner=p;Check("Unrelated fusion receives no Sly",!normal.Keywords.Contains(CardKeyword.Sly));
 var danceRelic=ModelDb.GetById<RelicModel>(ModelDb.GetId(blessedDance)).ToMutable();danceRelic.Owner=p;((List<RelicModel>)p.Relics).Add(danceRelic);
 var danceFusion=Fuse(Card(typeof(BladeDance)),Card(typeof(Adrenaline)));danceFusion.Owner=p;danceFusion.AddKeyword(CardKeyword.Exhaust);Check("Another blessing cannot erase existing fusion Exhaust",danceFusion.Keywords.Contains(CardKeyword.Exhaust));
 var m=Card(mem);m.Owner=p;var questFusion=Fuse(m,Card(typeof(Anger)));questFusion.Owner=p;
 var c=Card(cyrene);c.Owner=p;var cyreneFusion=Fuse(c,Card(typeof(Anger)));cyreneFusion.Owner=p;
 var predicates=medo.GetTypes().Where(t=>t.Namespace=="Medo.Scripts.Events").SelectMany(t=>t.GetMethods(F|BindingFlags.DeclaredOnly)).Where(method=>method.ReturnType==typeof(bool)&&method.GetParameters().Length==1&&method.GetParameters()[0].ParameterType==typeof(CardModel)).ToArray();
 int tested=0;
 foreach(var method in predicates){object? receiver=method.IsStatic?null:RuntimeHelpers.GetUninitializedObject(method.DeclaringType!);bool plain=(bool)method.Invoke(receiver,new object[]{m})!;bool plainCyrene=(bool)method.Invoke(receiver,new object[]{c})!;
  if(plain&&!plainCyrene){tested++;Check("Mem event predicate handles fusion: "+method.Name,(bool)method.Invoke(receiver,new object[]{questFusion})!);}
  if(plainCyrene&&!plain){tested++;Check("Cyrene event predicate handles fusion: "+method.Name,(bool)method.Invoke(receiver,new object[]{cyreneFusion})!);}
 }
 Check("Both quest stages have actual event predicate coverage",tested>=4);
 var deck=(List<CardModel>)p.Deck.Cards;deck.Add(questFusion);deck.Add(normal);
 CardModel single=m;Entry.NormalizeSingle(ref single);Check("Child removal resolves to physical deck fusion",ReferenceEquals(single,questFusion));
 IReadOnlyList<CardModel> batch=new[]{m,questFusion,normal};Entry.NormalizeBatch(ref batch);Check("Batch removal resolves and deduplicates parent",batch.Count==2&&ReferenceEquals(batch[0],questFusion)&&ReferenceEquals(batch[1],normal));
 var lone=Card(mem);lone.Owner=p;deck.Add(lone);Check("Independent task card remains its own removal target",ReferenceEquals(Entry.DeckRepresentative(lone),lone));
 deck.Remove(questFusion);Check("Detached fusion is not reused as deck removal target",ReferenceEquals(Entry.DeckRepresentative(m),m));
 Check("Event IL rewrites include quest completion records",baseline||Entry.PredicateRewrites>=4&&Entry.CompletionRewrites==2);
 Console.WriteLine($"REGRESSION_RESULT passed={pass} failed={fail}");Environment.ExitCode=fail==0?0:2;
}
static class Support{public static bool Quiet()=>false;}
