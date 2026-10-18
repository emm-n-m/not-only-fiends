using System.Text.Json;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// Ability score generation is not in the SRD; the expected values here are DMG Table 6–1
/// (point costs) and the PHB's 4d6-drop-lowest and reroll rules.
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
    [InlineData(new[] { 1, 6, 6, 6 }, 18)]
    [InlineData(new[] { 1, 1, 1, 1 }, 3)]
    [InlineData(new[] { 4, 2, 5, 2 }, 11)]
    public void KeepHighestThree_DropsOneLowestDie(int[] dice, int expected) =>
        Assert.Equal(expected, AbilityScoreGenerationRules.KeepHighestThree(dice));

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

    [Fact]
    public void RollFourDropLowest_RollsFourSixSidedDice()
    {
        var random = new Random(99);
        for (var roll = 0; roll < 200; roll++)
        {
            var dice = AbilityScoreGenerationRules.RollFourDropLowest(random);
            Assert.Equal(4, dice.Length);
            Assert.All(dice, d => Assert.InRange(d, 1, 6));
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
    public void AbilityGeneration_OmittedForManualCharacters()
    {
        var json = JsonSerializer.Serialize(new Character(), JsonOptions.Default);

        Assert.DoesNotContain("abilityGeneration", json);
    }
}
