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

        Assert.Equal(617, allSpells.Count);
        Assert.True(registry.TryGetSpell("acid_arrow", out var acidArrow));
        Assert.Equal("Acid Arrow", acidArrow!.Name);
        Assert.Equal("conjuration", acidArrow.School);

        // Alignment "smite" spells are domain-only; the Good/Chaos/Law/Evil
        // domains already reference them by ID.
        Assert.True(registry.TryGetSpell("holy_smite", out var holySmite));
        Assert.Equal(4, holySmite!.ClassLevels["domain:good"]);
        Assert.Contains(registry.GetSpellsForList("domain:chaos"), s => s.Id == "chaos_hammer");
    }

    [Fact]
    public void GetSpellsForClass_FiltersAndOrders()
    {
        var registry = TestContentHelper.LoadAllPacks();

        var sorcererSpells = registry.GetSpellsForClass("class:sorcerer", maxSpellLevel: 1).ToList();

        Assert.NotEmpty(sorcererSpells);
        Assert.All(sorcererSpells, spell => Assert.True(spell.ClassLevels["class:sorcerer"] <= 1));
        Assert.Equal(0, sorcererSpells.First().ClassLevels["class:sorcerer"]);
        Assert.Contains(sorcererSpells, s => s.Id == "acid_splash");
    }

    [RequiresPrivatePacksFact]
    public void GetSpellsForList_SupportsDomainSpellLists()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();

        var corruptionSpells = registry.GetSpellsForList("domain:corruption").ToList();

        Assert.NotEmpty(corruptionSpells);
        Assert.All(corruptionSpells, spell => Assert.True(spell.ClassLevels.ContainsKey("domain:corruption")));
        Assert.Contains(corruptionSpells, s => s.Id == "befoul");
    }

    [Fact]
    public void DomainDefinitions_ExposeBundledBonusSpells()
    {
        var registry = TestContentHelper.LoadAllPacks();

        var charmDomain = registry.GetDomain("domain:charm");

        Assert.Equal("charm_person", charmDomain.BonusSpells[1]);
        Assert.Equal("dominate_monster", charmDomain.BonusSpells[9]);
    }

    [Fact]
    public void BrokenSpellListReference_ProducesError()
    {
        var registry = new ContentRegistry();
        registry.RegisterSpell(new SpellDefinition
        {
            Id = "bad_spell",
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

    [Fact]
    public void NegativeSpellLevel_ProducesError()
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
            Id = "bad_spell",
            Name = "Bad Spell",
            School = "evocation",
            ClassLevels = new Dictionary<string, int> { ["class:wizard"] = -1 },
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
            e.Message.Contains("invalid level -1"));
    }
}
