using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Studio;

/// <summary>
/// Specialist wizard schools. A wizard may specialize in one school of magic and gives up others
/// in exchange; spells of a given-up school can never be learned, written into the spellbook or
/// cast.
///
/// Both choices are stored as ordinary <see cref="CharacterState.ClassFeatureSelections"/> entries,
/// so they need no new state and reach the sheet and the API for free. This type is the single
/// place that knows the feature-type ids and the rule, so the builder's filtering and the engine's
/// validation cannot drift apart.
/// </summary>
public static class WizardSchools
{
    public const string SpecializationFeature = "class_feature:wizard_specialization";
    public const string ProhibitedFeature = "class_feature:wizard_prohibited_schools";

    /// <summary>Option ids are "school:&lt;name&gt;"; spell definitions carry the bare name.</summary>
    public const string OptionPrefix = "school:";

    /// <summary>
    /// SRD: "Spells that do not fall into any of these schools are called universal spells." They
    /// belong to no school, so they are never prohibited and are always available.
    /// </summary>
    public const string Universal = "universal";

    /// <summary>
    /// SRD: "A wizard can never give up divination to fulfill this requirement." It can still be
    /// specialized in — at the reduced cost of one prohibited school instead of two.
    /// </summary>
    public const string Divination = "divination";

    public static string ToSchoolName(string optionId) =>
        optionId.StartsWith(OptionPrefix, StringComparison.Ordinal)
            ? optionId[OptionPrefix.Length..]
            : optionId;

    public static string ToOptionId(string schoolName) => OptionPrefix + schoolName;

    /// <summary>The school this character specializes in, or null for a universalist.</summary>
    public static string? Specialty(CharacterState state) =>
        state.ClassFeatureSelections.TryGetValue(SpecializationFeature, out var picks) && picks.Count > 0
            ? ToSchoolName(picks[0])
            : null;

    /// <summary>Bare school names the character has given up. Empty for a universalist.</summary>
    public static IReadOnlyCollection<string> ProhibitedSchools(CharacterState state) =>
        state.ClassFeatureSelections.TryGetValue(ProhibitedFeature, out var picks)
            ? picks.Select(ToSchoolName).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : Array.Empty<string>();

    /// <summary>
    /// True when this character may never learn or cast the given spell's school. Universal is
    /// never prohibited; an unknown or empty school is treated as allowed rather than guessed at.
    /// </summary>
    public static bool IsProhibited(CharacterState state, string? school)
    {
        if (string.IsNullOrEmpty(school)
            || string.Equals(school, Universal, StringComparison.OrdinalIgnoreCase))
            return false;

        return ProhibitedSchools(state).Contains(school);
    }

    /// <summary>
    /// The 0-level spells a wizard's spellbook holds without spending any of its budget.
    ///
    /// SRD: "A wizard begins play with a spellbook containing all 0-level wizard spells (except
    /// those from her prohibited school or schools, if any)". So "all cantrips" is not quite the
    /// rule — a specialist loses the ones from the schools it gave up, exactly as at every other
    /// spell level.
    /// </summary>
    public static IEnumerable<SpellDefinition> AutomaticCantrips(
        IEnumerable<SpellDefinition> spells, string classId, CharacterState state) =>
        spells.Where(s => s.ClassLevels.TryGetValue(classId, out var level) && level == 0)
              .Where(s => !IsProhibited(state, s.School));

    /// <summary>
    /// How many schools a wizard must give up for its chosen specialty.
    ///
    /// SRD: "she must also give up two other schools of magic (unless she chooses to specialize in
    /// divination...)". A universalist gives up none.
    /// </summary>
    public static int RequiredProhibitedCount(string? specialty) => specialty switch
    {
        null => 0,
        Divination => 1,
        _ => 2,
    };
}
