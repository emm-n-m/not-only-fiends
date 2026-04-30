using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Models;

/// <summary>
/// Deserialized from pack.json inside each pack directory.
/// </summary>
public class PackManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? Version { get; set; }
    public int Priority { get; set; } = 10;
    public List<string> Depends { get; set; } = new();
    public ConflictResolution? OnConflict { get; set; }
}

/// <summary>
/// User preferences for which packs are enabled and in what order.
/// </summary>
public class PackConfig
{
    public List<PackEntry> Packs { get; set; } = new();
}

public class PackEntry
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public ConflictResolution? OnConflict { get; set; }
}
