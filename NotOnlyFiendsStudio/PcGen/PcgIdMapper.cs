using System.Text.RegularExpressions;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.PcGen;

public class PcgIdMapper
{
    private static readonly Dictionary<string, string> RaceMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Human"] = "human",
        ["Outsider"] = "outsider",

        // SRD monsters as races
        ["Pixie"] = "pixie",
        ["Elf ~ Drow"] = "drow",
        ["Devil (Imp)"] = "devil_imp",
        ["Devil (Erinyes)"] = "devil_erinyes",
        ["Demon (Succubus)"] = "demon_succubus",
        ["Medusa"] = "medusa",
        ["Worg"] = "worg",
        ["Sahuagin (Mutant)"] = "sahuagin_mutant",
        ["Satyr"] = "satyr",
        ["Couatl"] = "couatl",
        ["Nymph"] = "nymph",

        // SRD companions/familiars
        ["Companion ~ Snake (Viper/Tiny)"] = "companion_snake_viper_tiny",
        ["Companion ~ Snake (Viper/Medium)"] = "companion_snake_viper_medium",
        ["Snake (Constrictor)"] = "companion_snake_constrictor",
        ["Companion ~ Tiger"] = "companion_tiger",
        ["Companion ~ Devil (Imp)"] = "companion_devil_imp",
        ["Companion ~ Raven"] = "companion_raven",
        ["Companion ~ Leopard"] = "companion_leopard",
        ["Companion ~ Hawk"] = "companion_hawk",
        ["Companion ~ Bat"] = "companion_bat",
        ["Companion ~ Elemental (Air Small)"] = "companion_elemental_air_small",
        ["Companion ~ Elemental (Water Small)"] = "companion_elemental_water_small",
        ["Companion ~ Shadow"] = "companion_shadow",

        // SRD dragons
        ["Dragon (Red Great Wyrm)"] = "dragon_red_great_wyrm",
        ["Dragon (Red Great Wyrm/Colossal Plus)"] = "dragon_red_great_wyrm_colossal_plus",

        // SRD monsters (srd_monsters pack)
        ["Aranea"] = "aranea",
        ["Hell Hound"] = "hell_hound",

        // DECEIT homebrew
        ["Archfiend"] = "archfiend",
    };

    private static readonly Dictionary<string, string> ClassMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Hand-crafted base classes
        ["Fighter"] = "class:fighter",
        ["Barbarian"] = "class:barbarian",
        ["Cleric"] = "class:cleric",
        ["Sorcerer"] = "class:sorcerer",

        // SRD base classes (from srd.json)
        ["Adept"] = "class:adept",
        ["Aristocrat"] = "class:aristocrat",
        ["Bard"] = "class:bard",
        ["Commoner"] = "class:commoner",
        ["Druid"] = "class:druid",
        ["Expert"] = "class:expert",
        ["Monk"] = "class:monk",
        ["Paladin"] = "class:paladin",
        ["Ranger"] = "class:ranger",
        ["Rogue"] = "class:rogue",
        ["Warrior"] = "class:warrior",
        ["Wizard"] = "class:wizard",

        // SRD prestige classes
        ["Arcane Archer"] = "class:arcane_archer",
        ["Arcane Trickster"] = "class:arcane_trickster",
        ["Archmage"] = "class:archmage",
        ["Assassin"] = "class:assassin",
        ["Blackguard"] = "class:blackguard",
        ["Dragon Disciple"] = "class:dragon_disciple",
        ["Duelist"] = "class:duelist",
        ["Dwarven Defender"] = "class:dwarven_defender",
        ["Eldritch Knight"] = "class:eldritch_knight",
        ["Hierophant"] = "class:hierophant",
        ["Horizon Walker"] = "class:horizon_walker",
        ["Loremaster"] = "class:loremaster",
        ["Mystic Theurge"] = "class:mystic_theurge",
        ["Shadowdancer"] = "class:shadowdancer",
        ["Thaumaturgist"] = "class:thaumaturgist",

        // SRD epic prestige classes
        ["Cosmic Descryer"] = "class:cosmic_descryer",

        // Unearthed Arcana variant classes
        ["Cleric (Cloistered Cleric)"] = "class:cloistered_cleric",
        ["Paladin of Tyranny"] = "class:paladin_of_tyranny",
        ["Ranger (Planar Ranger)"] = "class:planar_ranger",

        // Third-party prestige classes
        ["Dark Temptress"] = "class:dark_temptress",
        ["Blood Witch"] = "class:blood_witch",
        ["Blood Hexer"] = "class:blood_hexer",
        ["Spectral Loremaster"] = "class:spectral_loremaster",
        ["Deathseeker"] = "class:deathseeker",

        // DECEIT homebrew classes
        ["Favored Soul"] = "class:favored_soul",
        ["Arcane Hierophant"] = "class:arcane_hierophant",
        ["Archfiend"] = "class:archfiend",

        // Monster classes (racial HD with class-like progression)
        ["Red Dragon"] = "racial_hd:red_dragon",

        // Racial HD (PCGen uses creature type name as class)
        ["Outsider"] = "racial_hd:outsider",
        ["Animal"] = "racial_hd:animal",
        ["Elemental"] = "racial_hd:elemental",
        ["Fey"] = "racial_hd:fey",
        ["Magical Beast"] = "racial_hd:magical_beast",
        ["Monstrous Humanoid"] = "racial_hd:monstrous_humanoid",
        ["Undead"] = "racial_hd:undead",
    };

    private static readonly Dictionary<string, string> FeatOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        // Add overrides for feats whose names don't follow the algorithmic transform.
        // The '/' in "Claws/Fangs" survives DefaultIdTransform, yielding "claws/fangs";
        // the AEG Infernal Pact chain feat is stored as "claws_fangs".
        ["Claws/Fangs"] = "claws_fangs",
    };

    private static readonly Dictionary<string, string> SkillOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Knowledge (The Planes)"] = "knowledge_planes",
    };

    private static readonly Dictionary<string, string> EquipmentOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        // Populated as PcgImportRegression surfaces names that don't match the catalog
        // exactly (e.g. abbreviations, parenthetical color suffixes). Catalog ID on the right.
    };

    // Body-slot labels that PCGen uses in EQUIPSET — translated to the engine's slot vocabulary
    // (which mirrors EquipmentDefinition.Slot in the content catalog).
    private static readonly Dictionary<string, string> BodySlotMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Head"] = "head",
        ["Eyes"] = "eyes",
        ["Neck"] = "neck",
        ["Shoulders"] = "shoulders",
        ["Body"] = "body",
        ["Torso"] = "torso",
        ["Arms"] = "arms",
        ["Hands"] = "hands",
        ["Wrists"] = "wrists",
        ["Fingers"] = "ring",
        ["Ring"] = "ring",
        ["Waist"] = "waist",
        ["Feet"] = "feet",
        ["Foot"] = "feet",
    };

    // PCGen slot labels that mean "held weapon" rather than a body slot.
    private static readonly HashSet<string> WeaponSlotLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Equipped",
        "Primary Weapon",
        "Secondary Weapon",
        "Off-hand",
        "Two Hand",
        "Both Hands",
    };

    public string? MapRace(string pcgenRace)
    {
        var bare = RaceMap.GetValueOrDefault(pcgenRace);
        return bare == null ? null : "race:" + bare;
    }

    public string? MapClass(string pcgenClass)
    {
        return ClassMap.GetValueOrDefault(pcgenClass);
    }

    public static string MapFeatBare(string pcgenFeatKey)
    {
        if (FeatOverrides.TryGetValue(pcgenFeatKey, out var overrideId))
            return overrideId;
        return DefaultIdTransform(pcgenFeatKey);
    }

    public string MapFeat(string pcgenFeatKey) => "feat:" + MapFeatBare(pcgenFeatKey);

    public static string MapSkillBare(string pcgenSkillName)
    {
        if (SkillOverrides.TryGetValue(pcgenSkillName, out var overrideId))
            return overrideId;
        return DefaultIdTransform(pcgenSkillName);
    }

    public string MapSkill(string pcgenSkillName) => "skill:" + MapSkillBare(pcgenSkillName);

    public string MapDomain(string pcgenDomainName)
    {
        return "domain:" + DefaultIdTransform(pcgenDomainName);
    }

    public string MapTemplate(string pcgenTemplateName)
    {
        return "template:" + DefaultIdTransform(pcgenTemplateName);
    }

    /// <summary>
    /// Language ids are bare and unprefixed, unlike every other id here — that is the existing
    /// convention in content: <c>race:hellbred</c> grants <c>infernal</c> and
    /// <c>class:dragon_disciple</c> requires <c>draconic</c>. There is no language registry to
    /// validate against, so this is a pure name transform.
    /// </summary>
    public static string MapLanguage(string pcgenLanguageName) =>
        DefaultIdTransform(pcgenLanguageName);

    /// <summary>
    /// Resolves a PCGen item name (e.g. "Belt of Giant Strength +6") to a catalog ID.
    /// Strategy: explicit override → exact name match in registry → name with "+N" stripped.
    /// Returns null if nothing matches; caller is expected to warn and skip.
    /// </summary>
    public string? MapEquipment(string pcgenName, ContentRegistry? registry)
    {
        if (EquipmentOverrides.TryGetValue(pcgenName, out var ovr))
            return ovr;

        if (registry == null) return null;

        if (registry.TryGetEquipmentByName(pcgenName, out var def))
            return def!.Id;

        // Strip enhancement suffix ("Cloak of Resistance +3" → "Cloak of Resistance") for a second try.
        var stripped = Regex.Replace(pcgenName, @"\s*\+\d+\s*$", "").Trim();
        if (stripped != pcgenName && registry.TryGetEquipmentByName(stripped, out def))
            return def!.Id;

        return null;
    }

    public bool IsWeaponSlot(string pcgSlot) => WeaponSlotLabels.Contains(pcgSlot);

    /// <summary>
    /// Translates a PCGen EQUIPSET body slot label to the engine's slot vocabulary.
    /// Unknown labels fall back to "carried" so unrecognized assignments don't pretend to be equipped.
    /// </summary>
    public string MapSlot(string pcgSlot) =>
        BodySlotMap.TryGetValue(pcgSlot, out var s) ? s : "carried";

    public (bool MainHand, bool TwoHanded) InferHand(string pcgSlot) => pcgSlot.ToLowerInvariant() switch
    {
        "primary weapon" or "equipped" => (true, false),
        "secondary weapon" or "off-hand" => (false, false),
        "two hand" or "both hands" => (true, true),
        _ => (true, false),
    };

    public static string DefaultIdTransform(string name)
    {
        var result = name.ToLowerInvariant();
        result = result.Replace('~', ' ').Replace('(', ' ').Replace(')', ' ').Replace('-', ' ');
        result = result.Replace("'", "");
        result = result.Replace(' ', '_');
        result = Regex.Replace(result, "_+", "_");
        return result.Trim('_');
    }
}
