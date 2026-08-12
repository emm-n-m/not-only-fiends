namespace NotOnlyFiendsStudio.Models;

public enum BonusTarget
{
    AC,
    Attack,
    Damage,
    SaveFort,
    SaveRef,
    SaveWill,
    AllSaves,
    AbilityStr,
    AbilityDex,
    AbilityCon,
    AbilityInt,
    AbilityWis,
    AbilityCha,
    SkillRanks,
    SR,
    NaturalArmor
}

public enum EquipmentCategory
{
    Weapon,
    Armor,
    Shield,
    Wondrous,
    Ring,
    Rod,
    Staff,
    Wand,
    Scroll,
    Potion,
    Gear,
    Ammunition,
    Other
}

public enum ArmorKind
{
    Light,
    Medium,
    Heavy,
    Shield,
    TowerShield
}

public class WeaponProfile
{
    public string Damage { get; set; } = string.Empty;
    public int CritRangeLow { get; set; } = 20;
    public int CritMultiplier { get; set; } = 2;
    public int RangeFt { get; set; }
    public string DamageType { get; set; } = string.Empty;
    public bool TwoHanded { get; set; }
    public bool Light { get; set; }
    public bool Reach { get; set; }
    public bool DoubleWeapon { get; set; }
    public bool Ranged { get; set; }
    public bool Thrown { get; set; }
    public string Proficiency { get; set; } = string.Empty;
}

public class ArmorProfile
{
    public ArmorKind Kind { get; set; }
    public int ArmorBonus { get; set; }
    public int? MaxDex { get; set; }
    public int CheckPenalty { get; set; }
    public int ArcaneFailurePct { get; set; }
    public int Speed30 { get; set; } = 30;
    public int Speed20 { get; set; } = 20;
}

public class EquipmentDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public EquipmentCategory Category { get; set; }
    public string? Slot { get; set; }
    public int WeightLbs { get; set; }
    public long PriceCp { get; set; }
    public string? Description { get; set; }
    public List<Prerequisite> Prerequisites { get; set; } = new();
    public List<Permabuff> GrantedPermabuffs { get; set; } = new();
    public WeaponProfile? Weapon { get; set; }
    /// <summary>Magic enhancement (or cursed penalty) applied to attack and damage rolls.</summary>
    public int EnhancementBonus { get; set; }
    /// <summary>Total +1-equivalent value of magic special abilities, used by intelligent-item Ego.</summary>
    public int SpecialAbilityBonusEquivalent { get; set; }
    public ArmorProfile? Armor { get; set; }
    /// <summary>Personality and powers for a permanent intelligent magic item.</summary>
    public IntelligentItemDefinition? IntelligentItem { get; set; }
    public List<string> Tags { get; set; } = new();
}
