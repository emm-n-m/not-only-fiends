namespace NotOnlyFiendsStudio.Models;

public class CharacterState
{
    // Identity
    public string RaceId { get; set; } = string.Empty;
    public CreatureType Type { get; set; }
    public HashSet<string> Subtypes { get; set; } = new();
    public Size Size { get; set; }
    public Alignment Alignment { get; set; }
    public List<string> TemplateIds { get; set; } = new();
    public HashSet<string> Languages { get; set; } = new();

    // Ability Scores (fully modified at current HD)
    public AbilityScoreSet AbilityScores { get; set; } = new();

    // Progression
    public int TotalHD { get; set; }
    public List<string> HDList { get; set; } = new();
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

    // Feats
    public List<string> Feats { get; set; } = new();
    public Dictionary<FeatType, int> FeatTypeCounts { get; set; } = new();
    public Dictionary<string, int> FeatTagCounts { get; set; } = new();
    public List<FeatSlot> FeatSlots { get; set; } = new();
    public int PendingFeatSlots => FeatSlots.Count(s => s.Restriction == null);
    public int PendingBonusFeatSlots => FeatSlots.Count(s => s.Restriction != null);

    // Spellcasting
    public Dictionary<string, SpellcastingState> Spellcasting { get; set; } = new();

    // Domains — ordered list of selected domain IDs, plus owner map (domainId → granting classId)
    public List<string> Domains { get; set; } = new();
    public Dictionary<string, string> DomainOwners { get; set; } = new();
    // Pending domain picks, keyed by granting classId (cleric, prestige class, etc.)
    public Dictionary<string, int> PendingDomainSelections { get; set; } = new();

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
    public HashSet<string> Immunities { get; set; } = new();
    public HashSet<string> Capabilities { get; set; } = new();
    public Dictionary<string, int> Resistances { get; set; } = new();
    public List<DREntry> DamageReduction { get; set; } = new();
    public int? SpellResistance { get; set; }

    // Movement
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

    // Stored progression data for AdvanceSpellcasting to use
    public SpellcastingProgression? ProgressionData { get; set; }

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
