namespace NotOnlyFiendsFeed.Components;

/// <summary>
/// The facets a character is organised into. Shared by the builder and the sheet so that
/// flipping between editing and viewing keeps the same mental model.
/// </summary>
public enum CharacterTab
{
    /// <summary>Setup, ability scores, languages, and the level-up spine of the HD timeline.</summary>
    Summary,
    Feats,
    Skills,
    Spells,
    Equipment,
    Companions
}

/// <summary>One entry in a <c>TabStrip</c>.</summary>
/// <param name="Badge">
/// Optional count rendered on the tab — unspent feats, unspent skill points, warnings.
/// This is what makes the strip a to-do list rather than plain navigation. Null hides it.
/// </param>
public sealed record CharacterTabItem(
    CharacterTab Id,
    string Label,
    string? Badge = null,
    string BadgeClass = "bg-secondary");
