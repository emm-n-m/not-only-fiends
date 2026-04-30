using System.Text.Json;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsFeed.Services;

public sealed class ServerContentService
{
    public ContentRegistry Registry { get; }
    public IReadOnlyList<LoadedPack> LoadedPacks { get; }
    public ReplayStudio ReplayStudio { get; }
    public string? CharactersPath { get; }

    public ServerContentService(IConfiguration configuration, ILogger<ServerContentService> logger)
    {
        var bundledPacksPath = configuration["Content:BundledPacksPath"];

        string packsPath;
        string? extraPacksPath;
        string? charactersPath;

        if (!string.IsNullOrWhiteSpace(bundledPacksPath))
        {
            // Docker / deployed mode: explicit paths from configuration
            packsPath = bundledPacksPath;
            extraPacksPath = configuration["Content:ExtraPacksPath"];
            charactersPath = configuration["Content:CharactersPath"];
            logger.LogInformation("Content mode: configured paths (bundled={BundledPath})", packsPath);
        }
        else
        {
            // Local dev mode: find solution root, use content-public.json + .env
            var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
            packsPath = Path.Combine(solutionRoot, "NotOnlyFiendsStudio", "Content", "packs");

            var envVars = LoadEnvFile(solutionRoot);
            envVars.TryGetValue("EXTRA_PACKS_PATH", out extraPacksPath);
            envVars.TryGetValue("CHARACTERS_PATH", out charactersPath);

            logger.LogInformation("Content mode: local dev (solution={SolutionRoot})", solutionRoot);
        }

        CharactersPath = charactersPath;

        var packLoader = new PackLoader();
        var allPacks = new List<LoadedPack>();

        // Load bundled packs
        if (!string.IsNullOrWhiteSpace(bundledPacksPath))
        {
            // Docker mode: load all packs from bundled path
            allPacks.AddRange(packLoader.DiscoverPacks(packsPath));
        }
        else
        {
            // Local dev: filter by content-public.json
            var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
            var bundledPackIds = LoadBundledPackIds(solutionRoot);
            allPacks.AddRange(
                packLoader.DiscoverPacks(packsPath)
                    .Where(pack => bundledPackIds.Contains(pack.Manifest.Id)));
        }

        // Load extra packs if configured
        if (!string.IsNullOrWhiteSpace(extraPacksPath) && Directory.Exists(extraPacksPath))
        {
            allPacks.AddRange(packLoader.DiscoverPacks(extraPacksPath));
            logger.LogInformation("Loaded extra packs from {ExtraPath}", extraPacksPath);
        }

        var orderedPacks = packLoader.ResolveLoadOrder(allPacks);
        var registry = new ContentRegistry();

        foreach (var pack in orderedPacks)
        {
            if (pack.DirectoryPath == null)
                continue;

            var savedConflict = registry.OnConflict;
            if (pack.Manifest.OnConflict.HasValue)
                registry.OnConflict = pack.Manifest.OnConflict.Value;

            registry.LoadContentDirectory(pack.DirectoryPath);

            if (pack.Manifest.OnConflict.HasValue)
                registry.OnConflict = savedConflict;
        }

        registry.Validate();
        if (registry.HasErrors)
        {
            var firstErrors = string.Join(
                Environment.NewLine,
                registry.Errors.Take(10).Select(e => $"{e.Kind}: {e.Message}"));
            throw new InvalidOperationException($"Content validation failed:{Environment.NewLine}{firstErrors}");
        }

        Registry = registry;
        LoadedPacks = orderedPacks;
        ReplayStudio = new ReplayStudio(registry);

        logger.LogInformation("Content loaded: {PackCount} packs, {RaceCount} races, {DriverCount} drivers, {FeatCount} feats",
            orderedPacks.Count,
            registry.GetAllRaces().Count(),
            registry.GetAllDrivers().Count(),
            registry.GetAllFeats().Count());
    }

    private static string FindSolutionRoot(string startPath)
    {
        var dir = startPath;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "NotOnlyFiends.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not find solution root. For Docker deployments, set Content:BundledPacksPath in configuration.");
    }

    private static HashSet<string> LoadBundledPackIds(string solutionRoot)
    {
        var configPath = Path.Combine(solutionRoot, "content-public.json");
        using var stream = File.OpenRead(configPath);
        var config = JsonSerializer.Deserialize<PublicContentConfig>(stream, JsonOptions.Default)
            ?? throw new InvalidOperationException($"Failed to deserialize {configPath}");

        if (config.PublicPackIds.Count == 0)
            throw new InvalidOperationException($"{configPath} does not contain any bundled packs");

        return new HashSet<string>(config.PublicPackIds, StringComparer.Ordinal);
    }

    private static Dictionary<string, string> LoadEnvFile(string solutionRoot)
    {
        var envPath = Path.Combine(solutionRoot, ".env");
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(envPath))
            return vars;

        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0)
                continue;

            var key = trimmed[..eqIndex].Trim();
            var value = trimmed[(eqIndex + 1)..].Trim();
            vars[key] = value;
        }

        return vars;
    }

    private sealed class PublicContentConfig
    {
        public List<string> PublicPackIds { get; set; } = new();
    }
}
