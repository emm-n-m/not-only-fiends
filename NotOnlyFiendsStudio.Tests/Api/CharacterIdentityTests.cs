using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NotOnlyFiendsFeed.Services;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests.Api;

/// <summary>
/// A character's id belongs to its file, not to its name. Names are display text: a campaign may
/// hold six characters called "Lilly" — her mortal self and each ascension — and a character may
/// be renamed at any point without orphaning its file or dangling the links that point at it.
/// </summary>
public sealed class CharacterIdentityTests : IDisposable
{
    private readonly string _charactersPath = Path.Combine(
        Path.GetTempPath(),
        $"not-only-fiends-identity-tests-{Guid.NewGuid():N}");
    private readonly CharacterStore _store;
    private readonly ContentRegistry _registry = TestContentHelper.LoadBundledPacks();

    public CharacterIdentityTests()
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
    }

    public void Dispose()
    {
        if (Directory.Exists(_charactersPath))
            Directory.Delete(_charactersPath, recursive: true);
    }

    [Fact]
    public void SameNamedCharactersEachGetTheirOwnId()
    {
        var ids = new[]
        {
            _store.Create(Named("Lilly")),
            _store.Create(Named("Lilly")),
            _store.Create(Named("Lilly")),
        };

        Assert.Equal(new[] { "lilly", "lilly_2", "lilly_3" }, ids);
        Assert.Equal(3, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Equal("Lilly", _store.Get(id).Name));
    }

    [Fact]
    public void RenamingACharacterKeepsItsIdAndItsFile()
    {
        var id = _store.Create(Named("Lilly"));

        var character = _store.Get(id);
        character.Name = "Lilly, Archfiend Ascendant";
        _store.Replace(id, character);

        Assert.Equal("Lilly, Archfiend Ascendant", _store.Get(id).Name);
        Assert.Single(_store.List());
        // The name the id was originally slugged from is gone; the id is not.
        Assert.Equal("lilly", id);
    }

    [Fact]
    public void AnExplicitIdStillRefusesToOverwriteAnExistingCharacter()
    {
        _store.Create(Named("Lilly"), "lilly_666");

        var conflict = Assert.Throws<CharacterStoreException>(
            () => _store.Create(Named("Someone Else"), "lilly_666"));
        Assert.Equal("already_exists", conflict.Code);
    }

    [Fact]
    public void FindByNameRefusesToGuessBetweenSameNamedCharacters()
    {
        _store.Create(Named("Umbriel"));
        Assert.Equal("umbriel", _store.FindByName("umbriel")?.Id);

        _store.Create(Named("Umbriel"));
        Assert.Null(_store.FindByName("Umbriel"));
    }

    // ---- companion link repair -------------------------------------------

    /// <summary>
    /// A link written before its companion existed points at an id nothing answers to. Once the
    /// companion is saved, the source name re-points it — without this the link stays dead even
    /// though the character is right there.
    /// </summary>
    [Fact]
    public void ALinkWhoseIdIsStaleResolvesThroughTheSourceName()
    {
        var companionId = _store.Create(Named("Umbriel"), "umbriel_the_shadow");

        var master = Named("Countess");
        master.Ticks.Add(new Tick { DriverId = "class:fighter" });
        master.CompanionLinks.Add(new CompanionLink
        {
            LinkType = "shadow_companion",
            CompanionId = "umbriel",          // the guess PCGen's name produced
            SourceName = "Umbriel",           // what the master actually called it
            EffectiveLevelFormula = new Formula { Expression = "TotalHD" },
        });

        var result = Build(master);

        Assert.Equal(companionId, "umbriel_the_shadow");
        Assert.Single(result.Companions);
        Assert.DoesNotContain(result.MasterState.Warnings,
            warning => warning.Message.Contains("references missing companion"));
    }

    [Fact]
    public void AnUnresolvableLinkNamesBothTheIdAndTheSourceItCameFrom()
    {
        var master = Named("Countess");
        master.Ticks.Add(new Tick { DriverId = "class:fighter" });
        master.CompanionLinks.Add(new CompanionLink
        {
            LinkType = "leadership_cohort",
            CompanionId = "vzraella-_abyssal_herald",
            SourceName = "Vzraella, Abyssal Herald",
            Notes = "Imported from PCGen Cohort; source file: Vzraella, Abyssal Herald.pcg",
            EffectiveLevelFormula = new Formula { Expression = "TotalHD" },
        });

        var warning = Assert.Single(Build(master).MasterState.Warnings,
            w => w.Message.Contains("references missing companion"));

        Assert.Contains("vzraella-_abyssal_herald", warning.Message);
        Assert.Contains("Vzraella, Abyssal Herald", warning.Message);
        Assert.Contains("source file:", warning.Message);
    }

    private CompositeBuildResult Build(Character master)
    {
        var engine = new ReplayStudio(_registry);
        return new CompanionResolver(
            engine,
            id => _store.Exists(id) ? _store.Get(id) : null,
            name =>
            {
                var match = _store.FindByName(name);
                return match == null ? null : _store.Get(match.Id);
            }).Build(master);
    }

    private static Character Named(string name) => new()
    {
        Name = name,
        RaceId = "race:human",
        BaseAbilityScores = new AbilityScoreSet
        {
            STR = 12, DEX = 12, CON = 12, INT = 12, WIS = 12, CHA = 12
        }
    };
}
