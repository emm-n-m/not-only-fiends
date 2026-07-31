namespace NotOnlyFiendsStudio.Models;

public class CharacterState
{
    // Identity
    public string RaceId { get; set; } = string.Empty;
    public CreatureType Type { get; set; }
    public HashSet<string> Subtypes { get; set; } = new();
    public Size Size { get; set; }
    // These are explicit because creature type alone cannot tell whether a creature is
    // a legal target for templates such as Half-Fiend.
    public bool IsLiving { get; set; } = true;
    public bool IsCorporeal { get; set; } = true;
    public Alignment Alignment { get; set; }
    public string? Deity { get; set; }
    public List<string> TemplateIds { get; set; } = new();
    public HashSet<string> Languages { get; set; } = new();

    // Ability Scores (fully modified at current HD)
    public AbilityScoreSet AbilityScores { get; set; } = new();

    // Progression
    public int TotalHD { get; set; }
    public List<string> HDList { get; set; } = new();
    public List<HitDieEntry> HitDice { get; set; } = new();
    public int RacialHitDieSizeAdjustment { get; set; }
    public Dictionary<string, int> ClassLevels { get; set; } = new();

    // Effective level rules — templates/feats can grant bonus effective levels for class features
    public List<EffectiveLevelRule> EffectiveLevelRules { get; set; } = new();

    // Combat — pre-epic base values (frozen at HD 20)
    public int BaseBAB { get; set; }
    public SaveSet BaseSaves { get; set; } = new();

    // Epic bonuses (HD 21+)
    public int EpicAttackBonus { get; set; }
    public int EpicSaveBonus { get; set; }

    // Effective totals (base + epic)
    public int EffectiveBAB => BaseBAB + EpicAttackBonus;
    public SaveSet EffectiveSaves => new()
    {
        Fort = BaseSaves.Fort + EpicSaveBonus + AbilityScoreSet.Modifier(AbilityScores.CON),
        Ref = BaseSaves.Ref + EpicSaveBonus + AbilityScoreSet.Modifier(AbilityScores.DEX),
        Will = BaseSaves.Will + EpicSaveBonus + AbilityScoreSet.Modifier(AbilityScores.WIS)
    };

    // HP
    public int HP { get; set; }

    // Skills — ranks stored as half-ranks (int). 5 ranks = 10, 2.5 ranks = 5.
    public Dictionary<string, int> SkillHalfRanks { get; set; } = new();
    public HashSet<string> ClassSkills { get; set; } = new();
    /// <summary>Class skills for the current tick's driver (used for cost calculation).</summary>
    public HashSet<string> CurrentTickClassSkills { get; set; } = new();
    public int UnspentSkillPoints { get; set; }
    public int MaxHalfRanks { get; set; }
    /// <summary>Racial/misc skill bonuses (separate from ranks). Keyed by skill ID.</summary>
    public Dictionary<string, int> SkillBonuses { get; set; } = new();
    /// <summary>
    /// Synergy bonuses (5 ranks in X → +2 on Y), computed by the skill tail pass. Kept apart from
    /// <see cref="SkillBonuses"/> so that dictionary keeps meaning "what content granted".
    /// </summary>
    public Dictionary<string, int> SkillSynergyBonuses { get; set; } = new();
    /// <summary>
    /// The number a player actually rolls: whole ranks + key ability modifier + SkillBonuses +
    /// SkillSynergyBonuses. Computed by the tail pass, so valid only after evaluation finishes.
    /// Armor check penalty, untrained-use restrictions and situational modifiers are not applied.
    /// </summary>
    public Dictionary<string, int> SkillTotals { get; set; } = new();

    // Feats
    public List<string> Feats { get; set; } = new();
    public Dictionary<FeatType, int> FeatTypeCounts { get; set; } = new();
    public Dictionary<string, int> FeatTagCounts { get; set; } = new();
    public List<FeatSlot> FeatSlots { get; set; } = new();
    public int PendingFeatSlots => FeatSlots.Count(s => s.Restriction == null);
    public int PendingBonusFeatSlots => FeatSlots.Count(s => s.Restriction != null);

    // Spellcasting
    public Dictionary<string, SpellcastingState> Spellcasting { get; set; } = new();
    public List<CasterLevelModifier> CasterLevelModifiers { get; set; } = new();
    public List<ItemActivationLevelRule> ItemActivationLevelRules { get; set; } = new();

    public int EffectiveCasterLevel(string classId, SpellDefinition spell) =>
        Spellcasting.GetValueOrDefault(classId)?.CasterLevel is int baseLevel
            ? baseLevel + CasterLevelModifiers.Where(m => m.Matches(spell)).Sum(m => m.Value)
            : 0;

    // School → levels of every selected spell of that school (lowercase school names,
    // duplicates possible across classes). Recorded at spell-selection time, when the
    // engine has the spell definition in hand, so CanCastSpellSchool can check state
    // alone. Full-list casters never select spells, so — like CanCastSpellLevel — this
    // under-approximates them; the content-aware fix is tracked in TODO §1.
    public Dictionary<string, List<int>> SpellLevelsBySchool { get; set; } = new();

    // Domains — ordered list of selected domain IDs, plus owner map (domainId → granting classId)
    public List<string> Domains { get; set; } = new();
    public Dictionary<string, string> DomainOwners { get; set; } = new();
    // Pending domain picks, keyed by granting classId (cleric, prestige class, etc.)
    public Dictionary<string, int> PendingDomainSelections { get; set; } = new();

    /// <summary>
    /// Requests from <see cref="GrantDomainSpellLikeAbilities"/>, fulfilled by a tail pass once the
    /// domain list is final. Transient scaffolding, not a computed result.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<GrantDomainSpellLikeAbilities> PendingDomainSLAGrants { get; set; } = new();

    // Class Feature Selections (High Arcana, Loremaster Secrets, etc.)
    public Dictionary<string, List<string>> ClassFeatureSelections { get; set; } = new();
    public Dictionary<string, int> PendingClassFeatureSelections { get; set; } = new();

    // Combat — natural
    public int NaturalArmor { get; set; }
    public List<NaturalAttack> NaturalAttacks { get; set; } = new();

    // Level Adjustment / ECL
    public int LevelAdjustment { get; set; }
    public int ECL => TotalHD + LevelAdjustment;

    // Special Abilities
    public List<GrantedAbility> Abilities { get; set; } = new();
    public Dictionary<string, int> Counters { get; set; } = new();
    public List<SLA> SLAs { get; set; } = new();
    public List<SpecialAttack> SpecialAttacks { get; set; } = new();
    public HashSet<string> Immunities { get; set; } = new();
    public HashSet<string> Capabilities { get; set; } = new();
    public Dictionary<string, int> Resistances { get; set; } = new();
    public List<DREntry> DamageReduction { get; set; } = new();
    public int? SpellResistance { get; set; }

    // Movement
    /// <summary>Permanent speeds before armor/load reductions.</summary>
    public Dictionary<MovementMode, int> BaseSpeeds { get; set; } = new();
    public Dictionary<MovementMode, int> Speeds { get; set; } = new();

    // Equipment-derived. Computed post-tick after all class/race/template progression;
    // never written from per-tick code.
    public ArmorClass AC { get; set; } = new();
    public List<AttackLine> AttackLines { get; set; } = new();
    public EncumbranceState Encumbrance { get; set; } = new();

    // Companions/familiars/mounts/cohorts (master-side accumulator).
    // One entry per granter; tail pass recomputes EffectiveLevel against final state.
    public List<CompanionSlotState> CompanionSlots { get; set; } = new();
    // Pending species picks per linkType (parallel to PendingClassFeatureSelections,
    // but keyed on linkType for diagnostics/UX).
    public Dictionary<string, int> PendingCompanionSelections { get; set; } = new();

    // Leadership accumulators. Final values computed in tail pass when feat:leadership present.
    public int LeadershipScore { get; set; }
    public int LeadershipScoreModifier { get; set; }
    public int MaxCohortLevel { get; set; }
    public FollowerCounts Followers { get; set; } = new();

    // Companion-side: only set when this Character is a companion.
    // Studio reads CompanionOrigin.EffectiveMasterLevel into EffectiveMasterLevel
    // at the start of evaluation; templates and formulas can then reference it.
    public CompanionOrigin? CompanionOrigin { get; set; }
    public int EffectiveMasterLevel { get; set; }

    // Validation
    public List<Warning> Warnings { get; set; } = new();
}

public class Warning
{
    public int? TickIndex { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Readable by default. Two Razor views interpolated the instance rather than
    /// <see cref="Message"/> after this stopped being a plain string, and printed the type name to
    /// users instead of the warning — so the fallback is the message, not the class name.
    /// </summary>
    public override string ToString() =>
        TickIndex.HasValue ? $"HD {TickIndex}: {Message}" : Message;
}

public class CompanionSlotState
{
    public string LinkType { get; set; } = string.Empty;
    public string Granter { get; set; } = string.Empty;       // "class:druid" / "feat:leadership"
    public string ClassFeatureType { get; set; } = string.Empty; // selection feature type bound to this slot
    public Formula EffectiveLevelFormula { get; set; } = new();
    public int EffectiveLevel { get; set; }                   // recomputed in tail pass
    public string? SelectedSpecies { get; set; }
}

public class FollowerCounts
{
    public int Level1 { get; set; }
    public int Level2 { get; set; }
    public int Level3 { get; set; }
    public int Level4 { get; set; }
    public int Level5 { get; set; }
    public int Level6 { get; set; }
}

public class SaveSet
{
    public int Fort { get; set; }
    public int Ref { get; set; }
    public int Will { get; set; }

    public int GetSave(string save) => save.ToLowerInvariant() switch
    {
        "fort" => Fort,
        "ref" => Ref,
        "will" => Will,
        _ => throw new ArgumentException($"Unknown save: {save}")
    };
}

public class SaveProgression
{
    public ProgressionRate Fort { get; set; }
    public ProgressionRate Ref { get; set; }
    public ProgressionRate Will { get; set; }
}

public class GrantedAbility
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class SLA
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? UsesPerDay { get; set; }
    public int CasterLevel { get; set; }
    public int? SaveDC { get; set; }
}

public class HitDieEntry
{
    public string DriverId { get; set; } = string.Empty;
    public int DieSize { get; set; }
    public bool IsRacial { get; set; }
}

/// <summary>A durable, player-visible special attack. Combat resolution remains outside replay.</summary>
public class SpecialAttack
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? UsesPerDay { get; set; }
}

/// <summary>Persistent, source-scoped caster-level modifier; it never changes unrelated spells.</summary>
public class CasterLevelModifier
{
    public int Value { get; set; }
    public string? School { get; set; }
    public string? Subschool { get; set; }
    public string? Descriptor { get; set; }
    public bool Matches(SpellDefinition spell) =>
        (School == null || string.Equals(School, spell.School, StringComparison.OrdinalIgnoreCase)) &&
        (Subschool == null || string.Equals(Subschool, spell.Subschool, StringComparison.OrdinalIgnoreCase)) &&
        (Descriptor == null || spell.Descriptors.Any(d => string.Equals(Descriptor, d, StringComparison.OrdinalIgnoreCase)));
}

public class ItemActivationLevelRule
{
    public string ActivationKind { get; set; } = string.Empty;
    public string AsClassId { get; set; } = string.Empty;
    public string SourceClassId { get; set; } = string.Empty;
    public int Divisor { get; set; } = 1;
    public int MinimumLevel { get; set; }
    public int EffectiveLevel(CharacterState state) => Math.Max(MinimumLevel, state.ClassLevels.GetValueOrDefault(SourceClassId) / Divisor);
}

public class DREntry
{
    public int Value { get; set; }
    public string BypassedBy { get; set; } = string.Empty;
}

public class NaturalAttack
{
    public string Name { get; set; } = string.Empty;
    public string Damage { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
    public bool IsPrimary { get; set; } = true;
}

public class SpellcastingState
{
    public string ClassId { get; set; } = string.Empty;
    public CastingType CastingType { get; set; }
    public Ability CastingStat { get; set; }
    public int CasterLevel { get; set; }
    public int MaxSpellLevel { get; set; }
    public Dictionary<int, int> SpellsPerDay { get; set; } = new();
    public Dictionary<int, int>? SpellsKnown { get; set; }
    public List<SpellSelection> SelectedSpells { get; set; } = new();

    // Domain bonus spell slots (spell level → bonus count)
    public Dictionary<int, int> DomainBonusSlots { get; set; } = new();

    /// <summary>
    /// Specialist wizard bonus slots (spell level → bonus count). SRD: "A specialist wizard can
    /// prepare one additional spell of her specialty school per spell level each day." Unlike
    /// domain slots, which start at 1st level, this applies at every level she can cast — 0-level
    /// included, since the rule is stated per spell level with no exception.
    /// </summary>
    public Dictionary<int, int> SpecialtyBonusSlots { get; set; } = new();

    // Stored progression data for AdvanceSpellcasting to use
    public SpellcastingProgression? ProgressionData { get; set; }

    /// <summary>
    /// How this caster acquires spells. Falls back to the same inference
    /// <see cref="SpellcastingProgression.ResolvedAcquisition"/> makes, for the racial-grant paths
    /// that may not carry a progression reference.
    /// </summary>
    public SpellAcquisition Acquisition =>
        ProgressionData?.ResolvedAcquisition
        ?? (SpellsKnown != null ? SpellAcquisition.SpellsKnown : SpellAcquisition.FullList);

    public void ApplyProgression(int casterLevel)
    {
        if (ProgressionData == null) return;

        if (ProgressionData.SpellsPerDay.TryGetValue(casterLevel, out var spd))
        {
            SpellsPerDay = new Dictionary<int, int>(spd);
            MaxSpellLevel = spd.Keys.Max();
        }

        if (ProgressionData.SpellsKnown?.TryGetValue(casterLevel, out var sk) == true)
        {
            SpellsKnown = new Dictionary<int, int>(sk);
        }
    }
}

public class FeatSlot
{
    public string? Restriction { get; set; }
}

public class EffectiveLevelRule
{
    public string TargetDriverId { get; set; } = string.Empty;
    public Formula BonusFormula { get; set; } = new();
}

public class ArmorClass
{
    public Dictionary<BonusType, int> Components { get; set; } = new();
    public int DexContribution { get; set; }
    public int? MaxDexCap { get; set; }
    public int Total { get; set; } = 10;
    public int Touch { get; set; } = 10;
    public int FlatFooted { get; set; } = 10;
}

public class AttackLine
{
    public string Name { get; set; } = string.Empty;
    public List<int> Bonuses { get; set; } = new();
    public string Damage { get; set; } = string.Empty;
    public string Crit { get; set; } = string.Empty;
    public bool IsOffHand { get; set; }
    public bool IsRanged { get; set; }
    public string? Notes { get; set; }
}

public class EncumbranceState
{
    public int TotalWeightLbs { get; set; }
    public LoadCategory Load { get; set; } = LoadCategory.Light;
    public int LightMax { get; set; }
    public int MediumMax { get; set; }
    public int HeavyMax { get; set; }
}

public enum LoadCategory { Light, Medium, Heavy, OverLoad }

// Transient collector used during the post-tick equipment pass. Equipment permabuffs
// push contributions here; ReplayStudio's finalize step applies 3.5e stacking rules
// per (target, bonus-type) and writes the resulting AC / saves / abilities / attack lines
// back to CharacterState.
public class EquipmentPass
{
    public Dictionary<(BonusTarget Target, BonusType Type), List<int>> Contributions { get; } = new();
    public List<ArmorContribution> Armors { get; } = new();
    public List<WeaponContribution> Weapons { get; } = new();
    public int TotalWeightLbs { get; set; }

    public void Add(BonusTarget target, BonusType type, int value)
    {
        var key = (target, type);
        if (!Contributions.TryGetValue(key, out var list))
            Contributions[key] = list = new List<int>();
        list.Add(value);
    }
}

public class ArmorContribution
{
    public ArmorProfile Profile { get; set; } = new();
    public bool AsShield { get; set; }
}

public class WeaponContribution
{
    public string DisplayName { get; set; } = string.Empty;
    public WeaponProfile Profile { get; set; } = new();
    public int EnhancementBonus { get; set; }
    public bool MainHand { get; set; } = true;
    public bool TwoHanded { get; set; }
}
