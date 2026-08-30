namespace NotOnlyFiendsStudio.Models;

/// <summary>
/// A deity or patron that a character may follow. The character save keeps its allegiance as
/// free text for PCGen compatibility; the content registry resolves that value against either
/// <see cref="Id"/> or <see cref="Name"/> when a mechanical rule needs the definition.
/// </summary>
public class DeityDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Alignment Alignment { get; set; } = Alignment.N;
    public List<string> Titles { get; set; } = new();
    public List<string> Portfolio { get; set; } = new();
    public List<string> DomainIds { get; set; } = new();
    public string? FavoredWeaponId { get; set; }
    public string? Symbol { get; set; }
}
