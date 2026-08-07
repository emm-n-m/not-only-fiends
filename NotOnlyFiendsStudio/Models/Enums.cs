namespace NotOnlyFiendsStudio.Models;

public enum Ability { STR, DEX, CON, INT, WIS, CHA }

public enum Size { Fine, Diminutive, Tiny, Small, Medium, Large, Huge, Gargantuan, Colossal, ColossalPlus }

public enum CreatureType
{
    Aberration, Animal, Construct, Dragon, Elemental, Fey, Giant,
    Humanoid, MagicalBeast, MonstrousHumanoid, Ooze, Outsider,
    Plant, Undead, Vermin
}

public static class CreatureTypes
{
    /// <summary>
    /// SRD: undead and constructs are the two types that are not alive. Derived rather than
    /// authored because it follows from the type with no exceptions — a template that turns a
    /// creature undead makes it non-living by that fact alone, and content that had to restate it
    /// would silently disagree with the type sooner or later.
    /// </summary>
    public static bool IsLiving(CreatureType type) =>
        type is not (CreatureType.Undead or CreatureType.Construct);

    /// <summary>The incorporeal subtype, spelled once so race and template paths agree.</summary>
    public const string IncorporealSubtype = "incorporeal";
}

public enum MovementMode { Land, Fly, Swim, Burrow, Climb }

public enum FlightManeuverability { Clumsy, Poor, Average, Good, Perfect }

public enum Alignment { LG, LN, LE, NG, N, NE, CG, CN, CE }

public enum CastingType { Arcane, Divine }

/// <summary>
/// How a caster comes by the spells available to it. Distinct from <see cref="CastingType"/>,
/// which is only arcane vs divine — the two are independent (a wizard and a sorcerer are both
/// arcane and acquire spells completely differently).
/// </summary>
public enum SpellAcquisition
{
    /// <summary>
    /// The whole class list is available, with no per-level choice: cleric, druid, paladin,
    /// ranger, adept, blackguard. Preparation is a daily activity the engine does not model, so
    /// there is nothing for a character build to select.
    /// </summary>
    FullList,

    /// <summary>
    /// Spells must be written into a spellbook before they can be prepared: the wizard. Every
    /// 0-level spell is in the book from 1st level; 3 + Intelligence bonus 1st-level spells are
    /// chosen at 1st level, and 2 more of any castable level at each wizard level thereafter.
    /// </summary>
    Spellbook,

    /// <summary>
    /// A fixed number of spells known per spell level, cast without preparation: sorcerer, bard,
    /// assassin. The count comes from the class's <c>spellsKnown</c> progression.
    /// </summary>
    SpellsKnown,

    /// <summary>
    /// Epic spells acquired individually through development. They are permanent build choices,
    /// but are neither constrained by a spells-known table nor written in a spellbook.
    /// </summary>
    Developed,
}

public enum BABProgression { Good, Average, Poor }

public enum ProgressionRate { Good, Poor }

public enum FeatType
{
    General, FighterBonus, Metamagic, ItemCreation, Epic,
    Divine, Vile, Exalted, Tactical, Other
}

public enum AttributeTarget
{
    NaturalArmor,
    SpellResistance,
    LevelAdjustment,
    Resistance,
    AbilityScore,
    AllSaves
}

// 3.5e bonus-type taxonomy. Stacking rule: Dodge and Untyped stack with everything
// (including themselves); all other types only the highest of a given type applies
// per target (AC, attack, save, ability). See BonusStack helper in Studio/.
public enum BonusType
{
    Armor,
    Shield,
    Natural,
    NaturalEnhancement,
    Enhancement,
    Deflection,
    Dodge,
    Insight,
    Luck,
    Sacred,
    Profane,
    Morale,
    Competence,
    Resistance,
    Circumstance,
    Size,
    Racial,
    Untyped
}
