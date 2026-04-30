using System.Text.Json;

namespace NotOnlyFiendsStudio.Models;

public class SpellDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string School { get; set; } = string.Empty;
    public Dictionary<string, int> ClassLevels { get; set; } = new();
    public SpellComponents Components { get; set; } = new();
    public string CastingTime { get; set; } = string.Empty;
    public string Range { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string SavingThrow { get; set; } = string.Empty;
    public string SpellResistance { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Subschool { get; set; }
    public List<string> Descriptors { get; set; } = new();
    public string? Target { get; set; }
    public string? Area { get; set; }
    public string? Effect { get; set; }

    public int? GetLevelForClass(string classId) =>
        ClassLevels.TryGetValue(classId, out var level) ? level : null;
}

public class SpellComponents
{
    public bool Verbal { get; set; }
    public bool Somatic { get; set; }
    public bool DivineFocus { get; set; }
    public string? Material { get; set; }
    public string? Focus { get; set; }
    public JsonElement? XpCost { get; set; }
}
