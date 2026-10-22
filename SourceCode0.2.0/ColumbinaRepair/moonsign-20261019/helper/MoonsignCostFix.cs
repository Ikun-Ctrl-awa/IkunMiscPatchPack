using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace ColumbinaMod.LocalCompatibility;

public static class MoonsignCostFix
{
    public static IReadOnlyList<CardModel> EligibleCards(IEnumerable<CardModel> cards)
    {
        // X 费、不能打出和当前零费的牌不会因普通能量减费而得到可用收益。
        var eligible = new List<CardModel>();
        foreach (var card in cards)
            if (!card.EnergyCost.CostsX && card.EnergyCost.GetWithModifiers(CostModifiers.All) > 0)
                eligible.Add(card);
        return eligible;
    }

    public static void Reduce(CardModel source, PileType pileType)
    {
        var cards = EligibleCards(pileType.GetPile(source.Owner).Cards);
        if (cards.Count == 0) return;
        var selected = source.Owner.RunState.Rng.CombatCardSelection.NextItem(cards)!;
        selected.EnergyCost.AddThisCombat(-source.DynamicVars["CostReduction"].IntValue);
        selected.InvokeEnergyCostChanged();
    }
}
