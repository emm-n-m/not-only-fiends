namespace NotOnlyFiendsStudio.Models;

public class Character
{
    public string Name { get; set; } = string.Empty;
    public Alignment Alignment { get; set; } = Alignment.N;

    // Deity or patron allegiance (e.g. a cleric's god, a Mark feat's archdevil).
    // Free-form display name; null/empty means no allegiance.
    public string? Deity { get; set; }

    /// <summary>
    /// Free text rather than an enum, and deliberately so: it is descriptive rather than
    /// mechanical, and PCGen lets a sheet say anything here. The corpus uses Male, Female and
    /// Neuter, which is what the builder offers, but an imported value is kept verbatim so a
    /// round trip cannot quietly rewrite how someone described their character.
    /// </summary>
    public string? Gender { get; set; }

    // Initial State
    public string RaceId { get; set; } = string.Empty;
    public List<string> TemplateIds { get; set; } = new();
    public AbilityScoreSet BaseAbilityScores { get; set; } = new();

    /// <summary>
    /// Bonus languages chosen at creation, one per point of starting Intelligence modifier.
    /// A creation-time input like <see cref="BaseAbilityScores"/>, not a per-tick choice: 3.5
    /// prices these off starting Int, so a later ability increase does not buy another language
    /// (and losing Int does not take one away).
    /// </summary>
    public List<string> BonusLanguageIds { get; set; } = new();

    /// <summary>
    /// Languages asserted by an imported source character. These are preserved independently
    /// of starting-Intelligence picks because the source may not record how each was acquired.
    /// </summary>
    public List<string> SourceLanguageIds { get; set; } = new();

    /// <summary>
    /// Languages taken against a granted slot rather than bought from the starting-Intelligence
    /// budget — a raven familiar's "can speak one language of its master's choice", where the
    /// creature's own Intelligence 2 buys nothing at all. Each entry consumes one slot granted by
    /// <see cref="GrantLanguageSlot"/>; the engine warns about picks beyond the granted count and
    /// about any that duplicate a language already known.
    /// </summary>
    public List<string> GrantedLanguageIds { get; set; } = new();

    // HD Timeline — the build
    public List<Tick> Ticks { get; set; } = new();

    // Permanent events between ticks (Tomes, Wish inherent bonuses)
    public List<PermanentEvent> PermanentEvents { get; set; } = new();

    /// <summary>
    /// The DM-judgement half of the Leadership score. Most of the SRD's modifier table cannot be
    /// derived from a character sheet — whether a leader has great renown or a reputation for
    /// cruelty is a campaign fact — so they are stored as inputs. The two that *are* derivable
    /// (keeping a familiar/mount/companion, and recruiting a cohort of a different alignment) are
    /// computed instead and must not be set here.
    /// </summary>
    public LeadershipModifiers LeadershipModifiers { get; set; } = new();

    // Post-tick modifiers
    public List<EquipmentEntry> Equipment { get; set; } = new();

    /// <summary>
    /// Daily prepared-spell loadout. This is separate from <see cref="TickChoices.SpellSelections"/>,
    /// which represents permanent spellbook, known, or developed-spell choices.
    /// </summary>
    public List<PreparedSpellSelection> PreparedSpellSelections { get; set; } = new();

    // Companions/familiars/mounts/cohorts — stored as separate Character files,
    // referenced here. Resolution is host-side (CompanionResolver), not in ReplayStudio.
    public List<CompanionLink> CompanionLinks { get; set; } = new();

    // When this Character IS a companion, identifies its master link and the
    // effective master level injected by the host before evaluation.
    public CompanionOrigin? CompanionOrigin { get; set; }

    // Computed snapshot — populated at save time for external tools (AI, exports).
    // Ignored by the engine on load; always recomputed from inputs above.
    public CharacterSheet? Sheet { get; set; }

    public Character Clone() => new()
    {
        Name = Name,
        Alignment = Alignment,
        Deity = Deity,
        Gender = Gender,
        RaceId = RaceId,
        TemplateIds = new List<string>(TemplateIds),
        BaseAbilityScores = new AbilityScoreSet
        {
            STR = BaseAbilityScores.STR,
            DEX = BaseAbilityScores.DEX,
            CON = BaseAbilityScores.CON,
            INT = BaseAbilityScores.INT,
            WIS = BaseAbilityScores.WIS,
            CHA = BaseAbilityScores.CHA
        },
        LeadershipModifiers = LeadershipModifiers.Clone(),
        BonusLanguageIds = new List<string>(BonusLanguageIds),
        SourceLanguageIds = new List<string>(SourceLanguageIds),
        GrantedLanguageIds = new List<string>(GrantedLanguageIds),
        Ticks = Ticks.Select(t => new Tick
        {
            DriverId = t.DriverId,
            Choices = new TickChoices
            {
                AbilityIncrease = t.Choices.AbilityIncrease,
                HitPointsRolled = t.Choices.HitPointsRolled,
                FeatIds = t.Choices.FeatIds == null ? null : new List<string>(t.Choices.FeatIds),
                SkillAllocations = t.Choices.SkillAllocations == null
                    ? null
                    : t.Choices.SkillAllocations.Select(a => new SkillAllocation { SkillId = a.SkillId, HalfRanks = a.HalfRanks }).ToList(),
                SpellSelections = t.Choices.SpellSelections == null
                    ? null
                    : t.Choices.SpellSelections.Select(s => new SpellSelection { ClassId = s.ClassId, SpellId = s.SpellId, SpellLevel = s.SpellLevel }).ToList(),
                ClassFeatureChoices = t.Choices.ClassFeatureChoices == null
                    ? null
                    : t.Choices.ClassFeatureChoices.ToDictionary(
                        kv => kv.Key,
                        kv => new List<string>(kv.Value)),
                CompanionTemplateChoices = t.Choices.CompanionTemplateChoices == null
                    ? null
                    : new Dictionary<string, string>(t.Choices.CompanionTemplateChoices),
                UnknownChoices = t.Choices.UnknownChoices == null
                    ? null
                    : new Dictionary<string, System.Text.Json.JsonElement>(t.Choices.UnknownChoices)
            }
        }).ToList(),
        PermanentEvents = PermanentEvents.Select(e => new PermanentEvent
        {
            BeforeTick = e.BeforeTick,
            Permabuffs = new List<Permabuff>(e.Permabuffs)
        }).ToList(),
        Equipment = Equipment.Select(item => new EquipmentEntry
        {
            ItemId = item.ItemId,
            ContentId = item.ContentId,
            Slot = item.Slot,
            MainHand = item.MainHand,
            TwoHanded = item.TwoHanded,
            DoubleWeapon = item.DoubleWeapon,
            Quantity = item.Quantity,
            WeightLbsOverride = item.WeightLbsOverride,
            PriceCpOverride = item.PriceCpOverride,
            EnhancementBonusOverride = item.EnhancementBonusOverride,
            Permabuffs = new List<Permabuff>(item.Permabuffs),
        }).ToList(),
        PreparedSpellSelections = PreparedSpellSelections.Select(selection => new PreparedSpellSelection
        {
            ClassId = selection.ClassId,
            SpellLevel = selection.SpellLevel,
            SpellId = selection.SpellId,
            SlotKind = selection.SlotKind,
        }).ToList(),
        CompanionLinks = CompanionLinks.Select(link => new CompanionLink
        {
            LinkType = link.LinkType,
            CompanionId = link.CompanionId,
            SelectedSpecies = link.SelectedSpecies,
            EffectiveLevelFormula = link.EffectiveLevelFormula,
            FollowerLevel = link.FollowerLevel,
            Notes = link.Notes,
        }).ToList(),
        CompanionOrigin = CompanionOrigin,
        Sheet = null
    };
}

public class CompanionLink
{
    // "animal_companion" | "familiar" | "special_mount" | "improved_familiar" |
    // "wild_cohort" | "shadow_companion" | "leadership_cohort" | "leadership_follower"
    public string LinkType { get; set; } = string.Empty;
    // Stable ID or relative file path; resolved by the host.
    public string CompanionId { get; set; } = string.Empty;
    // Race ID picked by the user (mirrors ClassFeatureSelection result).
    public string? SelectedSpecies { get; set; }
    // Formula evaluated against master state to produce EffectiveMasterLevel.
    public Formula EffectiveLevelFormula { get; set; } = new();
    // Leadership follower tier (1-6). Zero means an imported source did not record it.
    public int FollowerLevel { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// What the source called this companion, verbatim. <see cref="CompanionId"/> is the identity;
    /// this is a repair hint for the case the id cannot resolve — the companion has not been
    /// imported yet, or the master and the companion's own record spell the name differently.
    /// Names are not identities (a campaign may hold six characters called "Lilly"), so this is
    /// only ever consulted after an id lookup misses, and only when it matches exactly one
    /// character. Null for links created in the app.
    /// </summary>
    public string? SourceName { get; set; }
}

public class CompanionOrigin
{
    public string LinkType { get; set; } = string.Empty;
    public int EffectiveMasterLevel { get; set; }
    public string? MasterCharacterId { get; set; }
}

/// <summary>
/// Read-only snapshot of computed character state, included in saved JSON
/// so external tools can read the character sheet without running the engine.
/// </summary>
public class CharacterSheet
{
    public string Race { get; set; } = string.Empty;
    public int TotalHD { get; set; }
    public int ECL { get; set; }
    public int HP { get; set; }
    public Dictionary<string, int> ClassLevels { get; set; } = new();
    public AbilityScoreSet AbilityScores { get; set; } = new();
    public int BAB { get; set; }
    public SaveSet Saves { get; set; } = new();
    public List<string> Feats { get; set; } = new();
    /// <summary>Whole ranks per skill. See <see cref="SkillTotals"/> for the number rolled.</summary>
    public Dictionary<string, int> Skills { get; set; } = new();
    public Dictionary<string, int> SkillBonuses { get; set; } = new();
    public Dictionary<string, int> SkillSynergyBonuses { get; set; } = new();
    /// <summary>Ranks + key ability modifier + granted bonuses + synergies, per skill.</summary>
    public Dictionary<string, int> SkillTotals { get; set; } = new();
    public List<GrantedAbility> Abilities { get; set; } = new();
    public Dictionary<string, int> Counters { get; set; } = new();
    public List<SLA> SLAs { get; set; } = new();
    public HashSet<string> Immunities { get; set; } = new();
    public HashSet<string> Capabilities { get; set; } = new();
    public HashSet<string> Languages { get; set; } = new();
    public Dictionary<string, int> Resistances { get; set; } = new();
    public List<DREntry> DamageReduction { get; set; } = new();
    public int? SpellResistance { get; set; }
    public int FastHealing { get; set; }
    public int TurnResistance { get; set; }
    public int NaturalArmor { get; set; }
    public Dictionary<string, List<string>> ClassFeatureSelections { get; set; } = new();
    public List<PreparedSpellSelection> PreparedSpellSelections { get; set; } = new();

    /// <summary>
    /// Per-class spellcasting summary keyed by class id (e.g. "class:sorcerer"). Includes the
    /// effective caster level — which for racial casters (Couatl, Nymph, Aranea) reflects the
    /// racial grant plus any stacking class levels (e.g. Nymph + 6 Druid → CL 13).
    /// </summary>
    public Dictionary<string, SpellcastingSummary> Spellcasting { get; set; } = new();

    public List<string> Warnings { get; set; } = new();

    public static CharacterSheet FromState(CharacterState state) => new()
    {
        Race = state.RaceId,
        TotalHD = state.TotalHD,
        ECL = state.ECL,
        HP = state.HP,
        ClassLevels = state.ClassLevels,
        AbilityScores = state.AbilityScores,
        BAB = state.EffectiveBAB,
        Saves = state.EffectiveSaves,
        Feats = state.Feats,
        Skills = state.SkillHalfRanks.ToDictionary(kv => kv.Key, kv => kv.Value / 2),
        SkillBonuses = state.SkillBonuses,
        SkillSynergyBonuses = state.SkillSynergyBonuses,
        SkillTotals = state.SkillTotals,
        Abilities = state.Abilities,
        Counters = state.Counters,
        SLAs = state.SLAs,
        Immunities = state.Immunities,
        Capabilities = state.Capabilities,
        Languages = state.Languages,
        Resistances = state.Resistances,
        DamageReduction = state.DamageReduction,
        SpellResistance = state.SpellResistance,
        FastHealing = state.FastHealing,
        TurnResistance = state.TurnResistance,
        NaturalArmor = state.NaturalArmor,
        ClassFeatureSelections = state.ClassFeatureSelections,
        PreparedSpellSelections = state.PreparedSpellSelections.Select(selection => new PreparedSpellSelection
        {
            ClassId = selection.ClassId,
            SpellLevel = selection.SpellLevel,
            SpellId = selection.SpellId,
            SlotKind = selection.SlotKind,
        }).ToList(),
        Spellcasting = state.Spellcasting.ToDictionary(
            kv => kv.Key,
            kv => SpellcastingSummary.FromState(kv.Value)),
        Warnings = state.Warnings
            .Select(w => w.TickIndex.HasValue ? $"HD {w.TickIndex}: {w.Message}" : w.Message)
            .ToList()
    };
}

/// <summary>
/// Serializable, display-oriented view of a single caster class on the sheet. Mirrors the
/// evaluation-time <see cref="SpellcastingState"/> minus internal progression/selection detail.
/// </summary>
public class SpellcastingSummary
{
    public string ClassId { get; set; } = string.Empty;
    public CastingType CastingType { get; set; }
    public Ability CastingStat { get; set; }
    /// <summary>
    /// Whether this caster has its whole list available, works from a spellbook, knows a fixed
    /// number of spells, or develops epic spells individually. Consumers must not offer a spell
    /// <em>choice</em> for <c>FullList</c>.
    /// </summary>
    public SpellAcquisition Acquisition { get; set; }
    public int CasterLevel { get; set; }
    public int MaxSpellLevel { get; set; }
    public Dictionary<int, int> SpellsPerDay { get; set; } = new();
    public Dictionary<int, int>? SpellsKnown { get; set; }
    public Dictionary<int, int> DomainBonusSlots { get; set; } = new();
    /// <summary>Specialist wizard bonus slots, castable only from the specialty school.</summary>
    public Dictionary<int, int> SpecialtyBonusSlots { get; set; } = new();
    /// <summary>Bonus slots granted by the casting ability score.</summary>
    public Dictionary<int, int> AbilityBonusSlots { get; set; } = new();

    public static SpellcastingSummary FromState(SpellcastingState sc) => new()
    {
        ClassId = sc.ClassId,
        CastingType = sc.CastingType,
        CastingStat = sc.CastingStat,
        Acquisition = sc.Acquisition,
        CasterLevel = sc.CasterLevel,
        MaxSpellLevel = sc.MaxSpellLevel,
        SpellsPerDay = new Dictionary<int, int>(sc.SpellsPerDay),
        SpellsKnown = sc.SpellsKnown is null ? null : new Dictionary<int, int>(sc.SpellsKnown),
        DomainBonusSlots = new Dictionary<int, int>(sc.DomainBonusSlots),
        SpecialtyBonusSlots = new Dictionary<int, int>(sc.SpecialtyBonusSlots),
        AbilityBonusSlots = new Dictionary<int, int>(sc.AbilityBonusSlots),
    };
}

public class PermanentEvent
{
    public int BeforeTick { get; set; }
    public List<Permabuff> Permabuffs { get; set; } = new();
}

public class Tick
{
    public string DriverId { get; set; } = string.Empty;
    public TickChoices Choices { get; set; } = new();
}

public class TickChoices
{
    public Ability? AbilityIncrease { get; set; }
    /// <summary>Optional source die result for this HD; null uses the ruleset's deterministic roll.</summary>
    public int? HitPointsRolled { get; set; }
    public List<string>? FeatIds { get; set; }
    public List<SkillAllocation>? SkillAllocations { get; set; }
    public List<SpellSelection>? SpellSelections { get; set; }
    public Dictionary<string, List<string>>? ClassFeatureChoices { get; set; }

    /// <summary>
    /// Template applied to a companion slot, keyed by link type. This is separate from the
    /// species choice because a planar ranger may choose a celestial or fiendish version of a
    /// normal animal, and the template belongs on the linked companion character.
    /// </summary>
    public Dictionary<string, string>? CompanionTemplateChoices { get; set; }

    /// <summary>Preserves misspelled choice fields so replay can report that they were ignored.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? UnknownChoices { get; set; }
}

public class AbilityScoreSet
{
    public int STR { get; set; }
    public int DEX { get; set; }
    public int CON { get; set; }
    public int INT { get; set; }
    public int WIS { get; set; }
    public int CHA { get; set; }

    public int GetScore(Ability ability) => ability switch
    {
        Ability.STR => STR,
        Ability.DEX => DEX,
        Ability.CON => CON,
        Ability.INT => INT,
        Ability.WIS => WIS,
        Ability.CHA => CHA,
        _ => throw new ArgumentException($"Unknown ability: {ability}")
    };

    public void SetScore(Ability ability, int value)
    {
        switch (ability)
        {
            case Ability.STR: STR = value; break;
            case Ability.DEX: DEX = value; break;
            case Ability.CON: CON = value; break;
            case Ability.INT: INT = value; break;
            case Ability.WIS: WIS = value; break;
            case Ability.CHA: CHA = value; break;
            default: throw new ArgumentException($"Unknown ability: {ability}");
        }
    }

    public static int Modifier(int score)
    {
        int diff = score - 10;
        // Floor division: -9/2 should be -5, not -4
        return diff >= 0 ? diff / 2 : (diff - 1) / 2;
    }
}

public class SkillAllocation
{
    public string SkillId { get; set; } = string.Empty;
    public int HalfRanks { get; set; }
}

public class SpellSelection
{
    public string ClassId { get; set; } = string.Empty;
    public int SpellLevel { get; set; }
    public string SpellId { get; set; } = string.Empty;
}

public class PreparedSpellSelection
{
    public string ClassId { get; set; } = string.Empty;
    public int SpellLevel { get; set; }
    public string SpellId { get; set; } = string.Empty;
    public PreparedSpellSlotKind SlotKind { get; set; } = PreparedSpellSlotKind.Normal;
}

public class EquipmentEntry
{
    public string ItemId { get; set; } = string.Empty;       // display label; kept for free-form/homebrew entries
    public string? ContentId { get; set; }                    // resolves via IContentLookup.TryGetEquipment
    public string Slot { get; set; } = string.Empty;
    public bool MainHand { get; set; } = true;                // weapon hand assignment
    public bool TwoHanded { get; set; }
    public bool DoubleWeapon { get; set; }
    public double Quantity { get; set; } = 1;
    /// <summary>PCGen character-specific size/customization weight; null uses catalog weight.</summary>
    public double? WeightLbsOverride { get; set; }
    /// <summary>PCGen character-specific size/customization price; null uses catalog price.</summary>
    public long? PriceCpOverride { get; set; }
    /// <summary>PCGen custom weapon enhancement; null uses the catalog definition.</summary>
    public int? EnhancementBonusOverride { get; set; }
    public List<Permabuff> Permabuffs { get; set; } = new(); // inline permabuffs (homebrew, or overrides on top of content)
}

/// <summary>
/// SRD Leadership feat, "Leadership Modifiers". Reputation applies to attracting both a cohort
/// and followers; the other two groups apply to one or the other, which is why a character has a
/// different effective score for each.
/// </summary>
public class LeadershipModifiers
{
    // Leader's Reputation — affects cohort and followers alike.
    public bool GreatRenown { get; set; }            // +2
    public bool FairnessAndGenerosity { get; set; }  // +1
    public bool SpecialPower { get; set; }           // +1
    public bool Failure { get; set; }                // -1
    public bool Aloofness { get; set; }              // -1
    public bool Cruelty { get; set; }                // -2

    /// <summary>Cohorts the leader has caused the death of. "-2, cumulative per cohort killed."</summary>
    public int CohortDeathsCaused { get; set; }      // -2 each, cohort only

    // Followers only.
    public bool HasStronghold { get; set; }          // +2
    public bool MovesAroundALot { get; set; }        // -1
    public bool CausedFollowerDeaths { get; set; }   // -1

    /// <summary>Modifiers that apply however the leader is recruiting.</summary>
    public int ReputationModifier =>
        (GreatRenown ? 2 : 0)
        + (FairnessAndGenerosity ? 1 : 0)
        + (SpecialPower ? 1 : 0)
        + (Failure ? -1 : 0)
        + (Aloofness ? -1 : 0)
        + (Cruelty ? -2 : 0);

    /// <summary>Cohort-side modifiers a character carries on its own, excluding the derived ones.</summary>
    public int CohortModifier => ReputationModifier - 2 * Math.Max(0, CohortDeathsCaused);

    public int FollowerModifier =>
        ReputationModifier
        + (HasStronghold ? 2 : 0)
        + (MovesAroundALot ? -1 : 0)
        + (CausedFollowerDeaths ? -1 : 0);

    public LeadershipModifiers Clone() => (LeadershipModifiers)MemberwiseClone();
}
