using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using STS2RitsuLib.Settings;
using Pack=IkunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static bool nativeAnimationWaitFixture,waitFixtureInstalled;
 static void KeepNativeWaits(ref bool __result){if(nativeAnimationWaitFixture)__result=false;}
 static void InstallNativeWaitFixture(){
  // 必须先于其他战斗夹具安装，避免等待命令提前内联未修补的非交互模式判断。
  if(!waitFixtureInstalled){new Harmony("Aikun020Probe.NativeWaitFixture").Patch(AccessTools.PropertyGetter(typeof(NonInteractiveMode),nameof(NonInteractiveMode.IsActive)),postfix:new HarmonyMethod(typeof(Entry),nameof(KeepNativeWaits)));waitFixtureInstalled=true;}
 }
 sealed class ResolutionAction(ulong owner,Func<Task> body):GameAction {
  public override ulong OwnerId=>owner;
  public override GameActionType ActionType=>GameActionType.Combat;
  protected override Task ExecuteAction()=>body();
  public override INetAction ToNetAction()=>throw new NotSupportedException("仅测试，不发送网络消息");
 }
}
