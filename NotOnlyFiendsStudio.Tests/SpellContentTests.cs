using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public class SpellContentTests
{
    [Fact]
    public void ContentRegistry_LoadsSpells()
    {
        var registry = TestContentHelper.LoadAllPacks();

        var allSpells = registry.GetAllSpells().ToList();

        Assert.Equal(618, allSpells.Count);
        Assert.True(registry.TryGetSpell("spell:acid_arrow", out var acidArrow));
        Assert.Equal("Acid Arrow", acidArrow!.Name);
        Assert.Equal("conjuration", acidArrow.School);

        // Alignment "smite" spells are domain-only; the Good/Chaos/Law/Evil
        // domains already reference them by ID.
        Assert.True(registry.TryGetSpell("spell:holy_smite", out var holySmite));
        Assert.Equal(4, holySmite!.ClassLevels["domain:good"]);
        Assert.Contains(registry.GetSpellsForList("domain:chaos"), s => s.Id == "spell:chaos_hammer");
    }

    [Fact]
    public void MassFrog_IsADevelopableLevelTenEpicSpell()
    {
        var registry = TestContentHelper.LoadBundledPacks();
        var spell = registry.GetSpell("spell:frog_mass");

        Assert.Equal("Mass Frog", spell.Name);
        Assert.Equal("transmutation", spell.School);
        Assert.Equal("300 ft.", spell.Range);
        Assert.Equal("40-ft.-radius hemisphere", spell.Area);
        Assert.Equal("permanent", spell.Duration);
        Assert.Equal("Fortitude negates", spell.SavingThrow);
        Assert.Equal("yes", spell.SpellResistance);
        Assert.True(spell.Components.Verbal);
        Assert.True(spell.Components.Somatic);
        Assert.Equal(10, spell.ClassLevels[EpicSpellcasting.CharismaListId]);
        Assert.Equal(10, spell.ClassLevels[EpicSpellcasting.IntelligenceListId]);
        Assert.Equal(10, spell.ClassLevels[EpicSpellcasting.WisdomListId]);
    }

    [Fact]
    public void GetSpellsForClass_FiltersAndOrders()
    {
        var registry = TestContentHelper.LoadAllPacks();

        var sorcererSpells = registry.GetSpellsForClass("class:sorcerer", maxSpellLevel: 1).ToList();

        Assert.NotEmpty(sorcererSpells);
        Assert.All(sorcererSpells, spell => Assert.True(spell.ClassLevels["class:sorcerer"] <= 1));
        Assert.Equal(0, sorcererSpells.First().ClassLevels["class:sorcerer"]);
        Assert.Contains(sorcererSpells, s => s.Id == "spell:acid_splash");
    }

    [RequiresPrivatePacksFact]
    public void GetSpellsForList_SupportsDomainSpellLists()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();

        var corruptionSpells = registry.GetSpellsForList("domain:corruption").ToList();

        Assert.NotEmpty(corruptionSpells);
        // A domain's spell list is its definition's bonusSpells (level → spell); spells may also
        // carry a redundant domain:* key. Either way, every returned spell belongs to the domain.
        var domainSpellIds = registry.GetDomain("domain:corruption").BonusSpells.Values.ToHashSet();
        Assert.All(corruptionSpells, spell => Assert.True(
            domainSpellIds.Contains(spell.Id) || spell.ClassLevels.ContainsKey("domain:corruption")));
        Assert.Contains(corruptionSpells, s => s.Id == "spell:befoul");
    }

    [Fact]
    public void DomainDefinitions_ExposeBundledBonusSpells()
    {
        var registry = TestContentHelper.LoadAllPacks();

        var charmDomain = registry.GetDomain("domain:charm");

        Assert.Equal("spell:charm_person", charmDomain.BonusSpells[1]);
        Assert.Equal("spell:dominate_monster", charmDomain.BonusSpells[9]);
    }

    [Fact]
    public void BrokenSpellListReference_ProducesError()
    {
        var registry = new ContentRegistry();
        registry.RegisterSpell(new SpellDefinition
        {
            Id = "spell:bad_spell",
            Name = "Bad Spell",
            School = "evocation",
            ClassLevels = new Dictionary<string, int> { ["list:nonexistent"] = 1 },
            Components = new SpellComponents { Verbal = true, Somatic = true },
            CastingTime = "1 standard action",
            Range = "close",
            Duration = "instantaneous",
            SavingThrow = "none",
            SpellResistance = "no",
            Description = "Bad data"
        });

        registry.Validate();

        Assert.Contains(registry.Errors, e =>
            e.Kind == ContentErrorKind.BrokenReference &&
            e.Message.Contains("list:nonexistent"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void SpellLevelOutsideNormalAndEpicRange_ProducesError(int invalidLevel)
    {
        var registry = new ContentRegistry();
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:wizard",
            Name = "Wizard",
            HitDie = 4,
            BABProgression = BABProgression.Poor,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Poor,
                Ref = ProgressionRate.Poor,
                Will = ProgressionRate.Good
            }
        });
        registry.RegisterSpell(new SpellDefinition
        {
            Id = "spell:bad_spell",
            Name = "Bad Spell",
            School = "evocation",
            ClassLevels = new Dictionary<string, int> { ["class:wizard"] = invalidLevel },
            Components = new SpellComponents { Verbal = true, Somatic = true },
            CastingTime = "1 standard action",
            Range = "close",
            Duration = "instantaneous",
            SavingThrow = "none",
            SpellResistance = "no",
            Description = "Bad data"
        });

        registry.Validate();

        Assert.Contains(registry.Errors, e =>
            e.Kind == ContentErrorKind.InvalidValue &&
            e.Message.Contains($"invalid level {invalidLevel}"));
    }
}
