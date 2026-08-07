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

        Assert.Equal(667, allSpells.Count);
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

    /// <summary>
    /// Both spellcasting prestige classes carry their own spell list on the class page rather
    /// than borrowing another class's. Their spells-per-day tables are useless without it, and an
    /// empty list is silent — the class simply warns on every spell a character selects — so the
    /// exact SRD lists are pinned here.
    /// </summary>
    [Theory]
    // Assassin Spell List, SRD assassin page.
    [InlineData("class:assassin", 1, "spell:disguise_self,spell:detect_poison,spell:feather_fall," +
        "spell:ghost_sound,spell:jump,spell:obscuring_mist,spell:sleep,spell:true_strike")]
    [InlineData("class:assassin", 2, "spell:alter_self,spell:cats_grace,spell:darkness," +
        "spell:foxs_cunning,spell:illusory_script,spell:invisibility,spell:pass_without_trace," +
        "spell:spider_climb,spell:undetectable_alignment")]
    [InlineData("class:assassin", 3, "spell:deep_slumber,spell:deeper_darkness,spell:false_life," +
        "spell:magic_circle_against_good,spell:misdirection,spell:nondetection")]
    [InlineData("class:assassin", 4, "spell:clairaudience_clairvoyance,spell:dimension_door," +
        "spell:freedom_of_movement,spell:glibness,spell:invisibility_greater," +
        "spell:locate_creature,spell:modify_memory,spell:poison")]
    // Blackguard Spell List, SRD blackguard page. "protection from elements" there is the 3.0
    // name of protection from energy, which is what is tagged.
    [InlineData("class:blackguard", 1, "spell:cause_fear,spell:corrupt_weapon," +
        "spell:cure_light_wounds,spell:doom,spell:inflict_light_wounds,spell:magic_weapon," +
        "spell:summon_monster_i")]
    [InlineData("class:blackguard", 2, "spell:bulls_strength,spell:cure_moderate_wounds," +
        "spell:darkness,spell:death_knell,spell:eagles_splendor,spell:inflict_moderate_wounds," +
        "spell:shatter,spell:summon_monster_ii")]
    [InlineData("class:blackguard", 3, "spell:contagion,spell:cure_serious_wounds," +
        "spell:deeper_darkness,spell:inflict_serious_wounds,spell:protection_from_energy," +
        "spell:summon_monster_iii")]
    [InlineData("class:blackguard", 4, "spell:cure_critical_wounds,spell:freedom_of_movement," +
        "spell:inflict_critical_wounds,spell:poison,spell:summon_monster_iv")]
    public void PrestigeClassSpellLists_MatchTheSrdClassPage(
        string classId, int spellLevel, string expectedIds)
    {
        var registry = TestContentHelper.LoadAllPacks();

        var actual = registry.GetAllSpells()
            .Where(spell => spell.ClassLevels.TryGetValue(classId, out var level) && level == spellLevel)
            .Select(spell => spell.Id)
            .OrderBy(id => id, StringComparer.Ordinal);

        Assert.Equal(expectedIds.Split(',').OrderBy(id => id, StringComparer.Ordinal), actual);
    }

    /// <summary>
    /// A class whose list is "the standard list, plus/minus a few" borrows it via
    /// <c>spellListSources</c> instead of re-tagging hundreds of spells. Losing that one field is
    /// silent — the class keeps its spells-per-day table and simply warns on every spell selected
    /// — so each borrowing class is pinned to the list it borrows.
    /// </summary>
    [Theory]
    // UA cloistered cleric: "all the standard cleric class features", plus a handful of spells.
    [InlineData("class:cloistered_cleric", "class:cleric")]
    // UA planar ranger: "spellcasting ability is largely unchanged from that of the standard
    // ranger" — the only difference is which creatures animal spells affect, not the list.
    [InlineData("class:planar_ranger", "class:ranger")]
    public void BorrowingCasters_DeclareTheListTheyBorrow(string classId, string expectedSource)
    {
        var registry = TestContentHelper.LoadAllPacks();

        var driver = Assert.IsType<HDDriver>(registry.GetAllDrivers().Single(d => d.Id == classId));
        Assert.NotNull(driver.Spellcasting);
        Assert.Contains(expectedSource, driver.Spellcasting!.SpellListSources);

        // The borrowed list actually resolves: a spell on the source list is reachable from the
        // borrowing class, which is what the replay engine checks a selection against.
        var sourceSpell = registry.GetAllSpells().First(s => s.ClassLevels.ContainsKey(expectedSource));
        Assert.True(registry.TryGetSpellLevelForList(sourceSpell, classId, out _));
    }

    /// <summary>
    /// The UA paladin variants *replace* the paladin list rather than adding to it, so they must
    /// carry their own tags and must not borrow — borrowing would silently readmit the good-aligned
    /// paladin spells the variant is defined by not having.
    /// </summary>
    [Fact]
    public void PaladinOfTyranny_ReplacesThePaladinListRatherThanBorrowingIt()
    {
        var registry = TestContentHelper.LoadAllPacks();

        var driver = Assert.IsType<HDDriver>(
            registry.GetAllDrivers().Single(d => d.Id == "class:paladin_of_tyranny"));
        Assert.Empty(driver.Spellcasting!.SpellListSources);

        var tagged = registry.GetAllSpells()
            .Where(s => s.ClassLevels.ContainsKey("class:paladin_of_tyranny"))
            .ToList();
        // 44 spells are named on the UA page, counting "magic circle against chaos/good" as two.
        Assert.Equal(44, tagged.Count);
        Assert.All(tagged, s => Assert.InRange(s.ClassLevels["class:paladin_of_tyranny"], 1, 4));

        // The two counterpart spells the UA page names but never describes. Both are authored
        // from their good-aligned SRD originals (see corrupt_weapon.json / unholy_sword.json).
        Assert.Contains(tagged, s => s.Id == "spell:corrupt_weapon");
        Assert.Contains(tagged, s => s.Id == "spell:unholy_sword");

        // Spells the variant explicitly drops must not leak back in from the standard list.
        foreach (var removed in new[] { "spell:bless_weapon", "spell:holy_sword", "spell:dispel_evil" })
        {
            Assert.True(registry.TryGetSpell(removed, out var spell) && spell != null, removed);
            Assert.False(registry.TryGetSpellLevelForList(spell!, "class:paladin_of_tyranny", out _),
                $"{removed} should not be on the paladin of tyranny's list");
        }
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
