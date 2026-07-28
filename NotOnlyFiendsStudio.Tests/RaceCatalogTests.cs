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

        Assert.Equal(new[] { "race:human", "race:drow" }, offered.Select(r => r.Id));
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
    public void EveryBundledRace_StatesALevelAdjustment()
    {
        // Recorded expectation: the public packs contain no null-LA races, so on a machine with no
        // private packs the default picker offers everything and the filter is a no-op. If this
        // ever fails, a public pack has gained an unpriced race and the picker will start hiding
        // it — which is the intended behaviour, but worth noticing deliberately.
        var registry = TestContentHelper.LoadBundledPacks();

        var unpriced = registry.GetAllRaces()
            .Where(r => !RaceCatalog.IsSanctionedPcRace(r))
            .Select(r => r.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(unpriced.Count == 0,
            $"bundled races without a level adjustment:\n{string.Join("\n", unpriced)}");
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
