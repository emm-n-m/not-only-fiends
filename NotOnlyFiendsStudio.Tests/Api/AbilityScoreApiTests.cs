using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NotOnlyFiendsFeed.Contracts;
using NotOnlyFiendsFeed.Services;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests.Api;

public sealed class AbilityScoreApiTests : IDisposable
{
    private const string Id = "ability-test";

    private readonly string _charactersPath = Path.Combine(
        Path.GetTempPath(),
        $"not-only-fiends-ability-tests-{Guid.NewGuid():N}");
    private readonly CharacterStore _store;
    private readonly AgentApiService _api;

    public AbilityScoreApiTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Content:BundledPacksPath"] = TestContentHelper.GetPacksPath(),
                ["Content:CharactersPath"] = _charactersPath
            })
            .Build();
        var content = new ServerContentService(configuration, NullLogger<ServerContentService>.Instance);
        _store = new CharacterStore(content);
        _api = new AgentApiService(content, _store);
        _store.Create(new Character
        {
            Name = "Ability Test",
            RaceId = "race:human",
            BaseAbilityScores = Scores(10, 10, 10, 10, 10, 10)
        }, Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_charactersPath))
            Directory.Delete(_charactersPath, recursive: true);
    }

    private static AbilityScoreSet Scores(int str, int dex, int con, int @int, int wis, int cha) =>
        new() { STR = str, DEX = dex, CON = con, INT = @int, WIS = wis, CHA = cha };

    private static int[] ScoreArray(AbilityScoreSet s) => new[] { s.STR, s.DEX, s.CON, s.INT, s.WIS, s.CHA };

    [Fact]
    public void PointBuy_WithinBudgetPersistsScoresAndBudget()
    {
        // The elite array is the DMG's 25-point standard.
        var elite = Scores(15, 14, 13, 12, 10, 8);

        var response = _api.SetAbilityScores(Id, new SetAbilityScoresRequest
        {
            Method = AbilityScoreMethod.PointBuy,
            Scores = elite
        });
        var persisted = _store.Get(Id);

        Assert.Equal(ScoreArray(elite), ScoreArray(persisted.BaseAbilityScores));
        Assert.Equal(AbilityScoreMethod.PointBuy, persisted.AbilityGeneration!.Method);
        Assert.Equal(AbilityScoreGenerationRules.StandardPointBuyBudget, persisted.AbilityGeneration.PointBuyBudget);
        Assert.Equal(15, response.State.AbilityScores.STR);
    }

    [Fact]
    public void PointBuy_OverBudgetIsRejectedAndNotPersisted()
    {
        // 15 + 14 + 13 + 12 + 10 + 9 costs one point more than the elite array.
        var ex = Assert.Throws<ArgumentException>(() => _api.SetAbilityScores(Id, new SetAbilityScoresRequest
        {
            Method = AbilityScoreMethod.PointBuy,
            Scores = Scores(15, 14, 13, 12, 10, 9)
        }));

        Assert.Contains("over the budget", ex.Message);
        Assert.Null(_store.Get(Id).AbilityGeneration);
    }

    [Fact]
    public void PointBuy_LargerBudgetAcceptsTheSameScores()
    {
        _api.SetAbilityScores(Id, new SetAbilityScoresRequest
        {
            Method = AbilityScoreMethod.PointBuy,
            Scores = Scores(15, 14, 13, 12, 10, 9),
            PointBuyBudget = 28
        });

        Assert.Equal(28, _store.Get(Id).AbilityGeneration!.PointBuyBudget);
    }

    [Theory]
    [InlineData(19)]
    [InlineData(7)]
    public void PointBuy_ScoreOutsideTableIsRejected(int str) =>
        Assert.Throws<ArgumentException>(() => _api.SetAbilityScores(Id, new SetAbilityScoresRequest
        {
            Method = AbilityScoreMethod.PointBuy,
            Scores = Scores(str, 8, 8, 8, 8, 8),
            PointBuyBudget = 32
        }));

    [Fact]
    public void PointBuy_NonstandardBudgetIsRejected() =>
        Assert.Throws<ArgumentException>(() => _api.SetAbilityScores(Id, new SetAbilityScoresRequest
        {
            Method = AbilityScoreMethod.PointBuy,
            Scores = Scores(8, 8, 8, 8, 8, 8),
            PointBuyBudget = 100
        }));

    [Fact]
    public void Roll_StoresRollsAndAssignsThemInOrder()
    {
        var expected = AbilityScoreGenerationRules.RollScores(new Random(7));

        _api.SetAbilityScores(Id, new SetAbilityScoresRequest { Method = AbilityScoreMethod.Rolled, Roll = true }, new Random(7));
        var persisted = _store.Get(Id);

        Assert.Equal(expected, persisted.AbilityGeneration!.Rolls);
        Assert.Equal(expected, ScoreArray(persisted.BaseAbilityScores));
    }

    [Fact]
    public void Rolled_RearrangingUsesEachRollOnce()
    {
        _api.SetAbilityScores(Id, new SetAbilityScoresRequest { Method = AbilityScoreMethod.Rolled, Roll = true }, new Random(7));
        var rolls = _store.Get(Id).AbilityGeneration!.Rolls;
        var reversed = rolls.AsEnumerable().Reverse().ToArray();

        _api.SetAbilityScores(Id, new SetAbilityScoresRequest
        {
            Method = AbilityScoreMethod.Rolled,
            Scores = Scores(reversed[0], reversed[1], reversed[2], reversed[3], reversed[4], reversed[5])
        });

        var persisted = _store.Get(Id);
        Assert.Equal(reversed, ScoreArray(persisted.BaseAbilityScores));
        Assert.Equal(rolls, persisted.AbilityGeneration!.Rolls);
    }

    [Fact]
    public void Rolled_ScoresThatAreNotTheRollsAreRejected()
    {
        _api.SetAbilityScores(Id, new SetAbilityScoresRequest { Method = AbilityScoreMethod.Rolled, Roll = true }, new Random(7));
        var rolls = _store.Get(Id).AbilityGeneration!.Rolls;

        // Every score one above its roll: same shape, but no longer the dice that came up.
        Assert.Throws<ArgumentException>(() => _api.SetAbilityScores(Id, new SetAbilityScoresRequest
        {
            Method = AbilityScoreMethod.Rolled,
            Scores = Scores(rolls[0] + 1, rolls[1], rolls[2], rolls[3], rolls[4], rolls[5])
        }));
        Assert.Equal(rolls, ScoreArray(_store.Get(Id).BaseAbilityScores));
    }

    [Fact]
    public void Rolled_ArrangingWithoutRollsIsRejected() =>
        Assert.Throws<ArgumentException>(() => _api.SetAbilityScores(Id, new SetAbilityScoresRequest
        {
            Method = AbilityScoreMethod.Rolled,
            Scores = Scores(10, 10, 10, 10, 10, 10)
        }));

    [Fact]
    public void Roll_WithScoresIsRejected() =>
        Assert.Throws<ArgumentException>(() => _api.SetAbilityScores(Id, new SetAbilityScoresRequest
        {
            Method = AbilityScoreMethod.Rolled,
            Roll = true,
            Scores = Scores(18, 18, 18, 18, 18, 18)
        }));

    [Fact]
    public void Manual_ClearsGenerationBookkeeping()
    {
        _api.SetAbilityScores(Id, new SetAbilityScoresRequest { Method = AbilityScoreMethod.Rolled, Roll = true }, new Random(7));

        _api.SetAbilityScores(Id, new SetAbilityScoresRequest
        {
            Method = AbilityScoreMethod.Manual,
            Scores = Scores(20, 3, 12, 12, 12, 12)
        });

        var persisted = _store.Get(Id);
        Assert.Null(persisted.AbilityGeneration);
        Assert.Equal(20, persisted.BaseAbilityScores.STR);
    }

    [Fact]
    public void FieldsForAnotherMethodAreRejected()
    {
        Assert.Throws<ArgumentException>(() => _api.SetAbilityScores(Id, new SetAbilityScoresRequest
        {
            Method = AbilityScoreMethod.Manual,
            Scores = Scores(10, 10, 10, 10, 10, 10),
            PointBuyBudget = 25
        }));
        Assert.Throws<ArgumentException>(() => _api.SetAbilityScores(Id, new SetAbilityScoresRequest
        {
            Method = AbilityScoreMethod.PointBuy,
            Scores = Scores(10, 10, 10, 10, 10, 10),
            Roll = true
        }));
    }

    [Fact]
    public void Rules_ExposePointBuyBudgetsAndCosts()
    {
        var rules = _api.GetRules();

        Assert.Equal(AbilityScoreGenerationRules.PointBuyBudgets.Select(b => b.Budget), rules.PointBuyBudgets.Select(b => b.Budget));
        Assert.Equal(AbilityScoreGenerationRules.PointBuyCost(18), rules.PointBuyCosts[18]);
        Assert.Equal(
            AbilityScoreGenerationRules.PointBuyMaximum - AbilityScoreGenerationRules.PointBuyMinimum + 1,
            rules.PointBuyCosts.Count);
    }
}
