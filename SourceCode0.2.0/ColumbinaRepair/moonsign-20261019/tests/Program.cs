using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;

var path=Path.GetFullPath(args[0]);
var dirs=new[]{Path.GetDirectoryName(path)!,@"D:\Games\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64",@"D:\Games\Steam\steamapps\workshop\content\2868840\3737335127\BaseLib"};
AssemblyLoadContext.Default.Resolving+=(ctx,name)=>{var p=dirs.Select(d=>Path.Combine(d,name.Name+".dll")).FirstOrDefault(File.Exists);return p==null?null:ctx.LoadFromAssemblyPath(p);};
var mod=AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
Run(mod);
static void Run(Assembly mod){
 MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn=true;
 const BindingFlags f=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 void Set(object o,string n,object? v)=>o.GetType().GetField(n,f)!.SetValue(o,v);
 int pass=0,fail=0;
 void Check(string n,bool b){Console.WriteLine($"{(b?"PASS":"FAIL")} {n}");if(b)pass++;else fail++;}
 CardModel Card(Type t,Player p){var c=(CardModel)(t.Assembly==typeof(Bash).Assembly?Activator.CreateInstance(t)!:RuntimeHelpers.GetUninitializedObject(t));typeof(AbstractModel).GetProperty("IsMutable",f)!.SetValue(c,true);c.Owner=p;return c;}
 (Player,CardModel) Fixture(string seed){
  var p=(Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
  var s=(RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
  var rngType=typeof(RunState).GetProperty("Rng")!.PropertyType;
  Set(s,"<Rng>k__BackingField",Activator.CreateInstance(rngType,seed));Set(s,"_players",new List<Player>{p});
  Set(p,"_runState",s);Set(p,"_runPiles",Array.Empty<CardPile>());Set(p,"<Creature>k__BackingField",new Creature(p,80,80));
  var pcs=(PlayerCombatState)RuntimeHelpers.GetUninitializedObject(typeof(PlayerCombatState));Set(pcs,"_player",p);
  foreach(var pair in new[]{("Hand",PileType.Hand),("DrawPile",PileType.Draw),("DiscardPile",PileType.Discard),("ExhaustPile",PileType.Exhaust),("PlayPile",PileType.Play)})Set(pcs,"<"+pair.Item1+">k__BackingField",new CardPile(pair.Item2));
  Set(p,"<PlayerCombatState>k__BackingField",pcs);
  return(p,Card(mod.GetType("ColumbinaMod.ColumbinaModCode.Cards.MoonsignAscendantGleam")!,p));
 }
 void Add(Player p,PileType pile,CardModel c)=>((List<CardModel>)pile.GetPile(p).Cards).Add(c);
 int Cost(CardModel c)=>c.EnergyCost.GetWithModifiers(CostModifiers.All);
 var reduce=mod.GetType("ColumbinaMod.ColumbinaModCode.Cards.MoonsignAscendantGleam")!.GetMethod("ReduceRandomCard",f)!;
 void Reduce(CardModel c,PileType pile)=>reduce.Invoke(c,new object[]{pile});
 int misses=0;
 for(int i=0;i<64;i++){
  var(p,source)=Fixture("gleam"+i);var paid=Card(typeof(Bash),p);
  foreach(var c in new[]{Card(typeof(Anger),p),Card(typeof(Whirlwind),p),Card(typeof(Clumsy),p),paid})Add(p,PileType.Hand,c);
  Reduce(source,PileType.Hand);if(Cost(paid)!=1)misses++;
 }
 var fixedVersion=mod.GetType("ColumbinaMod.LocalCompatibility.MoonsignCostFix")!=null;
 Check("Mixed zero/X/unplayable pool: 64 seeds, ineffective selections="+misses,fixedVersion?misses==0:misses>0);
 {
  var(p,source)=Fixture("multiple");var a=Card(typeof(Bash),p);var b=Card(typeof(Bash),p);Add(p,PileType.Hand,a);Add(p,PileType.Hand,b);
  Reduce(source,PileType.Hand);Check("Exactly one card reduced",Cost(a)+Cost(b)==3);
  Check("Other piles unchanged",p.PlayerCombatState!.DiscardPile.Cards.Count==0);
 }
 {
  var(p,source)=Fixture("empty");Reduce(source,PileType.Hand);Check("Empty pile is safe",true);
  var zero=Card(typeof(Anger),p);var x=Card(typeof(Whirlwind),p);var curse=Card(typeof(Clumsy),p);
  foreach(var c in new[]{zero,x,curse})Add(p,PileType.Hand,c);Reduce(source,PileType.Hand);
  Check("No eligible card is safe",Cost(zero)==0&&x.EnergyCost.CostsX&&Cost(curse)<0);
 }
 foreach(bool upgraded in new[]{false,true}){
  var(p,source)=Fixture("upgrade"+upgraded);typeof(CardModel).GetField("_currentUpgradeLevel",f)!.SetValue(source,upgraded?1:0);
  var cards=new[]{Card(typeof(Bash),p),Card(typeof(Bash),p),Card(typeof(Bash),p)};
  var piles=new[]{PileType.Hand,PileType.Draw,PileType.Discard};for(int i=0;i<3;i++)Add(p,piles[i],cards[i]);
  ((Task)source.GetType().GetMethod("OnPlay",f)!.Invoke(source,new object?[]{null,null})!).GetAwaiter().GetResult();
  Check("OnPlay hand/draw/discard upgraded="+upgraded,Cost(cards[0])==1&&Cost(cards[1])==1&&Cost(cards[2])==(upgraded?1:2));
 }
 if(fixedVersion){
  var(p,source)=Fixture("currentcost");var zeroed=Card(typeof(Bash),p);zeroed.EnergyCost.SetThisCombat(0);var taxed=Card(typeof(Anger),p);taxed.EnergyCost.AddThisCombat(2);
  Add(p,PileType.Hand,zeroed);Add(p,PileType.Hand,taxed);Reduce(source,PileType.Hand);
  Check("Eligibility uses current modified cost",Cost(zeroed)==0&&Cost(taxed)==1);
  var refs=mod.GetReferencedAssemblies();Check("No temporary Helper runtime dependency",refs.All(r=>r.Name!="Helper"));
 }
 Console.WriteLine($"REGRESSION_RESULT passed={pass} failed={fail}");Environment.ExitCode=fail==0?0:2;
}
