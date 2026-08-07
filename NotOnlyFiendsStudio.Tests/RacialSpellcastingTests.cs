using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

public class RacialSpellcastingTests
{
    private static (ContentRegistry registry, ReplayStudio engine) CreateStudio()
    {
        var registry = TestContentHelper.LoadBundledPacks();
        return (registry, new ReplayStudio(registry));
    }

    private static Character BuildAranea(int racialHD, int sorcererLevels)
    {
        var character = new Character
        {
            Name = "Test Aranea",
            Alignment = Alignment.N,
            RaceId = "race:aranea",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 }
        };
        for (int i = 0; i < racialHD; i++)
            character.Ticks.Add(new Tick { DriverId = "racial_hd:magical_beast" });
        for (int i = 0; i < sorcererLevels; i++)
            character.Ticks.Add(new Tick { DriverId = "class:sorcerer" });
        return character;
    }

    [Fact]
    public void Aranea_ThreeRacialHD_NoClassLevels_SeedsSorcererAtCL3()
    {
        var (_, engine) = CreateStudio();
        var state = engine.Evaluate(BuildAranea(racialHD: 3, sorcererLevels: 0));

        Assert.True(state.Spellcasting.TryGetValue("class:sorcerer", out var sc),
            "Expected class:sorcerer entry in state.Spellcasting (seeded by finalize step)");
        Assert.Equal(3, sc!.CasterLevel);
        Assert.NotEmpty(sc.SpellsPerDay);
    }

    [Fact]
    public void Aranea_JuvenileTwoHD_SeedsSorcererAtCL2()
    {
        var (_, engine) = CreateStudio();
        var state = engine.Evaluate(BuildAranea(racialHD: 2, sorcererLevels: 0));

        Assert.True(state.Spellcasting.TryGetValue("class:sorcerer", out var sc));
        Assert.Equal(2, sc!.CasterLevel);
    }

    [Fact]
    public void Aranea_AdvancedFiveHD_SeedsSorcererAtCL5()
    {
        var (_, engine) = CreateStudio();
        var state = engine.Evaluate(BuildAranea(racialHD: 5, sorcererLevels: 0));

        Assert.True(state.Spellcasting.TryGetValue("class:sorcerer", out var sc));
        Assert.Equal(5, sc!.CasterLevel);
    }

    [Fact]
    public void Aranea_RacialPlusClassLevels_Stacks()
    {
        // 3 racial HD + 2 class levels of Sorcerer → effective Sorcerer 5
        var (_, engine) = CreateStudio();
        var state = engine.Evaluate(BuildAranea(racialHD: 3, sorcererLevels: 2));

        Assert.True(state.Spellcasting.TryGetValue("class:sorcerer", out var sc));
        Assert.Equal(5, sc!.CasterLevel);
    }

    [Fact]
    public void Aranea_OneRacialHDOnly_SeedsSorcererAtCL1()
    {
        // Edge case: a single-HD aranea should still get CL 1
        var (_, engine) = CreateStudio();
        var state = engine.Evaluate(BuildAranea(racialHD: 1, sorcererLevels: 0));

        Assert.True(state.Spellcasting.TryGetValue("class:sorcerer", out var sc));
        Assert.Equal(1, sc!.CasterLevel);
    }

    private static Character BuildRacial(string raceId, string racialHdDriver, int racialHD,
        string? classDriver, int classLevels)
    {
        var character = new Character
        {
            Name = "Test",
            Alignment = Alignment.N,
            RaceId = raceId,
            BaseAbilityScores = new AbilityScoreSet { STR = 12, DEX = 12, CON = 12, INT = 16, WIS = 16, CHA = 16 }
        };
        for (int i = 0; i < racialHD; i++)
            character.Ticks.Add(new Tick { DriverId = racialHdDriver });
        for (int i = 0; i < classLevels; i++)
            character.Ticks.Add(new Tick { DriverId = classDriver! });
        return character;
    }

    [Fact]
    public void Couatl_NineOutsiderHD_NoClassLevels_SeedsSorcererAtCL9()
    {
        // SRD: "A couatl casts spells as a 9th-level sorcerer."
        var (_, engine) = CreateStudio();
        var state = engine.Evaluate(BuildRacial("race:couatl", "racial_hd:outsider", 9, null, 0));

        Assert.True(state.Spellcasting.TryGetValue("class:sorcerer", out var sc),
            "Expected couatl to cast as a sorcerer (seeded by finalize step)");
        Assert.Equal(9, sc!.CasterLevel);
    }

    [Fact]
    public void Nymph_NoClassLevels_SeedsDruidAtCL7()
    {
        // SRD: "A nymph casts divine spells as a 7th-level druid."
        var (_, engine) = CreateStudio();
        var state = engine.Evaluate(BuildRacial("race:nymph", "racial_hd:fey", 6, null, 0));

        Assert.True(state.Spellcasting.TryGetValue("class:druid", out var sc));
        Assert.Equal(7, sc!.CasterLevel);
    }

    [Fact]
    public void Nymph_PlusSixDruidLevels_StacksToCL13()
    {
        // Nymph (druid 7) + 6 Druid class levels → effective Druid 13 ("Nymph Archdruid")
        var (_, engine) = CreateStudio();
        var state = engine.Evaluate(BuildRacial("race:nymph", "racial_hd:fey", 6, "class:druid", 6));

        Assert.True(state.Spellcasting.TryGetValue("class:druid", out var sc));
        Assert.Equal(13, sc!.CasterLevel);
    }

    [Fact]
    public void Nymph_RacialSpellcastingDoesNotAdvanceDruidClassFeatures()
    {
        var (_, engine) = CreateStudio();
        var state = engine.Evaluate(BuildRacial("race:nymph", "racial_hd:fey", 6, "class:druid", 6));

        // The racial grant stacks for casting only. Druid 6 has two wild-shape uses and no
        // level-7+ class features; treating the grant as a class-feature level would inflate
        // both values.
        Assert.Equal(2, state.Counters.GetValueOrDefault("wild_shape_uses_per_day"));
        Assert.DoesNotContain(state.Abilities, ability => ability.Id == "a_thousand_faces");
    }

    [Fact]
    public void Couatl_PlusLoremaster_AdvancesRacialSorcasterToCL12()
    {
        // Couatl (sorcerer 9) + 3 Loremaster levels advance the racial arcane casting → CL 12.
        // Regression guard for the seed-before-first-class-tick ordering: a spellcasting-advancement
        // PrC taken by a racial-only caster must find the racial casting to advance it.
        var (_, engine) = CreateStudio();
        var state = engine.Evaluate(BuildRacial("race:couatl", "racial_hd:outsider", 9, "class:loremaster", 3));

        Assert.True(state.Spellcasting.TryGetValue("class:sorcerer", out var sc),
            "Loremaster should have found the couatl's racial sorcerer casting");
        Assert.Equal(12, sc!.CasterLevel);
        Assert.DoesNotContain(state.Warnings, w => w.Message.Contains("no matching spellcasting class"));
    }
}
