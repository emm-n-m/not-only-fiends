namespace NotOnlyFiendsStudio.Models;

public class ClassFeatureDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<ClassFeatureOption> Options { get; set; } = new();
    /// <summary>If set, options are enumerated dynamically from character state (e.g. feats with a given type).</summary>
    public DynamicOptionSource? DynamicSource { get; set; }
}

public class ClassFeatureOption
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<Permabuff> GrantedPermabuffs { get; set; } = new();
    /// <summary>Named benefit sets applied by later class levels after this option is selected.</summary>
    public Dictionary<string, List<Permabuff>> AdditionalPermabuffs { get; set; } = new();

    // Validation hints — informational; engine emits warnings when violated but
    // does not block the selection. Used by companion option lists (animal
    // companion level-gated pool, improved familiar alignment/CL gates).
    public int MinEffectiveLevel { get; set; }
    public string? RequiredAlignment { get; set; }
    public int RequiredCasterLevel { get; set; }
}

public class DynamicOptionSource
{
    /// <summary>What kind of pool to draw from. Currently supported: "feat".</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>For "feat" kind: the required feat type (e.g. "Metamagic"). Null to allow any feat.</summary>
    public string? FeatType { get; set; }
    /// <summary>For "feat" kind: an additional required tag.</summary>
    public string? Tag { get; set; }
}
