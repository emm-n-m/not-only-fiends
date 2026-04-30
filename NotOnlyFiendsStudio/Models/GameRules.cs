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
}

public interface IContentLookup
{
    bool TryGetFeat(string id, out FeatDefinition? feat);
    bool TryGetDomain(string id, out DomainDefinition? domain);
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

    public PermabuffContext(CharacterState state, GameRules rules, IContentLookup? content = null)
    {
        State = state;
        Rules = rules;
        Content = content;
    }
}
