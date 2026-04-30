namespace NotOnlyFiendsStudio.Models;

public class RaceDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public CreatureType Type { get; set; }
    public List<string> Subtypes { get; set; } = new();
    public Size Size { get; set; }
    public Dictionary<MovementMode, int> Speeds { get; set; } = new();
    public AbilityScoreSet? AbilityModifiers { get; set; }
    public int LevelAdjustment { get; set; }
    public int BonusFeats { get; set; }
    public int BonusSkillPointsPerHD { get; set; }

    // Racial HD driver ID — null for races with no racial HD (Human, etc.)
    public string? RacialHDDriverId { get; set; }

    // Delta to the racial HD driver's class skills (add/remove specific skills)
    public List<string> RacialClassSkillAdditions { get; set; } = new();
    public List<string> RacialClassSkillRemovals { get; set; } = new();

    // Flat abilities applied at creation
    public List<Permabuff> RacialPermabuffs { get; set; } = new();

    // Formula-based abilities that scale with total HD
    public List<ScalingFormula> ScalingFormulas { get; set; } = new();
}

public class ScalingFormula
{
    public AttributeTarget Target { get; set; }
    public string? ResistanceElement { get; set; }
    public Ability? AbilityScore { get; set; }
    public Formula Formula { get; set; } = new();
}
