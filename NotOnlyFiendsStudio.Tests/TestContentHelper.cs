using System.Text.Json;
using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public static class TestContentHelper
{
    public const string EnvFileName = ".env";

    public static string GetSolutionRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "NotOnlyFiends.sln")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir ?? throw new InvalidOperationException("Could not find solution root");
    }

    public static string GetPacksPath() =>
        Path.Combine(GetSolutionRoot(), "NotOnlyFiendsStudio", "Content", "packs");

    public static string GetContentPath() =>
        Path.Combine(GetSolutionRoot(), "NotOnlyFiendsStudio", "Content");

    public static string GetEnvFilePath() =>
        Path.Combine(GetSolutionRoot(), EnvFileName);

    public static IReadOnlyList<string> GetBundledPackIds()
    {
        var configPath = Path.Combine(GetSolutionRoot(), "content-public.json");
        using var stream = File.OpenRead(configPath);
        var config = JsonSerializer.Deserialize<PublicContentConfig>(stream, JsonOptions.Default)
            ?? throw new InvalidOperationException($"Failed to deserialize {configPath}");

        if (config.PublicPackIds.Count == 0)
            throw new InvalidOperationException($"{configPath} does not contain any bundled packs");

        return config.PublicPackIds;
    }

    public static string? GetOptionalPrivatePacksPath()
    {
        var env = LoadEnvFile();
        env.TryGetValue("EXTRA_PACKS_PATH", out var path);
        return NormalizeOptionalPath(path);
    }

    public static bool HasOptionalPrivatePacks()
    {
        var privatePacksPath = GetOptionalPrivatePacksPath();
        return privatePacksPath != null && Directory.Exists(privatePacksPath);
    }

    public static string? GetOptionalPcgenCharactersPath()
    {
        var env = LoadEnvFile();
        env.TryGetValue("PCGEN_CHARACTERS_PATH", out var path);
        return NormalizeOptionalPath(path);
    }

    public static bool HasOptionalPcgenCharacters()
    {
        var path = GetOptionalPcgenCharactersPath();
        return path != null && Directory.Exists(path);
    }

    /// <summary>
    /// Frozen .pcg fixtures, committed in the private materials repo under
    /// <c>test-fixtures/pcg/</c>.
    ///
    /// Tests that spell out exact expected values must read from here, never from
    /// <see cref="GetOptionalPcgenCharactersPath"/>: the live PCGen directory is a working set the
    /// user edits, so an assertion written against it fails whenever a character is retouched — a
    /// failure that says nothing about the code. Only corpus-wide sweeps and the baseline harness
    /// (which has an accept-the-diff workflow) should read the live directory.
    ///
    /// Refreshing a fixture is deliberate: copy the newer .pcg over the fixture and commit it in
    /// the materials repo, so the assertion updates land in the same review as the input change.
    /// </summary>
    public static string? GetOptionalPcgFixturesPath()
    {
        var privatePacksPath = GetOptionalPrivatePacksPath();
        return privatePacksPath == null
            ? null
            : Path.Combine(privatePacksPath, "test-fixtures", "pcg");
    }

    public static bool HasOptionalPcgFixtures()
    {
        var path = GetOptionalPcgFixturesPath();
        return path != null && Directory.Exists(path);
    }

    /// <summary>
    /// Full path to a frozen .pcg fixture by file name. Throws rather than falling back to the
    /// live corpus — a silent fallback would reintroduce exactly the drift these fixtures prevent.
    /// </summary>
    public static string PcgFixture(string fileName)
    {
        var dir = GetOptionalPcgFixturesPath()
            ?? throw new InvalidOperationException(
                $"EXTRA_PACKS_PATH is not set in {EnvFileName}; .pcg fixtures are unavailable.");
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Missing .pcg fixture '{fileName}' in {dir}. Copy it from PCGEN_CHARACTERS_PATH "
                + "and commit it in the materials repo.", path);
        return path;
    }

    public static string? GetOptionalCharactersPath()
    {
        var env = LoadEnvFile();
        env.TryGetValue("CHARACTERS_PATH", out var path);
        return NormalizeOptionalPath(path);
    }

    public static ContentRegistry LoadBundledPacks()
    {
        return LoadPacks(includePrivatePacks: false);
    }

    public static ContentRegistry LoadBundledAndPrivatePacksIfAvailable()
    {
        return LoadPacks(includePrivatePacks: true);
    }

    public static ContentRegistry LoadAllPacks()
        => LoadBundledPacks();

    private static Dictionary<string, string> LoadEnvFile()
    {
        var envPath = GetEnvFilePath();
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

    private static string? NormalizeOptionalPath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path;

    private static ContentRegistry LoadPacks(bool includePrivatePacks)
    {
        var registry = new ContentRegistry();
        var loader = new PackLoader();
        var bundledPackIds = new HashSet<string>(GetBundledPackIds(), StringComparer.Ordinal);
        var allPacks = loader
            .DiscoverPacks(GetPacksPath())
            .Where(pack => bundledPackIds.Contains(pack.Manifest.Id))
            .ToList();

        if (includePrivatePacks)
        {
            var privatePacksPath = GetOptionalPrivatePacksPath();
            if (privatePacksPath != null && Directory.Exists(privatePacksPath))
                allPacks.AddRange(loader.DiscoverPacks(privatePacksPath));
        }

        foreach (var pack in loader.ResolveLoadOrder(allPacks))
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

        return registry;
    }

    private sealed class PublicContentConfig
    {
        public List<string> PublicPackIds { get; set; } = new();
    }

}
