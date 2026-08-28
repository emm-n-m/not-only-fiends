using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NotOnlyFiendsFeed.Services;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests.Api;

public sealed class CharacterMutationPersistenceTests : IDisposable
{
    private readonly string _charactersPath = Path.Combine(
        Path.GetTempPath(),
        $"not-only-fiends-api-tests-{Guid.NewGuid():N}");
    private readonly CharacterStore _store;
    private readonly AgentApiService _api;

    public CharacterMutationPersistenceTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Content:BundledPacksPath"] = TestContentHelper.GetPacksPath(),
                ["Content:CharactersPath"] = _charactersPath
            })
            .Build();
        var content = new ServerContentService(
            configuration,
            NullLogger<ServerContentService>.Instance);
        _store = new CharacterStore(content);
        _api = new AgentApiService(content, _store);
    }

    [Fact]
    public void AppendTickPersistsTheRecalculatedSheet()
    {
        _store.Create(CreateHuman(), "mutation-test");

        var response = _api.AppendTick(
            "mutation-test",
            new Tick { DriverId = "class:fighter" });
        var persisted = _store.Get("mutation-test");

        Assert.Equal(1, response.Sheet.TotalHD);
        Assert.NotNull(persisted.Sheet);
        Assert.Equal(1, persisted.Sheet.TotalHD);
    }

    [Fact]
    public void EvaluationFailureDoesNotPersistTheMutation()
    {
        var character = CreateHuman();
        character.Ticks.Add(new Tick { DriverId = "class:fighter" });
        _api.EvaluateAndEnvelope("mutation-test", character);
        _store.Create(character, "mutation-test");

        var invalidEvent = new PermanentEvent
        {
            BeforeTick = 0,
            Permabuffs = new List<Permabuff>
            {
                new GrantTypedBonus
                {
                    Target = BonusTarget.AbilityStr,
                    BonusType = BonusType.Untyped,
                    Value = new Formula("1 +")
                }
            }
        };

        Assert.Throws<FormulaException>(() => _api.AppendEvent("mutation-test", invalidEvent));

        var persisted = _store.Get("mutation-test");
        Assert.Empty(persisted.PermanentEvents);
        Assert.Equal(1, persisted.Sheet?.TotalHD);
    }

    public void Dispose()
    {
        if (Directory.Exists(_charactersPath))
            Directory.Delete(_charactersPath, recursive: true);
    }

    private static Character CreateHuman() => new()
    {
        Name = "Mutation Test",
        RaceId = "race:human",
        BaseAbilityScores = new AbilityScoreSet
        {
            STR = 14, DEX = 12, CON = 12, INT = 10, WIS = 10, CHA = 10
        }
    };

    [Fact]
    public void AppendTick_ReportsPerFeatOutcomesAndNewWarnings()
    {
        _store.Create(CreateHuman(), "mutation-test");

        // Human fighter HD 1: standard + human bonus take the two general feats; the third
        // general feat cannot use the fighter-bonus slot and is dropped with only a warning.
        var response = _api.AppendTick("mutation-test", new Tick
        {
            DriverId = "class:fighter",
            Choices = new TickChoices
            {
                FeatIds = new List<string>
                {
                    "feat:iron_will",
                    "feat:skill_focus_skill:concentration",
                    "feat:lightning_reflexes"
                }
            }
        });

        Assert.NotNull(response.FeatOutcomes);
        var outcomes = response.FeatOutcomes!;
        Assert.Equal(3, outcomes.Count);

        Assert.True(outcomes[0].Applied);
        Assert.Equal("feat:iron_will", outcomes[0].CanonicalId);

        // Legacy dialect resolves to the canonical variant id on the receipt.
        Assert.True(outcomes[1].Applied);
        Assert.Equal("feat:skill_focus:concentration", outcomes[1].CanonicalId);

        Assert.False(outcomes[2].Applied);
        Assert.Contains("no available feat slot", outcomes[2].Reason);

        Assert.NotNull(response.NewWarnings);
        Assert.Contains(response.NewWarnings!, warning =>
            warning.Message.Contains("feat:lightning_reflexes") && warning.Message.Contains("dropped"));
    }

    [Fact]
    public void SimulateTick_ReportsOutcomesWithoutPersisting()
    {
        _store.Create(CreateHuman(), "mutation-test");

        var response = _api.SimulateTick("mutation-test", new Tick
        {
            DriverId = "class:fighter",
            Choices = new TickChoices { FeatIds = new List<string> { "feat:iron_will" } }
        });

        Assert.NotNull(response.FeatOutcomes);
        Assert.True(Assert.Single(response.FeatOutcomes!).Applied);
        Assert.Empty(_store.Get("mutation-test").Ticks);
    }

    [Fact]
    public void ReplaceCharacter_ReportsTheWarningsTheSaveIntroduced()
    {
        var character = CreateHuman();
        character.Ticks.Add(new Tick { DriverId = "class:fighter" });
        _store.Create(character, "mutation-test");

        var repaired = _store.Get("mutation-test");
        repaired.Ticks[0].Choices.FeatIds = new List<string> { "feat:unheard_of" };
        var response = _api.ReplaceCharacter("mutation-test", repaired);

        Assert.NotNull(response.NewWarnings);
        Assert.Contains(response.NewWarnings!, warning => warning.Message.Contains("feat:unheard_of"));
        Assert.NotNull(response.Warnings);
    }
}
