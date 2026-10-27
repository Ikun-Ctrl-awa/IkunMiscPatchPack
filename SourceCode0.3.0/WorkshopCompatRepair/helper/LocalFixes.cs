using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Runs;
using System.Reflection;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using HarmonyLib;

namespace WorkshopLocalFixes;

public static class NativePoolFix
{
    // 原版目录的数量断言只能统计游戏本体，不能把其他模组的无色牌算进去。
    public static CardModel[] NativeCards(IEnumerable<CardModel> cards)
    {
        var result = new List<CardModel>();
        foreach (var card in cards)
            if (card.GetType().Assembly == typeof(CardModel).Assembly) result.Add(card);
        return result.ToArray();
    }
}

public static class TogglePlacementFix
{
    // 多角色分页会让头像底部接近窗口边缘；空间不足时把开关放在头像上方。
    public static void KeepVisible(Control screen, Control portraits, CheckButton toggle)
    {
        if (!GodotObject.IsInstanceValid(screen) || !GodotObject.IsInstanceValid(portraits) || !GodotObject.IsInstanceValid(toggle)) return;
        var inverse = screen.GetGlobalTransform().AffineInverse();
        var top = (inverse * portraits.GetGlobalTransform() * Vector2.Zero).Y;
        var height = Math.Max(48f, toggle.GetCombinedMinimumSize().Y);
        var limit = screen.Size.Y - height - 12f;
        var y = toggle.OffsetTop;
        if (y > limit) y = top - height - 10f;
        toggle.OffsetTop = Math.Clamp(y, 20f, Math.Max(20f, limit));
        toggle.OffsetBottom = toggle.OffsetTop + height;
        toggle.ZIndex = 20;
    }
}

public static class ChestKeyFix
{
    // 仅为安装并启用钥匙机制的 Act4Heart 补接自定义宝箱奖励的跳过事件。
    public static void Grant(Player player)
    {
        var type = Type.GetType("Act4Heart.Keys.SapphireKey, Act4Heart");
        var mod = Type.GetType("Act4Heart.ModMain, Act4Heart");
        if (type == null || mod == null || player == null) return;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var config = mod.GetProperty("current_config", flags)?.GetValue(null);
        if (config?.GetType().GetField("keys_enable", flags)?.GetValue(config) is not true) return;
        foreach (var owned in player.Relics) if (owned.GetType() == type) return;
        foreach (var relic in ModelDb.AllRelics)
            if (relic.GetType() == type)
            {
                TaskHelper.RunSafely(RelicCmd.Obtain(relic.ToMutable(), player));
                Console.WriteLine("[WorkshopLocalFixes] Sapphire key granted for skipped chest reward.");
                return;
            }
    }

    public static void GrantShared(object session)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var draft = session.GetType().GetField("Draft", flags)?.GetValue(session);
        if (draft?.GetType().GetProperty("Skipped", flags)?.GetValue(draft) is not IEnumerable<int> skipped) return;
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null) return;
        foreach (var slot in skipped)
            if (slot >= 0 && slot < state.Players.Count) Grant(state.Players[slot]);
    }
}

public static class FusionRewardGenerationFix
{
    public static IEnumerable<CardCreationResult> TryCreate(Player player, int count, CardCreationOptions options)
    {
        // 其他卡池模组可能在奖励生成前替换卡池。候选消失时保留原奖励，不能中断战后结算。
        if (!options.GetPossibleCards(player).Any()) return Array.Empty<CardCreationResult>();
        return CardFactory.CreateForReward(player, count, options);
    }
}

public static class DisassemblyRemovalFix
{
    public static Task RemoveIfStillInCombat(CardModel card, bool skipVisuals)
    {
        // 能力融合牌在自动打出后已经被游戏移出战斗，不能再次移除并中断后续返还。
        // 仍在弃牌/消耗等战斗牌堆时，继续执行原命令及其动画和钩子。
        return card.Pile?.IsCombatPile == true
            ? CardPileCmd.RemoveFromCombat(card, skipVisuals)
            : Task.CompletedTask;
    }
}

[HarmonyPatch(typeof(RegenPower), nameof(RegenPower.BeforeSideTurnEndEarly))]
public static class PigMinionRegenFix
{
    public static void Prefix(RegenPower __instance, ref IEnumerable<Creature> participants)
    {
        // 原生回合参与者只含玩家，召唤宠物未包含在内。只替小猪的再生补上主人对应的回合。
        var owner = __instance.Owner;
        if (owner?.Monster?.GetType().FullName != "YuWanCard.Monsters.PigMinion" || owner.IsDead) return;
        var playerCreature = owner.PetOwner?.Creature;
        if (playerCreature == null) return;
        var list = participants.ToList();
        if (!list.Contains(owner) && list.Contains(playerCreature))
        {
            list.Add(owner);
            participants = list;
        }
    }
}

public static class HotkeyNameFix
{
    public static string? Normalize(string? text) => text?.Replace("KeypadAdd", "KpAdd", StringComparison.OrdinalIgnoreCase);
}
