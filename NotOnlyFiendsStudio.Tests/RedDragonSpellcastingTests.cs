using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

public class RedDragonSpellcastingTests
{
    private static Character RedDragon(int racialHd, int archmageLevels = 0)
    {
        var character = new Character
        {
            Name = "Red dragon",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 30, WIS = 10, CHA = 30,
            },
            Ticks = Enumerable.Range(0, racialHd)
                .Select(_ => new Tick { DriverId = "racial_hd:red_dragon" }).ToList(),
        };

        character.Ticks.AddRange(Enumerable.Range(0, archmageLevels).Select(_ => new Tick
        {
            DriverId = "class:archmage",
            Choices = new TickChoices
            {
                ClassFeatureChoices = new Dictionary<string, List<string>>
                {
                    ["advance_spellcasting"] = new() { "racial_hd:red_dragon" },
                },
            },
        }));
        return character;
    }

    [Theory]
    [InlineData(12, 0, -1)]
    [InlineData(13, 1, 1)]
    [InlineData(15, 1, 1)]
    [InlineData(16, 3, 1)]
    [InlineData(42, 19, 9)]
    [InlineData(52, 27, 9)]
    public void RacialHd_UsesPcGenCasterAndSpellProgression(int racialHd, int casterLevel, int maxSpellLevel)
    {
        var state = new ReplayStudio(TestContentHelper.LoadBundledPacks()).Evaluate(RedDragon(racialHd));

        if (maxSpellLevel < 0)
        {
            Assert.DoesNotContain("racial_hd:red_dragon", state.Spellcasting.Keys);
            return;
        }

        var casting = state.Spellcasting["racial_hd:red_dragon"];
        Assert.Equal(casterLevel, casting.CasterLevel);
        Assert.Equal(maxSpellLevel, casting.MaxSpellLevel);
        Assert.Equal(SpellAcquisition.SpellsKnown, casting.Acquisition);
    }

    [Fact]
    public void Archmage_AdvancesRedDragonArcaneCasting()
    {
        var state = new ReplayStudio(TestContentHelper.LoadBundledPacks()).Evaluate(RedDragon(52, 5));

        Assert.Equal(32, state.Spellcasting["racial_hd:red_dragon"].CasterLevel);
        Assert.DoesNotContain(state.Warnings,
            warning => warning.Message.Contains("AdvanceSpellcasting", StringComparison.Ordinal));
    }

    [Fact]
    public void CombinedSpellList_ExposesSorcererClericAndDomainSpells()
    {
        var registry = TestContentHelper.LoadBundledPacks();
        var spellIds = registry.GetSpellsForList("racial_hd:red_dragon", 9)
            .Select(spell => spell.Id).ToHashSet();

        Assert.Contains("spell:wish", spellIds);
        Assert.Contains("spell:miracle", spellIds);
        Assert.Contains("spell:chaos_hammer", spellIds);

        Assert.True(registry.TryGetSpellLevelForList(
            registry.GetSpell("spell:miracle"), "racial_hd:red_dragon", out var miracleLevel));
        Assert.Equal(9, miracleLevel);
    }
}
