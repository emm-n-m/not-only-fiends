using System.Text.Json;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// Ability score generation is not in the SRD; the expected values here are DMG Table 6–1
/// (point costs), the PHB's 4d6-drop-lowest and reroll rules, and the dice arithmetic of the
/// alternative roll systems.
/// </summary>
public class AbilityScoreGenerationTests
{
    private static AbilityScoreSet Scores(int str, int dex, int con, int @int, int wis, int cha) =>
        new() { STR = str, DEX = dex, CON = con, INT = @int, WIS = wis, CHA = cha };

    [Theory]
    // DMG Table 6–1: Ability Score Point Costs.
    [InlineData(8, 0)]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    [InlineData(11, 3)]
    [InlineData(12, 4)]
    [InlineData(13, 5)]
    [InlineData(14, 6)]
    [InlineData(15, 8)]
    [InlineData(16, 10)]
    [InlineData(17, 13)]
    [InlineData(18, 16)]
    public void PointBuyCost_MatchesDmgTable(int score, int cost) =>
        Assert.Equal(cost, AbilityScoreGenerationRules.PointBuyCost(score));

    [Theory]
    [InlineData(7)]
    [InlineData(19)]
    public void PointBuyCost_OutsideTableIsUnpriceable(int score) =>
        Assert.Null(AbilityScoreGenerationRules.PointBuyCost(score));

    [Fact]
    public void PointBuyTotal_EliteArrayCostsTheStandardBudget()
    {
        // The PHB elite array (15, 14, 13, 12, 10, 8) is the DMG's stated equivalent of a
        // standard 25-point buy.
        var elite = Scores(15, 14, 13, 12, 10, 8);

        Assert.Equal(AbilityScoreGenerationRules.StandardPointBuyBudget,
            AbilityScoreGenerationRules.PointBuyTotal(elite));
    }

    [Fact]
    public void PointBuyTotal_IsNullWhenAnyScoreIsUnpriceable() =>
        Assert.Null(AbilityScoreGenerationRules.PointBuyTotal(Scores(19, 8, 8, 8, 8, 8)));

    [Theory]
    [InlineData(new[] { 1, 6, 6, 6 }, 3, 18)]
    [InlineData(new[] { 1, 1, 1, 1 }, 3, 3)]
    [InlineData(new[] { 4, 2, 5, 2 }, 3, 11)]
    [InlineData(new[] { 2, 6, 1, 5, 3 }, 3, 14)] // 5d6 keep three
    [InlineData(new[] { 4, 2, 5 }, 3, 11)]       // 3d6 keeps everything
    public void KeepHighest_DropsTheLowestDice(int[] dice, int keep, int expected) =>
        Assert.Equal(expected, AbilityScoreGenerationRules.KeepHighest(dice, keep));

    [Theory]
    [InlineData(AbilityRollSystem.FourD6DropLowest, 3, 18)]
    [InlineData(AbilityRollSystem.ThreeD6, 3, 18)]
    [InlineData(AbilityRollSystem.ThreeD6InOrder, 3, 18)]
    [InlineData(AbilityRollSystem.TwoD6Plus6, 8, 18)]
    [InlineData(AbilityRollSystem.FiveD6KeepThree, 3, 18)]
    public void RollScores_StayInEachSystemsRange(AbilityRollSystem system, int min, int max)
    {
        var random = new Random(4321);
        var seen = new List<int>();
        for (var set = 0; set < 500; set++)
        {
            var scores = AbilityScoreGenerationRules.RollScores(random, system);
            Assert.Equal(Enum.GetValues<Ability>().Length, scores.Count);
            seen.AddRange(scores);
        }
        Assert.All(seen, s => Assert.InRange(s, min, max));
        // 3000 rolls reach both ends of 2d6+6 (1 in 36) and get near them for the rest.
        if (system == AbilityRollSystem.TwoD6Plus6)
        {
            Assert.Contains(8, seen);
            Assert.Contains(18, seen);
        }
    }

    [Fact]
    public void RollSystems_OnlyThreeD6InOrderIsInOrder() =>
        Assert.Equal(
            new[] { AbilityRollSystem.ThreeD6InOrder },
            AbilityScoreGenerationRules.RollSystems.Where(kv => kv.Value.InOrder).Select(kv => kv.Key));

    [Fact]
    public void RollSystems_CoverEveryEnumValue() =>
        Assert.Equal(Enum.GetValues<AbilityRollSystem>().Order(), AbilityScoreGenerationRules.RollSystems.Keys.Order());

    [Fact]
    public void RollScores_ProducesSixScoresInThreeToEighteen()
    {
        var random = new Random(1234);
        for (var set = 0; set < 200; set++)
        {
            var scores = AbilityScoreGenerationRules.RollScores(random);
            Assert.Equal(Enum.GetValues<Ability>().Length, scores.Count);
            Assert.All(scores, s => Assert.InRange(s, 3, 18));
        }
    }

    [Theory]
    // Highest score 13 or lower.
    [InlineData(new[] { 13, 13, 13, 13, 13, 13 }, true)]
    // Modifiers total 0 or less (+4 −4 −1 +0 +0 +0 = −1) despite an 18.
    [InlineData(new[] { 18, 3, 9, 10, 10, 10 }, true)]
    // Modifiers total exactly 0 (+2 −1 −1 +0 +0 +0).
    [InlineData(new[] { 14, 9, 8, 10, 10, 11 }, true)]
    // Positive total and a score above 13.
    [InlineData(new[] { 14, 10, 10, 10, 10, 10 }, false)]
    public void QualifiesForReroll_FollowsPhbRule(int[] scores, bool expected) =>
        Assert.Equal(expected, AbilityScoreGenerationRules.QualifiesForReroll(scores));

    [Fact]
    public void Clone_DeepCopiesAbilityGeneration()
    {
        var character = new Character
        {
            AbilityGeneration = new AbilityScoreGeneration
            {
                Method = AbilityScoreMethod.Rolled,
                Rolls = new List<int> { 15, 12, 11, 10, 9, 8 },
            }
        };

        var clone = character.Clone();
        clone.AbilityGeneration!.Rolls[0] = 3;

        Assert.Equal(AbilityScoreMethod.Rolled, clone.AbilityGeneration.Method);
        Assert.Equal(15, character.AbilityGeneration.Rolls[0]);
    }

    [Fact]
    public void AbilityGeneration_RoundTripsThroughJson()
    {
        var character = new Character
        {
            AbilityGeneration = new AbilityScoreGeneration { Method = AbilityScoreMethod.PointBuy, PointBuyBudget = 32 }
        };

        var json = JsonSerializer.Serialize(character, JsonOptions.Default);
        var loaded = JsonSerializer.Deserialize<Character>(json, JsonOptions.Default)!;

        Assert.Contains("\"pointBuy\"", json);
        Assert.Equal(AbilityScoreMethod.PointBuy, loaded.AbilityGeneration!.Method);
        Assert.Equal(32, loaded.AbilityGeneration.PointBuyBudget);
    }

    [Fact]
    public void RollSystem_RoundTripsThroughJson_AndDefaultsTo4d6ForOlderSaves()
    {
        var character = new Character
        {
            AbilityGeneration = new AbilityScoreGeneration
            {
                Method = AbilityScoreMethod.Rolled, RollSystem = AbilityRollSystem.TwoD6Plus6,
                Rolls = new() { 18, 15, 14, 12, 10, 9 }
            }
        };
        var json = JsonSerializer.Serialize(character, JsonOptions.Default);
        Assert.Contains("\"twoD6Plus6\"", json);
        Assert.Equal(AbilityRollSystem.TwoD6Plus6,
            JsonSerializer.Deserialize<Character>(json, JsonOptions.Default)!.AbilityGeneration!.RollSystem);

        var older = JsonSerializer.Deserialize<Character>(
            """{"abilityGeneration":{"method":"rolled","rolls":[15,14,13,12,10,8]}}""", JsonOptions.Default)!;
        Assert.Equal(AbilityRollSystem.FourD6DropLowest, older.AbilityGeneration!.RollSystem);
    }

    [Fact]
    public void AbilityGeneration_OmittedForManualCharacters()
    {
        var json = JsonSerializer.Serialize(new Character(), JsonOptions.Default);

        Assert.DoesNotContain("abilityGeneration", json);
    }
}
