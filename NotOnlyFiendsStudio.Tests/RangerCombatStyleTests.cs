using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

public class RangerCombatStyleTests
{
    private static readonly Lazy<ContentRegistry> Content =
        new(TestContentHelper.LoadBundledPacks);

    [Theory]
    [InlineData("combat_style:archery", "feat:rapid_shot", "feat:manyshot", "feat:improved_precise_shot")]
    [InlineData("combat_style:two_weapon_combat", "feat:two_weapon_fighting", "feat:improved_two_weapon_fighting", "feat:greater_two_weapon_fighting")]
    public void CombatStyle_SelectionGrantsAndAdvancesTheCorrectBonusFeats(
        string style, string initialFeat, string improvedFeat, string masteryFeat)
    {
        var character = new Character
        {
            RaceId = "race:human",
            Alignment = Alignment.NG,
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10
            },
            Ticks = Enumerable.Range(0, 11)
                .Select(_ => new Tick { DriverId = "class:ranger" })
                .ToList()
        };
        character.Ticks[1].Choices.ClassFeatureChoices = new Dictionary<string, List<string>>
        {
            ["class_feature:ranger_combat_style"] = new() { style }
        };

        var studio = new ReplayStudio(Content.Value);

        var level2 = studio.Evaluate(character, upToHD: 2);
        Assert.Contains(initialFeat, level2.Feats);
        Assert.DoesNotContain(improvedFeat, level2.Feats);

        var level6 = studio.Evaluate(character, upToHD: 6);
        Assert.Contains(improvedFeat, level6.Feats);
        Assert.DoesNotContain(masteryFeat, level6.Feats);

        var level11 = studio.Evaluate(character);
        Assert.Contains(masteryFeat, level11.Feats);
    }

    [Fact]
    public void CombatStyle_IsPendingUntilTheRangerMakesTheLevelTwoChoice()
    {
        var character = new Character
        {
            RaceId = "race:human",
            Alignment = Alignment.NG,
            Ticks = Enumerable.Range(0, 2).Select(_ => new Tick { DriverId = "class:ranger" }).ToList()
        };

        var state = new ReplayStudio(Content.Value).Evaluate(character);

        Assert.Equal(1, state.PendingClassFeatureSelections["class_feature:ranger_combat_style"]);
        Assert.DoesNotContain("feat:rapid_shot", state.Feats);
        Assert.DoesNotContain("feat:two_weapon_fighting", state.Feats);
    }
}
