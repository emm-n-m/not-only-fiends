using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Studio;

/// <summary>
/// The two standard ways of generating base ability scores: the DMG point buy and the PHB's
/// 4d6-drop-lowest roll. Neither is in the SRD, which leaves ability generation to the DM, so the
/// authority for every number here is the core rulebooks (DMG p. 169, PHB p. 7–8).
///
/// These only produce <see cref="Character.BaseAbilityScores"/>; the engine never consults them
/// during replay. They live here rather than in the Blazor layer so the builder and any future API
/// caller cost a point buy the same way.
/// </summary>
public static class AbilityScoreGenerationRules
{
    /// <summary>The DMG's standard point-buy budget ("standard" campaign).</summary>
    public const int StandardPointBuyBudget = 25;

    public const int PointBuyMinimum = 8;
    public const int PointBuyMaximum = 18;

    /// <summary>The DMG's campaign power levels, cheapest first.</summary>
    public static IReadOnlyList<(string Name, int Budget)> PointBuyBudgets { get; } = new[]
    {
        ("Low-powered", 15),
        ("Challenging", 22),
        ("Standard", StandardPointBuyBudget),
        ("Tough", 28),
        ("Heroic", 32),
    };

    // DMG Table 6–1: Ability Score Point Costs, indexed from a score of 8.
    private static readonly int[] PointCosts = { 0, 1, 2, 3, 4, 5, 6, 8, 10, 13, 16 };

    /// <summary>The cost of one score, or null when it is outside the 8–18 the table prices.</summary>
    public static int? PointBuyCost(int score) =>
        score is < PointBuyMinimum or > PointBuyMaximum ? null : PointCosts[score - PointBuyMinimum];

    /// <summary>
    /// The points a full set costs, or null if any score can't be bought at all — an unpriceable
    /// score is a different failure from being over budget, and the builder reports them apart.
    /// </summary>
    public static int? PointBuyTotal(AbilityScoreSet scores)
    {
        var total = 0;
        foreach (var ability in Enum.GetValues<Ability>())
        {
            if (PointBuyCost(scores.GetScore(ability)) is not { } cost) return null;
            total += cost;
        }
        return total;
    }

    /// <summary>
    /// Why a set can't be bought on <paramref name="budget"/>, or null when it can. Names the
    /// first offending score so a caller can fix it without re-deriving the table.
    /// </summary>
    public static string? PointBuyProblem(AbilityScoreSet scores, int budget)
    {
        foreach (var ability in Enum.GetValues<Ability>())
        {
            var score = scores.GetScore(ability);
            if (PointBuyCost(score) is null)
                return $"{ability} {score} is outside the point-buy range {PointBuyMinimum}–{PointBuyMaximum}";
        }
        var total = PointBuyTotal(scores)!.Value;
        return total > budget ? $"scores cost {total} points, over the budget of {budget}" : null;
    }

    public static bool IsStandardBudget(int budget) => PointBuyBudgets.Any(b => b.Budget == budget);

    /// <summary>Whether the six scores use exactly the six rolls, each once, in any order.</summary>
    public static bool IsArrangementOf(AbilityScoreSet scores, IReadOnlyCollection<int> rolls) =>
        rolls.Count == Enum.GetValues<Ability>().Length
        && Enum.GetValues<Ability>().Select(scores.GetScore).Order().SequenceEqual(rolls.Order());

    public static int KeepHighest(IEnumerable<int> dice, int keep) =>
        dice.OrderDescending().Take(keep).Sum();

    /// <summary>
    /// The roll systems on offer. 4d6 drop lowest is the PHB's; the others are the long-standing
    /// table alternatives — weaker (3d6), stricter (3d6 in order) or stronger (2d6+6, 5d6 keep
    /// three) — which every edition leaves to the DM.
    /// </summary>
    public static IReadOnlyDictionary<AbilityRollSystem, AbilityRollSpec> RollSystems { get; } =
        new Dictionary<AbilityRollSystem, AbilityRollSpec>
        {
            [AbilityRollSystem.FourD6DropLowest] = new("4d6 drop lowest", Dice: 4, Keep: 3, Bonus: 0, InOrder: false),
            [AbilityRollSystem.ThreeD6] = new("3d6", Dice: 3, Keep: 3, Bonus: 0, InOrder: false),
            [AbilityRollSystem.ThreeD6InOrder] = new("3d6 in order", Dice: 3, Keep: 3, Bonus: 0, InOrder: true),
            [AbilityRollSystem.TwoD6Plus6] = new("2d6+6", Dice: 2, Keep: 2, Bonus: 6, InOrder: false),
            [AbilityRollSystem.FiveD6KeepThree] = new("5d6 keep highest 3", Dice: 5, Keep: 3, Bonus: 0, InOrder: false),
        };

    /// <summary>One score: roll the system's dice, keep the highest, add its bonus.</summary>
    public static int RollScore(Random random, AbilityRollSpec spec) =>
        KeepHighest(Enumerable.Range(0, spec.Dice).Select(_ => random.Next(1, 7)).ToList(), spec.Keep) + spec.Bonus;

    /// <summary>Six scores in the order rolled — which, for an in-order system, is STR→CHA for good.</summary>
    public static List<int> RollScores(Random random, AbilityRollSystem system = AbilityRollSystem.FourD6DropLowest)
    {
        var spec = RollSystems[system];
        return Enumerable.Range(0, 6).Select(_ => RollScore(random, spec)).ToList();
    }

    /// <summary>
    /// The PHB lets a player scrap a set whose modifiers total 0 or less, or whose best score is
    /// 13 or lower. That rule is written for its own 4d6 method; the builder offers it for no other.
    /// </summary>
    public static bool QualifiesForReroll(IReadOnlyCollection<int> scores) =>
        scores.Sum(AbilityScoreSet.Modifier) <= 0 || scores.Max() <= 13;
}

/// <summary>
/// How a rolled set is generated. <see cref="InOrder"/> systems assign the six rolls STR→CHA and
/// forbid rearranging them; the rest let the player place each roll where they like.
/// </summary>
public sealed record AbilityRollSpec(string Name, int Dice, int Keep, int Bonus, bool InOrder);
