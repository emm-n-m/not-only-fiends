namespace NotOnlyFiendsStudio.Models;

/// <summary>
/// The id grammar for parametrized feats. Canonical form appends the selection with a colon —
/// "feat:skill_focus:spellcraft", "feat:weapon_focus:longsword" — following the catalog's
/// "type:instance" convention (compare "staff:abjuration"). The selection segment is always the
/// bare instance id, never re-prefixed with its content type: the feat's selectionRequired kind
/// already says what list it comes from.
///
/// Two legacy underscore dialects predate this and live in saved characters forever, because
/// saves are never rewritten: the bare-suffix form ("feat:skill_focus_spellcraft", produced by
/// the PCGen importer and old content) and the full-id form ("feat:skill_focus_skill:spellcraft",
/// produced by the builder UI). Readers therefore normalize any dialect through this class and
/// compare canonical ids; only writers emit the canonical form.
/// </summary>
public static class FeatVariantId
{
    private static readonly string[] SelectionTypePrefixes = { "skill:", "weapon:", "spell:", "school:" };

    /// <summary>The canonical variant id for a base feat and a selection in any dialect.</summary>
    public static string Canonical(string baseFeatId, string selection) =>
        $"{baseFeatId}:{NormalizeSelection(selection)}";

    /// <summary>Strips a content-type prefix from a selection, leaving the bare instance id.</summary>
    public static string NormalizeSelection(string selection)
    {
        foreach (var prefix in SelectionTypePrefixes)
            if (selection.StartsWith(prefix, StringComparison.Ordinal))
                return selection[prefix.Length..];
        return selection;
    }

    /// <summary>
    /// Extracts the raw (un-normalized) selection from a variant id in any dialect.
    /// False for the bare base id or an unrelated id.
    /// </summary>
    public static bool TryGetSelection(string featId, string baseFeatId, out string selection)
    {
        if (IsVariant(featId, baseFeatId))
        {
            selection = featId[(baseFeatId.Length + 1)..];
            return true;
        }

        selection = string.Empty;
        return false;
    }

    /// <summary>True when the id is a variant of the base feat, in either the canonical or a legacy dialect.</summary>
    public static bool IsVariant(string featId, string baseFeatId) =>
        featId.Length > baseFeatId.Length + 1
        && featId.StartsWith(baseFeatId, StringComparison.Ordinal)
        && featId[baseFeatId.Length] is ':' or '_';

    /// <summary>
    /// True when the id is the base feat itself or any variant of it. This is the prerequisite
    /// match: a prereq naming a base feat is satisfied by any selection of it, and a prereq
    /// naming a partial selection ("feat:skill_focus:knowledge") is satisfied by any completion
    /// ("feat:skill_focus:knowledge_arcana").
    /// </summary>
    public static bool IsBaseOrVariant(string featId, string baseFeatId) =>
        featId == baseFeatId || IsVariant(featId, baseFeatId);
}
