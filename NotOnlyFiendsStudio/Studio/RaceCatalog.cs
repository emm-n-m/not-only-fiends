using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Studio;

/// <summary>
/// Presentation rules for offering races as player-character choices.
///
/// <see cref="RaceDefinition.LevelAdjustment"/> is nullable precisely so this is decidable: 3.5
/// signals that a creature is legal as a PC by printing a Level Adjustment at all, so <c>null</c>
/// means "the source never priced this as a PC race" and <c>0</c> means "playable at no cost", like
/// a Human. Null contributes 0 to ECL — it is a provenance statement, not a different number.
///
/// Lives here rather than in the Blazor layer so the rule is unit-testable and so the API can
/// apply the same one.
/// </summary>
public static class RaceCatalog
{
    /// <summary>
    /// True when the source priced this race for player characters. False for monster, companion
    /// and creature entries that were never given a Level Adjustment.
    /// </summary>
    public static bool IsSanctionedPcRace(RaceDefinition race) => race.LevelAdjustment.HasValue;

    /// <summary>
    /// The races a picker should offer.
    ///
    /// Non-PC races are hidden by default but not removed: the builder is also used to construct
    /// companions and monsters, so a hard filter would break that workflow. <paramref
    /// name="alwaysIncludeId"/> keeps an already-selected race in the list even while non-PC races
    /// are hidden — without it, opening a companion character would show an empty selection and the
    /// picker would quietly revert the choice on blur.
    /// </summary>
    public static IEnumerable<RaceDefinition> ForPicker(
        IEnumerable<RaceDefinition> races,
        bool includeNonPcRaces,
        string? alwaysIncludeId = null)
    {
        if (includeNonPcRaces)
            return races;

        return races.Where(r => IsSanctionedPcRace(r)
                                || (alwaysIncludeId != null && r.Id == alwaysIncludeId));
    }

    /// <summary>
    /// Display suffix marking a race the source never sanctioned for player characters, so the
    /// picker and the sheet say the same thing. Empty for normal races.
    /// </summary>
    public static string NonPcMarker(RaceDefinition race) =>
        IsSanctionedPcRace(race) ? string.Empty : " — non-PC";

    /// <summary>
    /// How the sheet should render a race's level adjustment. Distinguishes "no sanctioned LA"
    /// (null) from "LA +0" (a Human), which previously rendered identically because the sheet
    /// printed nothing unless the value was greater than zero.
    /// </summary>
    public static string DescribeLevelAdjustment(RaceDefinition? race) => race switch
    {
        null => string.Empty,
        { LevelAdjustment: null } => "no sanctioned LA",
        { LevelAdjustment: 0 } => "LA +0",
        { LevelAdjustment: var la } => $"LA +{la}",
    };
}
