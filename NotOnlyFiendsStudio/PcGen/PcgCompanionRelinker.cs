using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.PcGen;

/// <summary>
/// Repairs companion links after a batch of .pcg files has been converted.
///
/// <see cref="PcgConverter"/> sees one file at a time, so it can only guess a companion's id from
/// the label the master used ("Infernal Bodyguard"). The companion's own record may carry a
/// different NAME ("Vzraela, Abyssal Herald"), and the saved character is filed under that name —
/// so the guessed id resolves to nothing and the companion silently drops off the master's sheet.
///
/// Once the whole corpus is converted the mapping is knowable: each link names the source file it
/// came from, and that file produced exactly one character. This pass rewrites the ids accordingly.
/// </summary>
public static class PcgCompanionRelinker
{
    /// <summary>
    /// Rewrites <see cref="CompanionLink.CompanionId"/> (and
    /// <see cref="CompanionOrigin.MasterCharacterId"/>) to the ids the given characters will
    /// actually be saved under. Mutates the characters in place.
    /// </summary>
    /// <param name="converted">Every character in the batch, paired with the .pcg it came from.</param>
    /// <param name="deriveId">
    /// The host's id derivation — must be the same function the character store saves under,
    /// or the repaired ids will miss in a new way.
    /// </param>
    /// <returns>A report naming every link that moved and every one still unresolved.</returns>
    public static RelinkReport Relink(
        IReadOnlyCollection<(string SourceFile, Character Character)> converted,
        Func<Character, string> deriveId)
    {
        ArgumentNullException.ThrowIfNull(converted);
        ArgumentNullException.ThrowIfNull(deriveId);

        // Source file -> saved id. Two files can legitimately produce one character (the same
        // creature saved twice in PCGen), so this is many-to-one and never ambiguous.
        var byFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Name -> saved id, for links whose file reference is missing. Ambiguous names are
        // dropped: guessing between two characters called "Lilly" is worse than not resolving.
        var byName = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var knownIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (sourceFile, character) in converted)
        {
            var id = deriveId(character);
            knownIds.Add(id);

            var key = FileKey(sourceFile);
            if (key != null)
                byFile[key] = id;

            var name = character.Name?.Trim();
            if (string.IsNullOrEmpty(name))
                continue;
            // Present-but-null marks "seen more than once, do not use".
            byName[name] = byName.TryGetValue(name, out var existing) && existing != id ? null : id;
        }

        var report = new RelinkReport();

        foreach (var (_, character) in converted)
        {
            var masterId = deriveId(character);

            foreach (var link in character.CompanionLinks)
            {
                var resolved = Resolve(link.SourceFile, link.SourceName, byFile, byName);
                Apply(
                    report, masterId, link.LinkType, link.CompanionId, resolved, knownIds,
                    link.SourceName ?? link.SourceFile,
                    newId => link.CompanionId = newId);
            }

            var origin = character.CompanionOrigin;
            if (origin?.MasterCharacterId is { Length: > 0 })
            {
                var resolved = Resolve(origin.SourceFile, origin.SourceName, byFile, byName);
                Apply(
                    report, masterId, origin.LinkType, origin.MasterCharacterId, resolved, knownIds,
                    origin.SourceName ?? origin.SourceFile,
                    newId => origin.MasterCharacterId = newId);
            }
        }

        return report;
    }

    private static void Apply(
        RelinkReport report,
        string masterId,
        string linkType,
        string currentId,
        string? resolvedId,
        HashSet<string> knownIds,
        string? sourceLabel,
        Action<string> assign)
    {
        if (resolvedId == null)
        {
            // Nothing to map it to. Only worth reporting if the id it holds is also unknown —
            // otherwise the link already points at a real character.
            if (!knownIds.Contains(currentId))
                report.Unresolved.Add(new UnresolvedLink(masterId, linkType, currentId, sourceLabel));
            return;
        }

        if (resolvedId == currentId)
            return;

        assign(resolvedId);
        report.Repointed.Add(new RepointedLink(masterId, linkType, currentId, resolvedId));
    }

    private static string? Resolve(
        string? sourceFile,
        string? sourceName,
        Dictionary<string, string> byFile,
        Dictionary<string, string?> byName)
    {
        var key = FileKey(sourceFile);
        if (key != null && byFile.TryGetValue(key, out var fromFile))
            return fromFile;

        var name = sourceName?.Trim();
        if (!string.IsNullOrEmpty(name) && byName.TryGetValue(name, out var fromName))
            return fromName; // null when the name is ambiguous — treated as unresolved
        return null;
    }

    /// <summary>
    /// Normalizes a PCGen file reference to a comparable key. References are recorded relative to
    /// wherever the user's PCGen save directory was, and may use either slash, so only the file
    /// name is portable.
    /// </summary>
    private static string? FileKey(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;
        var normalized = reference.Replace('\\', '/').Trim();
        var stem = Path.GetFileNameWithoutExtension(normalized);
        return string.IsNullOrWhiteSpace(stem) ? null : stem;
    }
}

public sealed record RepointedLink(string MasterId, string LinkType, string FromId, string ToId);

public sealed record UnresolvedLink(string MasterId, string LinkType, string CompanionId, string? SourceLabel);

public sealed class RelinkReport
{
    public List<RepointedLink> Repointed { get; } = new();
    public List<UnresolvedLink> Unresolved { get; } = new();
}
