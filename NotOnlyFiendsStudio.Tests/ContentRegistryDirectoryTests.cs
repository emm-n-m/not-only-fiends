using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

public class ContentRegistryDirectoryTests
{
    public static IEnumerable<object[]> RegisteredContentDirectories() => new[]
    {
        new object[] { "races" },
        new object[] { "classes" },
        new object[] { "racial_hd" },
        new object[] { "templates" },
        new object[] { "feats" },
        new object[] { "domains" },
        new object[] { "spells" },
        new object[] { "skills" },
        new object[] { "class_features" },
        new object[] { "equipment" },
    };

    [Theory]
    [MemberData(nameof(RegisteredContentDirectories))]
    public void LoadJsonForDirectory_LoadsEveryRegisteredContentType(string directoryName)
    {
        var sourceDirectory = Path.Combine(TestContentHelper.GetPacksPath(), "srd_core", directoryName);
        var file = Directory.GetFiles(sourceDirectory, "*.json", SearchOption.AllDirectories).First();
        var registry = new ContentRegistry();

        registry.LoadJsonForDirectory(directoryName, File.ReadAllText(file));

        Assert.False(registry.HasErrors, string.Join("\n", registry.Errors.Select(error => error.Message)));
        var count = directoryName switch
        {
            "races" => registry.GetAllRaces().Count(),
            "classes" or "racial_hd" => registry.GetAllDrivers().Count(),
            "templates" => registry.GetAllTemplates().Count(),
            "feats" => registry.GetAllFeats().Count(),
            "domains" => registry.GetAllDomains().Count(),
            "spells" => registry.GetAllSpells().Count(),
            "skills" => registry.GetAllSkills().Count(),
            "class_features" => registry.GetAllClassFeatures().Count(),
            "equipment" => registry.GetAllEquipment().Count(),
            _ => throw new ArgumentOutOfRangeException(nameof(directoryName))
        };
        Assert.True(count > 0);
    }

    [Fact]
    public void LoadJsonForDirectory_LoadsDeitiesWithoutRequiringClosedContentInTheBundledPack()
    {
        var registry = new ContentRegistry();

        registry.LoadJsonForDirectory("deities",
            """
            [{
              "id": "deity:test",
              "name": "Test Patron",
              "description": "A test-only patron.",
              "alignment": "n",
              "titles": [],
              "portfolio": ["testing"],
              "domainIds": [],
              "favoredWeaponId": null,
              "symbol": null
            }]
            """);

        Assert.Equal("Test Patron", registry.GetDeity("deity:test").Name);
    }

    [Fact]
    public void LoadJsonForDirectory_LoadsSalientDivineAbilitiesFromEpicPack()
    {
        var path = Path.Combine(TestContentHelper.GetPacksPath(), "srd_epic",
            "salient_divine_abilities", "srd.json");
        var registry = new ContentRegistry();

        registry.LoadJsonForDirectory("salient_divine_abilities", File.ReadAllText(path));

        Assert.Equal(99, registry.GetAllSalientDivineAbilities().Count());
        Assert.Equal(16, registry.GetSalientDivineAbility("salient:divine_creation").MinimumDivineRank);
    }

    [Fact]
    public void LoadJsonForDirectory_UnknownDirectoryThrows()
    {
        var registry = new ContentRegistry();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.LoadJsonForDirectory("not_a_content_type", "[]"));

        Assert.Contains("No handler for content directory", exception.Message);
    }
}
