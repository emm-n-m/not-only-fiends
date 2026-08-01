using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

public class BardContentTests
{
    private static Character Bard(int level) => new()
    {
        RaceId = "race:human",
        Alignment = Alignment.CN,
        BaseAbilityScores = new AbilityScoreSet
        {
            STR = 10,
            DEX = 10,
            CON = 10,
            INT = 10,
            WIS = 10,
            CHA = 16,
        },
        Ticks = Enumerable.Range(0, level)
            .Select(_ => new Tick { DriverId = "class:bard" })
            .ToList(),
    };

    [Fact]
    public void BardLevelOne_GrantsTheIndividualStartingMusicAbilities()
    {
        var state = new ReplayStudio(TestContentHelper.LoadAllPacks()).Evaluate(Bard(1));

        Assert.Contains(state.Abilities, ability => ability.Id == "bardic_music");
        Assert.Contains(state.Abilities, ability => ability.Id == "countersong");
        Assert.Contains(state.Abilities, ability => ability.Id == "fascinate");
        Assert.Contains(state.Abilities, ability => ability.Id == "inspire_courage_1");
    }

    [Fact]
    public void BardProgression_GrantsEveryLaterMusicAbilityAtItsSrdLevel()
    {
        var engine = new ReplayStudio(TestContentHelper.LoadAllPacks());
        var expected = new Dictionary<int, string>
        {
            [3] = "inspire_competence",
            [6] = "bard_suggestion",
            [9] = "inspire_greatness",
            [12] = "song_of_freedom",
            [15] = "inspire_heroics",
            [18] = "bard_mass_suggestion",
        };

        foreach (var (level, abilityId) in expected)
        {
            Assert.DoesNotContain(engine.Evaluate(Bard(level - 1)).Abilities, ability => ability.Id == abilityId);
            Assert.Contains(engine.Evaluate(Bard(level)).Abilities, ability => ability.Id == abilityId);
        }
    }

    [Theory]
    [InlineData(1, "inspire_courage_1")]
    [InlineData(8, "inspire_courage_2")]
    [InlineData(14, "inspire_courage_3")]
    [InlineData(20, "inspire_courage_4")]
    public void InspireCourage_ReplacesItsPreviousVersion(int level, string expectedId)
    {
        var state = new ReplayStudio(TestContentHelper.LoadAllPacks()).Evaluate(Bard(level));
        var inspireCourage = state.Abilities.Where(ability => ability.Id.StartsWith("inspire_courage_"));

        Assert.Equal(expectedId, Assert.Single(inspireCourage).Id);
    }
}
