namespace NotOnlyFiendsStudio.Models;

public class DomainDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<Permabuff> GrantedPermabuffs { get; set; } = new();
    public Dictionary<int, string> BonusSpells { get; set; } = new();
}
