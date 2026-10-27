using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static CardModel RepeatForm(Type type,int mode){
  var card=ModelDb.Card<Pack.ModForm>().ToMutable();var enchant=Enchant(type,1);
  if(mode==1){card.UpgradeInternal();card.FinalizeUpgradeInternal();}
  if(mode==2){var sharp=ModelDb.Enchantment<Sharp>().ToMutable();card.EnchantInternal(sharp,1);Extra(card,enchant);}
  else if(mode==3){var composite=Find("Plague.PlagueCompositeEnchantment").GetMethod("ConvertFrom",F)!.Invoke(null,[card,enchant])!;AccessTools.Field(typeof(CardModel),"<Enchantment>k__BackingField").SetValue(card,composite);}
  else{card.EnchantInternal(enchant,1);enchant.ModifyCard();}
  return card;
 }
 static async Task ModFormRepeatChecks(){
  int start=passes.Count;
  var ordinary=ModelDb.Card<Pack.ModForm>().ToMutable();ordinary.UpgradeInternal();ordinary.FinalizeUpgradeInternal();
  Check("form no enchant still single upgrade",!ordinary.IsUpgradable&&ordinary.CurrentUpgradeLevel==1);
  Check("form upgrade exposes real numeric delta",ordinary.DynamicVars["GenerationUpgradeLevel"].BaseValue==1);
  if(Minimal){Check("form repeat adapter safe without optional mods",Pack.ModFormRepeatUpgrades.Limit(ordinary)==0);return;}
  var audit=new List<object>();var repeats=new List<Type>();
  // 扫描本次真正加载并注册的全部附魔，记录文案与实际重复升级资格。
  foreach(var type in ModelDb.AllAbstractModelSubtypes.Where(t=>!t.IsAbstract&&typeof(EnchantmentModel).IsAssignableFrom(t)).OrderBy(t=>t.FullName)){
   var enchant=Enchant(type,1);var probe=ModelDb.Card<Bash>().ToMutable();probe.EnchantInternal(enchant,1);
   int cap=Pack.ModFormRepeatUpgrades.Limit(probe);
   string description=enchant.DynamicDescription.GetFormattedText();
   audit.Add(new{Type=type.FullName,Description=description,RepeatLimit=cap});
   if(cap>1)repeats.Add(type);
  }
  File.WriteAllText(Output+"/modform-enchantment-audit.json",JsonSerializer.Serialize(audit,new JsonSerializerOptions{WriteIndented=true}));
  Check("repeat scan includes scorching",repeats.Contains(Find("MoreEnchant.Enchantments.ScorchingEnchantment")));
  Check("repeat scan includes stacked shadow",repeats.Contains(Find("Plague.PlagueStackedShadowEnchantment")));
  foreach(var type in repeats){
   foreach(int mode in new[]{0,1,2,3}){
    var card=RepeatForm(type,mode);string label="form repeat "+type.Name+" mode="+mode;
    if(mode<2)Check(label+" legal enchant candidate",Enchant(type,1).CanEnchant(ModelDb.Card<Pack.ModForm>().ToMutable()));
    for(int n=card.CurrentUpgradeLevel;n<5;n++){Check(label+" eligible at "+n,card.IsUpgradable);card.UpgradeInternal();card.FinalizeUpgradeInternal();}
    Check(label+" actual title level and effect",card.CurrentUpgradeLevel==5&&card.Title.EndsWith("+5")&&card.GetDescriptionForPile(PileType.None).Contains("升级 +5")&&card.DynamicVars["GenerationUpgradeLevel"].BaseValue==5);
    for(int i=0;i<3;i++){
     card=(CardModel)card.MutableClone();
     Godot.GD.Print($"[Aikun020Probe] {label} cloned level={card.CurrentUpgradeLevel} cap={card.MaxUpgradeLevel} var={card.DynamicVars["GenerationUpgradeLevel"].BaseValue} ench={string.Join(',',Pack.EnchantmentView.Read(card).Select(e=>e.GetType().Name))}");
     card=Restore(card);
     Godot.GD.Print($"[Aikun020Probe] {label} restored level={card.CurrentUpgradeLevel} cap={card.MaxUpgradeLevel} var={card.DynamicVars["GenerationUpgradeLevel"].BaseValue} ench={string.Join(',',Pack.EnchantmentView.Read(card).Select(e=>e.GetType().Name))} description={card.GetDescriptionForPile(PileType.None)}");
     Check(label+" clone JSON "+i,card.CurrentUpgradeLevel==5&&card.IsUpgradable&&card.DynamicVars["GenerationUpgradeLevel"].BaseValue==5&&card.GetDescriptionForPile(PileType.None).Contains("升级 +5"));
    }
    card.UpgradeInternal();card.FinalizeUpgradeInternal();Check(label+" continue after load",card.CurrentUpgradeLevel==6&&card.DynamicVars["GenerationUpgradeLevel"].BaseValue==6);
   }
  }
  await PreviewSample(RepeatForm(Find("Plague.PlagueStackedShadowEnchantment"),0),"stacked shadow form",false,false);
  await PreviewSample(RepeatForm(Find("Plague.PlagueStackedShadowEnchantment"),2),"stacked shadow extra form",false,false);
  foreach(var type in repeats){
   var card=RepeatForm(type,2);
   AccessTools.Field(typeof(CardModel),"_currentUpgradeLevel").SetValue(card,1001);
   Check("repeat limit continues above 999 "+type.Name,card.MaxUpgradeLevel>=1002&&card.IsUpgradable);
   var other=repeats.First(t=>t!=type);Extra(card,Enchant(other,1));
   Check("mixed repeat providers preserve eligibility "+type.Name,card.MaxUpgradeLevel>=1002&&card.IsUpgradable&&Pack.EnchantmentView.Read(card).Length==3);
  }
  // 在测试探针中模拟只允许三次的附魔，防止通用适配把有限升级变成无限。
  var harmony=new Harmony("Aikun020Probe.FiniteRepeat");var getter=AccessTools.PropertyGetter(typeof(CardModel),nameof(CardModel.MaxUpgradeLevel));
  harmony.Patch(getter,postfix:new HarmonyMethod(typeof(Entry),nameof(FiniteRepeatProbe)){priority=Priority.First});
  try{
   var finite=ModelDb.Card<Pack.ModForm>().ToMutable();var e=ModelDb.Enchantment<Sharp>().ToMutable();e.Amount=703;finite.EnchantInternal(e,703);
   for(int i=0;i<3;i++){Check("finite form eligible "+i,finite.IsUpgradable);finite.UpgradeInternal();finite.FinalizeUpgradeInternal();}
   Check("finite form stops at three",finite.MaxUpgradeLevel==3&&!finite.IsUpgradable&&finite.CurrentUpgradeLevel==3);
   finite=Restore(finite);e=finite.Enchantment!;
   Check("finite form retains cap after load",finite.MaxUpgradeLevel==3&&!finite.IsUpgradable&&finite.CurrentUpgradeLevel==3);
   e.Amount=704;Check("finite rule follows amount",finite.MaxUpgradeLevel==4&&finite.IsUpgradable);
  }finally{harmony.UnpatchAll(harmony.Id);}
  File.WriteAllText(Output+"/modform-repeat-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Failed=0,EnchantmentsScanned=audit.Count,RepeatTypes=repeats.Select(t=>t.FullName),Checks=passes.Skip(start)},new JsonSerializerOptions{WriteIndented=true}));
 }
 static void FiniteRepeatProbe(CardModel __instance,ref int __result){
  if(__instance is Bash&&__instance.Enchantment is Sharp&&__instance.Enchantment.Amount is 703 or 704)__result=__instance.Enchantment.Amount-700;
 }
}
