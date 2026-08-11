using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NotOnlyFiendsFeed.Contracts;
using NotOnlyFiendsFeed.Services;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests.Api;

public class AgentApiServiceTests
{
    private static readonly Lazy<AgentApiService> SharedService = new(() =>
    {
        var config = new ConfigurationBuilder().Build();
        var logger = NullLogger<ServerContentService>.Instance;
        var contentService = new ServerContentService(config, logger);
        return new AgentApiService(contentService, new CharacterStore(contentService));
    });

    [Fact]
    public void CatalogIncludesBundledCoreContent()
    {
        var catalog = SharedService.Value.GetCatalog();

        Assert.Contains(catalog.LoadedPacks, pack => pack.Id == "srd_core");
        Assert.Contains(catalog.Races, race => race.Id == "race:human");
        Assert.Contains(catalog.Drivers, driver => driver.Id == "class:fighter");
        Assert.Equal(3, catalog.Equipment.Single(item => item.Id == "weapon:frost_brand").EnhancementBonus);
        Assert.True(catalog.SpellCount > 0);
    }

    [Fact]
    public void SkillsAndLanguagesCanBeFilteredByQuery()
    {
        var service = SharedService.Value;

        var skills = service.GetSkills("craft_alchemy").ToList();
        var languages = service.GetLanguages("under").ToList();

        var skill = Assert.Single(skills);
        Assert.Equal("skill:craft_alchemy", skill.Id);
        Assert.Equal("Undercommon", Assert.Single(languages).Name);

        Assert.DoesNotContain(service.GetSkills("craft_alchemy"), skill => skill.Id == "skill:appraise");
        Assert.DoesNotContain(service.GetLanguages("under"), language => language.Name == "Common");
    }

    [Fact]
    public void RaceCatalogExposesPlayerCharacterSanctioning()
    {
        var races = SharedService.Value.GetRaces().ToList();

        // The API must make the same distinction the builder's picker does — otherwise an agent
        // sees one flat list and cannot tell a PC race from a monster entry.
        var human = races.Single(r => r.Id == "race:human");
        Assert.True(human.IsPcRace);
        Assert.Equal(0, human.LevelAdjustment);

        // LA +0 is a real price, not a missing one: the flag must track "was it printed at all".
        Assert.All(races, r => Assert.Equal(r.LevelAdjustment.HasValue, r.IsPcRace));
    }

    [Fact]
    public void UnsanctionedRaceStillSerializesItsNullLevelAdjustment()
    {
        // The app configures WhenWritingNull globally, which would drop the key for exactly the
        // races the PC/non-PC distinction is about — leaving a caller reading `levelAdjustment`
        // with a missing field instead of an answer.
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        var json = System.Text.Json.JsonSerializer.Serialize(
            new RaceSummaryDto { Id = "race:x", Name = "X", LevelAdjustment = null, IsPcRace = false },
            options);

        Assert.Contains("\"levelAdjustment\":null", json);
    }

    [RequiresPrivatePacksFact]
    public void RaceCatalogMarksUnsanctionedRacesInsteadOfHidingThem()
    {
        var config = new ConfigurationBuilder().Build();
        var contentService = new ServerContentService(config, NullLogger<ServerContentService>.Instance);
        var races = new AgentApiService(contentService, new CharacterStore(contentService))
            .GetRaces().ToList();

        var unsanctioned = races.Where(r => !r.IsPcRace).ToList();
        Assert.NotEmpty(unsanctioned);
        Assert.All(unsanctioned, r => Assert.Null(r.LevelAdjustment));
    }

    [Fact]
    public void EvaluateReturnsSheetAndQualifiedFeats()
    {
        var response = SharedService.Value.Evaluate(new EvaluateCharacterRequest
        {
            Character = CreateFirstLevelHumanFighter()
        });

        Assert.Equal(1, response.State.TotalHD);
        Assert.Equal(1, response.Sheet.TotalHD);
        Assert.Contains(response.QualifiedFeats, feat => feat.Id == "feat:weapon_focus");
        Assert.Empty(response.PendingChoices.FeatChoices);
    }

    [Fact]
    public void EvaluateExposesWizardSpellbookChoicesAndCapacity()
    {
        var character = new Character
        {
            Name = "Wizard Spellbook API Test",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 18, WIS = 10, CHA = 10
            },
            Ticks =
            {
                new Tick
                {
                    DriverId = "class:wizard",
                    Choices = new TickChoices
                    {
                        SpellSelections = new List<SpellSelection>
                        {
                            new() { ClassId = "class:wizard", SpellLevel = 1, SpellId = "spell:magic_missile" }
                        }
                    }
                }
            }
        };

        var response = SharedService.Value.Evaluate(new EvaluateCharacterRequest { Character = character });

        var firstLevel = Assert.Single(response.PendingChoices.SpellChoices,
            group => group.ClassId == "class:wizard" && group.SpellLevel == 1);
        Assert.Equal(7, firstLevel.SpellbookLimit); // 3 + INT 18 modifier 4
        Assert.Equal(1, firstLevel.SpellbookUsed);
        Assert.Equal(6, firstLevel.SpellbookRemaining);
        Assert.Contains("spell:magic_missile", firstLevel.ExistingSelections);
        Assert.DoesNotContain(firstLevel.Options!, spell => spell.Id == "spell:magic_missile");
        Assert.True(firstLevel.OptionCount > 0);
    }

    [Fact]
    public void EvaluateExposesPreparedSpellChoicesAndSlotKinds()
    {
        var response = SharedService.Value.Evaluate(new EvaluateCharacterRequest
        {
            Character = new Character
            {
                Name = "API Prepared Cleric Test",
                RaceId = "race:human",
                BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 16, CHA = 10 },
                Ticks = new List<Tick> { new() { DriverId = "class:cleric" } }
            }
        });

        var normal = Assert.Single(response.PendingChoices.PreparedSpellChoices,
            group => group.ClassId == "class:cleric"
                && group.SpellLevel == 1
                && group.SlotKind == PreparedSpellSlotKind.Normal);
        Assert.Equal(2, normal.SlotCount); // one base slot plus one WIS bonus slot
        Assert.Equal(0, normal.PreparedCount);
        Assert.True(normal.OptionCount > 0);
    }

    [Fact]
    public void NextStepBuildsDriverPreviewWithPendingChoices()
    {
        var response = SharedService.Value.GetNextStep(new NextStepRequest
        {
            Character = new Character
            {
                Name = "Agent Test",
                RaceId = "race:human",
                BaseAbilityScores = new AbilityScoreSet
                {
                    STR = 16,
                    DEX = 14,
                    CON = 14,
                    INT = 10,
                    WIS = 12,
                    CHA = 8
                }
            },
            CandidateDriverIds = new List<string> { "class:fighter" }
        });

        var fighter = Assert.Single(response.DriverPreviews);
        Assert.Equal("class:fighter", fighter.Driver.Id);
        Assert.Equal(1, fighter.Preview.TotalHd);
        Assert.NotEmpty(fighter.PendingChoices.FeatChoices);
    }

    [Fact]
    public void NextStep_AbilityIncreaseIsDriverAwareForRacialHd()
    {
        var character = new Character
        {
            Name = "Pixie Test",
            RaceId = "race:pixie",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks =
            {
                new Tick { DriverId = "racial_hd:fey" },
                new Tick { DriverId = "racial_hd:fey" },
                new Tick { DriverId = "racial_hd:fey" },
            }
        };

        var response = SharedService.Value.GetNextStep(new NextStepRequest
        {
            Character = character,
            CandidateDriverIds = new List<string> { "racial_hd:fey", "class:bard" }
        });

        Assert.True(response.AbilityIncreaseDue);
        Assert.False(response.DriverPreviews.Single(p => p.Driver.Id == "racial_hd:fey").AbilityIncreaseDue);
        Assert.True(response.DriverPreviews.Single(p => p.Driver.Id == "class:bard").AbilityIncreaseDue);
    }

    [Theory]
    [InlineData(OptionDetail.None)]
    [InlineData(OptionDetail.Ids)]
    [InlineData(OptionDetail.Full)]
    public void NextStepOptionDetailControlsOptionPayload(OptionDetail detail)
    {
        var response = SharedService.Value.GetNextStep(
            new NextStepRequest
            {
                Character = CreateLevelZeroHuman(),
                CandidateDriverIds = new List<string> { "class:fighter" }
            },
            detail);

        var group = Assert.Single(
            Assert.Single(response.DriverPreviews).PendingChoices.FeatChoices,
            choice => choice.SlotType == "standard");

        // The count is available at every detail level, so an agent can always tell
        // how many options exist without paying for the list.
        Assert.True(group.OptionCount > 0);

        switch (detail)
        {
            case OptionDetail.None:
                Assert.Null(group.OptionIds);
                Assert.Null(group.Options);
                break;
            case OptionDetail.Ids:
                Assert.Equal(group.OptionCount, Assert.IsType<List<string>>(group.OptionIds).Count);
                Assert.Null(group.Options);
                break;
            case OptionDetail.Full:
                Assert.Null(group.OptionIds);
                Assert.Equal(group.OptionCount, Assert.IsType<List<FeatSummaryDto>>(group.Options).Count);
                break;
        }
    }

    [Fact]
    public void NextStepOptionDetailIdsMatchFullOptions()
    {
        var request = new NextStepRequest
        {
            Character = CreateLevelZeroHuman(),
            CandidateDriverIds = new List<string> { "class:fighter" }
        };

        var ids = SharedService.Value.GetNextStep(request, OptionDetail.Ids)
            .DriverPreviews.Single().PendingChoices.FeatChoices;
        var full = SharedService.Value.GetNextStep(request, OptionDetail.Full)
            .DriverPreviews.Single().PendingChoices.FeatChoices;

        Assert.Equal(full.Count, ids.Count);
        foreach (var (idGroup, fullGroup) in ids.Zip(full))
        {
            Assert.Equal(fullGroup.SlotType, idGroup.SlotType);
            Assert.Equal(fullGroup.Options!.Select(feat => feat.Id), idGroup.OptionIds!);
        }
    }

    [Theory]
    [InlineData(OptionDetail.None)]
    [InlineData(OptionDetail.Full)]
    public void NextStepOptionDetailAlsoGovernsDomainAndClassFeatureOptions(OptionDetail detail)
    {
        // Cleric contributes domain choices, wizard contributes a familiar class feature.
        var response = SharedService.Value.GetNextStep(
            new NextStepRequest
            {
                Character = CreateLevelZeroHuman(),
                CandidateDriverIds = new List<string> { "class:cleric", "class:wizard" }
            },
            detail);

        var domains = response.DriverPreviews
            .SelectMany(preview => preview.PendingChoices.DomainChoices)
            .ToList();
        var classFeatures = response.DriverPreviews
            .SelectMany(preview => preview.PendingChoices.ClassFeatureChoices)
            .ToList();

        Assert.NotEmpty(domains);
        Assert.NotEmpty(classFeatures);

        foreach (var group in domains)
        {
            Assert.True(group.OptionCount > 0);
            Assert.Equal(detail == OptionDetail.Full, group.Options != null);
        }

        foreach (var group in classFeatures)
        {
            Assert.True(group.OptionCount > 0);
            Assert.Equal(detail == OptionDetail.Full, group.Options != null);
        }
    }

    [RequiresPrivatePacksFact]
    public void NextStep_RestrictedDomainGroupOnlyExposesLegalDomains()
    {
        var response = SharedService.Value.GetNextStep(new NextStepRequest
        {
            Character = new Character
            {
                RaceId = "race:human",
                BaseAbilityScores = new AbilityScoreSet
                {
                    STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 16, CHA = 10
                },
                Ticks = new List<Tick> { new() { DriverId = "class:elemental_druid" } }
            }
        });

        var group = Assert.Single(response.CurrentPendingChoices.DomainChoices,
            choice => choice.OwnerClassId == "class:elemental_druid");
        var optionIds = group.Options!.Select(option => option.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new[] { "domain:air", "domain:earth", "domain:fire", "domain:plant", "domain:water" },
            optionIds.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void NextStepCurrentPendingChoicesExposeFamiliarSelectionKeyAndOptions()
    {
        var character = CreateLevelZeroHuman();
        character.Ticks.Add(new Tick { DriverId = "class:wizard", Choices = new TickChoices() });

        var response = SharedService.Value.GetNextStep(new NextStepRequest { Character = character });
        var familiar = Assert.Single(response.CurrentPendingChoices.ClassFeatureChoices,
            group => group.FeatureType == "class_feature:familiar_options");

        Assert.Equal("Familiar Options", familiar.FeatureName);
        Assert.Contains(familiar.Options!, option => option.Id == "race:companion_bat");
        // FeatureType is the exact TickChoices.ClassFeatureChoices key used to submit the pick.
        Assert.Contains("class_feature:familiar_options", familiar.FeatureType);
    }

    [Fact]
    public void NextStepExposesPlanarRangerCompanionTemplateChoices()
    {
        var character = CreateLevelZeroHuman();
        character.Alignment = Alignment.N;
        character.Ticks = Enumerable.Range(0, 4)
            .Select(_ => new Tick { DriverId = "class:planar_ranger", Choices = new TickChoices() })
            .ToList();

        var response = SharedService.Value.GetNextStep(new NextStepRequest { Character = character });
        var templates = Assert.Single(response.CurrentPendingChoices.CompanionTemplateChoices,
            group => group.LinkType == "animal_companion");

        Assert.Equal("companionTemplateChoices[animal_companion]", templates.ChoiceKey);
        Assert.Contains(templates.Options, option => option.Id == "template:celestial");
        Assert.Contains(templates.Options, option => option.Id == "template:fiendish");
    }

    [Fact]
    public void NextStepCurrentPendingChoicesStayFullyPopulated()
    {
        // optionDetail applies only to the driver previews, which repeat their options
        // per candidate. The caller's own pending choices describe a single state and
        // are what actually has to be filled, so they keep full options regardless.
        var response = SharedService.Value.GetNextStep(
            new NextStepRequest
            {
                Character = CreateFirstLevelHumanFighterWithOpenSlot(),
                CandidateDriverIds = new List<string> { "class:fighter" }
            },
            OptionDetail.None);

        Assert.NotEmpty(response.CurrentPendingChoices.FeatChoices);
        foreach (var group in response.CurrentPendingChoices.FeatChoices)
        {
            Assert.NotNull(group.Options);
            Assert.Equal(group.OptionCount, group.Options!.Count);
        }
    }

    private static Character CreateLevelZeroHuman() => new()
    {
        Name = "Agent Test",
        RaceId = "race:human",
        BaseAbilityScores = new AbilityScoreSet
        {
            STR = 16,
            DEX = 14,
            CON = 14,
            INT = 10,
            WIS = 12,
            CHA = 8
        }
    };

    /// <summary>A fighter who has not spent the feats their 1st HD granted.</summary>
    private static Character CreateFirstLevelHumanFighterWithOpenSlot()
    {
        var character = CreateLevelZeroHuman();
        character.Ticks.Add(new Tick { DriverId = "class:fighter", Choices = new TickChoices() });
        return character;
    }

    private static Character CreateFirstLevelHumanFighter() => new()
    {
        Name = "Brynn Ironfist",
        RaceId = "race:human",
        BaseAbilityScores = new AbilityScoreSet
        {
            STR = 16,
            DEX = 14,
            CON = 14,
            INT = 10,
            WIS = 12,
            CHA = 8
        },
        Ticks =
        {
            new Tick
            {
                DriverId = "class:fighter",
                Choices = new TickChoices
                {
                    FeatIds = new List<string> { "feat:power_attack", "feat:cleave", "feat:improved_initiative" }
                }
            }
        }
    };
}
