using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static readonly HashSet<CardModel> anvilLifted=new(ReferenceEqualityComparer.Instance);
 static int anvilObservedCap;
 static readonly Dictionary<CardModel,int> anvilRemovalCalls=new(ReferenceEqualityComparer.Instance);
 static void AnvilRemovalCall(IReadOnlyList<CardModel> cards){foreach(var card in cards)anvilRemovalCalls[card]=anvilRemovalCalls.GetValueOrDefault(card)+1;}
 static void AnvilLift(CardModel __instance,ref int __result){if(anvilLifted.Contains(__instance))__result=999;}
 static void AnvilCap(CardModel __instance)=>anvilObservedCap=Math.Max(anvilObservedCap,__instance.MaxUpgradeLevel);
 static async Task GhostlyAnvilChecks(){
  int start=passes.Count;var evidence=new List<object>();var option=Pack.Options.All.Single(o=>o.Key==Pack.GhostlyAnvilFix.Key);
  Check("anvil new independent option defaults on",new Pack.PackConfig().Get(option.Key)&&Pack.Options.All.Length==40&&Pack.Options.All.Count(o=>!o.Misc)==36);
  if(Minimal){Check("anvil missing YUI safely skipped",Pack.Settings.Status(option).Contains("未装载相关模组")&&!Pack.RuntimeLoader.States.ContainsKey(option.Key));return;}
  Check("anvil independent patch installed",Pack.RuntimeLoader.States.GetValueOrDefault(option.Key)=="已应用。");
  var type=Find("YUIExtra.CurseExtra.GhostlyAnvil");var canonical=ModelDb.GetById<CardModel>(ModelDb.GetId(type));
  CardModel Anvil(int n=0)=>Up(canonical.ToMutable(),n);
  CardModel[] Anvils(CardModel root)=>FusionQuestBlessingFix.Entry.Descendants(root).Prepend(root).Where(type.IsInstanceOfType).ToArray();
  async Task Deck(BloodFixture f,CardModel root,bool other=false){if(f.Run.CurrentMapPointHistoryEntry==null)f.Run.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Elite,MegaCrit.Sts2.Core.Rooms.RoomType.Elite,null);var p=other?f.Other:f.P;f.Run.AddCard(root,p);await CardPileCmd.Add(root,PileType.Deck,skipVisuals:true);}
  async Task Upgrade(CardModel root){CardCmd.Upgrade(root,CardPreviewStyle.None);await Pack.GhostlyAnvilFix.Pending(root).WaitAsync(TimeSpan.FromSeconds(8));}
  var h=new Harmony("Aikun020Probe.AnvilFixture");bool old=Pack.Settings.Startup[option.Key],normalizer=Pack.Settings.Startup["fusion_quest"];
  h.Patch(AccessTools.Method(typeof(CardPileCmd),nameof(CardPileCmd.RemoveFromDeck),[typeof(CardModel),typeof(bool)]),prefix:new HarmonyMethod(typeof(Entry),nameof(SwordNoPreview)));
  h.Patch(AccessTools.Method(typeof(CardPileCmd),nameof(CardPileCmd.RemoveFromDeck),[typeof(IReadOnlyList<CardModel>),typeof(bool)]),prefix:new HarmonyMethod(typeof(Entry),nameof(AnvilRemovalCall)));
  h.Patch(AccessTools.DeclaredPropertyGetter(type,nameof(CardModel.MaxUpgradeLevel)),postfix:new HarmonyMethod(typeof(Entry),nameof(AnvilLift)));
  h.Patch(AccessTools.DeclaredMethod(type,"OnUpgrade"),postfix:new HarmonyMethod(typeof(Entry),nameof(AnvilCap)));
  try{
   foreach(bool enabled in new[]{false,true})foreach(bool fused in new[]{false,true}){
    using var f=new BloodFixture();Pack.Settings.Startup[option.Key]=enabled;
    var root=fused?Fuse(Anvil(),ModelDb.Card<StrikeIronclad>().ToMutable()):Anvil();await Deck(f,root);
    // 升级内部接口是原实现漏处理的路径；融合后原移除器也不认识子卡。
    for(int n=0;n<3;n++){root.UpgradeInternal();root.FinalizeUpgradeInternal();await Pack.GhostlyAnvilFix.Pending(root);}
    Check($"anvil reproduces and fixes immediate removal enabled={enabled} fused={fused}",f.P.Deck.Cards.Contains(root)==!enabled);
   }
   Pack.Settings.Startup[option.Key]=true;
   foreach(string shape in new[]{"single","fused","nested","scorching-parent","scorching-child","raised-max"})foreach(bool normalize in new[]{false,true}){
    using var f=new BloodFixture();Pack.Settings.Startup["fusion_quest"]=normalize;
    var root=shape=="single"||shape=="raised-max"?Anvil():shape=="nested"?Fuse(Fuse(Anvil(),Up(ModelDb.Card<Bash>().ToMutable())),Up(ModelDb.Card<DefendIronclad>().ToMutable())):Fuse(Anvil(),Up(ModelDb.Card<Bash>().ToMutable()),Up(ModelDb.Card<DefendIronclad>().ToMutable()));
    var child=Anvils(root).Single();if(shape=="scorching-parent")Scorch(root);if(shape=="scorching-child")Scorch(child);if(shape=="raised-max")anvilLifted.Add(child);
    await Deck(f,root);string tag="anvil "+shape+" normalizer="+normalize;anvilObservedCap=0;
    var unrelated=ModelDb.Card<StrikeIronclad>().ToMutable();await Deck(f,unrelated);
    var expectedRemaining=f.P.Deck.Cards.Where(c=>!ReferenceEquals(c,root)).ToHashSet(ReferenceEqualityComparer.Instance);
    for(int n=1;n<=3;n++){
     await Upgrade(root);
     Check(tag+" own material count after real upgrade "+n,PreviewLevel(child)==n);
     Check(tag+" deck removal at exactly third upgrade "+n,f.P.Deck.Cards.Contains(root)==(n<3));
     Check(tag+" unrelated card stays and canonical unchanged "+n,f.P.Deck.Cards.Contains(unrelated)&&unrelated.CurrentUpgradeLevel==0&&canonical.CurrentUpgradeLevel==0);
     if(n<3)Check(tag+" total fusion level never substitutes material count "+n,Anvils(root).All(c=>PreviewLevel(c)<3)&&f.P.Deck.Cards.Contains(root));
    }
    GD.Print("[Aikun020Probe] anvil removal "+tag+" "+JsonSerializer.Serialize(new{Root=root.Id.ToString(),NativeRemovalCalls=anvilRemovalCalls.GetValueOrDefault(root),History=f.Run.CurrentMapPointHistoryEntry?.GetEntry(f.P.NetId).CardsRemoved.Select(c=>c.Id?.ToString()??"<extension-id>").ToArray()}));
    Check(tag+" root removed exactly once through native batch command",anvilRemovalCalls.GetValueOrDefault(root)==1);
    if(shape is "raised-max" or "scorching-parent")Check(tag+" threshold stays three under actual lifted limit",anvilObservedCap>=999);
    await Pack.GhostlyAnvilFix.Pending(root);Check(tag+" repeated settlement does not touch other cards",expectedRemaining.SetEquals(f.P.Deck.Cards)&&f.P.Deck.Cards.Contains(unrelated));
    evidence.Add(new{Shape=shape,Normalizer=normalize,ChildLevel=PreviewLevel(child),FusionTotal=root.CurrentUpgradeLevel,ObservedMaximum=anvilObservedCap,Removed=!f.P.Deck.Cards.Contains(root)});
   }
   foreach(bool fused in new[]{false,true}){
    using var f=new BloodFixture();var root=fused?Fuse(Anvil(2),ModelDb.Card<Bash>().ToMutable()):Anvil(2);root=Restore(root);await Deck(f,root);
    Check("anvil save restores exact child progress fused="+fused,PreviewLevel(Anvils(root).Single())==2&&f.P.Deck.Cards.Contains(root));
    await Upgrade(root);Check("anvil loaded progress completes immediately fused="+fused,!f.P.Deck.Cards.Contains(root)&&PreviewLevel(Anvils(root).Single())==3);
   }
   using(var f=new BloodFixture()){
    var root=Fuse(Anvil(1),Anvil(1),Up(ModelDb.Card<Bash>().ToMutable()));await Deck(f,root);var children=Anvils(root);
    Check("anvil two material levels are not summed",root.CurrentUpgradeLevel>=3&&children.All(c=>PreviewLevel(c)==1)&&f.P.Deck.Cards.Contains(root));
    children[0].UpgradeInternal();children[0].FinalizeUpgradeInternal();await Pack.GhostlyAnvilFix.Pending(root);Check("anvil direct child upgrade below three preserves parent",f.P.Deck.Cards.Contains(root)&&PreviewLevel(children[0])==2);
    children[0].UpgradeInternal();children[0].FinalizeUpgradeInternal();await Pack.GhostlyAnvilFix.Pending(root);Check("anvil direct child upgrade removes permanent root without child owner",!f.P.Deck.Cards.Contains(root)&&PreviewLevel(children[0])==3&&PreviewLevel(children[1])==1);
   }
   using(var f=new BloodFixture()){
    var root=Anvil(2);await Deck(f,root);var preview=(CardModel)root.ClonePreservingMutability();preview.DeckVersion=root;Up(preview);
    Check("anvil upgrading preview never deletes source deck card",preview.CurrentUpgradeLevel==3&&root.CurrentUpgradeLevel==2&&f.P.Deck.Cards.Contains(root));
    var temporary=f.Combat.CreateCard(canonical,f.P);temporary.DeckVersion=root;Up(temporary,3);
    Check("anvil temporary combat upgrade never deletes source deck card",temporary.CurrentUpgradeLevel==3&&root.CurrentUpgradeLevel==2&&f.P.Deck.Cards.Contains(root));
    await Upgrade(root);Check("anvil actual source still removes on real third upgrade",!f.P.Deck.Cards.Contains(root));
   }
   using(var f=new BloodFixture(multiplayer:true)){
    var first=Anvil(2);var second=Fuse(Anvil(2),ModelDb.Card<Bash>().ToMutable());var other=Anvil(1);await Deck(f,first);await Deck(f,second);await Deck(f,other,true);
    // 惰性枚举直接来自永久牌组，结算必须等批量升级循环完成，避免集合变动异常。
    CardCmd.Upgrade(f.P.Deck.Cards.Where(c=>c.IsUpgradable),CardPreviewStyle.None);await Task.WhenAll(Pack.GhostlyAnvilFix.Pending(first),Pack.GhostlyAnvilFix.Pending(second));
    Check("anvil lazy batch removes both completed roots safely",!f.P.Deck.Cards.Contains(first)&&!f.P.Deck.Cards.Contains(second));
    Check("anvil batch never touches other player's progress",f.Other.Deck.Cards.Contains(other)&&PreviewLevel(other)==1);
    await Upgrade(other);Check("anvil other player second upgrade stays",f.Other.Deck.Cards.Contains(other)&&PreviewLevel(other)==2);await Upgrade(other);Check("anvil other player third upgrade removes own card",!f.Other.Deck.Cards.Contains(other));
   }
   using(var f=new BloodFixture()){
    var hauntedType=Find("YUIExtra.CurseExtra.HauntedAnvil");var otherAnvil=Up(ModelDb.GetById<CardModel>(ModelDb.GetId(hauntedType)).ToMutable(),2);await Deck(f,otherAnvil);
    var ordinary=ModelDb.Card<Bash>().ToMutable();await Deck(f,ordinary);await Upgrade(ordinary);
    Check("anvil distinct HauntedAnvil and ordinary cards unaffected",f.P.Deck.Cards.Contains(otherAnvil)&&f.P.Deck.Cards.Contains(ordinary)&&ordinary.CurrentUpgradeLevel==1&&otherAnvil.CurrentUpgradeLevel==2);
   }
  }finally{anvilLifted.Clear();anvilRemovalCalls.Clear();Pack.Settings.Startup[option.Key]=old;Pack.Settings.Startup["fusion_quest"]=normalizer;h.UnpatchAll(h.Id);}
  File.WriteAllText(Output+"/ghostly-anvil-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-start,Failed=0,Checks=passes.Skip(start),Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
 }
}
