namespace NotOnlyFiendsStudio.Models;

public class SkillDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string KeyAbility { get; set; } = string.Empty;
    public bool TrainedOnly { get; set; }
    public bool ArmorCheckPenalty { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ParentSkill { get; set; }

    // Extracted synergy data (5 ranks here → bonus on target skill). Not yet
    // consumed by the engine; see TODO.md §1 "Smaller gaps".
    public List<SkillSynergy> Synergies { get; set; } = new();
}

public class SkillSynergy
{
    public string TargetSkillId { get; set; } = string.Empty;
    public int Bonus { get; set; }
}
