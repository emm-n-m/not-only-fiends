using System.Text.RegularExpressions;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.PcGen;

public class PcgIdMapper
{
    private static readonly Dictionary<string, string> PreferredRaceNames = new(StringComparer.Ordinal)
    {
        ["race:human"] = "Human",
        ["race:dwarf"] = "Dwarf",
        ["race:elf"] = "Elf",
        ["race:gnome"] = "Gnome",
        ["race:half_elf"] = "Half-Elf",
        ["race:half_orc"] = "Half-Orc",
        ["race:halfling"] = "Halfling",
        ["race:outsider"] = "Outsider",
        ["race:pixie"] = "Pixie",
        ["race:drow"] = "Elf ~ Drow",
        ["race:devil_imp"] = "Devil (Imp)",
        ["race:devil_erinyes"] = "Devil (Erinyes)",
        ["race:demon_succubus"] = "Demon (Succubus)",
        ["race:medusa"] = "Medusa",
        ["race:worg"] = "Worg",
        ["race:sahuagin_mutant"] = "Sahuagin (Mutant)",
        ["race:satyr"] = "Satyr",
        ["race:couatl"] = "Couatl",
        ["race:nymph"] = "Nymph",
        ["race:aranea"] = "Aranea",
        ["race:hell_hound"] = "Hell Hound",
        ["race:archfiend"] = "Archfiend",
        ["race:companion_snake_viper_tiny"] = "Companion ~ Snake (Viper/Tiny)",
        ["race:companion_snake_viper_medium"] = "Companion ~ Snake (Viper/Medium)",
        ["race:companion_snake_viper_large"] = "Companion ~ Snake (Viper/Large)",
        ["race:companion_snake_constrictor"] = "Snake (Constrictor)",
        ["race:companion_tiger"] = "Companion ~ Tiger",
        ["race:companion_devil_imp"] = "Companion ~ Devil (Imp)",
        ["race:companion_raven"] = "Companion ~ Raven",
        ["race:companion_leopard"] = "Companion ~ Leopard",
        ["race:companion_hawk"] = "Companion ~ Hawk",
        ["race:companion_bat"] = "Companion ~ Bat",
        ["race:companion_elemental_air_small"] = "Companion ~ Elemental (Air Small)",
        ["race:companion_elemental_water_small"] = "Companion ~ Elemental (Water Small)",
        ["race:companion_shadow"] = "Companion ~ Shadow",
        ["race:familiar_toad"] = "Companion ~ Toad",
        ["race:dragon_red_great_wyrm"] = "Dragon (Red Great Wyrm)",
        ["race:dragon_red_great_wyrm_colossal_plus"] = "Dragon (Red Great Wyrm/Colossal Plus)",
    };

    private static readonly Dictionary<string, string> PreferredClassNames = new(StringComparer.Ordinal)
    {
        ["class:fighter"] = "Fighter",
        ["class:barbarian"] = "Barbarian",
        ["class:cleric"] = "Cleric",
        ["class:sorcerer"] = "Sorcerer",
        ["class:adept"] = "Adept",
        ["class:aristocrat"] = "Aristocrat",
        ["class:bard"] = "Bard",
        ["class:commoner"] = "Commoner",
        ["class:druid"] = "Druid",
        ["class:expert"] = "Expert",
        ["class:monk"] = "Monk",
        ["class:paladin"] = "Paladin",
        ["class:ranger"] = "Ranger",
        ["class:rogue"] = "Rogue",
        ["class:warrior"] = "Warrior",
        ["class:wizard"] = "Wizard",
        ["class:arcane_archer"] = "Arcane Archer",
        ["class:arcane_trickster"] = "Arcane Trickster",
        ["class:archmage"] = "Archmage",
        ["class:assassin"] = "Assassin",
        ["class:blackguard"] = "Blackguard",
        ["class:dragon_disciple"] = "Dragon Disciple",
        ["class:duelist"] = "Duelist",
        ["class:dwarven_defender"] = "Dwarven Defender",
        ["class:eldritch_knight"] = "Eldritch Knight",
        ["class:hierophant"] = "Hierophant",
        ["class:horizon_walker"] = "Horizon Walker",
        ["class:loremaster"] = "Loremaster",
        ["class:mystic_theurge"] = "Mystic Theurge",
        ["class:shadowdancer"] = "Shadowdancer",
        ["class:thaumaturgist"] = "Thaumaturgist",
        ["class:cosmic_descryer"] = "Cosmic Descryer",
        ["class:cloistered_cleric"] = "Cleric (Cloistered Cleric)",
        ["class:paladin_of_tyranny"] = "Paladin of Tyranny",
        ["class:paladin_of_freedom"] = "Paladin of Freedom",
        ["class:paladin_of_slaughter"] = "Paladin of Slaughter",
        ["class:planar_ranger"] = "Ranger (Planar Ranger)",
        ["class:dark_temptress"] = "Dark Temptress",
        ["class:blood_witch"] = "Blood Witch",
        ["class:blood_hexer"] = "Blood Hexer",
        ["class:spectral_loremaster"] = "Spectral Loremaster",
        ["class:deathseeker"] = "Deathseeker",
        ["class:demonologist"] = "Demonologist",
        ["class:binder"] = "Binder",
        ["class:possessed"] = "Possessed",
        ["class:demon_summoner"] = "Demon Summoner",
        ["class:bargainer"] = "Bargainer",
        ["class:blood_archer"] = "Blood Archer",
        ["class:favored_soul"] = "Favored Soul",
        ["class:arcane_hierophant"] = "Arcane Hierophant",
        ["class:archfiend"] = "Archfiend",
        ["class:druid_like_bard"] = "Bard",
        ["class:elemental_druid"] = "Druid",
        ["racial_hd:outsider"] = "Outsider",
        ["racial_hd:animal"] = "Animal",
        ["racial_hd:elemental"] = "Elemental",
        ["racial_hd:fey"] = "Fey",
        ["racial_hd:magical_beast"] = "Magical Beast",
        ["racial_hd:monstrous_humanoid"] = "Monstrous Humanoid",
        ["racial_hd:undead"] = "Undead",
        ["racial_hd:red_dragon"] = "Red Dragon",
        ["racial_hd:cloud_dragon"] = "Cloud Dragon",
        ["racial_hd:mist_dragon"] = "Mist Dragon",
        ["racial_hd:the_oinodaemon"] = "The Oinodaemon",
    };
    private static readonly Dictionary<string, string> RaceMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Human"] = "human",
        ["Dwarf"] = "dwarf",
        ["Elf"] = "elf",
        ["Gnome"] = "gnome",
        ["Half-Elf"] = "half_elf",
        ["Half-Orc"] = "half_orc",
        ["Halfling"] = "halfling",
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
        ["Demonologist"] = "class:demonologist",
        ["Binder"] = "class:binder",
        ["Possessed"] = "class:possessed",
        ["Demon Summoner"] = "class:demon_summoner",
        ["Bargainer"] = "class:bargainer",
        ["Blood Archer"] = "class:blood_archer",

        // DECEIT homebrew classes
        ["Favored Soul"] = "class:favored_soul",
        ["Arcane Hierophant"] = "class:arcane_hierophant",

        // Monster classes (racial HD with class-like progression)
        // Archfiend levels are racial HD on the shared outsider chassis — the creature *is* its
        // Hit Dice, 24 being the floor. Its casting identity stays class:archfiend; see
        // MapCastingClass.
        ["Archfiend"] = "racial_hd:outsider",
        ["Red Dragon"] = "racial_hd:red_dragon",
        ["Cloud Dragon"] = "racial_hd:cloud_dragon",
        ["Mist Dragon"] = "racial_hd:mist_dragon",
        ["The Oinodaemon"] = "racial_hd:the_oinodaemon",

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

    /// <summary>Preferred PCGen save key for an engine race id.</summary>
    public string? ToPcgenRace(string raceId) => PreferredRaceNames.GetValueOrDefault(raceId);

    /// <summary>
    /// Preferred PCGen class key for a driver. The race disambiguates monster classes which use
    /// a shared racial-HD chassis in the engine but retain their own class key in PCGen.
    /// </summary>
    public string? ToPcgenClass(string driverId, string? raceId = null)
    {
        if (driverId == "racial_hd:outsider" && raceId == "race:archfiend")
            return "Archfiend";
        return PreferredClassNames.GetValueOrDefault(driverId);
    }

    public static string? ToPcgenSubstitutionLevel(string driverId) => driverId switch
    {
        "class:elemental_druid" => "Elemental Druid Option",
        _ => null,
    };

    public static string? ToPcgenVariantAbility(string driverId) => driverId switch
    {
        "class:druid_like_bard" => "Bard Variant ~ Druid-like Bard",
        _ => null,
    };

    public static string? ToPcgenRaceAddition(string raceId) => raceId switch
    {
        "race:nymph" => "ADD:[SPELLCASTER:Druid|CHOICE:Druid]",
        _ => null,
    };

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

    /// <summary>
    /// Where a PCGen class's <em>casting identity</em> differs from the driver its levels become.
    /// The Archfiend is a monster class: its levels are racial HD on a shared outsider chassis, but
    /// its spells belong to the Archfiend, which is the caster <c>template:archfiend</c> seeds and
    /// the list templates point at. Everything else casts as the class whose levels it took.
    /// </summary>
    private static readonly Dictionary<string, string> CastingClassOverrides = new(StringComparer.Ordinal)
    {
        ["Archfiend"] = "class:archfiend",
    };

    /// <inheritdoc cref="CastingClassOverrides"/>
    public string? MapCastingClass(string pcgenClass) =>
        CastingClassOverrides.TryGetValue(pcgenClass, out var casterId) ? casterId : MapClass(pcgenClass);

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
    /// <summary>
    /// Deliberate user-directed substitutions (2026-08-27): third-party spells re-specced to an
    /// SRD equivalent at the same spell level instead of being extracted. Extracting was reserved
    /// for signature spells (X-Ray Vision); these three are workhorse picks a standard spell
    /// covers. Substitutes are per receiving class, keyed by class id with "" as the default —
    /// the corpus proved a flat table wrong: Acid Splash is off-list for a favored soul, and
    /// Daze is enchantment, which one corpus wizard has given up. Levels verified against the
    /// .pcg SPELLLEVEL records (0/0/5).
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, string>> SpellSubstitutions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Dim Illumination"] = new()
            {
                ["class:favored_soul"] = "spell:light",
                [""] = "spell:acid_splash",
            },
            ["Lock/Unlock"] = new()
            {
                ["class:sorcerer"] = "spell:daze",
                [""] = "spell:open_close",
            },
            ["Blindness/Deafness (Mass)"] = new() { [""] = "spell:prying_eyes" },
        };

    public string? MapSpell(string pcgenSpellName, ContentRegistry? registry, string? classId = null)
    {
        if (SpellSubstitutions.TryGetValue(pcgenSpellName, out var byClass))
        {
            var substitute = byClass.GetValueOrDefault(classId ?? "") ?? byClass[""];
            return registry == null || registry.TryGetSpell(substitute, out _) ? substitute : null;
        }

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
