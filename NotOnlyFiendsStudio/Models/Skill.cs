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
}
