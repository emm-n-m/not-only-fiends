using System.Text.Json;
using System.Text.RegularExpressions;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsFeed.Services;

public sealed class CharacterStoreException : Exception
{
    public string Code { get; }

    public CharacterStoreException(string code, string message) : base(message)
    {
        Code = code;
    }
}

public sealed class CharacterFileInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required DateTime ModifiedUtc { get; init; }
    public CharacterSheet? Sheet { get; init; }
}

public sealed class CharacterStore
{
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9_-]{0,99}$", RegexOptions.Compiled);

    private readonly string? _rootPath;
    private readonly object _writeLock = new();

    public CharacterStore(ServerContentService contentService)
    {
        _rootPath = string.IsNullOrWhiteSpace(contentService.CharactersPath)
            ? null
            : contentService.CharactersPath;

        if (_rootPath != null)
            Directory.CreateDirectory(_rootPath);
    }

    public bool IsConfigured => _rootPath != null;

    public string RootPath => _rootPath
        ?? throw new CharacterStoreException("not_configured",
            "CHARACTERS_PATH is not set in .env");

    public IEnumerable<CharacterFileInfo> List()
    {
        var root = RootPath;
        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly))
        {
            CharacterFileInfo? info;
            try
            {
                info = ReadSummary(path);
            }
            catch
            {
                continue;
            }

            if (info != null)
                yield return info;
        }
    }

    public Character Get(string id)
    {
        var path = PathFor(id);
        if (!File.Exists(path))
            throw new CharacterStoreException("not_found", $"Character not found: {id}");

        return Deserialize(path);
    }

    public IEnumerable<CharacterFileInfo> FindMasters(string companionId)
    {
        var root = RootPath;
        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly))
        {
            Character character;
            try { character = Deserialize(path); }
            catch { continue; }

            if (!character.CompanionLinks.Any(l => l.CompanionId == companionId))
                continue;

            yield return new CharacterFileInfo
            {
                Id = Path.GetFileNameWithoutExtension(path),
                Name = character.Name,
                ModifiedUtc = File.GetLastWriteTimeUtc(path),
                Sheet = character.Sheet
            };
        }
    }

    public bool Exists(string id)
    {
        try
        {
            return File.Exists(PathFor(id));
        }
        catch (CharacterStoreException)
        {
            return false;
        }
    }

    public string Create(Character character, string? explicitId = null)
    {
        var id = explicitId != null ? ValidateId(explicitId) : DeriveId(character);
        var path = PathFor(id);

        lock (_writeLock)
        {
            if (File.Exists(path))
                throw new CharacterStoreException("already_exists", $"Character already exists: {id}");

            WriteAtomic(path, character);
        }

        return id;
    }

    public void Replace(string id, Character character)
    {
        var path = PathFor(id);
        lock (_writeLock)
        {
            WriteAtomic(path, character);
        }
    }

    public bool Delete(string id)
    {
        var path = PathFor(id);
        lock (_writeLock)
        {
            if (!File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }
    }

    private string PathFor(string id)
    {
        var safeId = ValidateId(id);
        return Path.Combine(RootPath, safeId + ".json");
    }

    private static string ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !IdPattern.IsMatch(id))
            throw new CharacterStoreException("invalid_id",
                $"Character id must match {IdPattern}: '{id}'");

        return id;
    }

    public static string DeriveId(Character character)
    {
        var name = character.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            throw new CharacterStoreException("invalid_id",
                "Character has no Name; supply an explicit id");

        var slug = new string(name.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : (c is ' ' or '-' or '_' ? '_' : '-'))
            .ToArray())
            .Trim('-', '_');

        if (string.IsNullOrEmpty(slug) || !IdPattern.IsMatch(slug))
            throw new CharacterStoreException("invalid_id",
                $"Could not derive a valid id from name '{character.Name}'");

        return slug;
    }

    private static void WriteAtomic(string targetPath, Character character)
    {
        var tempPath = targetPath + ".tmp";
        var json = JsonSerializer.Serialize(character, JsonOptions.Default);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, targetPath, overwrite: true);
    }

    private static Character Deserialize(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Character>(stream, JsonOptions.Default)
            ?? throw new CharacterStoreException("validation_failed",
                $"Character file could not be deserialized: {Path.GetFileName(path)}");
    }

    private static CharacterFileInfo? ReadSummary(string path)
    {
        var character = Deserialize(path);
        var id = Path.GetFileNameWithoutExtension(path);
        return new CharacterFileInfo
        {
            Id = id,
            Name = character.Name,
            ModifiedUtc = File.GetLastWriteTimeUtc(path),
            Sheet = character.Sheet
        };
    }
}
