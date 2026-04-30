using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.PcGen;

namespace NotOnlyFiendsStudio.Tests.PcGen;

public class PcgConverterTests
{
    private static PcgCharacterData CreateClericData()
    {
        return new PcgCharacterData
        {
            FileName = "test_cleric.pcg",
            CharacterName = "Test Cleric",
            Race = "Human",
            Alignment = "NG",
            BaseStats = new() { ["STR"] = 10, ["DEX"] = 12, ["CON"] = 14, ["INT"] = 10, ["WIS"] = 16, ["CHA"] = 8 },
            Classes = new() { new PcgClassEntry { Name = "Cleric", Level = 3 } },
            Levels = new()
            {
                new PcgLevelEntry { ClassName = "Cleric", ClassLevel = 1 },
                new PcgLevelEntry { ClassName = "Cleric", ClassLevel = 2 },
                new PcgLevelEntry { ClassName = "Cleric", ClassLevel = 3 },
            },
            Feats = new()
            {
                new PcgFeatEntry { Key = "Power Attack", Types = new() { "General" } },
            },
            Skills = new()
            {
                new PcgSkillEntry { Name = "Concentration", Ranks = 6.0, BoughtClass = "Cleric" },
            },
            Domains = new()
            {
                new PcgDomainEntry { Name = "War", SourceClass = "Cleric" },
            },
        };
    }

    [Fact]
    public void Convert_CleanCharacter_NoWarnings()
    {
        var data = CreateClericData();
        var mapper = new PcgIdMapper();
        // No registry — skip content validation
        var result = PcgConverter.Convert(data, mapper);

        Assert.Empty(result.Warnings);
        Assert.Equal("Test Cleric", result.Character.Name);
        Assert.Equal("human", result.Character.RaceId);
        Assert.Equal(3, result.Character.Ticks.Count);
        Assert.Equal("class:cleric", result.Character.Ticks[0].DriverId);
        Assert.Equal("Clean import", result.Summary);
    }

    [Fact]
    public void Convert_AbilityScores_MappedCorrectly()
    {
        var data = CreateClericData();
        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper);

        Assert.Equal(10, result.Character.BaseAbilityScores.STR);
        Assert.Equal(12, result.Character.BaseAbilityScores.DEX);
        Assert.Equal(14, result.Character.BaseAbilityScores.CON);
        Assert.Equal(16, result.Character.BaseAbilityScores.WIS);
    }

    [Fact]
    public void Convert_FeatsPlacedOnLastTick()
    {
        var data = CreateClericData();
        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper);

        var lastTickFeats = result.Character.Ticks[^1].Choices.FeatIds;
        Assert.NotNull(lastTickFeats);
        Assert.Contains("power_attack", lastTickFeats);

        // Earlier ticks should have no feats
        Assert.Null(result.Character.Ticks[0].Choices.FeatIds);
        Assert.Null(result.Character.Ticks[1].Choices.FeatIds);
    }

    [Fact]
    public void Convert_RepeatableFeat_AppliedToExpandedToMultipleEntries()
    {
        var data = CreateClericData();
        // Simulate a repeatable feat taken 3 times (comma-separated APPLIEDTO)
        data.Feats.Add(new PcgFeatEntry { Key = "Toughness", AppliedTo = ",,", Types = new() { "General" } });

        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper);

        var lastTickFeats = result.Character.Ticks[^1].Choices.FeatIds!;
        Assert.Equal(3, lastTickFeats.Count(f => f == "toughness"));
    }

    [Fact]
    public void Convert_SelectableFeat_AppliedToBecomesVariantId()
    {
        // Spell Focus has selectionRequired="school" and is repeatable. PCGen records
        // multiple takings as APPLIEDTO:Enchantment,Transmutation. The converter must
        // emit distinct variant IDs (spell_focus_enchantment, spell_focus_transmutation)
        // so the engine can tell the schools apart.
        var data = CreateClericData();
        data.Feats.Add(new PcgFeatEntry
        {
            Key = "Spell Focus",
            AppliedTo = "Enchantment,Transmutation",
            Types = new() { "General" },
        });

        var registry = TestContentHelper.LoadAllPacks();
        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper, registry);

        var allFeats = result.Character.Ticks
            .SelectMany(t => t.Choices.FeatIds ?? new List<string>())
            .ToList();
        Assert.Contains("spell_focus_enchantment", allFeats);
        Assert.Contains("spell_focus_transmutation", allFeats);
        Assert.DoesNotContain("spell_focus", allFeats);
    }

    [Fact]
    public void Convert_SelectableFeat_AppliedToWithParens_SnakeCased()
    {
        // Skill Focus uses APPLIEDTO values like "Knowledge (Arcana)" — the parens
        // and spaces must collapse to snake_case so the variant matches engine IDs.
        var data = CreateClericData();
        data.Feats.Add(new PcgFeatEntry
        {
            Key = "Skill Focus",
            AppliedTo = "Knowledge (Arcana),Spellcraft",
            Types = new() { "General" },
        });

        var registry = TestContentHelper.LoadAllPacks();
        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper, registry);

        var allFeats = result.Character.Ticks
            .SelectMany(t => t.Choices.FeatIds ?? new List<string>())
            .ToList();
        Assert.Contains("skill_focus_knowledge_arcana", allFeats);
        Assert.Contains("skill_focus_spellcraft", allFeats);
    }

    [Fact]
    public void Convert_WithRegistry_DistributesFeatsAcrossTicks()
    {
        // The state-aware placer should spread feats over natural feat-slot HDs (1, 3, 6, ...)
        // instead of dumping them all at the last tick. This is what makes prestige-class
        // entry feat prereqs satisfiable mid-timeline.
        // Stats must clear feat ability prereqs (Power Attack/Cleave need STR 13, Dodge DEX 13);
        // otherwise placement defers to last tick.
        var data = new PcgCharacterData
        {
            CharacterName = "Distrib Test",
            Race = "Human",
            BaseStats = new() { ["STR"] = 14, ["DEX"] = 14, ["CON"] = 10, ["INT"] = 10, ["WIS"] = 10, ["CHA"] = 10 },
            Classes = new() { new PcgClassEntry { Name = "Fighter", Level = 6 } },
            Levels = Enumerable.Range(1, 6)
                .Select(lvl => new PcgLevelEntry { ClassName = "Fighter", ClassLevel = lvl })
                .ToList(),
            Feats = new()
            {
                new PcgFeatEntry { Key = "Dodge" },
                new PcgFeatEntry { Key = "Power Attack" },
                new PcgFeatEntry { Key = "Cleave" },
            },
        };
        var registry = TestContentHelper.LoadAllPacks();
        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper, registry);

        var ticksWithFeats = result.Character.Ticks
            .Where(t => t.Choices.FeatIds is { Count: > 0 })
            .ToList();
        // We expect feats spread over multiple ticks, not all on the last one.
        Assert.True(ticksWithFeats.Count > 1,
            $"Expected feats distributed across ticks, got {ticksWithFeats.Count} tick(s) with feats");
    }

    [Fact]
    public void Convert_FeatPlacement_RespectsHasFeatPrereqs()
    {
        // greater_spell_focus has HasFeat prereq on spell_focus. Topological ordering
        // should place spell_focus_enchantment at an earlier (or equal) HD than
        // greater_spell_focus_enchantment.
        var data = new PcgCharacterData
        {
            CharacterName = "Topo Test",
            Race = "Human",
            BaseStats = new() { ["STR"] = 10, ["DEX"] = 10, ["CON"] = 10, ["INT"] = 10, ["WIS"] = 10, ["CHA"] = 14 },
            Classes = new() { new PcgClassEntry { Name = "Wizard", Level = 6 } },
            Levels = Enumerable.Range(1, 6)
                .Select(lvl => new PcgLevelEntry { ClassName = "Wizard", ClassLevel = lvl })
                .ToList(),
            Feats = new()
            {
                // PCGen-listed order intentionally has Greater first, to prove topo wins.
                new PcgFeatEntry { Key = "Greater Spell Focus", AppliedTo = "Enchantment" },
                new PcgFeatEntry { Key = "Spell Focus", AppliedTo = "Enchantment" },
            },
        };
        var registry = TestContentHelper.LoadAllPacks();
        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper, registry);

        int? sfHd = null, gsfHd = null;
        for (int i = 0; i < result.Character.Ticks.Count; i++)
        {
            var feats = result.Character.Ticks[i].Choices.FeatIds;
            if (feats == null) continue;
            if (feats.Contains("spell_focus_enchantment")) sfHd = i + 1;
            if (feats.Contains("greater_spell_focus_enchantment")) gsfHd = i + 1;
        }

        Assert.NotNull(sfHd);
        Assert.NotNull(gsfHd);
        Assert.True(sfHd < gsfHd,
            $"Expected spell_focus before greater_spell_focus, got {sfHd} and {gsfHd}");
    }

    [Fact]
    public void Convert_NonRepeatableFeat_NoAppliedTo_AddedOnce()
    {
        var data = CreateClericData();
        // A feat with no APPLIEDTO should be added once
        data.Feats.Add(new PcgFeatEntry { Key = "Dodge", Types = new() { "General" } });

        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper);

        var lastTickFeats = result.Character.Ticks[^1].Choices.FeatIds!;
        Assert.Single(lastTickFeats, f => f == "dodge");
    }

    [Fact]
    public void Convert_SkillsPlacedOnLastTick()
    {
        var data = CreateClericData();
        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper);

        var lastTickSkills = result.Character.Ticks[^1].Choices.SkillAllocations;
        Assert.NotNull(lastTickSkills);
        Assert.Single(lastTickSkills);
        Assert.Equal("concentration", lastTickSkills[0].SkillId);
        Assert.Equal(12, lastTickSkills[0].HalfRanks); // 6.0 ranks * 2

        // Earlier ticks should have no skill allocations
        Assert.Null(result.Character.Ticks[0].Choices.SkillAllocations);
    }

    [Fact]
    public void Convert_DomainsFrontLoadedToTick0()
    {
        var data = CreateClericData();
        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper);

        var domains = result.Character.Ticks[0].Choices.ClassFeatureChoices?["domains"];
        Assert.NotNull(domains);
        Assert.Contains("domain:war", domains);
    }

    [Fact]
    public void Convert_AbilityIncrease_MappedToCorrectTick()
    {
        var data = CreateClericData();
        data.Levels.Add(new PcgLevelEntry
        {
            ClassName = "Cleric",
            ClassLevel = 4,
            AbilityIncrease = "WIS"
        });
        data.Classes[0].Level = 4;

        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper);

        Assert.Equal(4, result.Character.Ticks.Count);
        Assert.Equal(Ability.WIS, result.Character.Ticks[3].Choices.AbilityIncrease);
        Assert.Null(result.Character.Ticks[0].Choices.AbilityIncrease);
    }

    [Fact]
    public void Convert_UnmappedRace_FallsBackToHuman()
    {
        var data = CreateClericData();
        data.Race = "UnknownAlienRace";

        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper);

        Assert.Equal("human", result.Character.RaceId);
        Assert.True(result.RaceDropped);
        Assert.Contains(result.Warnings, w => w.Contains("UnknownAlienRace") && w.Contains("no engine mapping"));
    }

    [Fact]
    public void Convert_UnmappedClass_TickSkipped()
    {
        var data = new PcgCharacterData
        {
            CharacterName = "Mixed",
            Race = "Human",
            BaseStats = new() { ["STR"] = 10, ["DEX"] = 10, ["CON"] = 10, ["INT"] = 10, ["WIS"] = 10, ["CHA"] = 10 },
            Classes = new()
            {
                new PcgClassEntry { Name = "Fighter", Level = 2 },
                new PcgClassEntry { Name = "SuperSecretClass", Level = 1 },
            },
            Levels = new()
            {
                new PcgLevelEntry { ClassName = "Fighter", ClassLevel = 1 },
                new PcgLevelEntry { ClassName = "SuperSecretClass", ClassLevel = 1 },
                new PcgLevelEntry { ClassName = "Fighter", ClassLevel = 2 },
            },
        };

        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper);

        // The SuperSecretClass tick should be skipped
        Assert.Equal(2, result.Character.Ticks.Count);
        Assert.All(result.Character.Ticks, t => Assert.Equal("class:fighter", t.DriverId));
        Assert.Contains("SuperSecretClass", result.DroppedClasses);
        Assert.Contains(result.Warnings, w => w.Contains("SuperSecretClass") && w.Contains("no engine mapping"));
    }

    [Fact]
    public void Convert_Templates_Included()
    {
        var data = CreateClericData();
        data.Templates = new()
        {
            new PcgTemplateEntry { Name = "Half-Fiend", IsInternal = false },
            new PcgTemplateEntry { Name = "Base Race Type ~ Humanoid", IsInternal = true },
        };

        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper);

        // Only non-internal template should be included
        Assert.Single(result.Character.TemplateIds);
        Assert.Equal("template:half_fiend", result.Character.TemplateIds[0]);
    }

    [Fact]
    public void Convert_EmptyData_GracefulResult()
    {
        var data = new PcgCharacterData
        {
            CharacterName = "",
            Race = "",
        };

        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper);

        Assert.Equal("human", result.Character.RaceId); // fallback
        Assert.True(result.RaceDropped);
        Assert.Empty(result.Character.Ticks);
    }

    [Fact]
    public void Convert_DoesNotSubtractRacialOrTemplateModifiers()
    {
        // PCGen's STAT:X|SCORE is the base score *before* racial/template mods —
        // confirmed by comparing real character .pcg files to PCGen TXT exports.
        // The engine adds race/template bonuses on top of BaseAbilityScores, so the
        // converter must pass STAT through unchanged (no subtraction).
        var data = new PcgCharacterData
        {
            CharacterName = "Test Pixie",
            Race = "Pixie",
            BaseStats = new() { ["STR"] = 8, ["DEX"] = 14, ["CON"] = 16, ["INT"] = 18, ["WIS"] = 10, ["CHA"] = 16 },
            Classes = new() { new PcgClassEntry { Name = "Sorcerer", Level = 1 } },
            Levels = new() { new PcgLevelEntry { ClassName = "Sorcerer", ClassLevel = 1 } },
        };

        var registry = TestContentHelper.LoadAllPacks();
        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper, registry);

        Assert.Equal(8, result.Character.BaseAbilityScores.STR);
        Assert.Equal(14, result.Character.BaseAbilityScores.DEX);
        Assert.Equal(16, result.Character.BaseAbilityScores.CON);
        Assert.Equal(18, result.Character.BaseAbilityScores.INT);
        Assert.Equal(10, result.Character.BaseAbilityScores.WIS);
        Assert.Equal(16, result.Character.BaseAbilityScores.CHA);
    }

    [Fact]
    public void Convert_SubtractsLevelUpIncreasesFromStat()
    {
        // PCGen's STAT already includes level-up ability increases (verified against
        // Fairy Queen Amethyst: STAT:INT:28 = rolled 18 + 10 PRESTAT:INT bumps across
        // levels 4/8/.../40). The engine re-applies those increases via AbilityIncrease
        // ticks at HD 4/8/12/16/20, so the converter must subtract them to avoid double-
        // counting. Only PRESTAT entries at HD % 4 == 0 are subtracted — level-1 PRESTAT
        // adjustments are creation-time quirks the engine does not re-apply.
        var data = new PcgCharacterData
        {
            CharacterName = "Level 8 Cleric",
            Race = "Human",
            BaseStats = new() { ["STR"] = 10, ["DEX"] = 10, ["CON"] = 10, ["INT"] = 10, ["WIS"] = 18, ["CHA"] = 10 },
            Classes = new() { new PcgClassEntry { Name = "Cleric", Level = 8 } },
            Levels = new()
            {
                new PcgLevelEntry { ClassName = "Cleric", ClassLevel = 1 },
                new PcgLevelEntry { ClassName = "Cleric", ClassLevel = 2 },
                new PcgLevelEntry { ClassName = "Cleric", ClassLevel = 3 },
                new PcgLevelEntry { ClassName = "Cleric", ClassLevel = 4, AbilityIncrease = "WIS" },
                new PcgLevelEntry { ClassName = "Cleric", ClassLevel = 5 },
                new PcgLevelEntry { ClassName = "Cleric", ClassLevel = 6 },
                new PcgLevelEntry { ClassName = "Cleric", ClassLevel = 7 },
                new PcgLevelEntry { ClassName = "Cleric", ClassLevel = 8, AbilityIncrease = "WIS" },
            },
        };

        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper);

        // Two WIS increases at HD 4 and 8 are both applied by the engine, so subtract both.
        Assert.Equal(16, result.Character.BaseAbilityScores.WIS); // 18 - 2
        Assert.Equal(10, result.Character.BaseAbilityScores.STR); // no increases, unchanged
    }

    [Fact]
    public void Convert_Level1PrestatNotSubtracted()
    {
        // Creation-time PRESTAT quirks at level 1 (e.g., Lilly's Bard=1|PRESTAT:STR=1)
        // are never re-applied by the engine, so we must not subtract them.
        var data = new PcgCharacterData
        {
            CharacterName = "Level 1 Quirk",
            Race = "Human",
            BaseStats = new() { ["STR"] = 9, ["DEX"] = 10, ["CON"] = 10, ["INT"] = 10, ["WIS"] = 10, ["CHA"] = 10 },
            Classes = new() { new PcgClassEntry { Name = "Bard", Level = 1 } },
            Levels = new()
            {
                new PcgLevelEntry { ClassName = "Bard", ClassLevel = 1, AbilityIncrease = "STR" },
            },
        };

        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper);

        Assert.Equal(9, result.Character.BaseAbilityScores.STR);
    }

    [Fact]
    public void Convert_WithRegistry_DropsUnknownFeats()
    {
        var data = CreateClericData();
        data.Feats.Add(new PcgFeatEntry { Key = "Totally Made Up Feat" });

        var registry = TestContentHelper.LoadAllPacks();
        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper, registry);

        // "Totally Made Up Feat" should be in DroppedFeats
        Assert.Contains("Totally Made Up Feat", result.DroppedFeats);
        Assert.Contains(result.Warnings, w => w.Contains("Totally Made Up Feat") && w.Contains("not found in content"));
    }

    [Fact]
    public void Convert_WithRegistry_DropsUnknownSkills()
    {
        var data = CreateClericData();
        data.Skills.Add(new PcgSkillEntry { Name = "Alien Technology", Ranks = 5.0 });

        var registry = TestContentHelper.LoadAllPacks();
        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper, registry);

        Assert.Contains("Alien Technology", result.DroppedSkills);
    }

    [Fact]
    public void ParseText_ProducesSameResultAsLines()
    {
        var content = string.Join("\n", new[]
        {
            "CHARACTERNAME:TextParsed",
            "RACE:Human",
            "ALIGN:NG",
            "STAT:STR|SCORE:14",
            "STAT:DEX|SCORE:12",
            "STAT:CON|SCORE:10",
            "STAT:INT|SCORE:10",
            "STAT:WIS|SCORE:10",
            "STAT:CHA|SCORE:10",
            "CLASS:Fighter|LEVEL:1|SKILLPOOL:0",
            "CLASSABILITIESLEVEL:Fighter=1|HITPOINTS:10|SKILLSGAINED:2",
        });

        var data = PcgParser.ParseText(content, "test.pcg");

        Assert.Equal("TextParsed", data.CharacterName);
        Assert.Equal("Human", data.Race);
        Assert.Equal(14, data.BaseStats["STR"]);
        Assert.Single(data.Classes);
        Assert.Equal("Fighter", data.Classes[0].Name);
        Assert.Single(data.Levels);
        Assert.Equal("test.pcg", data.FileName);
    }
}
