using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// The race picker previously listed <c>GetAllRaces()</c> unfiltered, so every monster race, every
/// companion race and every creature the source never priced as a PC option was offered as a
/// character choice with nothing marking it — a discoverability trap on the very first step of
/// character creation.
///
/// <c>LevelAdjustment</c> being <c>int?</c> is the signal that makes this decidable, and these
/// tests pin that meaning: null is "never sanctioned as a PC race", not "LA 0".
/// </summary>
public class RaceCatalogTests
{
    private static RaceDefinition Race(string id, int? levelAdjustment) => new()
    {
        Id = id,
        Name = id.Replace("race:", ""),
        Type = CreatureType.Humanoid,
        Size = Size.Medium,
        LevelAdjustment = levelAdjustment,
    };

    // Synthetic so the filter stays covered on machines without the private packs, where the only
    // real null-LA races live.
    private static List<RaceDefinition> Fixture() => new()
    {
        Race("race:human", 0),
        Race("race:drow", 2),
        Race("race:monstrous_thing", null),
        Race("race:companion_beast", null),
    };

    [Fact]
    public void NullLevelAdjustment_IsNotASanctionedPcRace()
    {
        Assert.False(RaceCatalog.IsSanctionedPcRace(Race("race:monstrous_thing", null)));
    }

    [Fact]
    public void ZeroLevelAdjustment_IsASanctionedPcRace()
    {
        // The whole point of the nullable: 0 means "playable at no cost" (Human), not "unpriced".
        Assert.True(RaceCatalog.IsSanctionedPcRace(Race("race:human", 0)));
    }

    [Fact]
    public void ByDefault_NullLaRacesAreExcluded()
    {
        var offered = RaceCatalog.ForPicker(Fixture(), includeNonPcRaces: false).ToList();

        // Name-ordered, no longer insertion-ordered — see OffersRacesPcFirstThenByName.
        Assert.Equal(new[] { "race:drow", "race:human" }, offered.Select(r => r.Id));
    }

    [Fact]
    public void WithTheToggleOn_NullLaRacesAreIncluded()
    {
        var offered = RaceCatalog.ForPicker(Fixture(), includeNonPcRaces: true).ToList();

        Assert.Equal(4, offered.Count);
        Assert.Contains(offered, r => r.Id == "race:monstrous_thing");
        Assert.Contains(offered, r => r.Id == "race:companion_beast");
    }

    [Fact]
    public void OffersRacesPcFirstThenByName()
    {
        // The picker truncates long lists, so registry load order is a discoverability trap: a
        // late-loading pack's race can sit past the cut and look absent (the brachina, 2026-08-27).
        // Sanctioned PC races lead, then non-PC entries, each block alphabetical by name.
        var offered = RaceCatalog.ForPicker(Fixture(), includeNonPcRaces: true).ToList();

        Assert.Equal(
            new[] { "race:drow", "race:human", "race:companion_beast", "race:monstrous_thing" },
            offered.Select(r => r.Id));
    }

    [Fact]
    public void AnAlreadySelectedNonPcRace_StaysInTheListWhileHidden()
    {
        // Opening a companion or monster character must not silently drop its own race from the
        // picker: SearchSelect reverts to a matching item on blur, so an absent selection would
        // quietly change the character.
        var offered = RaceCatalog
            .ForPicker(Fixture(), includeNonPcRaces: false, alwaysIncludeId: "race:companion_beast")
            .ToList();

        Assert.Contains(offered, r => r.Id == "race:companion_beast");
        Assert.DoesNotContain(offered, r => r.Id == "race:monstrous_thing");
    }

    [Theory]
    [InlineData(null, "no sanctioned LA")]
    [InlineData(0, "LA +0")]
    [InlineData(3, "LA +3")]
    public void LevelAdjustmentDescription_DistinguishesNullFromZero(int? levelAdjustment, string expected)
    {
        // The sheet printed "(LA +N)" only when N > 0, so null and 0 rendered identically.
        Assert.Equal(expected, RaceCatalog.DescribeLevelAdjustment(Race("race:x", levelAdjustment)));
    }

    [Fact]
    public void NonPcRacesAreMarked_AndPcRacesAreNot()
    {
        Assert.Equal(string.Empty, RaceCatalog.NonPcMarker(Race("race:human", 0)));
        Assert.NotEqual(string.Empty, RaceCatalog.NonPcMarker(Race("race:monstrous_thing", null)));
    }

    [Fact]
    public void EveryBundledRace_HasEvenAbilityModifiers()
    {
        AssertAllRacialAbilityModifiersAreEven(TestContentHelper.LoadBundledPacks().GetAllRaces());
    }

    [RequiresPrivatePacksFact]
    public void EveryPrivateRace_HasEvenAbilityModifiers()
    {
        AssertAllRacialAbilityModifiersAreEven(
            TestContentHelper.LoadBundledAndPrivatePacksIfAvailable().GetAllRaces());
    }

    private static void AssertAllRacialAbilityModifiersAreEven(IEnumerable<RaceDefinition> races)
    {
        // Monster stat blocks use 10 as the baseline for even scores and 11 for odd scores, so
        // every encoded racial ability modifier must be even.
        var oddModifiers = races
            .SelectMany(race => GetRacialAbilityModifiers(race))
            .Where(entry => entry.Value % 2 != 0)
            .Select(entry => $"{entry.RaceId} {entry.Ability}={entry.Value}")
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        Assert.True(oddModifiers.Count == 0,
            $"races with odd ability modifiers:\n{string.Join("\n", oddModifiers)}");
    }

    private static IEnumerable<(string RaceId, string Ability, int Value)> GetRacialAbilityModifiers(
        RaceDefinition race)
    {
        if (race.AbilityModifiers == null)
            yield break;

        yield return (race.Id, "STR", race.AbilityModifiers.STR);
        yield return (race.Id, "DEX", race.AbilityModifiers.DEX);
        yield return (race.Id, "CON", race.AbilityModifiers.CON);
        yield return (race.Id, "INT", race.AbilityModifiers.INT);
        yield return (race.Id, "WIS", race.AbilityModifiers.WIS);
        yield return (race.Id, "CHA", race.AbilityModifiers.CHA);
    }

    [Fact]
    public void EveryBundledRace_StatesALevelAdjustment()
    {
        // Recorded expectation (2026-08-27): a bundled race is unpriced exactly when its source
        // prints no level adjustment — every companion_/familiar_ animal and creature entry
        // (animals have no LA at all), plus these four monster entries which previously carried
        // an invented 0. The picker hides all of them behind the non-PC toggle. If this fails,
        // a public pack gained or repriced a race — intended behaviour, but worth noticing
        // deliberately.
        // Not the imp: the SRD imp entry prints LA +3 (PC-legal improved familiar), even though
        // the PCGen LST omits the tag — the LST's LEVELADJUSTMENT coverage is incomplete.
        var expectedUnpricedOutsideTheMenagerie = new[]
        {
            "race:dragon_red_great_wyrm",
            "race:dragon_red_great_wyrm_colossal_plus",
            "race:medusa",
        };

        var registry = TestContentHelper.LoadBundledPacks();
        var all = registry.GetAllRaces().ToList();

        bool IsMenagerie(RaceDefinition r) =>
            r.Id.StartsWith("race:companion_", StringComparison.Ordinal)
            || r.Id.StartsWith("race:familiar_", StringComparison.Ordinal);

        var pricedMenagerie = all
            .Where(r => IsMenagerie(r) && RaceCatalog.IsSanctionedPcRace(r))
            .Select(r => r.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        Assert.True(pricedMenagerie.Count == 0,
            $"companion/familiar races claiming a level adjustment:\n{string.Join("\n", pricedMenagerie)}");

        var unpricedElsewhere = all
            .Where(r => !IsMenagerie(r) && !RaceCatalog.IsSanctionedPcRace(r))
            .Select(r => r.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        Assert.Equal(expectedUnpricedOutsideTheMenagerie, unpricedElsewhere);
    }

    [RequiresPrivatePacksFact]
    public void TheFiveFiendishCodexRaces_AreHiddenByDefaultAndShownByTheToggle()
    {
        // The real null-LA races. They live in the private fiendish_codex_1 pack, and they are the
        // reason LevelAdjustment became nullable: five of them carried invented level adjustments
        // for months before the 2026-07-28 audit removed the guesses.
        var expected = new[]
        {
            "race:ekolid", "race:juvenile_nabassu", "race:armanite", "race:yochlol", "race:lilitu",
        };

        var races = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable().GetAllRaces().ToList();

        var hidden = RaceCatalog.ForPicker(races, includeNonPcRaces: false).Select(r => r.Id).ToHashSet();
        var shown = RaceCatalog.ForPicker(races, includeNonPcRaces: true).Select(r => r.Id).ToHashSet();

        foreach (var id in expected)
        {
            Assert.Contains(id, races.Select(r => r.Id));
            Assert.DoesNotContain(id, hidden);
            Assert.Contains(id, shown);
        }
    }
}
