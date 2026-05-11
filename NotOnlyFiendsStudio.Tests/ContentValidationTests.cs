using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public class ContentValidationTests
{
    [Fact]
    public void SRDContent_PassesValidation()
    {
        var registry = TestContentHelper.LoadAllPacks();
        registry.Validate();

        Assert.False(registry.HasErrors, string.Join("\n", registry.Errors.Select(e => e.Message)));
    }

    [Fact]
    public void SRDEquipment_LoadsCatalog()
    {
        var registry = TestContentHelper.LoadAllPacks();

        // Seed catalog should have weapons, armor, shields, wondrous, and rings.
        Assert.True(registry.TryGetEquipment("weapon:longsword", out var longsword));
        Assert.Equal("1d8", longsword!.Weapon!.Damage);
        Assert.True(registry.TryGetEquipment("armor:full_plate", out var fp));
        Assert.Equal(8, fp!.Armor!.ArmorBonus);
        Assert.True(registry.TryGetEquipment("shield:heavy_steel", out _));
        Assert.True(registry.TryGetEquipment("wondrous:cloak_of_resistance_3", out _));
        Assert.True(registry.TryGetEquipment("ring:protection_2", out _));
        Assert.True(registry.TryGetEquipment("wondrous:gauntlets_ogre_power", out _));
    }

    [Fact]
    public void BrokenRacialHDReference_ProducesError()
    {
        var registry = new ContentRegistry();
        registry.RegisterRace(new RaceDefinition
        {
            Id = "test_race",
            Name = "Test",
            Type = CreatureType.Humanoid,
            Size = Size.Medium,
            RacialHDDriverId = "racial_hd:nonexistent"
        });
        registry.Validate();

        Assert.True(registry.HasErrors);
        Assert.Contains(registry.Errors, e =>
            e.Kind == ContentErrorKind.BrokenReference &&
            e.Message.Contains("racial_hd:nonexistent"));
    }

    [Fact]
    public void BrokenHasFeatPrerequisite_ProducesError()
    {
        var registry = new ContentRegistry();
        registry.RegisterFeat(new FeatDefinition
        {
            Id = "bad_feat",
            Name = "Bad Feat",
            Prerequisites = new List<Prerequisite>
            {
                new HasFeat { FeatId = "nonexistent_feat" }
            }
        });
        registry.Validate();

        Assert.True(registry.HasErrors);
        Assert.Contains(registry.Errors, e =>
            e.Kind == ContentErrorKind.BrokenReference &&
            e.Message.Contains("nonexistent_feat"));
    }

    [Fact]
    public void BrokenMinClassLevel_ProducesError()
    {
        var registry = new ContentRegistry();
        registry.RegisterFeat(new FeatDefinition
        {
            Id = "bad_feat",
            Name = "Bad Feat",
            Prerequisites = new List<Prerequisite>
            {
                new MinClassLevel { ClassId = "class:nonexistent", Value = 4 }
            }
        });
        registry.Validate();

        Assert.True(registry.HasErrors);
        Assert.Contains(registry.Errors, e =>
            e.Kind == ContentErrorKind.BrokenReference &&
            e.Message.Contains("class:nonexistent"));
    }

    [Fact]
    public void BrokenGrantBonusFeat_ProducesError()
    {
        var registry = new ContentRegistry();
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:test",
            Name = "Test",
            HitDie = 10,
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Good, Ref = ProgressionRate.Poor, Will = ProgressionRate.Poor },
            LevelPermabuffs = new Dictionary<int, List<Permabuff>>
            {
                { 1, new List<Permabuff> { new GrantBonusFeat { FeatId = "nonexistent_feat" } } }
            }
        });
        registry.Validate();

        Assert.True(registry.HasErrors);
        Assert.Contains(registry.Errors, e =>
            e.Kind == ContentErrorKind.BrokenReference &&
            e.Message.Contains("nonexistent_feat"));
    }

    [Fact]
    public void ValidContent_NoErrors()
    {
        var registry = new ContentRegistry();
        registry.RegisterRace(new RaceDefinition
        {
            Id = "human",
            Name = "Human",
            Type = CreatureType.Humanoid,
            Size = Size.Medium
        });
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:fighter",
            Name = "Fighter",
            HitDie = 10,
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Good, Ref = ProgressionRate.Poor, Will = ProgressionRate.Poor }
        });
        registry.RegisterFeat(new FeatDefinition
        {
            Id = "power_attack",
            Name = "Power Attack"
        });
        registry.Validate();

        Assert.False(registry.HasErrors);
    }

    [Fact]
    public void MultipleContentRoots_MergeCorrectly()
    {
        var registry = TestContentHelper.LoadAllPacks();

        var countBefore = registry.GetAllFeats().Count();

        // Simulate a homebrew feat by registering directly
        registry.RegisterFeat(new FeatDefinition
        {
            Id = "custom_feat",
            Name = "Custom Feat"
        });

        var countAfter = registry.GetAllFeats().Count();
        Assert.Equal(countBefore + 1, countAfter);
        Assert.NotNull(registry.GetFeat("custom_feat"));

        // Original feats still present
        Assert.NotNull(registry.GetFeat("power_attack"));
    }

    [Fact]
    public void SameIdOverride_LaterWins()
    {
        var registry = new ContentRegistry();
        registry.RegisterFeat(new FeatDefinition { Id = "test_feat", Name = "Version 1" });
        registry.RegisterFeat(new FeatDefinition { Id = "test_feat", Name = "Version 2" });

        Assert.Equal("Version 2", registry.GetFeat("test_feat").Name);
    }
}
