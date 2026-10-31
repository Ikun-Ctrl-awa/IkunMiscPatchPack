using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using YuiExtra.Hooks;
namespace YuiExtra.LocalCompatibility;
public static class RelicReplayLifetimeFix
{
    public static bool TryRepeatIfAttached(IRelicTriggerRepeater repeater, RelicModel relic, IReadOnlyList<Creature> targets)
    {
        // 替换/消耗遗物后 Owner 仍保留，必须同时核实当前背包和移除标记。
        var owner = relic.Owner;
        if (owner == null || relic.HasBeenRemovedFromState || !owner.Relics.Contains(relic)) return false;
        // 重放列表是快照，前一个重放也可能移除后面的重放器。
        if (repeater is RelicModel model &&
            (model.Owner != owner || model.HasBeenRemovedFromState || !owner.Relics.Contains(model))) return false;
        return repeater.TryRepeat(relic, targets);
    }
}
