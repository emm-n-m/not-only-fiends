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

    /// <summary>
    /// False for entries that exist only to be granted (class proficiencies, marker feats)
    /// and are not feats a character may choose with a slot. Such feats are hidden from
    /// <c>GetAvailableFeats</c> but still satisfy <see cref="HasFeat"/> prerequisites once
    /// granted. Defaults to true so ordinary content needs no change.
    /// </summary>
    public bool Selectable { get; set; } = true;
}
