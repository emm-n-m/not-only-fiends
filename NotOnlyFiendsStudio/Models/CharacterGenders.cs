namespace NotOnlyFiendsStudio.Models;

/// <summary>
/// The genders the builder offers. <see cref="Character.Gender"/> is free text and is not
/// validated against this list — these are the values the imported corpus uses, offered so the
/// common case is a click, while anything a source recorded is preserved as written.
/// </summary>
public static class CharacterGenders
{
    public static readonly IReadOnlyList<string> Suggested = new[] { "Female", "Male", "Neuter" };
}
