using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

// 使用当前游戏的真实卡牌/生命实现与 Harmony，构造内存状态；不接触用户存档。
string modPath = Path.GetFullPath(args[0]);
var dirs = new[] { Path.GetDirectoryName(modPath)!, (System.Environment.GetEnvironmentVariable("STS2_GAME_MANAGED_DIR")??throw new InvalidOperationException("请设置 STS2_GAME_MANAGED_DIR")), Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"3812834784"), Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"3737335127/BaseLib") };
AssemblyLoadContext.Default.Resolving += (context, name) => {
    var path = dirs.Select(d => Path.Combine(d, name.Name + ".dll")).FirstOrDefault(File.Exists);
    return path == null ? null : context.LoadFromAssemblyPath(path);
};
var mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(modPath);
Regression.Run(mod);

static class Regression
{
    static int passed, failed;
    static Assembly mod = null!;
    static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static void Check(string name, bool result) { Console.WriteLine($"{(result ? "PASS" : "FAIL")} {name}"); if (result) passed++; else failed++; }
    static object? Call(string type, string name, params object?[] args) => mod.GetType(type)!.GetMethod(name, Flags)!.Invoke(null, args);
    static void Field(object target, string name, object? value) => target.GetType().GetField(name, Flags)!.SetValue(target, value);
    static MegaCrit.Sts2.Core.Models.CardModel Card(Type type)
    {
        var card = (MegaCrit.Sts2.Core.Models.CardModel)RuntimeHelpers.GetUninitializedObject(type);
        typeof(MegaCrit.Sts2.Core.Models.AbstractModel).GetProperty("IsMutable", Flags)!.SetValue(card, true);
        return card;
    }
    static MegaCrit.Sts2.Core.Runs.RunState State(int act, int hp, int ascension = 20)
    {
        var state = (MegaCrit.Sts2.Core.Runs.RunState)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Runs.RunState));
        var player = (MegaCrit.Sts2.Core.Entities.Players.Player)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Entities.Players.Player));
        var creature = new MegaCrit.Sts2.Core.Entities.Creatures.Creature(player, hp, hp);
        Field(player, "<Creature>k__BackingField", creature);
        Field(player, "<Deck>k__BackingField", new MegaCrit.Sts2.Core.Entities.Cards.CardPile(MegaCrit.Sts2.Core.Entities.Cards.PileType.Deck));
        Field(state, "_players", new List<MegaCrit.Sts2.Core.Entities.Players.Player> { player });
        Field(state, "_currentActIndex", act);
        Field(state, "<AscensionLevel>k__BackingField", ascension);
        typeof(MegaCrit.Sts2.Core.Runs.RunManager).GetProperty("State", Flags)!.SetValue(MegaCrit.Sts2.Core.Runs.RunManager.Instance, state);
        return state;
    }
    public static bool ConfigEnabledPrefix(ref bool __result) { __result = true; return false; }
    public static void Run(Assembly assembly)
    {
        mod = assembly;
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = true;
        mod.GetType("BetterAscension.ModLog")!.GetField("_logUnavailable", Flags)!.SetValue(null, true);
        var harmony = new HarmonyLib.Harmony("local.betterascension.regression");
        harmony.Patch(mod.GetType("BetterAscension.AscensionEffectConfig")!.GetMethod("GetLevelEnabled", Flags)!, prefix: new HarmonyLib.HarmonyMethod(typeof(Regression).GetMethod(nameof(ConfigEnabledPrefix), Flags)!));
        foreach (string name in new[] { "AscendersBaneEtherealPatch", "AscendersBaneKeywordsCachePatch" })
        {
            var type = mod.GetType("BetterAscension.Patches." + name)!;
            if (type.GetCustomAttributes<HarmonyLib.HarmonyPatch>().Any()) harmony.CreateClassProcessor(type).Patch();
        }
        var state = State(0, 80);
        var clumsy = Card(typeof(MegaCrit.Sts2.Core.Models.Cards.Clumsy));
        Check("A15 preserves innate Ethereal on Clumsy after repeated reads", Enumerable.Range(0, 4).All(_ => clumsy.Keywords.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Ethereal)));
        var strike = Card(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad));
        strike.AddKeyword(MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Ethereal);
        Check("A15 preserves explicitly added Ethereal on ordinary cards", Enumerable.Range(0, 4).All(_ => strike.Keywords.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Ethereal)));
        var bane = Card(typeof(MegaCrit.Sts2.Core.Models.Cards.AscendersBane));
        var deck = (List<MegaCrit.Sts2.Core.Models.CardModel>)state.Players[0].Deck.Cards;
        deck.Add(bane); deck.Add(clumsy); deck.Add(strike);
        var helper = mod.GetType("BetterAscension.LocalFixes.NewRunKeywordFix");
        helper?.GetMethod("InitializeBane")!.Invoke(null, new object?[] { state, true });
        Check("A15 removes starter AscendersBane Ethereal", !bane.Keywords.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Ethereal));
        Check("A15 startup does not change other cards", clumsy.Keywords.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Ethereal) && strike.Keywords.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Ethereal));
        bane.AddKeyword(MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Ethereal);
        Check("Later explicitly added Ethereal on AscendersBane remains valid", Enumerable.Range(0, 4).All(_ => bane.Keywords.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Ethereal)));
        State(0, 80, 14);
        Check("Lower ascension keeps the original Bane keyword", Card(typeof(MegaCrit.Sts2.Core.Models.Cards.AscendersBane)).Keywords.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Ethereal));
        var attritionType = mod.GetType("BetterAscension.Patches.ActAttritionPatch")!;
        var patch = attritionType.GetCustomAttributes<HarmonyLib.HarmonyPatch>().Single();
        Check("Attrition subscribes to true act transitions", patch.info.methodName == "AfterActEntered");
        state = State(0, 80);
        Call(attritionType.FullName!, "Prefix");
        Check("First act has no attrition", state.Players[0].Creature.MaxHp == 80);
        Field(state, "_currentActIndex", 1);
        Call(attritionType.FullName!, "Prefix");
        Check("Second act loses exactly six maximum HP", state.Players[0].Creature.MaxHp == 74);
        Call(attritionType.FullName!, "Prefix");
        Check("Duplicate notification in the same act is harmless", state.Players[0].Creature.MaxHp == 74);
        for (int reload = 0; reload < 3; reload++)
        {
            state = State(1, 74);
            Call("BetterAscension.AscensionConfigRuntime", "RestoreForLoadedRun", state);
            Call(attritionType.FullName!, "Prefix");
            Check($"Fresh RunState after reload {reload + 1} keeps maximum HP", state.Players[0].Creature.MaxHp == 74);
        }
        Field(state, "_currentActIndex", 2);
        Call(attritionType.FullName!, "Prefix");
        Check("Third act still loses exactly six maximum HP", state.Players[0].Creature.MaxHp == 68);
        state = State(1, 3); Call(attritionType.FullName!, "Prefix");
        Check("Maximum HP floor remains one", state.Players[0].Creature.MaxHp == 1);
        state = State(2, 80, 11); Call(attritionType.FullName!, "Prefix");
        Check("Below A12 does not lose maximum HP", state.Players[0].Creature.MaxHp == 80);
        Console.WriteLine($"REGRESSION_RESULT passed={passed} failed={failed}");
        Environment.ExitCode = failed == 0 ? 0 : 2;
    }
}
