using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 // 专项直接调用真实附魔、完成流程和原生文件存读，不把接口检查当作交互界面复现。
 static async Task CampfireSlChecks(){
  int first=passes.Count;var evidence=new List<object>();
  var option=Pack.Options.All.Single(o=>o.Key==Pack.EnchantRestSiteSaveFix.Key);
  Check("campfire independent option defaults on",new Pack.PackConfig().Get(option.Key)&&option.Required.SequenceEqual(new[]{"MoreEnchantments"}));
  if(Minimal){Check("campfire optional target absent safely",Pack.Settings.Status(option).Contains("未装载相关模组")&&!Pack.RuntimeLoader.States.ContainsKey(option.Key));return;}
  Check("campfire patch installed",Pack.RuntimeLoader.States.GetValueOrDefault(option.Key)=="已应用。");
  var tableType=Find("MoreEnchantmentsMod.EnchantersTable");var saveType=Find("MoreEnchantmentsMod.MoreEnchantSave");
  var canSave=AccessTools.Method(saveType,"CanSaveRunNow");var queue=AccessTools.Method(saveType,"QueueRunSave");var pending=AccessTools.Method(tableType,"GetOrCreateRestEventState");var complete=AccessTools.Method(tableType,"CompleteRestEvent");
  var completed=AccessTools.Property(tableType,"CompletedRestEventKey");var key=AccessTools.Method(tableType,"GetCurrentRestKey");
  var oldTest=TestMode.IsOn;var oldLocal=MegaCrit.Sts2.Core.Context.LocalContext.NetId;bool enabled=Pack.Settings.Active(option.Key);
  var listeners=AccessTools.Field(typeof(CombatStateTracker),"CombatStateChanged");var frontend=listeners.GetValue(CombatManager.Instance.StateTracker);
  try{
   listeners.SetValue(CombatManager.Instance.StateTracker,null);TestMode.IsOn=true;
   foreach(bool active in new[]{false,true}){
    Pack.Settings.Startup[option.Key]=active;
    var p=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var other=Player.CreateForNewRun<Silent>(UnlockState.all,2);
    var state=RunState.CreateForTest([p,other],seed:"CAMPFIRE-SL-CONTROL");RunManager.Instance.SetUpTest(state,new NetSingleplayerGameService(),shouldSave:true);MegaCrit.Sts2.Core.Context.LocalContext.NetId=p.NetId;
    // 完整 RunState 读档要求各幕已生成房间目录；CreateForTest 默认只创建空目录。
    foreach(var act in state.Acts)act.GenerateRooms(state.Rng.UpFront,state.UnlockState);
    var table=ModelDb.GetById<RelicModel>(ModelDb.GetId(tableType)).ToMutable();p.AddRelicInternal(table,silent:true);p.AddRelicInternal(ModelDb.Relic<MiniatureTent>().ToMutable(),silent:true);
    var card=state.CreateCard<Anger>(p);p.Deck.AddInternal(card,silent:true);AccessTools.Property(typeof(Player),nameof(Player.Gold)).SetValue(p,500);
    Check("campfire fixture tracks unique target card active="+active,p.Deck.Cards.Count(c=>c.Id==card.Id)==1);
    state.PushRoom(new RestSiteRoom());
    Check("campfire own autosave boundary active="+active,(bool)canSave.Invoke(null,[state])! == !active);
    Check("campfire null boundary retains original active="+active,(bool)canSave.Invoke(null,new object?[]{null})!);
    await SaveManager.Instance.SaveRun(null,false);
    var checkpoint=SaveManager.Instance.LoadRunSave();Check("campfire real entry checkpoint readable active="+active,checkpoint.Success&&checkpoint.SaveData!=null);
    var initialJson=JsonSerializer.Serialize(checkpoint.SaveData,JsonSerializationUtility.Options);
    pending.Invoke(table,null);await Wait(.12);if(SaveManager.Instance.CurrentRunSaveTask is {} writing)await writing;
    if(active)Check("campfire entering enchant keeps entry checkpoint",JsonSerializer.Serialize(SaveManager.Instance.LoadRunSave().SaveData,JsonSerializationUtility.Options)==initialJson);
    CardCmd.Upgrade(card);Check("campfire ordinary upgrade runs active="+active,card.CurrentUpgradeLevel==1);
    // 使用目标模组自己的付费与附魔方法，而非只伪造完成标记。
    var enchant=ModelDb.Enchantment<MegaCrit.Sts2.Core.Models.Enchantments.Sharp>();
    bool applied=await (Task<bool>)AccessTools.Method(tableType,"Enchant").Invoke(table,[card,enchant])!;
    Check("campfire native table enchant succeeds active="+active,applied&&card.Enchantment!=null&&p.Gold==401);
    complete.Invoke(table,null);await Wait(.18);if(SaveManager.Instance.CurrentRunSaveTask is {} saving)await saving;
    Check("campfire marker retained active="+active,(string)completed.GetValue(table)! ==(string)key.Invoke(null,[p])!);
    var stored=SaveManager.Instance.LoadRunSave();Check("campfire real checkpoint still readable active="+active,stored.Success);
    var restored=RunState.FromSerializable(stored.SaveData!);var restoredPlayer=restored.Players[0];var restoredCard=restoredPlayer.Deck.Cards.Single(c=>c.Id==card.Id);
    Check("campfire saved gold follows switch active="+active,restoredPlayer.Gold==(active?500:401));
    Check("campfire saved upgrade follows switch active="+active,restoredCard.CurrentUpgradeLevel==(active?0:1));
    Check("campfire saved enchant follows switch active="+active,(restoredCard.Enchantment==null)==active);
    if(active)for(int i=0;i<3;i++){
     queue.Invoke(null,[state]);await Wait(.05);
     var reloaded=RunState.FromSerializable(SaveManager.Instance.LoadRunSave().SaveData!);
     Check("campfire repeated SL restores entry "+i,reloaded.Players[0].Gold==500&&reloaded.Players[0].Deck.Cards.Single(c=>c.Id==card.Id).CurrentUpgradeLevel==0);
    }
    var own=(RestSiteOption)Activator.CreateInstance(Find("MoreEnchantmentsMod.EnchantCardRestSiteOption"),p)!;
    var heal=new HealRestSiteOption(p);var smith=new SmithRestSiteOption(p);var foreign=new CampfireForeignOption(p);
    var fusion=(RestSiteOption)Activator.CreateInstance(Find("CardFusion.FuseRestSiteOption"),p)!;
    var options=new List<RestSiteOption>{heal,own,smith,foreign,fusion};table.TryModifyRestSiteOptions(p,options);
    Check("campfire completed option scope active="+active,active?options.SequenceEqual(new RestSiteOption[]{heal,smith,foreign,fusion}):options.Count==0);
    Check("campfire removed own option active="+active,!options.Contains(own));
    var others=new List<RestSiteOption>{new HealRestSiteOption(other),new SmithRestSiteOption(other)};var copy=others.ToArray();table.TryModifyRestSiteOptions(other,others);
    Check("campfire other player choices unchanged active="+active,others.SequenceEqual(copy));
    // 未完成及新火堆仍正常提供附魔，不能靠清除存档标记恢复界面。
    completed.SetValue(table,"previous-rest-site");var freshOptions=new List<RestSiteOption>{heal,smith};table.TryModifyRestSiteOptions(p,freshOptions);
    Check("campfire new site still offers enchant active="+active,freshOptions.Any(o=>o.GetType()==own.GetType())&&freshOptions.Contains(heal)&&freshOptions.Contains(smith));
    complete.Invoke(table,null);
    // 原生进入下一房间前的 SaveRun 在补丁开启时仍实际落盘。
    await SaveManager.Instance.SaveRun(null,false);var normal=SaveManager.Instance.LoadRunSave();
    var after=RunState.FromSerializable(normal.SaveData!);Check("campfire normal native save persists changes active="+active,after.Players[0].Gold==401&&after.Players[0].Deck.Cards.Single(c=>c.Id==card.Id).CurrentUpgradeLevel==1);
    state.PushRoom(new MapRoom());Check("campfire map autosave remains available active="+active,(bool)canSave.Invoke(null,[state])!);
    AccessTools.Property(typeof(Player),nameof(Player.Gold)).SetValue(p,399);queue.Invoke(null,[state]);await Wait(.18);if(SaveManager.Instance.CurrentRunSaveTask is {} mapSave)await mapSave;
    Check("campfire map autosave still writes active="+active,RunState.FromSerializable(SaveManager.Instance.LoadRunSave().SaveData!).Players[0].Gold==399);
    evidence.Add(new{Active=active,EntryGold=500,CurrentGold=399,SavedAfterEnchant=restoredPlayer.Gold,PreservedOtherOptions=options.Select(o=>o.OptionId).ToArray()});
    RunManager.Instance.CleanUp();
   }
   // 无附魔台时不会接管原生休息/升级选项。
   Pack.Settings.Startup[option.Key]=true;var plain=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);var plainRun=RunState.CreateForTest([plain],seed:"CAMPFIRE-NO-TABLE");RunManager.Instance.SetUpTest(plainRun,new NetSingleplayerGameService());
   Check("campfire without table retains heal and smith",RestSiteOption.Generate(plain).Any(o=>o is HealRestSiteOption)&&RestSiteOption.Generate(plain).Any(o=>o is SmithRestSiteOption));
  }finally{
   Pack.Settings.Startup[option.Key]=enabled;MegaCrit.Sts2.Core.Context.LocalContext.NetId=oldLocal;
   try{if(RunManager.Instance.DebugOnlyGetState()!=null)RunManager.Instance.CleanUp();}finally{TestMode.IsOn=oldTest;listeners.SetValue(CombatManager.Instance.StateTracker,frontend);}
  }
  File.WriteAllText(Output+"/campfire-sl-checks.json",JsonSerializer.Serialize(new{Passed=passes.Count-first,Evidence=evidence},new JsonSerializerOptions{WriteIndented=true}));
 }
 sealed class CampfireForeignOption(Player owner):RestSiteOption(owner){public override string OptionId=>"AIKUN_OTHER_MOD_OPTION";public override Task<bool> OnSelect()=>Task.FromResult(true);}
}
