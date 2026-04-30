using System.Text.RegularExpressions;

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
        ["Medusa"] = "medusa",
        ["Worg"] = "worg",
        ["Sahuagin (Mutant)"] = "sahuagin_mutant",

        // SRD companions/familiars
        ["Companion ~ Snake (Viper/Tiny)"] = "companion_snake_viper_tiny",
        ["Companion ~ Snake (Viper/Medium)"] = "companion_snake_viper_medium",
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
        // Add overrides for feats whose names don't follow the algorithmic transform
    };

    private static readonly Dictionary<string, string> SkillOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Knowledge (The Planes)"] = "knowledge_planes",
    };

    public string? MapRace(string pcgenRace)
    {
        return RaceMap.GetValueOrDefault(pcgenRace);
    }

    public string? MapClass(string pcgenClass)
    {
        return ClassMap.GetValueOrDefault(pcgenClass);
    }

    public string MapFeat(string pcgenFeatKey)
    {
        if (FeatOverrides.TryGetValue(pcgenFeatKey, out var overrideId))
            return overrideId;
        return DefaultIdTransform(pcgenFeatKey);
    }

    public string MapSkill(string pcgenSkillName)
    {
        if (SkillOverrides.TryGetValue(pcgenSkillName, out var overrideId))
            return overrideId;
        return DefaultIdTransform(pcgenSkillName);
    }

    public string MapDomain(string pcgenDomainName)
    {
        return "domain:" + DefaultIdTransform(pcgenDomainName);
    }

    public string MapTemplate(string pcgenTemplateName)
    {
        return "template:" + DefaultIdTransform(pcgenTemplateName);
    }

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
