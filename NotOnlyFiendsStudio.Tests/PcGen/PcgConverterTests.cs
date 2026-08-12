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
    public void Convert_SpellLikeAbilitySelection_UsesPcGenDisplayName()
    {
        var data = new PcgCharacterData
        {
            CharacterName = "Succubus Attendant",
            Race = "Demon (Succubus)",
            Alignment = "CE",
            BaseStats = new() { ["STR"] = 12, ["DEX"] = 14, ["CON"] = 10, ["INT"] = 15, ["WIS"] = 17, ["CHA"] = 16 },
            Classes = new() { new PcgClassEntry { Name = "Outsider", Level = 12 } },
            Levels = Enumerable.Range(1, 12)
                .Select(level => new PcgLevelEntry { ClassName = "Outsider", ClassLevel = level })
                .ToList(),
            Feats = new()
            {
                new PcgFeatEntry { Key = "Quicken Spell-Like Ability", AppliedTo = "Charm Monster" },
            },
        };

        var registry = TestContentHelper.LoadBundledPacks();
        var result = PcgConverter.Convert(data, new PcgIdMapper(), registry);
        var state = new ReplayStudio(registry).Evaluate(result.Character);

        Assert.Contains("feat:quicken_spell_like_ability_charm_monster",
            result.Character.Ticks[^1].Choices.FeatIds!);
        Assert.Contains("feat:quicken_spell_like_ability_charm_monster", state.Feats);
        Assert.DoesNotContain(state.Warnings,
            warning => warning.Message.Contains("requires a valid spell_like_ability selection", StringComparison.Ordinal));
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
    public void Convert_DomainsPlacedOnTheirSourceTick()
    {
        var data = CreateClericData();
        var mapper = new PcgIdMapper();
        var result = PcgConverter.Convert(data, mapper);

        var domains = result.Character.Ticks[0].Choices.ClassFeatureChoices?["domains"];
        Assert.NotNull(domains);
        Assert.Contains("domain:war", domains);
    }

    [Fact]
    public void Convert_LaterDomainSourceLevel_IsPlacedOnMatchingTick()
    {
        var data = CreateClericData();
        data.Classes = new()
        {
            new PcgClassEntry { Name = "Bard", Level = 1 },
            new PcgClassEntry { Name = "Druid", Level = 2 },
        };
        data.Levels = new()
        {
            new PcgLevelEntry { ClassName = "Bard", ClassLevel = 1 },
            new PcgLevelEntry { ClassName = "Druid", ClassLevel = 1 },
            new PcgLevelEntry { ClassName = "Druid", ClassLevel = 2 },
        };
        data.Domains = new()
        {
            new PcgDomainEntry { Name = "Plant", SourceClass = "Druid", SourceLevel = 2 },
        };

        var registry = TestContentHelper.LoadAllPacks();
        var result = PcgConverter.Convert(data, new PcgIdMapper(), registry);

        Assert.Null(result.Character.Ticks[0].Choices.ClassFeatureChoices);
        Assert.Null(result.Character.Ticks[1].Choices.ClassFeatureChoices);
        Assert.Equal(new[] { "domain:plant" },
            result.Character.Ticks[2].Choices.ClassFeatureChoices!["imported_source_domains"]);

        var state = new ReplayStudio(registry).Evaluate(result.Character);
        Assert.Contains("domain:plant", state.Domains);
        Assert.Equal("class:druid", state.DomainOwners["domain:plant"]);
        Assert.DoesNotContain(state.Warnings,
            warning => warning.Message.Contains("pending domain", StringComparison.Ordinal));
    }

    [Fact]
    public void Convert_WizardSubclassAndProhibitedSchools_BecomeFirstLevelChoices()
    {
        var data = CreateClericData();
        data.Classes = new()
        {
            new PcgClassEntry
            {
                Name = "Wizard",
                Level = 1,
                Subclass = "Abjurer",
                ProhibitedSchools = new() { "Enchantment", "Evocation" },
            },
        };
        data.Levels = new() { new PcgLevelEntry { ClassName = "Wizard", ClassLevel = 1 } };
        data.Domains.Clear();

        var result = PcgConverter.Convert(data, new PcgIdMapper());
        var choices = result.Character.Ticks[0].Choices.ClassFeatureChoices!;

        Assert.Equal(new[] { "school:abjuration" }, choices[WizardSchools.SpecializationFeature]);
        Assert.Equal(new[] { "school:enchantment", "school:evocation" }, choices[WizardSchools.ProhibitedFeature]);
    }

    [Fact]
    public void Convert_PrestigeSpellcasterChoice_BecomesAdvanceChoice()
    {
        var data = CreateClericData();
        data.Classes = new()
        {
            new PcgClassEntry { Name = "Wizard", Level = 1 },
            new PcgClassEntry { Name = "Loremaster", Level = 1 },
        };
        data.Levels = new()
        {
            new PcgLevelEntry { ClassName = "Wizard", ClassLevel = 1 },
            new PcgLevelEntry { ClassName = "Loremaster", ClassLevel = 1, SpellcasterChoices = new() { "Wizard" } },
        };
        data.Domains.Clear();

        var result = PcgConverter.Convert(data, new PcgIdMapper());

        Assert.Equal(new[] { "class:wizard" },
            result.Character.Ticks[1].Choices.ClassFeatureChoices!["advance_spellcasting"]);
    }

    [Fact]
    public void ParseText_ExtractsDomainLevelWizardSchoolsAndSpellcasterChoices()
    {
        const string pcg = """
            CLASS:Wizard|SUBCLASS:Abjurer|LEVEL:1|PROHIBITED:Enchantment,Evocation
            CLASSABILITIESLEVEL:Loremaster=1|HITPOINTS:2|ADD:[SPELLCASTER:ANY|CHOICE:Wizard]
            DOMAIN:Plant|SOURCE:[TYPE:PCClass|NAME:Druid|LEVEL:2]
            """;

        var data = PcgParser.ParseText(pcg);

        Assert.Equal(new[] { "Enchantment", "Evocation" }, data.Classes[0].ProhibitedSchools);
        Assert.Equal(new[] { "Wizard" }, data.Levels[0].SpellcasterChoices);
        Assert.Equal("Druid", data.Domains[0].SourceClass);
        Assert.Equal(2, data.Domains[0].SourceLevel);
    }

    [Fact]
    public void ParseText_ExtractsSelectableClassAbilities()
    {
        const string pcg = "ABILITY:Special Ability|KEY:High Arcana|APPLIEDTO:Arcane Fire|CLASS:Archmage|LEVEL:1";

        var data = PcgParser.ParseText(pcg);

        var ability = Assert.Single(data.ClassAbilities);
        Assert.Equal("Special Ability", ability.Category);
        Assert.Equal("High Arcana", ability.Key);
        Assert.Equal("Arcane Fire", ability.AppliedTo);
        Assert.Equal("Archmage", ability.ClassName);
        Assert.Equal(1, ability.ClassLevel);
    }

    [Fact]
    public void Convert_MapsArchmageSelectionsToTheirGrantedTicks()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var data = new PcgCharacterData
        {
            Race = "Human",
            BaseStats = new() { ["INT"] = 18 },
            Classes = new()
            {
                new() { Name = "Wizard", Level = 1 },
                new() { Name = "Archmage", Level = 2 },
            },
            Levels = new()
            {
                new() { ClassName = "Wizard", ClassLevel = 1 },
                new() { ClassName = "Archmage", ClassLevel = 1 },
                new() { ClassName = "Archmage", ClassLevel = 2 },
            },
            ClassAbilities = new()
            {
                new() { Key = "High Arcana", AppliedTo = "Arcane Fire" },
                new() { Key = "High Arcana", AppliedTo = "Arcane Reach" },
            },
        };

        var result = PcgConverter.Convert(data, new PcgIdMapper(), registry);

        Assert.Equal(new[] { "arcane_fire" },
            result.Character.Ticks[1].Choices.ClassFeatureChoices!["class_feature:high_arcana"]);
        Assert.Equal(new[] { "arcane_reach" },
            result.Character.Ticks[2].Choices.ClassFeatureChoices!["class_feature:high_arcana"]);
        Assert.Empty(result.DroppedClassAbilities);
    }

    [Fact]
    public void Convert_AnimalTricksAreIgnoredAsCreatureProperties()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var data = new PcgCharacterData
        {
            Race = "Human",
            BaseStats = new() { ["INT"] = 10 },
            Classes = new() { new() { Name = "Fighter", Level = 1 } },
            Levels = new() { new() { ClassName = "Fighter", ClassLevel = 1 } },
            ClassAbilities = new()
            {
                new() { Key = "Animal Trick ~ Attack", AppliedTo = "Attack" },
                new() { Key = "Animal Trick ~ Fetch", AppliedTo = "Fetch" }
            }
        };

        var result = PcgConverter.Convert(data, new PcgIdMapper(), registry);

        Assert.Empty(result.DroppedClassAbilities);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("Animal Trick", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Convert_HitPointRolls_AreStoredOnTheirTicks()
    {
        var data = CreateClericData();
        data.Levels[0].HitPoints = 8;
        data.Levels[1].HitPoints = 3;

        var result = PcgConverter.Convert(data, new PcgIdMapper());

        Assert.Equal(8, result.Character.Ticks[0].Choices.HitPointsRolled);
        Assert.Equal(3, result.Character.Ticks[1].Choices.HitPointsRolled);
        Assert.Null(result.Character.Ticks[2].Choices.HitPointsRolled);
    }

    [Fact]
    public void ParseText_ExtractsMasterFollowersAndTemporaryBonuses()
    {
        const string pcg = """
            MASTER:Witch|TYPE:Animal Companion|HITDICE:2|FILE:Witch.pcg|ADJUSTMENT:-3
            FOLLOWER:Witch's companion|TYPE:Animal Companion|RACE:COMPANION ~ HAWK|HITDICE:0|FILE:Witch's companion.pcg
            TEMPBONUS:SPELL=Fox's Cunning|TBTARGET:PC|TBBONUS:STAT
            """;

        var data = PcgParser.ParseText(pcg);

        Assert.Equal("Witch", data.Master!.Name);
        Assert.Equal("Animal Companion", data.Master.Type);
        Assert.Equal(2, data.Master.HitDice);
        Assert.Equal(-3, data.Master.Adjustment);
        Assert.Equal("Witch's companion", Assert.Single(data.Followers).Name);
        Assert.Equal(0, Assert.Single(data.Followers).HitDice);
        Assert.Equal("SPELL=Fox's Cunning", Assert.Single(data.TemporaryBonuses).Split('|')[0]);
    }

    [Fact]
    public void Convert_LeadershipFollower_UsesDedicatedLinkTypeAndRecordedTier()
    {
        var data = CreateClericData();
        data.Followers.Add(new PcgFollowerEntry
        {
            Name = "Scout", Type = "Follower", Race = "Human", File = "Scout.pcg", HitDice = 3,
        });

        var link = Assert.Single(PcgConverter.Convert(data, new PcgIdMapper()).Character.CompanionLinks);

        Assert.Equal("leadership_follower", link.LinkType);
        Assert.Equal(3, link.FollowerLevel);
    }

    [Fact]
    public void Convert_Familiar_UsesFamiliarGrantingClassLevels_NotCasterLevel()
    {
        var data = CreateClericData();
        data.Followers.Add(new PcgFollowerEntry
        {
            Name = "Familiar", Type = "Familiar", Race = "Companion ~ Cat", File = "Familiar.pcg",
        });

        var link = Assert.Single(PcgConverter.Convert(data, new PcgIdMapper()).Character.CompanionLinks);
        var masterState = new CharacterState
        {
            ClassLevels = new Dictionary<string, int>
            {
                ["class:sorcerer"] = 6,
                ["class:arcane_trickster"] = 10,
                ["class:dark_temptress"] = 10,
            },
            Spellcasting = new Dictionary<string, SpellcastingState>
            {
                ["class:sorcerer"] = new() { CasterLevel = 23 }
            }
        };

        Assert.Equal("ClassLevel(wizard) + ClassLevel(sorcerer)", link.EffectiveLevelFormula.Expression);
        Assert.Equal(6, link.EffectiveLevelFormula.Evaluate(masterState));
    }

    [Theory]
    [InlineData("Familiar", "familiar")]
    [InlineData("Improved Familiar", "improved_familiar")]
    public void Convert_ImportedFamiliar_GetsUniversalFamiliarProgressionTemplate(
        string pcgenType,
        string expectedLinkType)
    {
        var data = CreateClericData();
        data.Master = new PcgMasterEntry
        {
            Name = "Master",
            Type = pcgenType,
            File = "Master.pcg",
        };
        data.Templates.Add(new PcgTemplateEntry
        {
            Name = "Familiar Race Change",
            IsInternal = true,
        });

        var character = PcgConverter.Convert(data, new PcgIdMapper()).Character;

        Assert.Equal(expectedLinkType, character.CompanionOrigin!.LinkType);
        Assert.Equal(
            new[] { "template:familiar_standard" },
            character.TemplateIds);
    }

    [Theory]
    [InlineData("Animal Companion", "animal_companion", "template:animal_companion_standard")]
    [InlineData("Familiar", "familiar", "template:familiar_standard")]
    [InlineData("Improved Familiar", "improved_familiar", "template:familiar_standard")]
    [InlineData("Special Mount", "special_mount", "template:special_mount_standard")]
    public void Convert_ImportedCompanion_GetsMatchingProgressionTemplate(
        string pcgenType, string expectedLinkType, string expectedTemplate)
    {
        var data = CreateClericData();
        data.Master = new PcgMasterEntry
        {
            Name = "Master",
            Type = pcgenType,
            File = "Master.pcg",
        };

        var character = PcgConverter.Convert(data, new PcgIdMapper()).Character;

        Assert.Equal(expectedLinkType, character.CompanionOrigin!.LinkType);
        Assert.Contains(expectedTemplate, character.TemplateIds);
    }

    [Fact]
    public void IdMapper_MapsCompositeArcaneSorcererSource()
    {
        Assert.Equal("class:sorcerer", new PcgIdMapper().MapClass("Sorcerer/Cleric (Arcane)"));
    }

    [Fact]
    public void Convert_MasterFollowersAndTemporaryBonuses_AreExplicit()
    {
        var data = CreateClericData();
        data.Master = new PcgMasterEntry
        {
            Name = "Witch",
            Type = "Animal Companion",
            File = "Witch.pcg",
        };
        data.Followers.Add(new PcgFollowerEntry
        {
            Name = "Witch's companion",
            Type = "Animal Companion",
            Race = "Companion ~ Hawk",
            File = "Witch's companion.pcg",
        });
        data.TemporaryBonuses.Add("SPELL=Fox's Cunning|TBTARGET:PC");

        var result = PcgConverter.Convert(data, new PcgIdMapper());

        Assert.Equal("animal_companion", result.Character.CompanionOrigin!.LinkType);
        Assert.Equal("witch", result.Character.CompanionOrigin.MasterCharacterId);
        var link = Assert.Single(result.Character.CompanionLinks);
        Assert.Equal("animal_companion", link.LinkType);
        Assert.Equal("witch-s_companion", link.CompanionId);
        Assert.Equal("race:companion_hawk", link.SelectedSpecies);
        Assert.Contains("SPELL=Fox's Cunning", result.IgnoredTemporaryBonuses);
        Assert.Contains(result.Warnings, warning => warning.Contains("temporary modifier"));
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
    public void Convert_RacialHdPrestat_IsNotImportedOrSubtracted()
    {
        var data = new PcgCharacterData
        {
            CharacterName = "Fey Test",
            Race = "Pixie",
            BaseStats = new() { ["STR"] = 10, ["DEX"] = 10, ["CON"] = 10, ["INT"] = 10, ["WIS"] = 11, ["CHA"] = 10 },
            Classes = new() { new PcgClassEntry { Name = "Fey", Level = 4 } },
            Levels = new()
            {
                new PcgLevelEntry { ClassName = "Fey", ClassLevel = 1 },
                new PcgLevelEntry { ClassName = "Fey", ClassLevel = 2 },
                new PcgLevelEntry { ClassName = "Fey", ClassLevel = 3 },
                new PcgLevelEntry { ClassName = "Fey", ClassLevel = 4, AbilityIncrease = "WIS" },
            },
        };

        var result = PcgConverter.Convert(data, new PcgIdMapper());

        Assert.Null(result.Character.Ticks[3].Choices.AbilityIncrease);
        Assert.Equal(11, result.Character.BaseAbilityScores.WIS);
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
    public void Convert_SpellsKnown_ImportsOnlyKnownSpellRows()
    {
        var data = CreateClericData();
        data.Classes = new() { new PcgClassEntry { Name = "Sorcerer", Level = 3 } };
        data.Levels = Enumerable.Range(1, 3)
            .Select(level => new PcgLevelEntry { ClassName = "Sorcerer", ClassLevel = level })
            .ToList();
        data.Domains.Clear();
        data.Spells = new()
        {
            new PcgSpellEntry { Name = "Magic Missile", ClassName = "Sorcerer", SpellLevel = 1, Book = "Known Spells" },
            new PcgSpellEntry { Name = "Shield", ClassName = "Sorcerer", SpellLevel = 1, Book = "Known Spells" },
            new PcgSpellEntry { Name = "Mage Armor", ClassName = "Sorcerer", SpellLevel = 1, Book = "Prepared Spells" },
        };

        var registry = TestContentHelper.LoadAllPacks();
        var result = PcgConverter.Convert(data, new PcgIdMapper(), registry);

        var spells = result.Character.Ticks[^1].Choices.SpellSelections;
        Assert.NotNull(spells);
        Assert.Equal(2, spells.Count);
        Assert.Contains(spells, s => s.ClassId == "class:sorcerer" && s.SpellId == "spell:magic_missile" && s.SpellLevel == 1);
        Assert.Contains(spells, s => s.ClassId == "class:sorcerer" && s.SpellId == "spell:shield" && s.SpellLevel == 1);
        Assert.DoesNotContain(spells, s => s.SpellId == "spell:mage_armor");
    }

    [Fact]
    public void Convert_Wizard_ImportsSpellbookButNotAvailableOrPreparedRows()
    {
        var data = CreateClericData();
        data.Classes = new() { new PcgClassEntry { Name = "Wizard", Level = 3 } };
        data.Levels = Enumerable.Range(1, 3)
            .Select(level => new PcgLevelEntry { ClassName = "Wizard", ClassLevel = level })
            .ToList();
        data.Domains.Clear();
        data.Spells = new()
        {
            // PCGen calls the wizard's entire available class list "Known Spells".
            new PcgSpellEntry { Name = "Magic Missile", ClassName = "Wizard", SpellLevel = 1, Book = "Known Spells" },
            new PcgSpellEntry { Name = "Shield", ClassName = "Wizard", SpellLevel = 1, Book = "Spellbook (Wizard's/Blank)" },
            new PcgSpellEntry { Name = "Invisibility", ClassName = "Wizard", SpellLevel = 2, Book = "Prepared Spells" },
        };

        var registry = TestContentHelper.LoadAllPacks();
        var result = PcgConverter.Convert(data, new PcgIdMapper(), registry);

        var spell = Assert.Single(result.Character.Ticks[^1].Choices.SpellSelections!);
        Assert.Equal("class:wizard", spell.ClassId);
        Assert.Equal("spell:shield", spell.SpellId);
        Assert.Equal(1, spell.SpellLevel);
    }

    [Fact]
    public void Convert_FullListCaster_DoesNotPersistDailyPreparation()
    {
        var data = CreateClericData();
        data.Spells = new()
        {
            new PcgSpellEntry { Name = "Bless", ClassName = "Cleric", SpellLevel = 1, Book = "Prepared Spells" },
            new PcgSpellEntry { Name = "Command", ClassName = "Cleric", SpellLevel = 1, Book = "Known Spells" },
        };

        var registry = TestContentHelper.LoadAllPacks();
        var result = PcgConverter.Convert(data, new PcgIdMapper(), registry);

        Assert.Null(result.Character.Ticks[^1].Choices.SpellSelections);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("Bless") || warning.Contains("Command"));
    }

    [Fact]
    public void Convert_UnknownSelectedSpell_IsWarnedAndDropped()
    {
        var data = CreateClericData();
        data.Classes = new() { new PcgClassEntry { Name = "Sorcerer", Level = 1 } };
        data.Levels = new() { new PcgLevelEntry { ClassName = "Sorcerer", ClassLevel = 1 } };
        data.Domains.Clear();
        data.Spells = new()
        {
            new PcgSpellEntry { Name = "Totally Made Up Spell", ClassName = "Sorcerer", SpellLevel = 1, Book = "Known Spells" },
        };

        var registry = TestContentHelper.LoadAllPacks();
        var result = PcgConverter.Convert(data, new PcgIdMapper(), registry);

        Assert.Null(result.Character.Ticks[^1].Choices.SpellSelections);
        Assert.Contains("Totally Made Up Spell", result.DroppedSpells);
        Assert.Contains(result.Warnings, warning => warning.Contains("Totally Made Up Spell") && warning.Contains("no engine mapping"));
        Assert.Contains("1 spell(s) missing", result.Summary);
    }

    [Fact]
    public void Convert_SelectedSpellWithModeledRacialSource_IsImported()
    {
        var data = CreateClericData();
        data.Spells = new()
        {
            new PcgSpellEntry { Name = "Wish", ClassName = "Red Dragon", SpellLevel = 9, Book = "Known Spells" },
        };

        var registry = TestContentHelper.LoadAllPacks();
        var result = PcgConverter.Convert(data, new PcgIdMapper(), registry);

        var selection = Assert.Single(result.Character.Ticks[^1].Choices.SpellSelections!);
        Assert.Equal("racial_hd:red_dragon", selection.ClassId);
        Assert.Equal("spell:wish", selection.SpellId);
        Assert.Equal(9, selection.SpellLevel);
        Assert.Empty(result.DroppedSpells);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("no modeled spellcasting"));
    }

    [Fact]
    public void Convert_SelectedSpells_AreDeduplicated()
    {
        var data = CreateClericData();
        data.Classes = new() { new PcgClassEntry { Name = "Sorcerer", Level = 1 } };
        data.Levels = new() { new PcgLevelEntry { ClassName = "Sorcerer", ClassLevel = 1 } };
        data.Domains.Clear();
        data.Spells = new()
        {
            new PcgSpellEntry { Name = "Magic Missile", ClassName = "Sorcerer", SpellLevel = 1, Book = "Known Spells" },
            new PcgSpellEntry { Name = "Magic Missile", ClassName = "Sorcerer", SpellLevel = 1, Book = "Known Spells" },
        };

        var registry = TestContentHelper.LoadAllPacks();
        var result = PcgConverter.Convert(data, new PcgIdMapper(), registry);

        Assert.Single(result.Character.Ticks[^1].Choices.SpellSelections!);
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
    public void Convert_Languages_BecomeVisibleSourceLanguageInputs()
    {
        var data = PcgParser.ParseText(PcgWithLanguages(LanguageLine), "polyglot.pcg");

        var result = PcgConverter.Convert(data, new PcgIdMapper());

        Assert.Equal(
            new[] { "abyssal", "auran", "celestial", "common", "draconic", "infernal" },
            result.Character.SourceLanguageIds);
        Assert.Empty(result.Character.PermanentEvents);
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
        // SourceLanguageIds is persisted input, so an imported character keeps its languages
        // after the first save/load and the Builder can show them explicitly.
        var data = PcgParser.ParseText(PcgWithLanguages(LanguageLine), "polyglot.pcg");
        var character = PcgConverter.Convert(data, new PcgIdMapper()).Character;

        var json = System.Text.Json.JsonSerializer.Serialize(character, JsonOptions.Default);
        var reloaded = System.Text.Json.JsonSerializer.Deserialize<Character>(json, JsonOptions.Default)!;

        Assert.Contains("draconic", reloaded.SourceLanguageIds);
        Assert.Equal(6, reloaded.SourceLanguageIds.Count);
    }

    // --- Alternate class features that decide the driver -------------------------

    private static PcgCharacterData BardData(string variantKey) => new()
    {
        CharacterName = $"Test {variantKey}",
        Race = "Human",
        Alignment = "CN",
        BaseStats = new() { ["CHA"] = 16 },
        Classes = new() { new PcgClassEntry { Name = "Bard", Level = 2 } },
        Levels = new()
        {
            new PcgLevelEntry { ClassName = "Bard", ClassLevel = 1 },
            new PcgLevelEntry { ClassName = "Bard", ClassLevel = 2 },
        },
        Skills = new() { new PcgSkillEntry { Name = "Perform", Ranks = 5.0, BoughtClass = "Bard" } },
        Spells = new()
        {
            new PcgSpellEntry
            {
                Name = "Charm Person", ClassName = "Bard", SpellLevel = 1, Book = "Known Spells",
            },
        },
        ClassAbilities = new() { new PcgClassAbilityEntry { Category = "ACF", Key = variantKey } },
    };

    /// <summary>
    /// The .pcg still says <c>CLASS:Bard</c> — only the ACF row distinguishes the variant — so
    /// everything filed under the class name has to follow the swap, spell rows included.
    /// </summary>
    [Fact]
    public void Convert_DruidLikeBardAcf_SelectsTheVariantDriverForEverythingNamingTheClass()
    {
        var registry = TestContentHelper.LoadAllPacks();

        var result = PcgConverter.Convert(
            BardData("Bard Variant ~ Druid-like Bard"), new PcgIdMapper(), registry);

        Assert.All(result.Character.Ticks, tick => Assert.Equal("class:druid_like_bard", tick.DriverId));
        Assert.All(
            result.Character.Ticks.SelectMany(t => t.Choices.SpellSelections ?? new()),
            spell => Assert.Equal("class:druid_like_bard", spell.ClassId));
        Assert.Empty(result.DroppedClassAbilities);
    }

    /// <summary>"Regular Bard" selects no variant, and must not be reported as an unmatched pick.</summary>
    [Fact]
    public void Convert_RegularBardAcf_KeepsTheBaseClassAndDoesNotWarn()
    {
        var registry = TestContentHelper.LoadAllPacks();

        var result = PcgConverter.Convert(
            BardData("Bard Variant ~ Regular Bard"), new PcgIdMapper(), registry);

        Assert.All(result.Character.Ticks, tick => Assert.Equal("class:bard", tick.DriverId));
        Assert.Empty(result.DroppedClassAbilities);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Bard Variant"));
    }

    /// <summary>
    /// PCGen records the character's gender and the engine used to throw it away. It is free text
    /// on the way through: the corpus only ever says Female, Male or Neuter, but a value outside
    /// that set is a description of someone's character, not an error to normalise away.
    /// </summary>
    [Theory]
    [InlineData("GENDER:Male", "Male")]
    [InlineData("GENDER:Neuter", "Neuter")]
    [InlineData("GENDER:Nonbinary", "Nonbinary")]
    [InlineData("GENDER:  Female  ", "Female")]
    [InlineData("", null)]
    public void Convert_Gender_IsCarriedFromTheSourceVerbatim(string genderLine, string? expected)
    {
        var data = PcgParser.ParseText(
            $"CHARACTERNAME:Test\nRACE:Human\nALIGN:N\n{genderLine}\nSTAT:STR|SCORE:10\n", "g.pcg");

        Assert.Equal(expected, PcgConverter.Convert(data, new PcgIdMapper()).Character.Gender);
    }

    /// <summary>Clone is what the builder edits through, so a dropped field vanishes on the first edit.</summary>
    [Fact]
    public void Clone_KeepsGender()
    {
        Assert.Equal("Neuter", new Character { Gender = "Neuter" }.Clone().Gender);
    }

    private static PcgCharacterData DruidData(string? substitutionClass) => new()
    {
        CharacterName = "Test Druid",
        Race = "Human",
        Alignment = "N",
        BaseStats = new() { ["WIS"] = 16 },
        Classes = new() { new PcgClassEntry { Name = "Druid", Level = 2 } },
        Levels = new()
        {
            new PcgLevelEntry { ClassName = "Druid", ClassLevel = 1, SubstitutionClass = substitutionClass },
            new PcgLevelEntry { ClassName = "Druid", ClassLevel = 2 },
        },
    };

    /// <summary>
    /// A substitution class rides on the level row rather than the CLASS row, so the class name
    /// alone cannot resolve the driver — the same problem the bard's ACF has, from a different tag.
    /// </summary>
    [Fact]
    public void Convert_SubstitutionLevel_SelectsTheSubstitutionClassDriver()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        if (registry.GetAllDrivers().All(d => d.Id != "class:elemental_druid"))
            return; // private packs unavailable

        var result = PcgConverter.Convert(
            DruidData("Elemental Druid Option"), new PcgIdMapper(), registry);

        Assert.All(result.Character.Ticks, tick => Assert.Equal("class:elemental_druid", tick.DriverId));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Substitution class"));
    }

    /// <summary>An unknown substitution builds the base class, and says it did.</summary>
    [Fact]
    public void Convert_UnmappedSubstitutionLevel_BuildsTheBaseClassAndWarns()
    {
        var registry = TestContentHelper.LoadAllPacks();

        var result = PcgConverter.Convert(
            DruidData("Some Unextracted Druid Option"), new PcgIdMapper(), registry);

        Assert.All(result.Character.Ticks, tick => Assert.Equal("class:druid", tick.DriverId));
        Assert.Contains(result.Warnings,
            w => w.Contains("Substitution class 'Some Unextracted Druid Option'"));
    }

    /// <summary>
    /// The import regression converts a whole corpus through one <see cref="PcgIdMapper"/>, so a
    /// variant resolved for one character must not follow the mapper onto the next.
    /// </summary>
    [Fact]
    public void Convert_ClassSelectingAcf_DoesNotLeakAcrossCharactersSharingAMapper()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var mapper = new PcgIdMapper();

        PcgConverter.Convert(BardData("Bard Variant ~ Druid-like Bard"), mapper, registry);
        var plainBard = PcgConverter.Convert(
            BardData("Bard Variant ~ Regular Bard"), mapper, registry);

        Assert.All(plainBard.Character.Ticks, tick => Assert.Equal("class:bard", tick.DriverId));
    }
}
