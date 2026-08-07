using System.Text.Json;
using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public class PackLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PackLoader _loader = new();

    public PackLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PackLoaderTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreatePack(string id, int priority = 10, List<string>? depends = null,
        ConflictResolution? onConflict = null)
    {
        var packDir = Path.Combine(_tempDir, id);
        Directory.CreateDirectory(packDir);

        var manifest = new PackManifest
        {
            Id = id,
            Name = id,
            Priority = priority,
            Depends = depends ?? new(),
            OnConflict = onConflict
        };

        var json = JsonSerializer.Serialize(manifest, JsonOptions.Default);
        File.WriteAllText(Path.Combine(packDir, "pack.json"), json);
        return packDir;
    }

    private void AddFeatContent(string packDir, string featId, string featName)
    {
        var featsDir = Path.Combine(packDir, "feats");
        Directory.CreateDirectory(featsDir);
        var json = $@"[{{ ""id"": ""{featId}"", ""name"": ""{featName}"", ""prerequisites"": [], ""grantedPermabuffs"": [] }}]";
        File.WriteAllText(Path.Combine(featsDir, $"{featId}.json"), json);
    }

    // --- Discovery ---

    [Fact]
    public void DiscoverPacks_FindsPacksWithManifests()
    {
        CreatePack("pack_a");
        CreatePack("pack_b");
        // Directory without pack.json should be ignored
        Directory.CreateDirectory(Path.Combine(_tempDir, "not_a_pack"));

        var packs = _loader.DiscoverPacks(_tempDir);

        Assert.Equal(2, packs.Count);
        Assert.Contains(packs, p => p.Manifest.Id == "pack_a");
        Assert.Contains(packs, p => p.Manifest.Id == "pack_b");
    }

    [Fact]
    public void DiscoverPacks_EmptyDirectory_ReturnsEmpty()
    {
        var packs = _loader.DiscoverPacks(_tempDir);
        Assert.Empty(packs);
    }

    [Fact]
    public void DiscoverPacks_NonexistentDirectory_ReturnsEmpty()
    {
        var packs = _loader.DiscoverPacks(Path.Combine(_tempDir, "nope"));
        Assert.Empty(packs);
    }

    // --- Load Order: Priority ---

    [Fact]
    public void ResolveLoadOrder_SortsByPriority()
    {
        CreatePack("homebrew", priority: 50);
        CreatePack("srd_core", priority: 0);
        CreatePack("supplement", priority: 10);

        var packs = _loader.DiscoverPacks(_tempDir);
        var ordered = _loader.ResolveLoadOrder(packs);

        Assert.Equal("srd_core", ordered[0].Manifest.Id);
        Assert.Equal("supplement", ordered[1].Manifest.Id);
        Assert.Equal("homebrew", ordered[2].Manifest.Id);
    }

    [Fact]
    public void ResolveLoadOrder_SamePriority_SortsByIdForStability()
    {
        CreatePack("beta", priority: 10);
        CreatePack("alpha", priority: 10);

        var packs = _loader.DiscoverPacks(_tempDir);
        var ordered = _loader.ResolveLoadOrder(packs);

        Assert.Equal("alpha", ordered[0].Manifest.Id);
        Assert.Equal("beta", ordered[1].Manifest.Id);
    }

    // --- Load Order: Dependencies ---

    [Fact]
    public void ResolveLoadOrder_DependencyBeforePriority()
    {
        CreatePack("srd_core", priority: 0);
        CreatePack("supplement", priority: 10, depends: new() { "srd_core" });

        var packs = _loader.DiscoverPacks(_tempDir);
        var ordered = _loader.ResolveLoadOrder(packs);

        Assert.Equal("srd_core", ordered[0].Manifest.Id);
        Assert.Equal("supplement", ordered[1].Manifest.Id);
    }

    [Fact]
    public void ResolveLoadOrder_HigherPriorityDep_StillLoadedFirst()
    {
        // Even though "base" has higher priority number, "dependent" depends on it
        CreatePack("base", priority: 50);
        CreatePack("dependent", priority: 0, depends: new() { "base" });

        var packs = _loader.DiscoverPacks(_tempDir);
        var ordered = _loader.ResolveLoadOrder(packs);

        Assert.Equal("base", ordered[0].Manifest.Id);
        Assert.Equal("dependent", ordered[1].Manifest.Id);
    }

    [Fact]
    public void ResolveLoadOrder_DiamondDependency()
    {
        CreatePack("core", priority: 0);
        CreatePack("left", priority: 10, depends: new() { "core" });
        CreatePack("right", priority: 10, depends: new() { "core" });
        CreatePack("top", priority: 20, depends: new() { "left", "right" });

        var packs = _loader.DiscoverPacks(_tempDir);
        var ordered = _loader.ResolveLoadOrder(packs);

        Assert.Equal("core", ordered[0].Manifest.Id);
        // left and right both at priority 10, alphabetical
        Assert.Equal("left", ordered[1].Manifest.Id);
        Assert.Equal("right", ordered[2].Manifest.Id);
        Assert.Equal("top", ordered[3].Manifest.Id);
    }

    [Fact]
    public void ResolveLoadOrder_ManifestOnlyPreservesDependencyOrder()
    {
        var manifests = new List<PackManifest>
        {
            new() { Id = "addon", Priority = 0, Depends = new() { "core" } },
            new() { Id = "core", Priority = 50 }
        };

        var ordered = _loader.ResolveLoadOrder(manifests);

        Assert.Equal(new[] { "core", "addon" }, ordered.Select(pack => pack.Manifest.Id));
        Assert.All(ordered, pack => Assert.Null(pack.DirectoryPath));
    }

    // --- Error cases ---

    [Fact]
    public void ResolveLoadOrder_CycleDetected_Throws()
    {
        CreatePack("a", depends: new() { "b" });
        CreatePack("b", depends: new() { "a" });

        var packs = _loader.DiscoverPacks(_tempDir);
        var ex = Assert.Throws<InvalidOperationException>(() => _loader.ResolveLoadOrder(packs));
        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveLoadOrder_MissingDependency_Throws()
    {
        CreatePack("orphan", depends: new() { "nonexistent" });

        var packs = _loader.DiscoverPacks(_tempDir);
        var ex = Assert.Throws<InvalidOperationException>(() => _loader.ResolveLoadOrder(packs));
        Assert.Contains("nonexistent", ex.Message);
    }

    [Fact]
    public void ResolveLoadOrder_DisabledDependencyIsUnavailable()
    {
        CreatePack("core");
        CreatePack("addon", depends: new() { "core" });
        var config = new PackConfig
        {
            Packs = new() { new PackEntry { Id = "core", Enabled = false } }
        };

        var packs = _loader.DiscoverPacks(_tempDir);
        var ex = Assert.Throws<InvalidOperationException>(() => _loader.ResolveLoadOrder(packs, config));

        Assert.Contains("core", ex.Message);
    }

    [Fact]
    public void DiscoverPacks_MalformedManifestThrows()
    {
        var packDir = Path.Combine(_tempDir, "broken");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "pack.json"), "{ not valid json");

        Assert.Throws<JsonException>(() => _loader.DiscoverPacks(_tempDir));
    }

    [Fact]
    public void ResolveLoadOrder_DuplicatePackIdsThrows()
    {
        var packs = new List<LoadedPack>
        {
            new() { Manifest = new PackManifest { Id = "duplicate" } },
            new() { Manifest = new PackManifest { Id = "duplicate" } }
        };

        Assert.Throws<ArgumentException>(() => _loader.ResolveLoadOrder(packs));
    }

    // --- PackConfig filtering ---

    [Fact]
    public void ResolveLoadOrder_DisabledPackSkipped()
    {
        CreatePack("enabled_pack");
        CreatePack("disabled_pack");

        var config = new PackConfig
        {
            Packs = new()
            {
                new PackEntry { Id = "disabled_pack", Enabled = false }
            }
        };

        var packs = _loader.DiscoverPacks(_tempDir);
        var ordered = _loader.ResolveLoadOrder(packs, config);

        Assert.Single(ordered);
        Assert.Equal("enabled_pack", ordered[0].Manifest.Id);
    }

    [Fact]
    public void ResolveLoadOrder_PackNotInConfig_EnabledByDefault()
    {
        CreatePack("unlisted");

        var config = new PackConfig { Packs = new() };

        var packs = _loader.DiscoverPacks(_tempDir);
        var ordered = _loader.ResolveLoadOrder(packs, config);

        Assert.Single(ordered);
    }

    // --- Integration: LoadPacks ---

    [Fact]
    public void LoadPacks_LoadsContentInOrder()
    {
        var coreDir = CreatePack("core", priority: 0);
        AddFeatContent(coreDir, "feat:power_attack", "Power Attack");

        var supDir = CreatePack("supplement", priority: 10, depends: new() { "core" });
        AddFeatContent(supDir, "feat:improved_sunder", "Improved Sunder");

        var registry = new ContentRegistry();
        _loader.LoadPacks(registry, _tempDir);

        Assert.NotNull(registry.GetFeat("feat:power_attack"));
        Assert.NotNull(registry.GetFeat("feat:improved_sunder"));
    }

    [Fact]
    public void LoadPacks_PerPackConflictResolution()
    {
        // Core pack defines feat with LastWins (default)
        var coreDir = CreatePack("core", priority: 0);
        AddFeatContent(coreDir, "shared_feat", "Core Version");

        // Override pack uses FirstWins — should NOT override core's version
        var overrideDir = CreatePack("override", priority: 10, depends: new() { "core" },
            onConflict: ConflictResolution.FirstWins);
        AddFeatContent(overrideDir, "shared_feat", "Override Version");

        var registry = new ContentRegistry();
        _loader.LoadPacks(registry, _tempDir);

        // FirstWins on the override pack means core's version is kept
        var feat = registry.GetFeat("shared_feat");
        Assert.Equal("Core Version", feat.Name);
    }

    [Fact]
    public void LoadPacks_DefaultLastWins_LaterOverrides()
    {
        var coreDir = CreatePack("core", priority: 0);
        AddFeatContent(coreDir, "shared_feat", "Core Version");

        var laterDir = CreatePack("later", priority: 10, depends: new() { "core" });
        AddFeatContent(laterDir, "shared_feat", "Later Version");

        var registry = new ContentRegistry();
        _loader.LoadPacks(registry, _tempDir);

        var feat = registry.GetFeat("shared_feat");
        Assert.Equal("Later Version", feat.Name);
    }

    [Fact]
    public void LoadPacks_ConfigConflictOverrideTakesPriorityOverManifest()
    {
        var coreDir = CreatePack("core", priority: 0);
        AddFeatContent(coreDir, "shared_feat", "Core Version");

        var laterDir = CreatePack("later", priority: 10, depends: new() { "core" },
            onConflict: ConflictResolution.Error);
        AddFeatContent(laterDir, "shared_feat", "Later Version");
        var config = new PackConfig
        {
            Packs = new()
            {
                new PackEntry { Id = "later", OnConflict = ConflictResolution.LastWins }
            }
        };

        var registry = new ContentRegistry();
        _loader.LoadPacks(registry, _tempDir, config);

        Assert.Equal("Later Version", registry.GetFeat("shared_feat").Name);
        Assert.False(registry.HasErrors);
    }

    // --- PackManifest serialization ---

    [Fact]
    public void PackManifest_RoundTripSerialization()
    {
        var manifest = new PackManifest
        {
            Id = "test_pack",
            Name = "Test Pack",
            Description = "A test",
            Author = "Tester",
            Version = "1.0.0",
            Priority = 5,
            Depends = new() { "srd_core" },
            OnConflict = ConflictResolution.Warn
        };

        var json = JsonSerializer.Serialize(manifest, JsonOptions.Default);
        var deserialized = JsonSerializer.Deserialize<PackManifest>(json, JsonOptions.Default)!;

        Assert.Equal("test_pack", deserialized.Id);
        Assert.Equal("Test Pack", deserialized.Name);
        Assert.Equal(5, deserialized.Priority);
        Assert.Single(deserialized.Depends);
        Assert.Equal("srd_core", deserialized.Depends[0]);
        Assert.Equal(ConflictResolution.Warn, deserialized.OnConflict);
    }

    [Fact]
    public void PackManifest_MinimalJson_UsesDefaults()
    {
        var json = """{ "id": "minimal", "name": "Minimal Pack" }""";
        var manifest = JsonSerializer.Deserialize<PackManifest>(json, JsonOptions.Default)!;

        Assert.Equal("minimal", manifest.Id);
        Assert.Equal(10, manifest.Priority); // default
        Assert.Empty(manifest.Depends);
        Assert.Null(manifest.OnConflict);
    }
}
