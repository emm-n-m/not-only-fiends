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
            RaceId = "aranea",
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
}
