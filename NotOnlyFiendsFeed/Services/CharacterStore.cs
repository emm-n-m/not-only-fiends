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
        lock (_writeLock)
        {
            string id;
            if (explicitId != null)
            {
                id = ValidateId(explicitId);
                if (File.Exists(PathFor(id)))
                    throw new CharacterStoreException("already_exists", $"Character already exists: {id}");
            }
            else
            {
                // Names are not identities. A campaign may hold six characters called "Lilly" —
                // her mortal self, each ascension — so a name collision picks the next free id
                // rather than refusing the save. Pass explicitId when the caller wants the id
                // to be exactly one thing and a clash to be an error.
                id = ReserveDerivedId(character);
            }

            WriteAtomic(PathFor(id), character);
            return id;
        }
    }

    /// <summary>
    /// First free id derived from the character's name: <c>lilly</c>, then <c>lilly_2</c>,
    /// <c>lilly_3</c>… Callers must already hold the write lock.
    /// </summary>
    private string ReserveDerivedId(Character character)
    {
        var baseId = DeriveId(character);
        if (!File.Exists(PathFor(baseId)))
            return baseId;

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{baseId}_{suffix}";
            if (!File.Exists(PathFor(candidate)))
                return candidate;
        }

        throw new CharacterStoreException("already_exists",
            $"Could not find a free id for '{baseId}' after 999 attempts");
    }

    /// <summary>
    /// The single character whose <see cref="Character.Name"/> matches, ignoring case. Returns null
    /// when nothing matches *or* when several do — a name shared by six Lillys identifies none of
    /// them, and guessing between them would silently link the wrong character.
    /// </summary>
    public CharacterFileInfo? FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        CharacterFileInfo? match = null;
        foreach (var info in List())
        {
            if (!string.Equals(info.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            if (match != null)
                return null;
            match = info;
        }

        return match;
    }

    public void Replace(string id, Character character)
    {
        var path = PathFor(id);
        lock (_writeLock)
        {
            WriteAtomic(path, character);
        }
    }

    /// <summary>
    /// Runs a read-modify-validate-write operation under the store lock. If the
    /// callback throws, the original file is left untouched.
    /// </summary>
    public TResult Update<TResult>(string id, Func<Character, TResult> update)
    {
        var path = PathFor(id);
        lock (_writeLock)
        {
            if (!File.Exists(path))
                throw new CharacterStoreException("not_found", $"Character not found: {id}");

            var character = Deserialize(path);
            var result = update(character);
            WriteAtomic(path, character);
            return result;
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

    /// <summary>
    /// A starting id suggestion slugged from the character's name, used only when a character is
    /// first created. An id is assigned once and then belongs to the file: renaming a character
    /// must never re-derive it, or every companion link pointing at the old slug would dangle and
    /// the previous file would be orphaned.
    /// </summary>
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
