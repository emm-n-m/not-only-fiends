namespace NotOnlyFiendsStudio.Models;

public enum Ability { STR, DEX, CON, INT, WIS, CHA }

public enum Size { Fine, Diminutive, Tiny, Small, Medium, Large, Huge, Gargantuan, Colossal, ColossalPlus }

public enum CreatureType
{
    Aberration, Animal, Construct, Dragon, Elemental, Fey, Giant,
    Humanoid, MagicalBeast, MonstrousHumanoid, Ooze, Outsider,
    Plant, Undead, Vermin
}

public enum MovementMode { Land, Fly, Swim, Burrow, Climb }

public enum Alignment { LG, LN, LE, NG, N, NE, CG, CN, CE }

public enum CastingType { Arcane, Divine }

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
