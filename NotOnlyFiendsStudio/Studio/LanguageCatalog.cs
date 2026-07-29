using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Studio;

/// <summary>
/// The rules for offering bonus languages as choices.
///
/// Lives here rather than in the Blazor layer so the builder, the REST API and the engine's own
/// validation all apply one rule — the race picker's PC-sanctioning check was written in the UI
/// first and the API silently disagreed with it for a while, which is the mistake this avoids.
/// </summary>
public static class LanguageCatalog
{
    /// <summary>
    /// How many bonus languages a character may choose: one per point of starting Intelligence
    /// modifier, and never negative. "Starting" means race and template modifiers plus the base
    /// scores, before any level-up ability increase — 3.5 prices bonus languages once, at creation.
    /// </summary>
    public static int Allowance(int startingIntelligence) =>
        Math.Max(0, AbilityScoreSet.Modifier(startingIntelligence));

    /// <summary>
    /// The languages a race may spend its picks on: its own bonus list, or every non-secret
    /// language for races the source describes as taking "any" (human, half-elf). Languages the
    /// race already speaks automatically are excluded — paying a pick for one would be a trap.
    /// </summary>
    public static IEnumerable<LanguageDefinition> OfferedBonusLanguages(
        RaceDefinition? race,
        IEnumerable<LanguageDefinition> allLanguages)
    {
        if (race is null) return Array.Empty<LanguageDefinition>();

        var automatic = race.AutomaticLanguages.ToHashSet(StringComparer.Ordinal);

        var offered = race.BonusLanguagesAny
            ? allLanguages.Where(l => !l.IsSecret)
            : allLanguages.Where(l => race.BonusLanguages.Contains(l.Id, StringComparer.Ordinal));

        return offered.Where(l => !automatic.Contains(l.Id)).OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase);
    }
}
