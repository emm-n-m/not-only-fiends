namespace NotOnlyFiendsStudio.Models;

public class CharacterState
{
    // Identity
    public string RaceId { get; set; } = string.Empty;
    public CreatureType Type { get; set; }
    /// <summary>
    /// The race's own creature type, before any template moved it. Recorded so that revoking
    /// a template can recompute the type from the bottom of the stack: base race, then every
    /// still-applied template's override in application order.
    /// </summary>
    public CreatureType BaseRaceType { get; set; }

    /// <summary>
    /// The HD after which the race's per-HD bonus skill points stop accruing — an ascension
    /// ends the character's racial identity going forward while the race remains on the sheet
    /// as her origin. The acquisition tick itself still pays (the level completes, then you
    /// transform); everything banked before it stays banked. Null = never ends.
    /// </summary>
    public int? RacialBonusSkillPointsEndAfterHD { get; set; }
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

    /// <summary>
    /// Hit dice that arrived free with a monster race and so are not levels the character earned —
    /// <see cref="RaceDefinition.MonsterClassHD"/>. Zero for every ordinary race. Character level for
    /// the every-four-levels ability increase is <see cref="TotalHD"/> minus this.
    /// </summary>
    public int FreeMonsterClassHD { get; set; }
    public List<string> HDList { get; set; } = new();
    public List<HitDieEntry> HitDice { get; set; } = new();
    public int RacialHitDieSizeAdjustment { get; set; }

    /// <summary>
    /// Smallest hit die any of this creature's HD may roll, from an undead template's "increase
    /// all current and future Hit Dice to d12s". Zero when nothing has raised it.
    /// </summary>
    public int HitDieSizeFloor { get; set; }
    public Dictionary<string, int> ClassLevels { get; set; } = new();

    // Effective level rules — templates/feats can grant bonus effective levels for class features
    public List<EffectiveLevelRule> EffectiveLevelRules { get; set; } = new();

    /// <summary>
    /// "Casts as an Nth-level &lt;class&gt;" grants collected as they are applied, from a race or a
    /// template alike, and seeded into <see cref="Spellcasting"/> by the engine's finalize phase —
    /// which must run after the ticks, so a formula reading HD sees the final count.
    /// </summary>
    public List<GrantRacialSpellcasting> RacialSpellcastingGrants { get; set; } = new();

    // Combat — pre-epic base values (frozen at HD 20)
    public int BaseBAB { get; set; }
    public SaveSet BaseSaves { get; set; } = new();

    /// <summary>
    /// Save progression contributed by HD drivers only. This remains separate from
    /// <see cref="BaseSaves"/> because that legacy total also receives racial, feat,
    /// template, and equipment bonuses. Familiar inheritance needs the master's base
    /// save bonuses as calculated from class and racial-HD progression.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public SaveSet ProgressionBaseSaves { get; set; } = new();

    // Epic bonuses (HD 21+)
    public int EpicAttackBonus { get; set; }
    public int EpicSaveBonus { get; set; }

    /// <summary>
    /// Class features that add an ability modifier to every save — paladin Divine Grace, blackguard
    /// Dark Blessing. Held as rules rather than folded into <see cref="BaseSaves"/> at the granting
    /// tick, because the modifier tracks the *final* score: level-up increases, tomes and worn items
    /// all land after the class level that grants the feature, and a value banked at that tick would
    /// silently understate every one of them.
    /// </summary>
    public List<AbilitySaveBonus> AbilitySaveBonuses { get; set; } = new();

    /// <summary>
    /// Ability-derived AC bonuses that must be evaluated after final ability scores and worn
    /// equipment are known. Examples are monk Wisdom AC and nymph Charisma deflection.
    /// </summary>
    public List<AbilityACBonus> AbilityACBonuses { get; set; } = new();

    /// <summary>
    /// Permanent typed save bonuses from feats and racial/class features. They are kept separate
    /// from progression so the normal 3.5e stacking rule (highest bonus of each type) still
    /// applies when several sources grant the same save bonus.
    /// </summary>
    public List<SaveBonus> SaveBonuses { get; set; } = new();

    /// <summary>
    /// Total ability-modifier save bonus. Distinct sources stack (they are untyped bonuses from
    /// different class features); the same source granted more than once does not, so a feature
    /// re-applied by a scaling or template path cannot double.
    /// </summary>
    public int AbilitySaveBonusTotal => AbilitySaveBonuses
        .DistinctBy(bonus => bonus.SourceId, StringComparer.Ordinal)
        .Sum(bonus =>
        {
            var modifier = AbilityModifier(bonus.Ability);
            return bonus.PositiveOnly ? Math.Max(0, modifier) : modifier;
        });

    /// <summary>
    /// Whether the creature has this ability score at all. SRD "Nonabilities": some creatures
    /// lack an ability entirely rather than having a score of 0. Undead and constructs have no
    /// Constitution — the d12 Hit Die is what pays for it — and an incorporeal creature has no
    /// Strength, using Dexterity for its attacks instead.
    ///
    /// Derived from what the creature is, for the same reason <see cref="IsLiving"/> and
    /// <see cref="IsCorporeal"/> are: content that had to restate it would drift from the type.
    /// </summary>
    public bool HasAbility(Ability ability) => ability switch
    {
        Ability.CON => IsLiving,
        Ability.STR => IsCorporeal,
        _ => true,
    };

    /// <summary>
    /// The modifier for an ability, which is +0 for a nonability — not the −5 its placeholder
    /// score would otherwise produce. Every rule that reads an ability modifier goes through
    /// here so a missing ability cannot leak in as a penalty.
    /// </summary>
    public int AbilityModifier(Ability ability) =>
        HasAbility(ability) ? AbilityScoreSet.Modifier(AbilityScores.GetScore(ability)) : 0;

    // Effective totals (base + epic)
    public int EffectiveBAB => BaseBAB + EpicAttackBonus;
    public SaveSet EffectiveSaves => new()
    {
        Fort = BaseSaves.Fort + EpicSaveBonus + AbilityModifier(Ability.CON) + AbilitySaveBonusTotal + SaveBonusTotal(SaveTarget.Fort) - EquipmentNegativeLevels,
        Ref = BaseSaves.Ref + EpicSaveBonus + AbilityModifier(Ability.DEX) + AbilitySaveBonusTotal + SaveBonusTotal(SaveTarget.Ref) - EquipmentNegativeLevels,
        Will = BaseSaves.Will + EpicSaveBonus + AbilityModifier(Ability.WIS) + AbilitySaveBonusTotal + SaveBonusTotal(SaveTarget.Will) - EquipmentNegativeLevels
    };

    private int SaveBonusTotal(SaveTarget target) =>
        SaveBonuses.Where(bonus => bonus.Target == target)
            .GroupBy(bonus => bonus.BonusType)
            .Sum(group => group.Key is BonusType.Dodge or BonusType.Untyped
                ? group.Sum(bonus => bonus.Value)
                : Math.Max(0, group.Max(bonus => bonus.Value)));

    // HP
    public int HP { get; set; }

    /// <summary>Flat hit-point grants that must survive the Constitution tail pass.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int FlatHitPointBonuses { get; set; }

    // Skills — ranks stored as half-ranks (int). 5 ranks = 10, 2.5 ranks = 5.
    public Dictionary<string, int> SkillHalfRanks { get; set; } = new();
    public HashSet<string> ClassSkills { get; set; } = new();
    /// <summary>Class skills for the current tick's driver (used for cost calculation).</summary>
    public HashSet<string> CurrentTickClassSkills { get; set; } = new();
    public int UnspentSkillPoints { get; set; }
    public int MaxHalfRanks { get; set; }
    /// <summary>Per-HD accruals that explain the skill-point pool to API callers and the UI.</summary>
    public List<SkillPointAccrual> SkillPointAccruals { get; set; } = new();
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
    public List<PreparedSpellSelection> PreparedSpellSelections { get; set; } = new();
    public List<CasterLevelModifier> CasterLevelModifiers { get; set; } = new();
    public List<ItemActivationLevelRule> ItemActivationLevelRules { get; set; } = new();

    /// <summary>
    /// Extra spell lists a caster may draw from beyond the list its HD driver declares. Populated
    /// by <see cref="AddSpellListSource"/> permabuffs (e.g. an Archfiend's choose-your-list
    /// template borrowing the sorcerer/cleric/druid list). Read by the spell-selection validator
    /// in addition to the driver-level <see cref="SpellcastingProgression.SpellListSources"/>.
    /// </summary>
    public List<SpellListSourceRule> ExtraSpellListSources { get; set; } = new();

    /// <summary>
    /// Domain owners (class ids) whose domain selections add the domain's spell list to the
    /// caster's known-spell pool instead of granting cleric-style prepared domain slots and
    /// granted powers. Set by <see cref="GrantDomainSelection"/> with <c>AsSpellListSources</c>
    /// — the Archfiend / Red Dragon pattern: an arcane spontaneous caster who simply *knows*
    /// its domain spells rather than preparing them from a bonus slot.
    /// </summary>
    public HashSet<string> SpellListSourceDomainOwners { get; set; } = new();

    /// <summary>
    /// The caster level currently available to the character after equipment-derived negative
    /// levels. The underlying spellcasting progression remains unchanged because equipment is
    /// applied after the HD timeline and must not retroactively alter level-up choices.
    /// </summary>
    public int EffectiveCasterLevel(string classId) =>
        Spellcasting.GetValueOrDefault(classId)?.CasterLevel is int baseLevel
            ? Math.Max(0, baseLevel - EquipmentNegativeLevels)
            : 0;

    public int EffectiveCasterLevel(string classId, SpellDefinition spell) =>
        Spellcasting.GetValueOrDefault(classId)?.CasterLevel is int baseLevel
            ? Math.Max(0, baseLevel + CasterLevelModifiers.Where(m => m.Matches(spell)).Sum(m => m.Value)
                - EquipmentNegativeLevels)
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
    /// Domains a granting class may spend its picks on, keyed by the same classId. Absent means
    /// unrestricted, which is the usual case; present means the class named a shorter list.
    /// </summary>
    public Dictionary<string, List<string>> DomainSelectionRestrictions { get; set; } = new();

    /// <summary>
    /// Spell-list exclusions created by a domain choice. The outer key is the affected class or
    /// list id and the value contains spell ids that are unavailable after that choice. This is
    /// separate from the static <see cref="HDDriver.Spellcasting"/> exclusions because a domain
    /// choice is a character decision made during replay.
    /// </summary>
    public Dictionary<string, HashSet<string>> DynamicSpellListExclusions { get; set; } = new();

    /// <summary>Selected-domain → opposed-domain rules declared by granting content.</summary>
    public Dictionary<string, Dictionary<string, string>> DomainSpellListExclusionRules { get; set; } = new();

    /// <summary>Returns whether replay has removed a spell from this class/list for this character.</summary>
    public bool IsSpellExcludedFromList(string spellListId, string spellId) =>
        DynamicSpellListExclusions.GetValueOrDefault(spellListId)?.Contains(spellId) == true;

    /// <summary>
    /// Variant class id → the class it varies, mirrored from <see cref="HDDriver.VariantOf"/> so
    /// formulas can resolve it without the registry. Read by <c>EffectiveClassLevel()</c>: a rule
    /// raising "your ranger level" reaches a planar ranger too.
    /// </summary>
    public Dictionary<string, string> ClassVariantBases { get; set; } = new();

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
    /// <summary>
    /// Language picks granted outright rather than bought from the starting-Intelligence budget.
    /// Spent by <c>Character.GrantedLanguageIds</c>.
    /// </summary>
    public int GrantedLanguageSlots { get; set; }

    /// <summary>What granted those slots, for the builder to label the picker with.</summary>
    public List<string> GrantedLanguageSources { get; set; } = new();

    public HashSet<string> Immunities { get; set; } = new();
    public HashSet<string> Capabilities { get; set; } = new();
    public Dictionary<string, int> Resistances { get; set; } = new();
    public List<DREntry> DamageReduction { get; set; } = new();
    public int? SpellResistance { get; set; }
    /// <summary>Hit points regained at the start of each turn, when the creature has at least 1 HP.</summary>
    public int FastHealing { get; set; }
    /// <summary>Bonus on checks to resist being turned or rebuked.</summary>
    public int TurnResistance { get; set; }

    // Movement
    /// <summary>Permanent speeds before armor/load reductions.</summary>
    public Dictionary<MovementMode, int> BaseSpeeds { get; set; } = new();
    public Dictionary<MovementMode, int> Speeds { get; set; } = new();
    public FlightManeuverability? FlyManeuverability { get; set; }

    // Equipment-derived. Computed post-tick after all class/race/template progression;
    // never written from per-tick code.
    public ArmorClass AC { get; set; } = new();
    public List<AttackLine> AttackLines { get; set; } = new();
    public EncumbranceState Encumbrance { get; set; } = new();
    public List<IntelligentItemState> IntelligentItems { get; set; } = new();
    /// <summary>Temporary negative levels imposed by equipped intelligent items.</summary>
    public int EquipmentNegativeLevels { get; set; }
    /// <summary>
    /// Typed AC/attack/damage contributions granted by feats, class features, templates, or
    /// permanent events. Equipment uses the transient <see cref="EquipmentPass"/> collector;
    /// these survive until the same final combat pass runs.
    /// </summary>
    public List<TypedBonusContribution> PersistentBonusContributions { get; set; } = new();
    public List<WeaponBonusContribution> WeaponBonusContributions { get; set; } = new();

    // Companions/familiars/mounts/cohorts (master-side accumulator).
    // One entry per granter; tail pass recomputes EffectiveLevel against final state.
    public List<CompanionSlotState> CompanionSlots { get; set; } = new();
    // Pending species picks per linkType (parallel to PendingClassFeatureSelections,
    // but keyed on linkType for diagnostics/UX).
    public Dictionary<string, int> PendingCompanionSelections { get; set; } = new();

    // Leadership accumulators. Final values computed in tail pass when feat:leadership present.
    public int LeadershipScore { get; set; }
    public int LeadershipScoreModifier { get; set; }

    /// <summary>
    /// Effective Leadership score for attracting a cohort: the base score plus reputation, minus
    /// the cohort-side penalties. The SRD's modifier groups differ for cohorts and followers, so
    /// one number cannot serve both.
    /// </summary>
    public int LeadershipCohortScore { get; set; }

    /// <summary>Effective Leadership score for attracting followers.</summary>
    public int LeadershipFollowerScore { get; set; }

    /// <summary>
    /// Why the two scores differ from the base, in the order the SRD lists them. Display only —
    /// the sheet has to be able to explain a number a player did not expect.
    /// </summary>
    public List<string> LeadershipModifierNotes { get; set; } = new();

    public int MaxCohortLevel { get; set; }

    /// <summary>
    /// Followers actually linked, counted by the level they occupy — which is their **ECL**, not
    /// their hit dice. A level-adjusted follower costs a leader a higher slot than its HD suggests:
    /// a 6 HD aranea with LA +4 fills a 10th-level slot. Populated host-side by
    /// <c>CompanionResolver</c>, which is the only place the followers themselves are evaluated.
    /// Compare against <see cref="Followers"/> for capacity.
    /// </summary>
    public Dictionary<int, int> FollowerOccupancy { get; set; } = new();
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

public class SkillPointAccrual
{
    public string Source { get; set; } = string.Empty;
    public int BasePoints { get; set; }
    public int IntelligenceModifier { get; set; }
    public int FirstHdMultiplier { get; set; } = 1;
    public int Points { get; set; }
}

public class CompanionSlotState
{
    public string LinkType { get; set; } = string.Empty;
    public string Granter { get; set; } = string.Empty;       // "class:druid" / "feat:leadership"
    public string ClassFeatureType { get; set; } = string.Empty; // selection feature type bound to this slot
    public Formula EffectiveLevelFormula { get; set; } = new();
    public int EffectiveLevel { get; set; }                   // recomputed in tail pass
    public string? SelectedSpecies { get; set; }
    public string? SelectedTemplateId { get; set; }
}

/// <summary>
/// How many followers of each level a character can lead. Keyed by follower level rather than
/// held in fixed properties because the ceiling is not fixed: the base table stops at 6th and
/// Table: Epic Leadership prints to 10th, but the halving rule that continues past the printed
/// table keeps producing followers as the score climbs — a Leadership score of 60 reaches 11th
/// and 12th. The only hard limit is the SRD's "A character can't have a follower of higher than
/// 20th level".
/// </summary>
public class FollowerCounts
{
    /// <summary>The SRD's ceiling: no follower may be above 20th level.</summary>
    public const int MaxFollowerLevel = 20;

    /// <summary>Follower level → count. Levels the character cannot field are simply absent.</summary>
    public Dictionary<int, int> ByLevel { get; set; } = new();

    public int At(int level) => ByLevel.GetValueOrDefault(level);

    /// <summary>Highest follower level with at least one follower; 0 when there are none.</summary>
    public int HighestLevel => ByLevel.Count == 0
        ? 0
        : ByLevel.Where(entry => entry.Value > 0).Select(entry => entry.Key).DefaultIfEmpty(0).Max();

    // Convenience accessors for the six levels the base table prints; the sheet and most tests
    // only ever ask about these.
    public int Level1 { get => At(1); set => ByLevel[1] = value; }
    public int Level2 { get => At(2); set => ByLevel[2] = value; }
    public int Level3 { get => At(3); set => ByLevel[3] = value; }
    public int Level4 { get => At(4); set => ByLevel[4] = value; }
    public int Level5 { get => At(5); set => ByLevel[5] = value; }
    public int Level6 { get => At(6); set => ByLevel[6] = value; }
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

    // Runtime provenance used by replay to keep HD-scaled SLAs current. This is
    // deliberately not part of CharacterState's public JSON contract.
    internal bool CasterLevelTracksTotalHD { get; set; }
}

public class HitDieEntry
{
    public string DriverId { get; set; } = string.Empty;
    public int DieSize { get; set; }
    public bool IsRacial { get; set; }
    public int? SavedRoll { get; set; }
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

/// <summary>
/// One "add this ability's modifier to all saving throws" class feature, e.g. paladin Divine Grace
/// or blackguard Dark Blessing. <see cref="SourceId"/> identifies the feature so repeat grants of
/// the same one collapse, while two different features still stack.
/// </summary>
public class AbilitySaveBonus
{
    public string SourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Ability Ability { get; set; }

    /// <summary>
    /// SRD wording for both known cases is "applies his Charisma modifier (if positive)", so a
    /// penalty is not carried over to saves. Content can opt out for a feature that says otherwise.
    /// </summary>
    public bool PositiveOnly { get; set; } = true;
}

public class AbilityACBonus
{
    public string SourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Ability? Ability { get; set; }
    public BonusType BonusType { get; set; } = BonusType.Untyped;
    public Formula Value { get; set; } = new();
    public bool PositiveOnly { get; set; } = true;
    public bool RequiresUnarmored { get; set; }
    public bool RequiresUnencumbered { get; set; }
}

public class SaveBonus
{
    public SaveTarget Target { get; set; }
    public BonusType BonusType { get; set; } = BonusType.Untyped;
    public int Value { get; set; }
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

    /// <summary>
    /// How much of <see cref="CasterLevel"/> a racial/template "casts as an Nth-level X" grant has
    /// contributed so far. The grant is seeded before the first class tick, so a prestige class can
    /// find the caster and advance it — but a formula reading HD is only partly true at that point.
    /// The finalize pass tops the difference up rather than overwriting, so advancement earned in
    /// between survives. Zero when no such grant applies.
    /// </summary>
    public int RacialGrantCasterLevel { get; set; }
    public int MaxSpellLevel { get; set; }
    public Dictionary<int, int> SpellsPerDay { get; set; } = new();
    public Dictionary<int, int>? SpellsKnown { get; set; }
    public List<SpellSelection> SelectedSpells { get; set; } = new();

    // Domain bonus spell slots (spell level → bonus count)
    public Dictionary<int, int> DomainBonusSlots { get; set; } = new();

    // Casting-ability bonus spell slots (spell level → bonus count). These are kept separate
    // from base progression and other restricted slots so sheets can explain the total.
    public Dictionary<int, int> AbilityBonusSlots { get; set; } = new();

    /// <summary>
    /// Specialist wizard bonus slots (spell level → bonus count). SRD: "A specialist wizard can
    /// prepare one additional spell of her specialty school per spell level each day." Unlike
    /// domain slots, which start at 1st level, this applies at every level she can cast — 0-level
    /// included, since the rule is stated per spell level with no exception.
    /// </summary>
    public Dictionary<int, int> SpecialtyBonusSlots { get; set; } = new();

    // Stored progression data for AdvanceSpellcasting to use
    public SpellcastingProgression? ProgressionData { get; set; }

    // Dynamic spell lists such as developed epic spells have no HD-driver progression.
    public SpellAcquisition? AcquisitionOverride { get; set; }

    /// <summary>
    /// How this caster acquires spells. Falls back to the same inference
    /// <see cref="SpellcastingProgression.ResolvedAcquisition"/> makes, for the racial-grant paths
    /// that may not carry a progression reference.
    /// </summary>
    public SpellAcquisition Acquisition =>
        AcquisitionOverride
        ?? ProgressionData?.ResolvedAcquisition
        ?? (SpellsKnown != null ? SpellAcquisition.SpellsKnown : SpellAcquisition.FullList);

    /// <summary>
    /// Extra slots at a spell level from every bonus source — casting ability, domains and a
    /// specialist wizard's school. Each source only ever keys levels the caster can already cast,
    /// so this never invents access to a level the base table withholds.
    /// </summary>
    public int BonusSlotsAt(int spellLevel) =>
        AbilityBonusSlots.GetValueOrDefault(spellLevel)
        + DomainBonusSlots.GetValueOrDefault(spellLevel)
        + SpecialtyBonusSlots.GetValueOrDefault(spellLevel);

    /// <summary>
    /// Slots the character actually casts at a spell level: the class table plus every bonus.
    /// This is the number a sheet or summary should show; <see cref="SpellsPerDay"/> alone is the
    /// base progression and understates a caster with a high casting stat, domains or a specialty.
    /// </summary>
    public int TotalSlotsAt(int spellLevel) =>
        SpellsPerDay.GetValueOrDefault(spellLevel) + BonusSlotsAt(spellLevel);

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

/// <summary>
/// A rule granting a caster access to an additional spell list. Matched against a
/// <see cref="SpellcastingState"/> by <see cref="ClassId"/> and/or <see cref="CastingType"/>
/// (null means "any"); <see cref="ListId"/> is the borrowed list, e.g. <c>class:sorcerer</c>.
/// </summary>
public class SpellListSourceRule
{
    public string? ClassId { get; set; }
    public CastingType? CastingType { get; set; }
    public string ListId { get; set; } = string.Empty;
}

public class FeatSlot
{
    public string? Restriction { get; set; }
}

public class EffectiveLevelRule
{
    public string TargetDriverId { get; set; } = string.Empty;
    public Formula BonusFormula { get; set; } = new();

    /// <summary>
    /// What the bonus levels are good for. Two different rules share this list: "your class level
    /// counts as higher for class features" (Unseelie Champion, Arcane Hierophant) and "you cast
    /// as an Nth-level druid" (Nymph, Aranea, Ghaele). The second is spellcasting only — a nymph
    /// casts as a 7th-level druid but does not gain a 7th-level druid's wild shape or animal
    /// companion — so anything reasoning about class abilities must exclude it.
    /// </summary>
    public EffectiveLevelScope Scope { get; set; } = EffectiveLevelScope.ClassFeatures;
}

public enum EffectiveLevelScope
{
    /// <summary>Counts for class features and for spellcasting.</summary>
    ClassFeatures,
    /// <summary>Counts for caster level only, never for class features.</summary>
    SpellcastingOnly
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
    public double TotalWeightLbs { get; set; }
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
    public Dictionary<(string SkillId, BonusType Type), List<int>> SkillContributions { get; } = new();
    public List<ArmorContribution> Armors { get; } = new();
    public List<WeaponContribution> Weapons { get; } = new();
    public double TotalWeightLbs { get; set; }

    public void Add(BonusTarget target, BonusType type, int value)
    {
        var key = (target, type);
        if (!Contributions.TryGetValue(key, out var list))
            Contributions[key] = list = new List<int>();
        list.Add(value);
    }

    public void AddSkill(string skillId, BonusType type, int value)
    {
        var key = (skillId, type);
        if (!SkillContributions.TryGetValue(key, out var list))
            SkillContributions[key] = list = new List<int>();
        list.Add(value);
    }
}

public class TypedBonusContribution
{
    public BonusTarget Target { get; set; }
    public BonusType BonusType { get; set; }
    public int Value { get; set; }
}

public class WeaponBonusContribution
{
    public string WeaponId { get; set; } = string.Empty;
    public BonusTarget Target { get; set; }
    public BonusType BonusType { get; set; }
    public int Value { get; set; }
}

public class ArmorContribution
{
    public ArmorProfile Profile { get; set; } = new();
    public bool AsShield { get; set; }
}

public class WeaponContribution
{
    public string ItemId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public WeaponProfile Profile { get; set; } = new();
    public int EnhancementBonus { get; set; }
    public bool MainHand { get; set; } = true;
    public bool TwoHanded { get; set; }
}
