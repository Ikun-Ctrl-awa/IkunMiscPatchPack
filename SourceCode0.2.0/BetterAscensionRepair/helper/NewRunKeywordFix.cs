using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;

namespace BetterAscension.LocalFixes;

public static class NewRunKeywordFix
{
    // 只在新开局配置固化后处理玩家起始牌组；读取词条时不得修改卡牌。
    // 原型保持原版词条，避免跨存档、跨进阶污染缓存；之后添加的虚无仍可正常保留。
    public static void InitializeBane(RunState? state, bool enabled)
    {
        if (state == null || !enabled) return;
        foreach (var player in state.Players)
        foreach (var card in player.Deck.Cards)
            if (card is AscendersBane) card.RemoveKeyword(CardKeyword.Ethereal);
    }
}
