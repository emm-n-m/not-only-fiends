namespace NotOnlyFiendsStudio.Models;

public class RaceDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public CreatureType Type { get; set; }
    public List<string> Subtypes { get; set; } = new();
    public Size Size { get; set; }
    /// <summary>
    /// Null (the normal case) derives from <see cref="Type"/> via
    /// <see cref="CreatureTypes.IsLiving"/>. Set it only for a creature that contradicts its type.
    /// </summary>
    public bool? IsLiving { get; set; }
    /// <summary>
    /// Null (the normal case) derives from the presence of the incorporeal subtype. Set it only
    /// for a creature that contradicts its subtypes.
    /// </summary>
    public bool? IsCorporeal { get; set; }
    public Dictionary<MovementMode, int> Speeds { get; set; } = new();
    public FlightManeuverability? FlyManeuverability { get; set; }
    public AbilityScoreSet? AbilityModifiers { get; set; }
    // Nullable so that "playable at no cost" (0, e.g. Human) is distinguishable from
    // "the source never priced this as a PC race" (null, e.g. the Fiendish Codex demons —
    // 3.5 signals PC-legality by printing a Level Adjustment at all). Null contributes 0
    // to ECL; it is a provenance statement, not a different number.
    public int? LevelAdjustment { get; set; }

    /// <summary>
    /// SRD alternative animal companions: "A druid of sufficiently high level can select her
    /// animal companion from one of the following lists, applying the indicated adjustment to the
    /// druid's level (in parentheses) for purposes of determining the companion's characteristics
    /// and special abilities." A Large viper is on the 4th-level list at –3, so a druid 6 fields
    /// it as a druid 3 would. Zero (the default) is the base 1st-level list.
    ///
    /// Distinct from <see cref="LevelAdjustment"/>, which prices a race as a PC.
    /// </summary>
    public int CompanionLevelModifier { get; set; }
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

    /// <summary>
    /// Templates every creature of this race carries, applied at creation before the character's
    /// own so those layer on top. This is where a race says what it <em>is</em> in template terms:
    /// an Archfiend casts as its own HD, rebukes undead and draws on two domains, and that is true
    /// of every Archfiend whether or not a saved character happens to list it.
    ///
    /// Kept on the race rather than copied into each character so it cannot go stale — changing a
    /// race's identity changes every creature of it, and a character whose race changes stops
    /// carrying the old one. Listing the same template on the character too is harmless; it applies
    /// once.
    /// </summary>
    public List<string> ImpliedTemplateIds { get; set; } = new();

    /// <summary>
    /// PCGen's <c>MONSTERCLASS:&lt;class&gt;:&lt;hd&gt;</c> — a race whose hit dice are levels of a
    /// monster <em>class</em> rather than a generic <c>racial_hd:</c> driver. The Archfiend race is
    /// the case in hand: it arrives as 24 levels of <c>class:archfiend</c>, and a character may then
    /// buy more levels of that same class on top.
    ///
    /// The driver stays one continuous run and keeps <see cref="DriverKind.Class"/>, because that is
    /// what those levels are for everything the chassis computes: base saves are per class, taken
    /// once from the class's total level, so splitting a 29-level run into 24 racial + 5 class would
    /// pay the level-1 "+2" of a good save twice (2 + 24/2 = 14 plus 2 + 5/2 = 4, against the correct
    /// 2 + 29/2 = 16).
    ///
    /// What the free HD do change is the ability-increase schedule: they are not levels the character
    /// earned, so the every-four-levels increase counts from the end of the allotment. Ember's three
    /// increases land at total HD 28, 32 and 36 — character levels 4, 8 and 12 — which is exactly
    /// what PCGen recorded for her.
    /// </summary>
    public string? MonsterClassDriverId { get; set; }

    /// <inheritdoc cref="MonsterClassDriverId"/>
    public int? MonsterClassHD { get; set; }

    /// <summary>
    /// Hit dice that came free with the race and so do not count toward the character's own level.
    /// Zero for every ordinary race, including races with a <c>racial_hd:</c> driver — those ticks
    /// are already excluded by their driver kind.
    /// </summary>
    public int FreeMonsterClassHD => MonsterClassDriverId == null ? 0 : MonsterClassHD ?? 0;

    // Delta to the racial HD driver's class skills (add/remove specific skills)
    public List<string> RacialClassSkillAdditions { get; set; } = new();
    public List<string> RacialClassSkillRemovals { get; set; } = new();

    // Flat abilities applied at creation
    public List<Permabuff> RacialPermabuffs { get; set; } = new();

    /// <summary>
    /// Permabuffs applied to the <b>master</b> when a creature of this race is bound as a familiar
    /// — the SRD's "Familiar Special" column, where the benefit runs the other way (a toad familiar
    /// gives its master +3 hit points, a rat +2 on Fortitude saves). Ignored unless the creature is
    /// actually linked as a familiar, so an unbound animal of the same race grants nothing.
    /// Conditional entries — the hawk's Spot in bright light, the owl's in shadows — are absent:
    /// the engine has no representation for a situational bonus.
    /// </summary>
    public List<Permabuff> FamiliarMasterPermabuffs { get; set; } = new();

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
