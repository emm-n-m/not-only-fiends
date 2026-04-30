namespace NotOnlyFiendsStudio.Models;

public class FeatDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public FeatType Type { get; set; }
    public List<Prerequisite> Prerequisites { get; set; } = new();
    public List<Permabuff> GrantedPermabuffs { get; set; } = new();
    public bool Repeatable { get; set; }
    public string? SelectionRequired { get; set; }
    public List<string> Tags { get; set; } = new();
}
