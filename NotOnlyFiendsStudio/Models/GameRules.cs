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

    public static GameRules Standard35e() => new();

    public bool GrantsStandardFeat(int totalHD) => StandardFeatHDs.Contains(totalHD);

    public bool GrantsEpicFeat(int totalHD) =>
        totalHD >= EpicFeatStartHD && (totalHD - EpicFeatStartHD) % EpicFeatInterval == 0;

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
