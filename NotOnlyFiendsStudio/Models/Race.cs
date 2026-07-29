namespace NotOnlyFiendsStudio.Models;

public class RaceDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public CreatureType Type { get; set; }
    public List<string> Subtypes { get; set; } = new();
    public Size Size { get; set; }
    public Dictionary<MovementMode, int> Speeds { get; set; } = new();
    public AbilityScoreSet? AbilityModifiers { get; set; }
    // Nullable so that "playable at no cost" (0, e.g. Human) is distinguishable from
    // "the source never priced this as a PC race" (null, e.g. the Fiendish Codex demons —
    // 3.5 signals PC-legality by printing a Level Adjustment at all). Null contributes 0
    // to ECL; it is a provenance statement, not a different number.
    public int? LevelAdjustment { get; set; }
    public int BonusFeats { get; set; }
    public int BonusSkillPointsPerHD { get; set; }

    /// <summary>Languages every member of the race speaks, granted at creation.</summary>
    public List<string> AutomaticLanguages { get; set; } = new();

    /// <summary>
    /// The languages this race may spend Int-based bonus language picks on. Ignored when
    /// <see cref="BonusLanguagesAny"/> is set.
    /// </summary>
    public List<string> BonusLanguages { get; set; } = new();

    /// <summary>
    /// The race may pick <em>any</em> non-secret language, as humans and half-elves do in the SRD.
    /// A flag rather than a wildcard entry in <see cref="BonusLanguages"/> so the "except secret
    /// languages" half of that rule is expressed by the data model instead of by a magic string.
    /// </summary>
    public bool BonusLanguagesAny { get; set; }

    // Racial HD driver ID — null for races with no racial HD (Human, etc.)
    public string? RacialHDDriverId { get; set; }

    // Delta to the racial HD driver's class skills (add/remove specific skills)
    public List<string> RacialClassSkillAdditions { get; set; } = new();
    public List<string> RacialClassSkillRemovals { get; set; } = new();

    // Flat abilities applied at creation
    public List<Permabuff> RacialPermabuffs { get; set; } = new();

    // Formula-based abilities that scale with total HD
    public List<ScalingFormula> ScalingFormulas { get; set; } = new();

    public List<NaturalAttack> NaturalAttacks { get; set; } = new();
}

public class ScalingFormula
{
    public AttributeTarget Target { get; set; }
    public string? ResistanceElement { get; set; }
    public Ability? AbilityScore { get; set; }
    public Formula Formula { get; set; } = new();
}
