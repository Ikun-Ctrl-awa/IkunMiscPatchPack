using System.Reflection;
using MegaCrit.Sts2.Core.Models;
namespace CardFusion.LocalCompatibility;

public static class MoreEnchantFusionBridge
{
    private static Type? Find(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(name, false);
            if (type != null) return type;
        }
        return null;
    }

    public static IReadOnlyList<EnchantmentModel> Read(CardModel card)
    {
        var stack = Find("MoreEnchantmentsMod.MoreEnchantStack");
        var result = new List<EnchantmentModel>();
        if (stack != null)
        {
            var method = stack.GetMethod("AllEnchantments", BindingFlags.Static | BindingFlags.NonPublic)!;
            foreach (var enchantment in (IEnumerable<EnchantmentModel>)method.Invoke(null, new object[] { card })!)
                result.Add(enchantment);
        }
        else if (card.Enchantment != null) result.Add(card.Enchantment);
        return result;
    }

    private static bool IsFused(CardModel card) => card.GetType().FullName == "CardFusion.FusedCard";

    private static IReadOnlyList<CardModel> Children(CardModel card)
        => (IReadOnlyList<CardModel>)card.GetType().GetProperty("AllCards", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(card)!;

    private static IReadOnlyList<EnchantmentModel> Inherited(CardModel card)
    {
        var result = new List<EnchantmentModel>(Read(card));
        if (!IsFused(card)) return result;
        var budget = new Dictionary<Type, int>();
        foreach (var enchantment in result)
        {
            var type = enchantment.GetType();
            budget.TryGetValue(type, out int value);
            budget[type] = value + Math.Max(1, enchantment.Amount);
        }
        // 旧版本融合牌可能只保存了第一项；补齐子卡上仍可恢复的词条。
        foreach (var child in Children(card))
            foreach (var enchantment in Read(child))
            {
                var type = enchantment.GetType();
                budget.TryGetValue(type, out int remaining);
                int weight = Math.Max(1, enchantment.Amount);
                budget[type] = Math.Max(0, remaining - weight);
                if (remaining >= weight) continue;
                if (remaining == 0) result.Add(enchantment);
                else
                {
                    var copy = (EnchantmentModel)enchantment.ClonePreservingMutability();
                    copy.Amount = weight - remaining;
                    result.Add(copy);
                }
            }
        return result;
    }

    public static void Attach(CardModel target, EnchantmentModel source, int amount)
    {
        var copy = (EnchantmentModel)source.ClonePreservingMutability();
        copy.Amount = amount;
        if (target.Enchantment == null)
        {
            target.EnchantInternal(copy, amount);
            copy.ModifyCard();
            target.FinalizeUpgradeInternal();
        }
        else
        {
            var stack = Find("MoreEnchantmentsMod.MoreEnchantStack")!;
            var method = stack.GetMethod("RestoreExtra", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(CardModel), typeof(EnchantmentModel), typeof(bool) }, null)!;
            // 使用提供方的绑定和修改流程；继承已有词条不重新消耗附魔额度。
            method.Invoke(null, new object[] { target, copy, true });
        }
        // 克隆保留持久状态；由提供方更新在新卡上实际产生的费用变化等记录。
        copy.Amount = amount;
    }

    public static bool TryInherit(CardModel target, IReadOnlyList<CardModel> sources, string tag)
    {
        if (Find("MoreEnchantmentsMod.MoreEnchantStack") == null ||
            Find("MultiEnchantmentMod.Api.MultiEnchantmentApi") != null) return false;
        if (target == null || sources == null) return true;
        var originals = new HashSet<CardModel>(ReferenceEqualityComparer.Instance);
        var copied = new HashSet<EnchantmentModel>(ReferenceEqualityComparer.Instance);
        // 调用方同时传入展平的母卡和旧融合牌。优先读取旧融合牌完整词条，避免继承两次。
        foreach (var source in sources)
            if (source != null && IsFused(source))
                foreach (var child in Children(source)) originals.Add(child);
        int count = 0;
        bool temporaryOwner = false;
        if (target.Owner == null)
        {
            foreach (var source in sources)
            {
                if (source == null || source.Owner == null) continue;
                target.Owner = source.Owner;
                temporaryOwner = true;
                break;
            }
        }
        try
        {
            foreach (var source in sources)
            {
                if (source == null || originals.Contains(source)) continue;
                foreach (var enchantment in Inherited(source))
                {
                    if (!copied.Add(enchantment)) continue;
                    Attach(target, enchantment, enchantment.Amount);
                    count++;
                }
            }
        }
        finally
        {
            // Create 返回后才由游戏 AddCard 正式设置 Owner，避免双重绑定。
            if (temporaryOwner) target.Owner = null!;
        }
        Console.WriteLine($"[CardFusion.LocalCompatibility] {tag}: inherited {count} enchantments via MoreEnchantments");
        return true;
    }
}
