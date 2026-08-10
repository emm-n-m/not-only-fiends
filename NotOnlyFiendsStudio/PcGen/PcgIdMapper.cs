using System.Text.RegularExpressions;
using NotOnlyFiendsStudio.Models;
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
        ["Companion ~ Snake (Viper/Large)"] = "companion_snake_viper_large",
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
        // PCGen files familiars under the same "Companion ~" prefix. The toad is a familiar only —
        // it is not on any animal-companion list — so it resolves to the familiar race.
        ["Companion ~ Toad"] = "familiar_toad",

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
        ["Sorcerer/Cleric (Arcane)"] = "class:sorcerer",

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

        // PCGen's level-zero pseudo classes for developed epic spells. These are recognized
        // spell lists in the engine, but deliberately are not selectable HD drivers.
        ["Epic Spells (CHA)"] = EpicSpellcasting.CharismaListId,
        ["Epic Spells (INT)"] = EpicSpellcasting.IntelligenceListId,
        ["Epic Spells (WIS)"] = EpicSpellcasting.WisdomListId,

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
        ["Paladin of Freedom"] = "class:paladin_of_freedom",
        ["Paladin of Slaughter"] = "class:paladin_of_slaughter",
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
        ["Knowledge (Architecture and Engineering)"] = "knowledge_architecture",
        ["Knowledge (History/Abyss)"] = "knowledge_history_abyss",
        ["Knowledge (The Planes)"] = "knowledge_planes",
    };

    private static readonly Dictionary<string, string> EquipmentOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        // Populated as PcgImportRegression surfaces names that don't match the catalog
        // exactly (e.g. abbreviations, parenthetical color suffixes). Catalog ID on the right.
        // Material/enhancement/size-variant names PCGen generates via its item customizer —
        // the catalog only carries the base item, so masterwork/material/+N/size are lost on import.
        ["Masterwork Cold Iron Longsword +2"] = "weapon:masterwork_cold_iron_longsword",
        ["Sylvan Scimitar +2 (Silver/Wounding)"] = "weapon:sylvan_scimitar",
        ["Oathbow (Small)"] = "weapon:oathbow",
        ["Harp Bow (Small)"] = "weapon:harp_bow",
        ["Harp Bow (Medium)"] = "weapon:harp_bow",
        ["Flail +2 (Heavy/Cold Iron)"] = "weapon:flail",
        ["Elven Chain (Small)"] = "armor:elven_chain",
        ["Longbow +1 (Small)"] = "weapon:longbow",
        ["Arrows +3 (50)"] = "ammunition:arrows_20",
        ["Slaying Arrow (Greater/Outsiders Slaying Arrow (Greater/evil))"] = "ammunition:slaying_arrow_greater",
        // PCGen names that reorder/reformat an existing catalog item's display name rather than
        // varying it — the catalog entry already covers all color/size variants in its description.
        ["Robe of the Archmagi (White)"] = "wondrous:robe_of_the_archmagi",
        ["Robe of the Archmagi (Black)"] = "wondrous:robe_of_the_archmagi",
        ["Horn of Blasting (Greater)"] = "wondrous:horn_of_blasting_greater",
        ["Horn of Valhalla (Iron)"] = "wondrous:horn_of_valhalla",
        ["Boots (Winged)"] = "wondrous:boots_winged",
        ["Vestments (Druid)"] = "wondrous:vestment_druid_s",
        ["Outfit (Entertainer's)"] = "gear:entertainers_outfit",
        ["Outfit (Scholar's)"] = "gear:scholars_outfit",
        ["Outfit (Scholar's/Small)"] = "gear:scholars_outfit",
        ["Outfit (Traveler's)"] = "gear:travelers_outfit",
        ["Pouch (Belt)"] = "gear:pouch_belt_empty",
        ["Spellbook (Wizard's/Blank)"] = "gear:spellbook_wizards_blank",
        ["Thieves' Tools (Tiny)"] = "gear:thieves_tools",
        ["Vestments (Cleric's)"] = "gear:clerics_vestments",
        ["Ring of Wizardry IV"] = "ring:wizardry_iv",
        ["Ring of Wizardry IX"] = "ring:wizardry_ix",
        ["Staff (Abjuration)"] = "staff:abjuration",
        ["Staff (Divination)"] = "staff:divination",
        ["Staff (Woodlands)"] = "staff:woodlands",
        ["Staff (Cosmos)"] = "staff:cosmos",
        ["Staff (Domination)"] = "staff:domination",
        ["Rod (Epic Spellcaster)"] = "rod:epic_spellcasting",
        // LST-style names the retired pcgen_srd pack used to answer. srd_core carries the same
        // items under their SRD names, so these spellings need an explicit bridge.
        ["Shield, Light Wood"] = "shield:light_wooden",
        ["Shield, Light Metal"] = "shield:light_steel",
        ["Shield, Heavy Wood"] = "shield:heavy_wooden",
        ["Shield, Heavy Metal"] = "shield:heavy_steel",
        ["Shield, Tower Wood"] = "shield:tower",
        ["Shieldbash (Light)"] = "weapon:shieldbash_light",
        ["Shieldbash (Heavy)"] = "weapon:shieldbash_heavy",
        ["Sword, Short"] = "weapon:shortsword",
        ["Shuriken"] = "weapon:shuriken",
        ["Longbow (Composite)"] = "weapon:longbow_composite",
        ["Shortbow (Composite)"] = "weapon:shortbow_composite",
        ["Cold Iron Longsword"] = "weapon:masterwork_cold_iron_longsword",
        ["Silver Dagger"] = "weapon:masterwork_silver_dagger",
        // PCGen models a rod's or staff's melee attack as a separate weapon entry; the catalog
        // carries one item per rod/staff, so both spellings resolve to it.
        ["Rod (Besiegement)"] = "rod:besiegement",
        ["Rod (Epic Might)"] = "rod:epic_might",
        ["Rod (Fortification)"] = "rod:fortification",
        ["Rod of the Black Wyrm"] = "rod:wyrm_black_copper",
        ["Rod of the Copper Wyrm"] = "rod:wyrm_black_copper",
        ["Rod of the White Wyrm"] = "rod:wyrm_white_brass",
        ["Rod of the Brass Wyrm"] = "rod:wyrm_white_brass",
        ["Rod of the Green Wyrm"] = "rod:wyrm_green_bronze",
        ["Rod of the Bronze Wyrm"] = "rod:wyrm_green_bronze",
        ["Rod of the Blue Wyrm"] = "rod:wyrm_blue_silver",
        ["Rod of the Silver Wyrm"] = "rod:wyrm_blue_silver",
        ["Rod of the Red Wyrm"] = "rod:wyrm_red_gold",
        ["Rod of the Gold Wyrm"] = "rod:wyrm_red_gold",
        ["Staff (Fiery Power)"] = "staff:fiery_power",
        ["Staff (Nature's Fury)"] = "staff:natures_fury",
        ["Staff of Planar Might (Chaotic Outsider Bane)"] = "staff:planar_might",
        ["Staff of Planar Might (Evil Outsider Bane)"] = "staff:planar_might",
        ["Staff of Planar Might (Good Outsider Bane)"] = "staff:planar_might",
        ["Staff of Planar Might (Lawful Outsider Bane)"] = "staff:planar_might",
        // PCGen splits an item's granted attack into its own equipment row. The catalog folds
        // each of these into the parent item (as a GrantWeaponLine), so point them at the parent.
        ["Demon Armor Claw Attack"] = "armor:demon_armor",
        ["Armor of the Abyssal Horde Claw Attack"] = "armor:armor_of_the_abyssal_horde",
        ["Flurry of Blows"] = "weapon:unarmed_strike",
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
        ["Arms"] = "wrists",
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
        "Primary Hand",
        "Secondary Weapon",
        "Secondary Hand",
        "Off-hand",
        "Two Hand",
        "Both Hands",
        "Double Weapon",
    };

    public string? MapRace(string pcgenRace)
    {
        var bare = RaceMap.GetValueOrDefault(pcgenRace);
        return bare == null ? null : "race:" + bare;
    }

    /// <summary>
    /// Unearthed Arcana alternate class features that decide which driver a class row resolves to.
    /// PCGen keeps the base class on the CLASS row and records the variant as a separate ACF
    /// ability, so the class name alone cannot answer it — a "Druid-like Bard" is still
    /// <c>CLASS:Bard</c>. The engine models a variant as its own driver (as it does the paladin
    /// variants and the cloistered cleric), because that is the only way to express what the
    /// variant *loses*. Keyed by the ACF's PCGen KEY. "Regular Bard" is listed too: it selects no
    /// variant, and naming it here is what stops it being reported as an unmatched selection.
    ///
    /// This is deliberately static and read-only. The resolved swap is per character, and
    /// PcgIdMapper instances are shared across a whole corpus by the import regression — holding
    /// the swap here would let one character's variant follow the mapper onto the next.
    /// </summary>
    private static readonly Dictionary<string, (string PcgenClass, string DriverId)> ClassSelectingAcf =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // PCGen writes this one variant two ways — from the bard's own variant pool and from
            // its generic ACF pool — quoting the same UA paragraph in both. Same variant, same
            // driver. (The two disagree on the companion's level: the generic row halves it,
            // which UA does not say for the bard. See CONTENT_GAPS.md.)
            ["Bard Variant ~ Druid-like Bard"] = ("Bard", "class:druid_like_bard"),
            ["Bard ~ Animal Companion"] = ("Bard", "class:druid_like_bard"),
            ["Bard Variant ~ Regular Bard"] = ("Bard", "class:bard"),
        };

    public static bool TryGetClassSelectingAcf(
        string abilityKey, out string pcgenClass, out string driverId)
    {
        if (ClassSelectingAcf.TryGetValue(abilityKey, out var swap))
        {
            (pcgenClass, driverId) = swap;
            return true;
        }

        (pcgenClass, driverId) = (string.Empty, string.Empty);
        return false;

    }

    public static bool IsClassSelectingAcf(string abilityKey) =>
        ClassSelectingAcf.ContainsKey(abilityKey);

    /// <summary>
    /// PCGen substitution classes that resolve to a driver of their own, keyed by the name on the
    /// level row's <c>SUBSTITUTIONLEVEL:</c> field. Substitution is per level in PCGen and
    /// per class here, which is only equivalent while a substitution class differs from its base
    /// at one level — true of the entries below. A substitution class that changed several levels
    /// would need the character's substituted levels tracked individually instead.
    /// </summary>
    private static readonly Dictionary<string, (string PcgenClass, string DriverId)> SubstitutionClasses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Elemental Druid Option"] = ("Druid", "class:elemental_druid"),
        };

    public static bool TryGetSubstitutionClass(
        string substitutionName, out string pcgenClass, out string driverId)
    {
        if (SubstitutionClasses.TryGetValue(substitutionName, out var swap))
        {
            (pcgenClass, driverId) = swap;
            return true;
        }

        (pcgenClass, driverId) = (string.Empty, string.Empty);
        return false;
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
    /// Resolves a PCGen spell name to the catalog. Prefer the conventional ID, then fall back to
    /// an exact display-name match for legacy names whose content ID uses different word order.
    /// Without a registry, return the conventional ID so lightweight conversion remains usable.
    /// </summary>
    public string? MapSpell(string pcgenSpellName, ContentRegistry? registry)
    {
        // PCGen stores parenthesized qualifiers in reverse display order. The content id follows
        // the repository's normal "mass" suffix convention.
        if (pcgenSpellName.Equals("Frog (Mass)", StringComparison.OrdinalIgnoreCase))
            return registry == null || registry.TryGetSpell("spell:frog_mass", out _)
                ? "spell:frog_mass"
                : null;

        var id = "spell:" + DefaultIdTransform(pcgenSpellName);
        if (registry == null || registry.TryGetSpell(id, out _))
            return id;

        return registry.TryGetSpellByName(pcgenSpellName, out var spell)
            ? spell!.Id
            : null;
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

    public (bool MainHand, bool TwoHanded, bool DoubleWeapon) InferHand(string pcgSlot) => pcgSlot.ToLowerInvariant() switch
    {
        "primary weapon" or "primary hand" or "equipped" => (true, false, false),
        "secondary weapon" or "secondary hand" or "off-hand" => (false, false, false),
        "two hand" or "both hands" => (true, true, false),
        "double weapon" => (true, false, true),
        _ => (true, false, false),
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
