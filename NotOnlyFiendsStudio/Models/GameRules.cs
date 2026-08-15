namespace NotOnlyFiendsStudio.Models;

public class GameRules
{
    // Thresholds
    public int EpicThreshold { get; init; } = 20;
    public int AbilityIncreaseInterval { get; init; } = 4;

    // First HD rules
    public bool FirstHDMaxHP { get; init; } = true;
    public int FirstHDSkillMultiplier { get; init; } = 4;
    public int RacialBonusSkillFirstHDMultiplier { get; init; } = 4;

    // Feat schedule
    public HashSet<int> StandardFeatHDs { get; init; } = new() { 1, 3, 6, 9, 12, 15, 18 };
    public int EpicFeatInterval { get; init; } = 3;
    public int EpicFeatStartHD { get; init; } = 21;

    // Skill rank cap
    public Func<int, int> MaxHalfRanks { get; init; } = totalHD => (totalHD + 3) * 2;

    // BAB formula: (progression, level) → cumulative total at that level
    public Func<BABProgression, int, int> CalculateBABTotal { get; init; } = (prog, level) => prog switch
    {
        BABProgression.Good => level,
        BABProgression.Average => level * 3 / 4,
        BABProgression.Poor => level / 2,
        _ => 0
    };

    // Save formula: (rate, level) → cumulative total at that level
    public Func<ProgressionRate, int, int> CalculateSaveTotal { get; init; } = (rate, level) =>
    {
        if (level <= 0) return 0;
        return rate switch
        {
            ProgressionRate.Good => 2 + level / 2,
            ProgressionRate.Poor => level / 3,
            _ => 0
        };
    };

    // Shared by AC and attack calculations. ColossalPlus follows the Colossal
    // modifier because the core 3.5e size table has no larger category.
    public Func<Size, int> CalculateSizeModifier { get; init; } = size => size switch
    {
        Size.Fine => 8,
        Size.Diminutive => 4,
        Size.Tiny => 2,
        Size.Small => 1,
        Size.Medium => 0,
        Size.Large => -1,
        Size.Huge => -2,
        Size.Gargantuan => -4,
        Size.Colossal or Size.ColossalPlus => -8,
        _ => 0
    };

    /// <summary>
    /// The Hide skill's own size table, which is four times the AC/attack one and applies only to
    /// Hide. SRD: "A creature larger or smaller than Medium takes a size bonus or penalty on Hide
    /// checks depending on its size category: Fine +16, Diminutive +12, Tiny +8, Small +4,
    /// Large –4, Huge –8, Gargantuan –12, Colossal –16."
    /// </summary>
    public Func<Size, int> CalculateHideSizeModifier { get; init; } = size => size switch
    {
        Size.Fine => 16,
        Size.Diminutive => 12,
        Size.Tiny => 8,
        Size.Small => 4,
        Size.Medium => 0,
        Size.Large => -4,
        Size.Huge => -8,
        Size.Gargantuan => -12,
        Size.Colossal or Size.ColossalPlus => -16,
        _ => 0
    };

    /// <summary>
    /// Skills that take a size modifier, and the table each uses. Only Hide does in the SRD; it is
    /// keyed here rather than hard-coded at the totalling site so a variant ruleset can change it.
    /// </summary>
    public IReadOnlyDictionary<string, Func<GameRules, Size, int>> SkillSizeModifiers { get; init; } =
        new Dictionary<string, Func<GameRules, Size, int>>(StringComparer.Ordinal)
        {
            ["skill:hide"] = (rules, size) => rules.CalculateHideSizeModifier(size),
        };

    public static GameRules Standard35e() => new();

    public bool GrantsStandardFeat(int totalHD) => StandardFeatHDs.Contains(totalHD);

    /// <summary>
    /// This ruleset stores racial ability adjustments on the race itself. Racial-HD ticks therefore
    /// do not also grant the selectable every-four-HD ability increase used by class levels.
    ///
    /// <paramref name="characterLevel"/> is the count of levels that are the character's own, which
    /// is total HD for most characters but excludes a monster race's free HD — see
    /// <see cref="RaceDefinition.MonsterClassHD"/>. Those HD arrive with the race rather than being
    /// levelled through, so they no more grant an increase than a racial-HD tick does, even though
    /// the driver carrying them is a class.
    /// </summary>
    public bool GrantsAbilityIncrease(int characterLevel, DriverKind driverKind) =>
        driverKind == DriverKind.Class && characterLevel > 0
        && characterLevel % AbilityIncreaseInterval == 0;

    public bool GrantsEpicFeat(int totalHD) =>
        totalHD >= EpicFeatStartHD && (totalHD - EpicFeatStartHD) % EpicFeatInterval == 0;

    /// <summary>
    /// Returns the number of bonus spells granted at a spell level by a positive casting
    /// ability modifier. D&amp;D 3.5 Table 1-1 gives one slot when the modifier reaches the
    /// spell level, then one additional slot for every four modifier points beyond it.
    /// </summary>
    public static int BonusSpellSlots(int abilityModifier, int spellLevel) =>
        abilityModifier < spellLevel
            ? 0
            : 1 + (abilityModifier - spellLevel) / 4;

    // 3.5e PHB carrying capacity, STR 1..29. Each +10 STR multiplies loads by 4 (handled below).
    private static readonly (int Light, int Medium, int Heavy)[] EncumbranceTable =
    {
        (0, 0, 0),       // STR 0 (placeholder)
        (3, 6, 10),      // 1
        (6, 13, 20),     // 2
        (10, 20, 30),    // 3
        (13, 26, 40),    // 4
        (16, 33, 50),    // 5
        (20, 40, 60),    // 6
        (23, 46, 70),    // 7
        (26, 53, 80),    // 8
        (30, 60, 90),    // 9
        (33, 66, 100),   // 10
        (38, 76, 115),   // 11
        (43, 86, 130),   // 12
        (50, 100, 150),  // 13
        (58, 116, 175),  // 14
        (66, 133, 200),  // 15
        (76, 153, 230),  // 16
        (86, 173, 260),  // 17
        (100, 200, 300), // 18
        (116, 233, 350), // 19
        (133, 266, 400), // 20
        (153, 306, 460), // 21
        (173, 346, 520), // 22
        (200, 400, 600), // 23
        (233, 466, 700), // 24
        (266, 533, 800), // 25
        (306, 613, 920), // 26
        (346, 693, 1040),// 27
        (400, 800, 1200),// 28
        (466, 933, 1400),// 29
    };

    public (int Light, int Medium, int Heavy) GetCarryingCapacity(int str)
    {
        if (str <= 0) return (0, 0, 0);
        if (str <= 29) return EncumbranceTable[str];
        // For STR ≥ 30, capacity at STR (X+10) = capacity at STR X × 4.
        var doublings = (str - 20) / 10;
        var reducedStr = str - doublings * 10;  // 30→20, 35→25, 40→20, etc.
        var mult = 1L << (2 * doublings);       // 4, 16, 64, ...
        var (l, m, h) = EncumbranceTable[reducedStr];
        return ((int)(l * mult), (int)(m * mult), (int)(h * mult));
    }
}

public static class BonusStack
{
    // Stack with self & everything: Dodge and Untyped.
    private static readonly HashSet<BonusType> StackingTypes = new() { BonusType.Dodge, BonusType.Untyped };

    public static bool IsStacking(BonusType type) => StackingTypes.Contains(type);

    public static int Aggregate(BonusType type, IEnumerable<int> values) =>
        IsStacking(type) ? values.Sum() : values.DefaultIfEmpty(0).Max();
}

public interface IContentLookup
{
    bool TryGetFeat(string id, out FeatDefinition? feat);
    bool TryGetTemplate(string id, out TemplateDriver? template);
    bool TryGetClassFeature(string id, out ClassFeatureDefinition? classFeature);
    bool TryGetDomain(string id, out DomainDefinition? domain);
    bool TryGetEquipment(string id, out EquipmentDefinition? equipment);
}

public class PermabuffContext
{
    public CharacterState State { get; }
    public GameRules Rules { get; }
    public IContentLookup? Content { get; }
    public TickChoices? CurrentTickChoices { get; set; }
    // Driver id of the current tick being processed (null outside tick context, e.g. race/template setup).
    public string? CurrentDriverId { get; set; }
    public DriverKind? CurrentDriverKind { get; set; }
    public int? CurrentRacialHitDieMaximum { get; set; }
    // The largest hit-die size any of the character's templates will ever impose, acquired or
    // not. A saved roll is a source input for the full timeline: a lich's 8 rolled at bard 3
    // is valid even while the die is still a d6, because the die becomes a d12 at acquisition.
    // Informs only the out-of-range warning in AddHitDie, never the banked die size.
    public int? SavedRollDieCeiling { get; set; }
    // Feat id currently being applied (non-null only while a feat's GrantedPermabuffs are cascading).
    // Permabuffs that need to attribute a source should prefer this over CurrentDriverId when set.
    public string? CurrentFeatId { get; set; }
    // Transient collector populated only during ReplayStudio's post-tick equipment pass.
    // Equipment-specific permabuffs (GrantTypedBonus, GrantArmorProfile, GrantWeaponLine)
    // push contributions here so the finalize step can apply 3.5e stacking rules.
    public EquipmentPass? EquipmentPass { get; set; }

    public PermabuffContext(CharacterState state, GameRules rules, IContentLookup? content = null)
    {
        State = state;
        Rules = rules;
        Content = content;
    }
}
