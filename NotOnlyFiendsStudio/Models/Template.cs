namespace NotOnlyFiendsStudio.Models;

/// <summary>
/// How a template may enter a character build. Inherited templates are creation-time
/// heritage and cannot be manually delayed to a later HD. Acquired templates may be
/// selected at creation or added after the character has started adventuring. Internal
/// templates are implementation helpers, not direct player choices.
/// </summary>
public enum TemplateAcquisitionKind
{
    Internal,
    Inherited,
    Acquired,
}

public class TemplateDriver
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    // Fail closed for newly-authored content: it must be explicitly classified before it
    // appears as a direct character-builder choice.
    public TemplateAcquisitionKind AcquisitionKind { get; set; } = TemplateAcquisitionKind.Internal;

    public bool IsPlayerSelectable => AcquisitionKind != TemplateAcquisitionKind.Internal;

    // Validated against the FINISHED state (tail pass), not at creation: acquired
    // templates like Unseelie Champion gate on class levels that do not exist yet
    // when templates are applied. Unmet prerequisites warn, matching driver behavior.
    public List<Prerequisite> Prerequisites { get; set; } = new();

    // Checked before this template mutates the base creature. Existing Prerequisites
    // intentionally remain final-state checks for acquired-template requirements.
    public List<Prerequisite> ApplicabilityPrerequisites { get; set; } = new();

    // One-time modifications at creation
    public CreatureType? TypeOverride { get; set; }
    public Dictionary<CreatureType, CreatureType> TypeOverridesByBaseType { get; set; } = new();
    public List<string> SubtypeAdditions { get; set; } = new();
    public AbilityScoreSet? AbilityModifiers { get; set; }
    public int? NaturalArmor { get; set; }
    public Dictionary<MovementMode, int> SpeedModifiers { get; set; } = new();
    public FlightManeuverability? FlyManeuverability { get; set; }
    public List<DerivedSpeedRule> DerivedSpeedRules { get; set; } = new();
    public int RacialHitDieSizeAdjustment { get; set; }
    public int? RacialHitDieMaximum { get; set; }

    /// <summary>
    /// "A lich has a +5 natural armor bonus <em>or the base creature's natural armor bonus,
    /// whichever is better</em>" — a floor, where <see cref="NaturalArmor"/> is a delta. The
    /// distinction is the difference between a lich troll at 5 and a lich troll at 10: the
    /// vampire really does say "the base creature's natural armor bonus improves by +6", and the
    /// lich really does not.
    /// </summary>
    public int? NaturalArmorFloor { get; set; }

    /// <summary>
    /// "Increase all current and future Hit Dice to d12s" — the undead templates' hit-die rule,
    /// which pays for having no Constitution score. Unlike
    /// <see cref="RacialHitDieSizeAdjustment"/> this reaches class hit dice too, and it is a
    /// floor rather than a delta: a d12 barbarian is unaffected, and a d6 bard becomes d12.
    /// </summary>
    public int? HitDieSizeFloor { get; set; }

    /// <summary>
    /// Spell resistance from overlapping sources does not stack — the best applies — so a
    /// template's SR is a floor like <see cref="NaturalArmorFloor"/>, never a delta: a
    /// transformation granting SR 12 leaves an alu-fiend's higher racial SR alone.
    /// </summary>
    public int? SpellResistanceFloor { get; set; }
    public int LevelAdjustment { get; set; }
    public List<NaturalAttack> NaturalAttacks { get; set; } = new();
    public List<Permabuff> CreationPermabuffs { get; set; } = new();

    // Scaling: keyed by exact HD. Permabuffs fire ONCE when totalHD == key.
    public SortedDictionary<int, List<Permabuff>> ScalingPermabuffs { get; set; } = new();

    // Formula-based abilities recalculated every tick
    public List<ScalingFormula> ScalingFormulas { get; set; } = new();

    // Companion scaling: keyed by effective MASTER level. Fired in ReplayStudio's
    // tail pass for every entry with key <= state.EffectiveMasterLevel. Used by
    // animal_companion / familiar / special_mount / wild_cohort templates.
    public SortedDictionary<int, List<Permabuff>> CompanionScalingPermabuffs { get; set; } = new();

    public List<Permabuff> GetTickPermabuffs(int totalHD, CharacterState state)
    {
        var buffs = new List<Permabuff>();

        if (ScalingPermabuffs.TryGetValue(totalHD, out var thresholdBuffs))
            buffs.AddRange(thresholdBuffs);

        foreach (var sf in ScalingFormulas)
            buffs.Add(new SetAttribute(sf.Target, sf.Formula.Evaluate(state), sf.ResistanceElement, sf.AbilityScore));

        return buffs;
    }

    // Returns all CompanionScalingPermabuffs entries with key <= effectiveMasterLevel.
    // Called once at the end of evaluation, NOT per tick.
    public List<Permabuff> GetCompanionScalingPermabuffs(int effectiveMasterLevel)
    {
        var buffs = new List<Permabuff>();
        foreach (var (key, perms) in CompanionScalingPermabuffs)
        {
            if (key <= effectiveMasterLevel)
                buffs.AddRange(perms);
            else
                break; // SortedDictionary iterates in key order
        }
        return buffs;
    }
}

public class DerivedSpeedRule
{
    public MovementMode Mode { get; set; }
    public MovementMode SourceMode { get; set; } = MovementMode.Land;
    public int Multiplier { get; set; } = 1;
    public int? Maximum { get; set; }
    public Size? MinimumSize { get; set; }
    /// <summary>Do not replace a permanent speed already greater than the derived value.</summary>
    public bool PreserveBetterExisting { get; set; }
}
