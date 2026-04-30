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
