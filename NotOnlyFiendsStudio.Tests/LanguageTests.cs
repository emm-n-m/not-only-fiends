using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

public class LanguageTests
{
    private static ContentRegistry Content() => TestContentHelper.LoadBundledPacks();

    private static Character HumanWith(int intelligence, params string[] bonusLanguages) => new()
    {
        Name = "Test",
        RaceId = "race:human",
        BaseAbilityScores = new AbilityScoreSet
        {
            STR = 10, DEX = 10, CON = 10, INT = intelligence, WIS = 10, CHA = 10
        },
        BonusLanguageIds = bonusLanguages.ToList(),
        Ticks = new List<Tick> { new() { DriverId = "class:fighter" } }
    };

    [Fact]
    public void SrdLanguagesLoadAsContent()
    {
        var languages = Content().GetAllLanguages().ToList();

        Assert.Contains(languages, l => l.Id == "common");
        Assert.Contains(languages, l => l.Id == "draconic");
        // Druidic is the SRD's one secret language; "any bonus language" must never include it.
        Assert.True(languages.Single(l => l.Id == "druidic").IsSecret);
    }

    [Fact]
    public void RaceGrantsItsAutomaticLanguages()
    {
        var state = new ReplayStudio(Content()).Evaluate(HumanWith(10));

        Assert.Contains("common", state.Languages);
    }

    [Fact]
    public void DwarfGrantsBothAutomaticLanguages()
    {
        var content = Content();
        var character = HumanWith(10);
        character.RaceId = "race:dwarf";

        var state = new ReplayStudio(content).Evaluate(character);

        Assert.Contains("common", state.Languages);
        Assert.Contains("dwarven", state.Languages);
    }

    [Fact]
    public void NymphGrantsItsAutomaticLanguages()
    {
        var content = Content();
        var character = HumanWith(10);
        character.RaceId = "race:nymph";

        var state = new ReplayStudio(content).Evaluate(character);

        Assert.Contains("common", state.Languages);
        Assert.Contains("sylvan", state.Languages);
    }

    [Theory]
    [InlineData(10, 0)]   // no modifier, no picks
    [InlineData(12, 1)]
    [InlineData(18, 4)]
    [InlineData(6, 0)]    // negative modifier never takes a language away
    public void AllowanceIsOnePerPointOfStartingIntModifier(int intelligence, int expected)
    {
        Assert.Equal(expected, LanguageCatalog.Allowance(intelligence));
    }

    [Fact]
    public void BonusLanguagesAreGrantedUpToTheAllowance()
    {
        // Int 14 → +2 → both picks land.
        var state = new ReplayStudio(Content()).Evaluate(HumanWith(14, "draconic", "elven"));

        Assert.Contains("draconic", state.Languages);
        Assert.Contains("elven", state.Languages);
    }

    [Fact]
    public void PicksBeyondTheAllowanceAreRefusedWithAWarning()
    {
        // Int 12 → +1, but two languages asked for.
        var state = new ReplayStudio(Content()).Evaluate(HumanWith(12, "draconic", "elven"));

        Assert.Contains("draconic", state.Languages);
        Assert.DoesNotContain("elven", state.Languages);
        Assert.Contains(state.Warnings, w => w.Message.Contains("exceeds the 1 allowed"));
    }

    [Fact]
    public void SecretLanguagesAreNeverOfferedToAnAnyRace()
    {
        var content = Content();
        var human = content.GetRace("race:human");

        var offered = LanguageCatalog.OfferedBonusLanguages(human, content.GetAllLanguages()).ToList();

        Assert.True(human.BonusLanguagesAny);
        Assert.Contains(offered, l => l.Id == "draconic");
        Assert.DoesNotContain(offered, l => l.Id == "druidic");
        // Already spoken automatically — paying a pick for it would be a trap.
        Assert.DoesNotContain(offered, l => l.Id == "common");
    }

    [Fact]
    public void RaceWithAFixedListOffersOnlyThatList()
    {
        var content = Content();
        var halfling = content.GetRace("race:halfling");

        var offered = LanguageCatalog.OfferedBonusLanguages(halfling, content.GetAllLanguages())
            .Select(l => l.Id).ToList();

        Assert.Equal(new[] { "dwarven", "elven", "gnome", "goblin", "orc" }, offered.OrderBy(x => x));
        Assert.DoesNotContain("draconic", offered);
    }

    [Fact]
    public void ALanguageOutsideTheRaceListIsRefused()
    {
        var content = Content();
        var character = HumanWith(14, "draconic");
        character.RaceId = "race:halfling";   // halflings cannot take Draconic

        var state = new ReplayStudio(content).Evaluate(character);

        Assert.DoesNotContain("draconic", state.Languages);
        Assert.Contains(state.Warnings, w => w.Message.Contains("not offered by"));
    }

    [Fact]
    public void DragonDiscipleIsEnterableByABuiltCharacter()
    {
        // The point of the whole feature: class:dragon_disciple gates on HasLanguage{draconic},
        // which until now only an imported character could ever satisfy — there was no way to
        // acquire a language in the builder, so a core SRD class was unenterable.
        var content = Content();
        var character = HumanWith(12, "draconic");
        character.Ticks = new List<Tick>
        {
            new() { DriverId = "class:sorcerer" },
            new() { DriverId = "class:sorcerer" },
            new() { DriverId = "class:sorcerer" },
            new() { DriverId = "class:dragon_disciple" }
        };

        var state = new ReplayStudio(content).Evaluate(character);

        Assert.Contains("draconic", state.Languages);
        Assert.DoesNotContain(state.Warnings,
            w => w.Message.Contains("prerequisite not met") && w.Message.Contains("Draconic"));
    }

    [Fact]
    public void BonusLanguagesSurviveACloneRoundTrip()
    {
        var clone = HumanWith(14, "draconic", "elven").Clone();

        Assert.Equal(new[] { "draconic", "elven" }, clone.BonusLanguageIds);
    }

    /// <summary>
    /// SRD wizard.html: "A raven familiar can speak one language of its master's choice as a
    /// supernatural ability." A raven's Intelligence of 2 buys no bonus languages at all, so this
    /// cannot come out of the starting-Intelligence budget — it is a granted slot.
    /// </summary>
    [Fact]
    public void RavenFamiliar_MaySpeakOneLanguageDespiteBuyingNoneWithIntelligence()
    {
        var registry = TestContentHelper.LoadBundledPacks();
        var raven = new Character
        {
            Name = "Raven",
            RaceId = "race:companion_raven",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick> { new() { DriverId = "racial_hd:animal" } },
        };

        var baseline = new ReplayStudio(registry).Evaluate(raven);
        Assert.Equal(1, baseline.GrantedLanguageSlots);
        // Int 10 - 8 racial = 2, whose modifier buys nothing.
        Assert.Equal(0, LanguageCatalog.Allowance(baseline.AbilityScores.INT));

        raven.GrantedLanguageIds.Add("common");
        var spoken = new ReplayStudio(registry).Evaluate(raven);

        Assert.Contains("common", spoken.Languages);
        Assert.DoesNotContain(spoken.Warnings, w => w.Message.Contains("Granted language"));
    }

    [Fact]
    public void GrantedLanguagesBeyondTheSlotCountAreRefused()
    {
        var registry = TestContentHelper.LoadBundledPacks();
        var raven = new Character
        {
            Name = "Raven",
            RaceId = "race:companion_raven",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick> { new() { DriverId = "racial_hd:animal" } },
            GrantedLanguageIds = new List<string> { "common", "elven" },
        };

        var state = new ReplayStudio(registry).Evaluate(raven);

        Assert.Contains("common", state.Languages);
        Assert.DoesNotContain("elven", state.Languages);
        Assert.Contains(state.Warnings, w => w.Message.Contains("exceeds the 1 language slot"));
    }

    /// <summary>A creature with no granted slot cannot take one by writing the list directly.</summary>
    [Fact]
    public void GrantedLanguagesWithoutASlotAreRefused()
    {
        var registry = TestContentHelper.LoadBundledPacks();
        var state = new ReplayStudio(registry).Evaluate(new Character
        {
            Name = "Toad",
            RaceId = "race:familiar_toad",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick> { new() { DriverId = "racial_hd:animal" } },
            GrantedLanguageIds = new List<string> { "common" },
        });

        Assert.DoesNotContain("common", state.Languages);
        Assert.Contains(state.Warnings, w => w.Message.Contains("exceeds the 0 language slot"));
    }
}
