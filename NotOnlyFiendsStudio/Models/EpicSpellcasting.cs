namespace NotOnlyFiendsStudio.Models;

/// <summary>
/// Stable spell-list identifiers used for developed epic spells. PCGen models these as
/// level-zero pseudo classes keyed by casting ability; they are spell lists, not HD drivers.
/// </summary>
public static class EpicSpellcasting
{
    public const int SpellLevel = 10;
    public const string CharismaListId = "class:epic_spells_cha";
    public const string IntelligenceListId = "class:epic_spells_int";
    public const string WisdomListId = "class:epic_spells_wis";

    public static bool IsSpellList(string id) =>
        id is CharismaListId or IntelligenceListId or WisdomListId;

    public static string ListIdFor(Ability ability) => ability switch
    {
        Ability.CHA => CharismaListId,
        Ability.INT => IntelligenceListId,
        Ability.WIS => WisdomListId,
        _ => throw new ArgumentOutOfRangeException(nameof(ability), ability,
            "Epic spellcasting requires a mental casting ability."),
    };

    public static bool TryGetCastingAbility(string id, out Ability ability)
    {
        ability = id switch
        {
            CharismaListId => Ability.CHA,
            IntelligenceListId => Ability.INT,
            WisdomListId => Ability.WIS,
            _ => default,
        };
        return IsSpellList(id);
    }
}
