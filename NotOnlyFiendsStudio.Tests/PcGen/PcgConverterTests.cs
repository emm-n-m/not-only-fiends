using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.PcGen;
using NotOnlyFiendsStudio.Studio;

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
        Assert.Equal("race:human", result.Character.RaceId);
        Assert.Equal(3, result.Character.Ticks.Count);
        Assert.Equal("class:cleric", result.Character.Ticks[0].DriverId);
        Assert.Equal("Clean import", result.Summary);
    }

    [Theory]
    [InlineData("N")]
    [InlineData("TN")]
    [InlineData("tn")]
    public void Convert_TrueNeutralAlignment_MapsToNeutral(string pcgenAlignment)
    {
        var data = CreateClericData();
        data.Alignment = pcgenAlignment;

        var result = PcgConverter.Convert(data, new PcgIdMapper());

        Assert.Equal(Alignment.N, result.Character.Alignment);
    }

    [Fact]
    public void Convert_UnknownAlignment_FallsBackToNeutral()
    {
        var data = CreateClericData();
        data.Alignment = "unknown";

        var result = PcgConverter.Convert(data, new PcgIdMapper());

        Assert.Equal(Alignment.N, result.Character.Alignment);
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
        Assert.Contains("feat:power_attack", lastTickFeats);

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
        Assert.Equal(3, lastTickFeats.Count(f => f == "feat:toughness"));
    }

    [Fact]
    public void Convert_SelectableFeat_PreservesSchoolAndSkillInFeatId()
    {
        var data = CreateClericData();
        // PCGen records the chosen school/skill in APPLIEDTO; each comma-separated
        // value is a separate taking of the repeatable feat.
        data.Feats.Add(new PcgFeatEntry
        { Key = "Spell Focus", AppliedTo = "Conjuration,Evocation", Types = new() { "General" } });
        data.Feats.Add(new PcgFeatEntry
        { Key = "Skill Focus", AppliedTo = "Knowledge (Arcana)", Types = new() { "General" } });

        var mapper = new PcgIdMapper();
        var registry = TestContentHelper.LoadAllPacks();
        var result = PcgConverter.Convert(data, mapper, registry);

        var feats = result.Character.Ticks[^1].Choices.FeatIds!;
        Assert.Contains("feat:spell_focus_conjuration", feats);
        Assert.Contains("feat:spell_focus_evocation", feats);
        Assert.Contains("feat:skill_focus_knowledge_arcana", feats);
        // The bare id must not be stored: prestige classes such as Cosmic Descryer
        // and Archmage gate on the variant ids.
        Assert.DoesNotContain("feat:spell_focus", feats);
        Assert.DoesNotContain("feat:skill_focus", feats);
    }

    [Fact]
    public void Convert_SelectableFeat_EmptyAppliedTo_FallsBackToBaseId()
    {
        var data = CreateClericData();
        // No selection recorded — still one taking, but no variant suffix to add.
        data.Feats.Add(new PcgFeatEntry { Key = "Spell Focus", Types = new() { "General" } });

        var mapper = new PcgIdMapper();
        var registry = TestContentHelper.LoadAllPacks();
        var result = PcgConverter.Convert(data, mapper, registry);

        var feats = result.Character.Ticks[^1].Choices.FeatIds!;
        Assert.Single(feats, f => f == "feat:spell_focus");
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
        Assert.Single(lastTickFeats, f => f == "feat:dodge");
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
        Assert.Equal("skill:concentration", lastTickSkills[0].SkillId);
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

        Assert.Equal("race:human", result.Character.RaceId);
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

        Assert.Equal("race:human", result.Character.RaceId); // fallback
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

    // --- Languages ---
    //
    // Every .pcg in the corpus carries a full pipe-delimited language list and the importer used
    // to drop all of it, so CharacterState.Languages had exactly one writer in all of content
    // (race:hellbred) and class:dragon_disciple's HasLanguage{draconic} prerequisite was
    // satisfiable by nothing at all. These run ungated — ParseText takes inline content, so no
    // .pcg corpus is needed.

    private const string LanguageLine =
        "LANGUAGE:Abyssal|LANGUAGE:Auran|LANGUAGE:Celestial|LANGUAGE:Common|LANGUAGE:Draconic|LANGUAGE:Infernal";

    private static string PcgWithLanguages(string languageLine) => string.Join("\n", new[]
    {
        "CHARACTERNAME:Polyglot",
        "RACE:Human",
        "ALIGN:NG",
        "STAT:STR|SCORE:10",
        "STAT:DEX|SCORE:10",
        "STAT:CON|SCORE:10",
        "STAT:INT|SCORE:16",
        "STAT:WIS|SCORE:10",
        "STAT:CHA|SCORE:10",
        languageLine,
        "CLASS:Fighter|LEVEL:1|SKILLPOOL:0",
        "CLASSABILITIESLEVEL:Fighter=1|HITPOINTS:10|SKILLSGAINED:2",
    });

    [Fact]
    public void Parse_PipeDelimitedLanguageLine_YieldsEveryLanguage()
    {
        var data = PcgParser.ParseText(PcgWithLanguages(LanguageLine), "polyglot.pcg");

        Assert.Equal(
            new[] { "Abyssal", "Auran", "Celestial", "Common", "Draconic", "Infernal" },
            data.Languages);
    }

    [Fact]
    public void Parse_SingleLanguageLine_Works()
    {
        var data = PcgParser.ParseText(PcgWithLanguages("LANGUAGE:Common"), "one.pcg");

        Assert.Equal(new[] { "Common" }, data.Languages);
    }

    [Fact]
    public void Parse_RepeatedLanguages_AreNotDuplicated()
    {
        var data = PcgParser.ParseText(
            PcgWithLanguages("LANGUAGE:Common|LANGUAGE:common|LANGUAGE:Draconic"), "dupes.pcg");

        Assert.Equal(new[] { "Common", "Draconic" }, data.Languages);
    }

    [Fact]
    public void Parse_NoLanguageLine_LeavesTheListEmpty()
    {
        var data = PcgParser.ParseText(PcgWithLanguages("# no languages here"), "none.pcg");

        Assert.Empty(data.Languages);
    }

    [Theory]
    [InlineData("Draconic", "draconic")]
    [InlineData("Infernal", "infernal")]
    // Bare and unprefixed, matching content: race:hellbred grants "infernal", not "language:infernal".
    [InlineData("Sylvan", "sylvan")]
    [InlineData("Undercommon", "undercommon")]
    [InlineData("Gnome", "gnome")]
    public void MapLanguage_ProducesTheBareContentId(string pcgenName, string expected)
    {
        Assert.Equal(expected, PcgIdMapper.MapLanguage(pcgenName));
    }

    [Fact]
    public void Convert_Languages_BecomeGrantLanguagePermabuffsBeforeTheFirstTick()
    {
        var data = PcgParser.ParseText(PcgWithLanguages(LanguageLine), "polyglot.pcg");

        var result = PcgConverter.Convert(data, new PcgIdMapper());

        var evt = Assert.Single(result.Character.PermanentEvents);
        Assert.Equal(0, evt.BeforeTick);

        var granted = evt.Permabuffs.OfType<GrantLanguage>().Select(g => g.LanguageId).ToList();
        Assert.Equal(
            new[] { "abyssal", "auran", "celestial", "common", "draconic", "infernal" },
            granted);
    }

    [Fact]
    public void Convert_NoLanguages_AddsNoPermanentEvent()
    {
        var result = PcgConverter.Convert(CreateClericData(), new PcgIdMapper());

        Assert.Empty(result.Character.PermanentEvents);
    }

    [Fact]
    public void Convert_LanguagesSurviveARoundTripThroughCharacterJson()
    {
        // GrantLanguage has to keep its $type discriminator through save/load, or an imported
        // character loses its languages the first time it is written to disk.
        var data = PcgParser.ParseText(PcgWithLanguages(LanguageLine), "polyglot.pcg");
        var character = PcgConverter.Convert(data, new PcgIdMapper()).Character;

        var json = System.Text.Json.JsonSerializer.Serialize(character, JsonOptions.Default);
        var reloaded = System.Text.Json.JsonSerializer.Deserialize<Character>(json, JsonOptions.Default)!;

        var granted = reloaded.PermanentEvents
            .SelectMany(e => e.Permabuffs)
            .OfType<GrantLanguage>()
            .Select(g => g.LanguageId)
            .ToList();
        Assert.Contains("draconic", granted);
        Assert.Equal(6, granted.Count);
    }
}
