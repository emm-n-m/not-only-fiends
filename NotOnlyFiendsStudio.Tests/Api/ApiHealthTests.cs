using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NotOnlyFiendsFeed.Services;

namespace NotOnlyFiendsStudio.Tests.Api;

/// <summary>
/// /api/health must say whether the character store is usable. An unset CHARACTERS_PATH is the
/// single most likely misconfiguration, and without this flag a caller discovers it one 503 at
/// a time instead of on the first probe.
/// </summary>
public sealed class ApiHealthTests
{
    private static AgentApiService BuildService(string? charactersPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Content:BundledPacksPath"] = TestContentHelper.GetPacksPath(),
                ["Content:CharactersPath"] = charactersPath
            })
            .Build();
        var content = new ServerContentService(
            configuration,
            NullLogger<ServerContentService>.Instance);
        return new AgentApiService(content, new CharacterStore(content));
    }

    [Fact]
    public void UnconfiguredCharacterStoreReportsDegraded()
    {
        var health = BuildService(charactersPath: null).GetHealth();

        Assert.Equal("degraded", health.Status);
        Assert.False(health.CharacterStoreConfigured);
    }

    [Fact]
    public void ConfiguredCharacterStoreReportsOk()
    {
        var charactersPath = Path.Combine(
            Path.GetTempPath(),
            $"not-only-fiends-health-tests-{Guid.NewGuid():N}");
        try
        {
            var health = BuildService(charactersPath).GetHealth();

            Assert.Equal("ok", health.Status);
            Assert.True(health.CharacterStoreConfigured);
        }
        finally
        {
            if (Directory.Exists(charactersPath))
                Directory.Delete(charactersPath, recursive: true);
        }
    }
}
