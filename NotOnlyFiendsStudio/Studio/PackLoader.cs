using System.Text.Json;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Studio;

public class LoadedPack
{
    public required PackManifest Manifest { get; init; }
    public string? DirectoryPath { get; init; }
}

public class PackLoader
{
    /// <summary>
    /// Scan a directory for subdirectories containing pack.json manifests.
    /// </summary>
    public List<LoadedPack> DiscoverPacks(string packsRoot)
    {
        var packs = new List<LoadedPack>();
        if (!Directory.Exists(packsRoot)) return packs;

        foreach (var dir in Directory.GetDirectories(packsRoot))
        {
            var manifestPath = Path.Combine(dir, "pack.json");
            if (!File.Exists(manifestPath)) continue;

            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<PackManifest>(json, JsonOptions.Default)
                ?? throw new InvalidOperationException($"Failed to deserialize pack.json in {dir}");

            packs.Add(new LoadedPack { Manifest = manifest, DirectoryPath = dir });
        }

        return packs;
    }

    /// <summary>
    /// Resolve load order: filter disabled packs, topological sort on Depends,
    /// stable-sort by Priority within tiers.
    /// </summary>
    public List<LoadedPack> ResolveLoadOrder(List<LoadedPack> packs, PackConfig? config = null)
    {
        var filtered = FilterByConfig(packs, config);
        var byId = filtered.ToDictionary(p => p.Manifest.Id);

        ValidateDependencies(filtered, byId);
        return TopologicalSort(filtered, byId);
    }

    /// <summary>
    /// Overload for Blazor (no filesystem): resolve order from manifests only.
    /// </summary>
    public List<LoadedPack> ResolveLoadOrder(List<PackManifest> manifests, PackConfig? config = null)
    {
        var packs = manifests.Select(m => new LoadedPack { Manifest = m }).ToList();
        return ResolveLoadOrder(packs, config);
    }

    /// <summary>
    /// Discover packs, resolve order, and load each into the registry.
    /// </summary>
    public void LoadPacks(ContentRegistry registry, string packsRoot, PackConfig? config = null)
    {
        var packs = DiscoverPacks(packsRoot);
        var ordered = ResolveLoadOrder(packs, config);

        foreach (var pack in ordered)
        {
            if (pack.DirectoryPath == null) continue;

            var perPackConflict = GetEffectiveConflictResolution(pack, config);
            var savedConflict = registry.OnConflict;

            if (perPackConflict.HasValue)
                registry.OnConflict = perPackConflict.Value;

            registry.LoadContentDirectory(pack.DirectoryPath);

            if (perPackConflict.HasValue)
                registry.OnConflict = savedConflict;
        }
    }

    private static List<LoadedPack> FilterByConfig(List<LoadedPack> packs, PackConfig? config)
    {
        if (config == null) return packs;

        var configEntries = config.Packs.ToDictionary(e => e.Id);
        return packs.Where(p =>
        {
            if (configEntries.TryGetValue(p.Manifest.Id, out var entry))
                return entry.Enabled;
            return true; // not in config = enabled by default
        }).ToList();
    }

    private static void ValidateDependencies(List<LoadedPack> packs, Dictionary<string, LoadedPack> byId)
    {
        foreach (var pack in packs)
        {
            foreach (var dep in pack.Manifest.Depends)
            {
                if (!byId.ContainsKey(dep))
                    throw new InvalidOperationException(
                        $"Pack '{pack.Manifest.Id}' depends on '{dep}' which is not available");
            }
        }
    }

    /// <summary>
    /// Kahn's algorithm for topological sort, with priority-based stable ordering within tiers.
    /// </summary>
    private static List<LoadedPack> TopologicalSort(List<LoadedPack> packs, Dictionary<string, LoadedPack> byId)
    {
        var inDegree = packs.ToDictionary(p => p.Manifest.Id, _ => 0);
        var dependents = packs.ToDictionary(p => p.Manifest.Id, _ => new List<string>());

        foreach (var pack in packs)
        {
            foreach (var dep in pack.Manifest.Depends)
            {
                inDegree[pack.Manifest.Id]++;
                dependents[dep].Add(pack.Manifest.Id);
            }
        }

        // Start with packs that have no dependencies, sorted by priority
        var ready = packs
            .Where(p => inDegree[p.Manifest.Id] == 0)
            .OrderBy(p => p.Manifest.Priority)
            .ThenBy(p => p.Manifest.Id)
            .ToList();

        var result = new List<LoadedPack>();
        var readyIndex = 0;

        while (readyIndex < ready.Count)
        {
            var current = ready[readyIndex++];
            result.Add(current);

            // Find newly unblocked packs
            var newlyReady = new List<LoadedPack>();
            foreach (var depId in dependents[current.Manifest.Id])
            {
                inDegree[depId]--;
                if (inDegree[depId] == 0)
                    newlyReady.Add(byId[depId]);
            }

            // Insert newly ready packs in priority order
            if (newlyReady.Count > 0)
            {
                newlyReady = newlyReady
                    .OrderBy(p => p.Manifest.Priority)
                    .ThenBy(p => p.Manifest.Id)
                    .ToList();
                ready.InsertRange(readyIndex, newlyReady);
            }
        }

        if (result.Count != packs.Count)
        {
            var missing = packs.Select(p => p.Manifest.Id).Except(result.Select(r => r.Manifest.Id));
            throw new InvalidOperationException(
                $"Dependency cycle detected among packs: {string.Join(", ", missing)}");
        }

        return result;
    }

    private static ConflictResolution? GetEffectiveConflictResolution(LoadedPack pack, PackConfig? config)
    {
        // User config override takes priority over pack manifest
        if (config != null)
        {
            var entry = config.Packs.FirstOrDefault(e => e.Id == pack.Manifest.Id);
            if (entry?.OnConflict != null) return entry.OnConflict;
        }
        return pack.Manifest.OnConflict;
    }
}
