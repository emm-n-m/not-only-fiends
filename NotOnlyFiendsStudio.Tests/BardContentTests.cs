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

    // --- Unearthed Arcana "simple variant": the druid-like bard --------------------

    private static Character DruidLikeBard(int level)
    {
        var character = Bard(level);
        character.Ticks = character.Ticks
            .Select(_ => new Tick { DriverId = "class:druid_like_bard" })
            .ToList();
        return character;
    }

    /// <summary>
    /// UA gives the variant "animal companion (as druid), nature sense (as druid), resist
    /// nature's lure (as druid), wild empathy (as druid)" and takes away "bardic knowledge,
    /// inspire courage, inspire competence, inspire greatness, inspire heroics". Resist nature's
    /// lure arrives at 4th, the level a druid gains it (PCGen gates it on <c>BardLVL &gt;= 4</c>).
    /// </summary>
    [Fact]
    public void DruidLikeBard_TradesTheInspiringAbilitiesForTheDruidicOnes()
    {
        var engine = new ReplayStudio(TestContentHelper.LoadAllPacks());
        var early = engine.Evaluate(DruidLikeBard(3));
        var later = engine.Evaluate(DruidLikeBard(15));

        foreach (var gained in new[] { "animal_companion", "nature_sense", "wild_empathy" })
            Assert.Contains(early.Abilities, ability => ability.Id == gained);

        Assert.DoesNotContain(engine.Evaluate(DruidLikeBard(3)).Abilities,
            ability => ability.Id == "resist_natures_lure");
        Assert.Contains(engine.Evaluate(DruidLikeBard(4)).Abilities,
            ability => ability.Id == "resist_natures_lure");

        foreach (var lost in new[]
                 {
                     "bardic_knowledge", "inspire_competence", "inspire_greatness", "inspire_heroics",
                 })
            Assert.DoesNotContain(later.Abilities, ability => ability.Id == lost);
        Assert.DoesNotContain(later.Abilities, ability => ability.Id.StartsWith("inspire_courage_"));

        // The abilities the variant says nothing about are untouched.
        foreach (var kept in new[] { "bardic_music", "countersong", "fascinate", "song_of_freedom" })
            Assert.Contains(later.Abilities, ability => ability.Id == kept);
    }

    /// <summary>
    /// The variant "has all the standard bard class features, except as noted below", and the
    /// spellcasting table is not among the exceptions. Comparing the two drivers rather than
    /// spelling the table out again is what keeps the copy honest if the bard's ever changes.
    /// </summary>
    [Fact]
    public void DruidLikeBard_CastsExactlyAsABardDoes()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var bard = (HDDriver)registry.GetDriver("class:bard");
        var variant = (HDDriver)registry.GetDriver("class:druid_like_bard");

        Assert.Equal(bard.HitDie, variant.HitDie);
        Assert.Equal(bard.SkillPointsPerLevel, variant.SkillPointsPerLevel);
        Assert.Equal(bard.BABProgression, variant.BABProgression);
        Assert.Equal(bard.ClassSkills, variant.ClassSkills);
        Assert.Equal(bard.Spellcasting!.CastingType, variant.Spellcasting!.CastingType);
        Assert.Equal(bard.Spellcasting.CastingStat, variant.Spellcasting.CastingStat);
        Assert.Equal(bard.Spellcasting.SpellsPerDay, variant.Spellcasting.SpellsPerDay);
        Assert.Equal(bard.Spellcasting.SpellsKnown, variant.Spellcasting.SpellsKnown);

        // It has no list of its own — it draws on the bard's.
        Assert.Equal(new[] { "class:bard" }, variant.Spellcasting.SpellListSources);
    }

    /// <summary>The companion advances on the variant's own levels, one for one, as a druid's.</summary>
    [Fact]
    public void DruidLikeBard_AdvancesAnAnimalCompanionAtFullLevel()
    {
        var state = new ReplayStudio(TestContentHelper.LoadAllPacks()).Evaluate(DruidLikeBard(7));
        var slot = Assert.Single(state.CompanionSlots, s => s.LinkType == "animal_companion");

        Assert.Equal(7, slot.EffectiveLevelFormula.Evaluate(state));
    }
}
