namespace NotOnlyFiendsStudio.Models;

public class Character
{
    public string Name { get; set; } = string.Empty;
    public Alignment Alignment { get; set; } = Alignment.N;

    // Initial State
    public string RaceId { get; set; } = string.Empty;
    public List<string> TemplateIds { get; set; } = new();
    public AbilityScoreSet BaseAbilityScores { get; set; } = new();

    // HD Timeline — the build
    public List<Tick> Ticks { get; set; } = new();

    // Permanent events between ticks (Tomes, Wish inherent bonuses)
    public List<PermanentEvent> PermanentEvents { get; set; } = new();

    // Post-tick modifiers
    public List<EquipmentEntry> Equipment { get; set; } = new();

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
        Ticks = Ticks.Select(t => new Tick
        {
            DriverId = t.DriverId,
            Choices = new TickChoices
            {
                AbilityIncrease = t.Choices.AbilityIncrease,
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
                        kv => new List<string>(kv.Value))
            }
        }).ToList(),
        PermanentEvents = PermanentEvents.Select(e => new PermanentEvent
        {
            BeforeTick = e.BeforeTick,
            Permabuffs = new List<Permabuff>(e.Permabuffs)
        }).ToList(),
        Equipment = new List<EquipmentEntry>(Equipment),
        CompanionLinks = new List<CompanionLink>(CompanionLinks),
        CompanionOrigin = CompanionOrigin,
        Sheet = null
    };
}

public class CompanionLink
{
    // "animal_companion" | "familiar" | "special_mount" | "improved_familiar" | "wild_cohort" | "leadership_cohort"
    public string LinkType { get; set; } = string.Empty;
    // Stable ID or relative file path; resolved by the host.
    public string CompanionId { get; set; } = string.Empty;
    // Race ID picked by the user (mirrors ClassFeatureSelection result).
    public string? SelectedSpecies { get; set; }
    // Formula evaluated against master state to produce EffectiveMasterLevel.
    public Formula EffectiveLevelFormula { get; set; } = new();
    public string? Notes { get; set; }
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
    public int NaturalArmor { get; set; }
    public Dictionary<string, List<string>> ClassFeatureSelections { get; set; } = new();

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
        NaturalArmor = state.NaturalArmor,
        ClassFeatureSelections = state.ClassFeatureSelections,
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
    /// Whether this caster has its whole list available, works from a spellbook, or knows a fixed
    /// number of spells. Consumers must not offer a spell *choice* for <c>FullList</c>.
    /// </summary>
    public SpellAcquisition Acquisition { get; set; }
    public int CasterLevel { get; set; }
    public int MaxSpellLevel { get; set; }
    public Dictionary<int, int> SpellsPerDay { get; set; } = new();
    public Dictionary<int, int>? SpellsKnown { get; set; }
    public Dictionary<int, int> DomainBonusSlots { get; set; } = new();
    /// <summary>Specialist wizard bonus slots, castable only from the specialty school.</summary>
    public Dictionary<int, int> SpecialtyBonusSlots { get; set; } = new();

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
    public List<string>? FeatIds { get; set; }
    public List<SkillAllocation>? SkillAllocations { get; set; }
    public List<SpellSelection>? SpellSelections { get; set; }
    public Dictionary<string, List<string>>? ClassFeatureChoices { get; set; }
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

public class EquipmentEntry
{
    public string ItemId { get; set; } = string.Empty;       // display label; kept for free-form/homebrew entries
    public string? ContentId { get; set; }                    // resolves via IContentLookup.TryGetEquipment
    public string Slot { get; set; } = string.Empty;
    public bool MainHand { get; set; } = true;                // weapon hand assignment
    public bool TwoHanded { get; set; }
    public List<Permabuff> Permabuffs { get; set; } = new(); // inline permabuffs (homebrew, or overrides on top of content)
}
