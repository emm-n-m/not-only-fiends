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
        Assert.Contains(catalog.Races, race => race.Id == "human");
        Assert.Contains(catalog.Drivers, driver => driver.Id == "class:fighter");
        Assert.True(catalog.SpellCount > 0);
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
        Assert.Contains(response.QualifiedFeats, feat => feat.Id == "weapon_focus");
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
                RaceId = "human",
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

    private static Character CreateFirstLevelHumanFighter() => new()
    {
        Name = "Brynn Ironfist",
        RaceId = "human",
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
                    FeatIds = new List<string> { "power_attack", "cleave", "improved_initiative" }
                }
            }
        }
    };
}
