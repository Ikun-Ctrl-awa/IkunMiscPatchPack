using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using STS2RitsuLib.Models.Capabilities;
using STS2RitsuLib.Scaffolding.Content;

namespace IkunMiscPatchPack;

public sealed class DongniDisgust : ModCardTemplate {
 public DongniDisgust():base(0,CardType.Curse,CardRarity.Curse,TargetType.Self){}
 public override CardPoolModel Pool=>ModelDb.CardPool<CurseCardPool>();
 public override int MaxUpgradeLevel=>1;
 public override IEnumerable<CardKeyword> CanonicalKeywords=>[CardKeyword.Ethereal,CardKeyword.Exhaust];
 public override bool CanBeGeneratedInCombat=>Settings.Active(TowerCurseContent.Key);
 public override bool CanBeGeneratedByModifiers=>CanBeGeneratedInCombat;
 public override string PortraitPath=>DongniDisgustArt.PortraitPath;
 public override string BetaPortraitPath=>PortraitPath;
 protected override string PortraitPngPath=>PortraitPath;
 public override string? CustomPortraitPath=>PortraitPath;
 public override string? CustomBetaPortraitPath=>PortraitPath;
 public override IEnumerable<string> AllPortraitPaths=>[PortraitPath];
 protected override IEnumerable<IHoverTip> AdditionalHoverTips=>[HoverTipFactory.FromKeyword(CardKeyword.Eternal)];
 protected override IEnumerable<DynamicVar> CanonicalVars=>[new StringVar("DrawText",""),new StringVar("ReleaseText","选择至多5张卡牌，移除它们的[gold]永恒[/gold]。"),new StringVar("UpgradeText","\n可以被[gold]升级[/gold]。")];
 protected override void OnUpgrade(){((StringVar)DynamicVars["DrawText"]).StringValue="抽1张牌。\n";((StringVar)DynamicVars["ReleaseText"]).StringValue="移除牌组中所有卡牌的[gold]永恒[/gold]。";((StringVar)DynamicVars["UpgradeText"]).StringValue="";}
 protected override async Task OnPlay(PlayerChoiceContext choiceContext,CardPlay cardPlay){if(IsUpgraded)await CardPileCmd.Draw(choiceContext,1,Owner);}
}

// 原生直接添加的词条不会自动写入卡牌DTO；用既有Ritsu能力保存永恒的最终值。
// 不占用原生附魔槽，也不移除其他附魔，后续新的词条操作仍可正常改变永恒。
public sealed class DongniEternalState : CardCapability {
 bool? eternal;
 bool applying;
 public override bool ShouldReceiveOwnerHooks=>false;
 public void SetEternal(bool value){eternal=value&&!DongniDisgustRules.RejectEternal(Owner);Apply();MarkDirty();}
 public void Record(bool value){if(applying||eternal==value)return;eternal=value;MarkDirty();}
 public override IModelCapability CloneFor(AbstractModel clonedOwner){var clone=(DongniEternalState)base.CloneFor(clonedOwner);clone.eternal=eternal;clone.applying=false;clone.Apply();return clone;}
 protected override JsonNode? SaveAdditionalState()=>eternal.HasValue?JsonValue.Create(eternal.Value):null;
 protected override void LoadAdditionalState(JsonNode? state,int schemaVersion){eternal=state?.GetValue<bool>();Apply();}
 protected override void OnAttach(CardModel owner)=>Apply();
 protected override void OnLoadedFromSave(CardModel owner)=>Apply();
 void Apply(){
  if(Owner==null||!eternal.HasValue)return;applying=true;
  try{if(DongniDisgustRules.RejectEternal(Owner))eternal=false;bool present=Owner.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Eternal);if(eternal.Value&&!present)CardCmd.ApplyKeyword(Owner,CardKeyword.Eternal);else if(!eternal.Value)CardCmd.RemoveKeyword(Owner,CardKeyword.Eternal);}
  finally{applying=false;}
 }
}

public static class DongniDisgustRules {
 sealed class Entry {public bool Joined,Left;}
 sealed record Departure(CardModel Root,Player Owner,bool[] Upgraded);
 sealed class FusionScope(Player owner,FusionScope? parent){public Player Owner=owner;public FusionScope? Parent=parent;public List<Departure> Departures=[];}
 static readonly ConditionalWeakTable<CardModel,Entry> entries=new();
 static readonly AsyncLocal<FusionScope?> fusion=new();
 static Harmony? harmony;static bool fusionInstalled;
 static IEnumerable<CardModel> Parts(CardModel card)=>new[]{card}.Concat(FusionQuestBlessingFix.Entry.Descendants(card));
 static DongniDisgust[] Effects(CardModel card)=>Parts(card).OfType<DongniDisgust>().Distinct().ToArray();
 internal static bool RejectEternal(CardModel? card)=>card!=null&&card is not DongniDisgust&&Effects(card).Length>0;
 public static void Install(Harmony h){
  harmony=h;
  h.Patch(AccessTools.Method(typeof(Hook),nameof(Hook.AfterCardChangedPiles)),postfix:new HarmonyMethod(typeof(DongniDisgustRules),nameof(Changed)));
  h.Patch(AccessTools.Method(typeof(CardPileCmd),nameof(CardPileCmd.RemoveFromDeck),[typeof(IReadOnlyList<CardModel>),typeof(bool)]),prefix:new HarmonyMethod(typeof(DongniDisgustRules),nameof(Removing)){priority=Priority.Last},postfix:new HarmonyMethod(typeof(DongniDisgustRules),nameof(Removed)));
  h.Patch(AccessTools.Method(typeof(CardCmd),nameof(CardCmd.Transform),[typeof(IEnumerable<CardTransformation>),typeof(MegaCrit.Sts2.Core.Random.Rng),typeof(MegaCrit.Sts2.Core.Nodes.CommonUi.CardPreviewStyle)]),prefix:new HarmonyMethod(typeof(DongniDisgustRules),nameof(Transforming)),postfix:new HarmonyMethod(typeof(DongniDisgustRules),nameof(Transformed)));
  h.Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.AddKeyword)),prefix:new HarmonyMethod(typeof(DongniDisgustRules),nameof(AllowKeyword)),postfix:new HarmonyMethod(typeof(DongniDisgustRules),nameof(KeywordAdded)));
  h.Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.RemoveKeyword)),postfix:new HarmonyMethod(typeof(DongniDisgustRules),nameof(KeywordRemoved)));
  h.Patch(AccessTools.PropertyGetter(typeof(CardModel),"BetaPortraitPngPath"),prefix:new HarmonyMethod(typeof(DongniDisgustRules),nameof(BetaPath)));
 }
 public static void AfterMods(){
  if(fusionInstalled)return;var option=FusionQuestBlessingFix.Entry.Find("CardFusion.FuseRestSiteOption");if(option==null)return;
  // 除直接添加外，也拦住素材、附魔和其他模组动态合并出的永恒。
  var keywordMethod=AccessTools.Method(typeof(CardModel),nameof(CardModel.GetKeywordsWithSources));
  harmony!.Patch(keywordMethod,postfix:new HarmonyMethod(typeof(DongniDisgustRules),nameof(FusionKeywords)){priority=Priority.Last,after=Harmony.GetPatchInfo(keywordMethod)?.Owners.ToArray()??[]});
  harmony!.Patch(AccessTools.Method(option,"OnSelect"),prefix:new HarmonyMethod(typeof(DongniDisgustRules),nameof(BeginFusion)),postfix:new HarmonyMethod(typeof(DongniDisgustRules),nameof(EndFusion)),finalizer:new HarmonyMethod(typeof(DongniDisgustRules),nameof(FusionFailed)));
  fusionInstalled=true;
 }
 // 异步局部作用域只覆盖实际火堆融合；预览、其他玩家及普通卡牌获取不受影响。
 static void BeginFusion(object __instance,out FusionScope __state){var owner=(Player)AccessTools.Property(__instance.GetType(),"Owner").GetValue(__instance)!;__state=new(owner,fusion.Value);fusion.Value=__state;}
 static void EndFusion(FusionScope __state,ref Task<bool> __result){__result=FinishFusion(__result,__state);fusion.Value=__state.Parent;}
 static Exception? FusionFailed(Exception? __exception,FusionScope? __state){if(__state!=null&&ReferenceEquals(fusion.Value,__state))fusion.Value=__state.Parent;return __exception;}
 static async Task<bool> FinishFusion(Task<bool> original,FusionScope scope){
  bool success=false;try{success=await original;return success;}
  finally{if(success){foreach(var record in scope.Departures)entries.GetOrCreateValue(record.Root).Left=true;}else await Release(scope.Departures.ToArray(),false);}
 }
 static void ClearEternal(IEnumerable<CardModel> cards){foreach(var part in cards.SelectMany(Parts).Distinct())part.GetOrCreateCapability<DongniEternalState>().SetEternal(false);}
 static bool BetaPath(CardModel __instance,ref string __result){if(__instance is not DongniDisgust)return true;__result=__instance.PortraitPath;return false;}
 static bool AllowKeyword(CardModel __instance,CardKeyword keyword)=>keyword!=CardKeyword.Eternal||!RejectEternal(__instance);
 static void FusionKeywords(CardModel __instance,ref IReadOnlySet<CardKeyword> __result){if(__result.Contains(CardKeyword.Eternal)&&RejectEternal(__instance))__result=__result.Where(k=>k!=CardKeyword.Eternal).ToHashSet();}
 static void KeywordAdded(CardModel __instance,CardKeyword keyword){if(keyword==CardKeyword.Eternal&&__instance.TryGetCapability<DongniEternalState>(out var state))state.Record(!RejectEternal(__instance));}
 static void KeywordRemoved(CardModel __instance,CardKeyword keyword){if(keyword==CardKeyword.Eternal&&__instance.TryGetCapability<DongniEternalState>(out var state))state.Record(false);}
 static void Changed(CardModel card,PileType oldPile,ref Task __result){if(oldPile!=PileType.Deck&&Effects(card).Length>0)__result=JoinedAfter(__result,card);}
 static async Task JoinedAfter(Task original,CardModel card){await original;Join(card);}
 static void Join(CardModel card){
  if(card.Pile?.Type!=PileType.Deck||Effects(card).Length==0)return;
  var state=entries.GetOrCreateValue(card);if(state.Joined&&!state.Left)return;state.Joined=true;state.Left=false;
  if(fusion.Value?.Owner==card.Owner&&card is not DongniDisgust){ClearEternal([card]);return;}
  foreach(var other in card.Owner.Deck.Cards.ToArray())if(!ReferenceEquals(other,card))other.GetOrCreateCapability<DongniEternalState>().SetEternal(true);
 }
 static Departure[] Capture(IEnumerable<CardModel> cards)=>cards.Distinct().Where(c=>c.Pile?.Type==PileType.Deck&&Effects(c).Length>0)
  .Select(c=>new Departure(c,c.Owner,Effects(c).Select(e=>e.IsUpgraded).ToArray())).ToArray();
 static void Removing(IReadOnlyList<CardModel> cards,out Departure[] __state)=>__state=Capture(cards);
 static void Removed(Departure[] __state,ref Task __result){if(__state.Length>0)__result=RemovedAfter(__result,__state);}
 static async Task RemovedAfter(Task original,Departure[] records){try{await original;}finally{await Release(records);}}
 static void Transforming(ref IEnumerable<CardTransformation> transformations,out Departure[] __state){var array=transformations.ToArray();transformations=array;__state=Capture(array.Select(t=>t.Original));}
 static void Transformed(Departure[] __state,ref Task<IEnumerable<CardPileAddResult>> __result)=>__result=TransformedAfter(__result,__state);
 static async Task<IEnumerable<CardPileAddResult>> TransformedAfter(Task<IEnumerable<CardPileAddResult>> original,Departure[] records){
  try{var results=(await original).ToArray();foreach(var result in results)if(result.success)Join(result.cardAdded);return results;}
  finally{await Release(records);}
 }
 static async Task Release(Departure[] records,bool suppressFusion=true){
  foreach(var record in records){
   if(record.Owner.Deck.Cards.Contains(record.Root))continue;
   if(suppressFusion&&fusion.Value is {} scope&&scope.Owner==record.Owner){if(!scope.Departures.Any(d=>ReferenceEquals(d.Root,record.Root)))scope.Departures.Add(record);continue;}
   var state=entries.GetOrCreateValue(record.Root);if(state.Left)continue;state.Left=true;
   foreach(bool upgraded in record.Upgraded){
    // 升级版直接清除整个永久牌组（含融合子牌），不打开选牌界面。
    if(upgraded){ClearEternal(record.Owner.Deck.Cards.ToArray());continue;}
    int available=record.Owner.Deck.Cards.Count(c=>c.Keywords.Contains(CardKeyword.Eternal));int max=Math.Min(5,available);if(max==0)continue;
    var prompt=new LocString("card_selection","AIKUN_DONGNI_DISGUST_FIVE");
    var selected=await CardSelectCmd.FromDeckGeneric(record.Owner,new CardSelectorPrefs(prompt,0,max){Cancelable=true},c=>c.Keywords.Contains(CardKeyword.Eternal));
    foreach(var card in selected.Distinct().Take(max))if(card.Owner==record.Owner&&card.Pile==record.Owner.Deck&&card.Keywords.Contains(CardKeyword.Eternal))
     ClearEternal([card]);
   }
  }
 }
 public static void Localize(){
  if(LocManager.Instance==null)return;string id=ModelDb.GetId<DongniDisgust>().Entry;
  LocManager.Instance.GetTable("cards").MergeWith(new(){[id+".title"]="东尼的厌恶",[id+".description"]="{DrawText}这张牌加入你的牌组时，为牌组中所有其他卡牌添加[gold]永恒[/gold]。\n这张牌从牌组中变化或移除时，{ReleaseText}{UpgradeText}"});
  LocManager.Instance.GetTable("card_selection").MergeWith(new(){["AIKUN_DONGNI_DISGUST_FIVE"]="选择至多5张卡牌，移除它们的永恒。"});
 }
}

public static class DongniDisgustArt {
 static readonly object artLock=new();static string? file;
 public static string PortraitPath {get{lock(artLock){
  if(file==null||!File.Exists(file)){
   using var stream=typeof(DongniDisgust).Assembly.GetManifestResourceStream("cardart.dongni_disgust.png")??throw new FileNotFoundException("东尼的厌恶卡图缺失");
   using var bytes=new MemoryStream();stream.CopyTo(bytes);using var image=new Image();if(image.LoadPngFromBuffer(bytes.ToArray())!=Error.Ok)throw new InvalidDataException("东尼的厌恶卡图PNG无效");
   file=EnchantmentIconTexture.Cache(image,"card-portraits");
  }
  return EnchantmentIconTexture.ResourcePath(file);
 }}}
}
