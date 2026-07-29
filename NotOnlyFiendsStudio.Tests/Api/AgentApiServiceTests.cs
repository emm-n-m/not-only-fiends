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
        Assert.True(catalog.SpellCount > 0);
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
