using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
namespace FusionQuestBlessingFix;

[ModInitializer(nameof(Initialize))]
public static class Entry {
 public const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 public static readonly Harmony Harmony=new("local.fusion.quest-blessing.v1");
 public static int PredicateRewrites,CompletionRewrites;
 static bool installed;
 public static void Initialize(){((Godot.SceneTree)Godot.Engine.GetMainLoop()).CreateTimer(1).Timeout+=Install;}
 public static Type? Find(string fullName)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(fullName,false)).FirstOrDefault(t=>t!=null);
 public static void Install(){
  if(installed)return;installed=true;
  Harmony.Patch(AccessTools.Method(typeof(CardModel),"GetKeywordsWithSources"),postfix:new HarmonyMethod(typeof(Entry),nameof(BlessingKeywords)){priority=Priority.Last});
  Harmony.Patch(AccessTools.Method(typeof(CardPileCmd),"RemoveFromDeck",new[]{typeof(CardModel),typeof(bool)}),prefix:new HarmonyMethod(typeof(Entry),nameof(NormalizeSingle)){priority=Priority.First});
  Harmony.Patch(AccessTools.Method(typeof(CardPileCmd),"RemoveFromDeck",new[]{typeof(IReadOnlyList<CardModel>),typeof(bool)}),prefix:new HarmonyMethod(typeof(Entry),nameof(NormalizeBatch)){priority=Priority.First});
  var medo=Find("Medo.Scripts.Cards.Mem")?.Assembly;
  if(medo!=null)foreach(var t in medo.GetTypes().Where(t=>t.Namespace=="Medo.Scripts.Events"))foreach(var method in t.GetMethods(Flags|BindingFlags.DeclaredOnly)){
   if(method.ReturnType==typeof(bool)&&method.GetParameters().Length==1&&method.GetParameters()[0].ParameterType==typeof(CardModel))
    Harmony.Patch(method,transpiler:new HarmonyMethod(typeof(Entry),nameof(QuestPredicate)));
   if(method.Name=="MoveNext"&&(t.Name.Contains("<BecomeHero>")||t.Name.Contains("<Journey>")))
    Harmony.Patch(method,transpiler:new HarmonyMethod(typeof(Entry),nameof(QuestCompletion)));
  }
  Log($"installed dynamic blessing keywords, single/batch deck removal; Medo predicates={PredicateRewrites} completions={CompletionRewrites}");
 }
 public static void Log(string s){if(MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn)Console.WriteLine("[FusionQuestBlessingFix] "+s);else Godot.GD.Print("[FusionQuestBlessingFix] "+s);}
 public static IReadOnlyList<CardModel> Children(CardModel card)=>card.GetType().FullName=="CardFusion.FusedCard"?(IReadOnlyList<CardModel>)card.GetType().GetProperty("AllCards",Flags)!.GetValue(card)!:Array.Empty<CardModel>();
 public static IEnumerable<CardModel> Descendants(CardModel card){
  var seen=new HashSet<CardModel>(ReferenceEqualityComparer.Instance);var pending=new Stack<CardModel>(Children(card));
  while(pending.Count>0){var child=pending.Pop();if(!seen.Add(child))continue;yield return child;foreach(var nested in Children(child))pending.Push(nested);}
 }
 public static bool IsOrContains<T>(CardModel card) where T:CardModel=>card is T||Descendants(card).Any(c=>c is T);
 public static void BlessingKeywords(CardModel __instance,KeywordSources sources,ref IReadOnlySet<CardKeyword> __result){
  if(!sources.HasFlag(KeywordSources.Global)||!__instance.IsMutable||__instance.Owner==null||Children(__instance).Count==0)return;
  var registry=Find("YuiExtra.Relics.CardBlessingRegistry");if(registry==null)return;
  var lookup=registry.GetMethod("TryGetActiveEffect",Flags)!;HashSet<CardKeyword>? merged=null;
  var ownerField=typeof(CardModel).GetField("_owner",Flags)!;
  foreach(var child in Descendants(__instance)){
   if(!child.IsMutable)continue;
   var oldOwner=child.Owner;
   try {
    // 只在查询期间为子卡绑定融合牌玩家；动态赐福不写入永久本地词条。
    ownerField.SetValue(child,__instance.Owner);var args=new object?[]{child,null};
    if(!(bool)lookup.Invoke(null,args)!)continue;
    var granted=new HashSet<CardKeyword>();
    args[1]!.GetType().GetMethod("ModifyKeywordsInCombat",Flags)!.Invoke(args[1],new object[]{child,granted});
    // 只合并赐福额外赋予的词条，不让某张子卡的去词条效果删除其他子卡的词条。
    if(granted.Count>0){merged??=new HashSet<CardKeyword>(__result);merged.UnionWith(granted);}
   }finally{ownerField.SetValue(child,oldOwner);}
  }
  if(merged!=null&&!merged.SetEquals(__result))__result=merged;
 }
 public static CardModel DeckRepresentative(CardModel card){
  var owner=card.Owner;if(owner==null)return card;
  // 优先独立卡；只将实际属于当前牌组融合牌的子卡转换为其父卡。
  if(owner.Deck.Cards.Any(c=>ReferenceEquals(c,card)))return card;
  return owner.Deck.Cards.FirstOrDefault(p=>Descendants(p).Any(c=>ReferenceEquals(c,card)))??card;
 }
 public static void NormalizeSingle(ref CardModel __0)=>__0=DeckRepresentative(__0);
 public static void NormalizeBatch(ref IReadOnlyList<CardModel> __0){
  var normalized=new List<CardModel>();var seen=new HashSet<CardModel>(ReferenceEqualityComparer.Instance);
  foreach(var card in __0){var parent=DeckRepresentative(card);if(seen.Add(parent))normalized.Add(parent);}
  __0=normalized;
 }
 public static IEnumerable<CodeInstruction> QuestPredicate(IEnumerable<CodeInstruction> original){
  var code=original.ToList();for(int i=0;i+2<code.Count;i++){
   if(code[i].opcode!=OpCodes.Isinst||code[i].operand is not Type t||t.FullName is not ("Medo.Scripts.Cards.Mem" or "Medo.Scripts.Cards.Cyrene")||code[i+1].opcode!=OpCodes.Ldnull||code[i+2].opcode!=OpCodes.Cgt_Un)continue;
   // 保留分支标签和异常块，只替换原有类型谓词，不改变事件奖励/流程。
   code[i].opcode=OpCodes.Call;code[i].operand=typeof(Entry).GetMethod(nameof(IsOrContains))!.MakeGenericMethod(t);
   code[i+1].opcode=OpCodes.Nop;code[i+1].operand=null;code[i+2].opcode=OpCodes.Nop;code[i+2].operand=null;PredicateRewrites++;
  }return code;
 }
 public static IEnumerable<CodeInstruction> QuestCompletion(IEnumerable<CodeInstruction> original,MethodBase __originalMethod){
  foreach(var instruction in original){
   if(instruction.Calls(AccessTools.Method(typeof(PlayerCmd),nameof(PlayerCmd.CompleteQuest)))){
    instruction.operand=typeof(Entry).GetMethod(__originalMethod.DeclaringType!.Name.Contains("<BecomeHero>")?nameof(CompleteMem):nameof(CompleteCyrene));CompletionRewrites++;
   }yield return instruction;
  }
 }
 public static void CompleteMem(CardModel card)=>Complete(card,"Medo.Scripts.Cards.Mem");
 public static void CompleteCyrene(CardModel card)=>Complete(card,"Medo.Scripts.Cards.Cyrene");
 static void Complete(CardModel card,string typeName){
  if(card.GetType().FullName==typeName){PlayerCmd.CompleteQuest(card);return;}
  foreach(var child in Descendants(card).Where(c=>c.GetType().FullName==typeName)){
   var ownerField=typeof(CardModel).GetField("_owner",Flags)!;var old=child.Owner;
   try{ownerField.SetValue(child,card.Owner);PlayerCmd.CompleteQuest(child);}finally{ownerField.SetValue(child,old);}
  }
 }
}
