using System.Text.Json;
using System.Text.Json.Serialization;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// Deserializes every pack JSON file with unmapped members disallowed. The normal
/// loader silently skips unknown properties, so a typo'd property name (e.g. "ranks"
/// instead of "value" on MinSkillRanks) leaves the default value in place and the
/// content loads without error — the 2026-07 LST audit found two prestige-class
/// skill gates dead this way.
/// </summary>
public class StrictContentDeserializationTests
{
    private static readonly Dictionary<string, Type> DirectoryTypes = new()
    {
        ["races"] = typeof(List<RaceDefinition>),
        ["classes"] = typeof(List<Driver>),
        ["racial_hd"] = typeof(List<Driver>),
        ["templates"] = typeof(List<TemplateDriver>),
        ["feats"] = typeof(List<FeatDefinition>),
        ["deities"] = typeof(List<DeityDefinition>),
        ["salient_divine_abilities"] = typeof(List<SalientDivineAbilityDefinition>),
        ["domains"] = typeof(List<DomainDefinition>),
        ["spells"] = typeof(List<SpellDefinition>),
        ["skills"] = typeof(List<SkillDefinition>),
        ["class_features"] = typeof(List<ClassFeatureDefinition>),
        ["equipment"] = typeof(List<EquipmentDefinition>),
    };

    private static JsonSerializerOptions CreateStrictOptions() => new(JsonOptions.Default)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static List<string> FindUnmappedMembers(string packsRoot)
    {
        var options = CreateStrictOptions();
        var failures = new List<string>();
        foreach (var packDir in Directory.GetDirectories(packsRoot))
        {
            if (!File.Exists(Path.Combine(packDir, "pack.json")))
                continue;
            foreach (var (dirName, type) in DirectoryTypes)
            {
                var contentDir = Path.Combine(packDir, dirName);
                if (!Directory.Exists(contentDir))
                    continue;
                foreach (var file in Directory.GetFiles(contentDir, "*.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        JsonSerializer.Deserialize(File.ReadAllText(file), type, options);
                    }
                    catch (JsonException ex)
                    {
                        failures.Add($"{Path.GetRelativePath(packsRoot, file)}: {ex.Message}");
                    }
                }
            }
        }
        return failures;
    }

    [Fact]
    public void BundledPacks_HaveNoUnmappedJsonMembers()
    {
        var failures = FindUnmappedMembers(TestContentHelper.GetPacksPath());
        Assert.True(failures.Count == 0,
            "Unknown JSON properties (typo'd names are silently dropped by the real loader):\n"
            + string.Join("\n", failures));
    }

    [RequiresPrivatePacksFact]
    public void PrivatePacks_HaveNoUnmappedJsonMembers()
    {
        var failures = FindUnmappedMembers(TestContentHelper.GetOptionalPrivatePacksPath()!);
        Assert.True(failures.Count == 0,
            "Unknown JSON properties (typo'd names are silently dropped by the real loader):\n"
            + string.Join("\n", failures));
    }
}
