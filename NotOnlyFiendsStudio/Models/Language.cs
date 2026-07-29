namespace NotOnlyFiendsStudio.Models;

/// <summary>
/// A language a character can speak.
///
/// Languages on <see cref="CharacterState.Languages"/> are plain string ids and deliberately stay
/// that way — PCGen import mints them from arbitrary source text, and a character that speaks
/// something no pack defines is still a valid character. This type exists for the other direction:
/// to <em>offer</em> languages as choices (and to answer "any language except secret ones", which
/// cannot be enumerated without a list). Never validate <c>CharacterState.Languages</c> against it.
/// </summary>
public class LanguageDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// A language restricted to a class or group, which "any bonus language" never includes —
    /// Druidic is the SRD's example ("Druids are forbidden to teach this language to nondruids").
    /// </summary>
    public bool IsSecret { get; set; }

    /// <summary>The alphabet it is written in, where the source states one. Flavour, not mechanics.</summary>
    public string? Script { get; set; }
}
