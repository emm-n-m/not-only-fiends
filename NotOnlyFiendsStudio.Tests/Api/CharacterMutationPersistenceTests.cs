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
}
