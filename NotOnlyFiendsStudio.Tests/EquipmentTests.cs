using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

public class EquipmentTests
{
    private static ContentRegistry BuildRegistry()
    {
        var registry = new ContentRegistry();
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:human",
            Name = "Human",
            Type = CreatureType.Humanoid,
            Size = Size.Medium,
            Speeds = new Dictionary<MovementMode, int> { { MovementMode.Land, 30 } }
        });
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:fighter",
            Name = "Fighter",
            HitDie = 10,
            SkillPointsPerLevel = 2,
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Good,
                Ref = ProgressionRate.Poor,
                Will = ProgressionRate.Poor
            }
        });
        registry.RegisterFeat(new FeatDefinition
        {
            Id = "feat:two_weapon_fighting",
            Name = "Two-Weapon Fighting",
            Type = FeatType.General
        });
        return registry;
    }

    private static Character BuildFighter(int level, int str = 16, int dex = 14, int con = 14)
    {
        var ticks = new List<Tick>();
        for (int i = 0; i < level; i++)
            ticks.Add(new Tick { DriverId = "class:fighter", Choices = new TickChoices() });
        return new Character
        {
            Name = "Test Fighter",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = str, DEX = dex, CON = con, INT = 10, WIS = 10, CHA = 10 },
            Ticks = ticks
        };
    }

    [Fact]
    public void NoEquipment_AcTotalIs10PlusDex()
    {
        var registry = BuildRegistry();
        var engine = new ReplayStudio(registry);
        var character = BuildFighter(level: 1, dex: 14);

        var state = engine.Evaluate(character);

        Assert.Equal(12, state.AC.Total); // 10 + dex 2
        Assert.Equal(12, state.AC.Touch);
        Assert.Equal(10, state.AC.FlatFooted);
    }

    [Fact]
    public void NegativeDexterityStillAppliesWhileFlatFooted()
    {
        var state = new ReplayStudio(BuildRegistry()).Evaluate(BuildFighter(level: 1, dex: 8));

        Assert.Equal(9, state.AC.Total);
        Assert.Equal(9, state.AC.Touch);
        Assert.Equal(9, state.AC.FlatFooted);
    }

    [Fact]
    public void SmallSizeImprovesArmorClassAndWeaponAttacks()
    {
        var registry = BuildRegistry();
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:small_test",
            Name = "Small Test Race",
            Type = CreatureType.Humanoid,
            Size = Size.Small,
            Speeds = new Dictionary<MovementMode, int> { [MovementMode.Land] = 20 }
        });
        var character = BuildFighter(level: 1, str: 16, dex: 14);
        character.RaceId = "race:small_test";
        character.Equipment.Add(new EquipmentEntry
        {
            ItemId = "Test Sword",
            Permabuffs = new List<Permabuff>
            {
                new GrantWeaponLine
                {
                    Profile = new WeaponProfile { Damage = "1d6" },
                    DisplayName = "Test Sword"
                }
            }
        });

        var state = new ReplayStudio(registry).Evaluate(character);

        Assert.Equal(1, state.AC.Components[BonusType.Size]);
        Assert.Equal(13, state.AC.Total);
        Assert.Equal(11, state.AC.FlatFooted);
        Assert.Equal(5, Assert.Single(state.AttackLines).Bonuses[0]); // BAB 1 + STR 3 + size 1.
    }

    [Fact]
    public void ConstitutionEquipmentRecalculatesHitPoints()
    {
        var character = BuildFighter(level: 2, con: 12);
        character.Equipment.Add(new EquipmentEntry
        {
            ItemId = "Constitution Amulet",
            Permabuffs = new List<Permabuff>
            {
                new GrantTypedBonus
                {
                    Target = BonusTarget.AbilityCon,
                    BonusType = BonusType.Enhancement,
                    Value = new Formula("2")
                }
            }
        });

        var state = new ReplayStudio(BuildRegistry()).Evaluate(character);

        Assert.Equal(14, state.AbilityScores.CON);
        Assert.Equal(20, state.HP); // 10 + 2 on first HD, then 6 + 2.
    }

    [Fact]
    public void FullPlate_GivesArmor8_CapsDexAt1()
    {
        var registry = BuildRegistry();
        registry.RegisterEquipment(new EquipmentDefinition
        {
            Id = "armor:full_plate",
            Name = "Full Plate",
            Category = EquipmentCategory.Armor,
            Slot = "body",
            WeightLbs = 50,
            Armor = new ArmorProfile { Kind = ArmorKind.Heavy, ArmorBonus = 8, MaxDex = 1, CheckPenalty = -6, Speed30 = 20, Speed20 = 15 }
        });
        var engine = new ReplayStudio(registry);
        var character = BuildFighter(1, dex: 18);
        character.Equipment.Add(new EquipmentEntry { ContentId = "armor:full_plate", Slot = "body" });

        var state = engine.Evaluate(character);

        Assert.Equal(8, state.AC.Components[BonusType.Armor]);
        Assert.Equal(1, state.AC.DexContribution); // dex +4 capped at +1
        Assert.Equal(1, state.AC.MaxDexCap);
        Assert.Equal(19, state.AC.Total);   // 10 + 8 + 1
        Assert.Equal(11, state.AC.Touch);   // 10 + dex 1 (armor excluded)
        Assert.Equal(18, state.AC.FlatFooted); // 10 + 8 (no dex)
    }

    [Fact]
    public void TwoArmorBonuses_DoNotStack_HighestWins()
    {
        var registry = BuildRegistry();
        var engine = new ReplayStudio(registry);
        var character = BuildFighter(1);
        character.Equipment.Add(new EquipmentEntry
        {
            ItemId = "Leather +2",
            Permabuffs = new List<Permabuff>
            {
                new GrantArmorProfile { Profile = new ArmorProfile { Kind = ArmorKind.Light, ArmorBonus = 4 } }
            }
        });
        character.Equipment.Add(new EquipmentEntry
        {
            ItemId = "Chain Shirt",
            Permabuffs = new List<Permabuff>
            {
                new GrantArmorProfile { Profile = new ArmorProfile { Kind = ArmorKind.Light, ArmorBonus = 4 } }
            }
        });

        var state = engine.Evaluate(character);

        Assert.Equal(4, state.AC.Components[BonusType.Armor]);
    }

    [Fact]
    public void TwoDodgeBonuses_DoStack()
    {
        var registry = BuildRegistry();
        var engine = new ReplayStudio(registry);
        var character = BuildFighter(1);
        character.Equipment.Add(new EquipmentEntry
        {
            ItemId = "Dodge Boots",
            Permabuffs = new List<Permabuff>
            {
                new GrantTypedBonus { Target = BonusTarget.AC, BonusType = BonusType.Dodge, Value = new Formula("1") }
            }
        });
        character.Equipment.Add(new EquipmentEntry
        {
            ItemId = "Dodge Ring",
            Permabuffs = new List<Permabuff>
            {
                new GrantTypedBonus { Target = BonusTarget.AC, BonusType = BonusType.Dodge, Value = new Formula("1") }
            }
        });

        var state = engine.Evaluate(character);

        Assert.Equal(2, state.AC.Components[BonusType.Dodge]);
    }

    [Fact]
    public void TwoDeflectionBonuses_DoNotStack()
    {
        var registry = BuildRegistry();
        var engine = new ReplayStudio(registry);
        var character = BuildFighter(1);
        character.Equipment.Add(new EquipmentEntry
        {
            ItemId = "Ring +2",
            Permabuffs = new List<Permabuff>
            {
                new GrantTypedBonus { Target = BonusTarget.AC, BonusType = BonusType.Deflection, Value = new Formula("2") }
            }
        });
        character.Equipment.Add(new EquipmentEntry
        {
            ItemId = "Ring +1",
            Permabuffs = new List<Permabuff>
            {
                new GrantTypedBonus { Target = BonusTarget.AC, BonusType = BonusType.Deflection, Value = new Formula("1") }
            }
        });

        var state = engine.Evaluate(character);

        Assert.Equal(2, state.AC.Components[BonusType.Deflection]);
    }

    [Fact]
    public void FullKit_FighterAcMatchesExpected()
    {
        var registry = BuildRegistry();
        registry.RegisterEquipment(new EquipmentDefinition
        {
            Id = "armor:full_plate",
            Name = "Full Plate",
            Category = EquipmentCategory.Armor,
            WeightLbs = 50,
            Armor = new ArmorProfile { Kind = ArmorKind.Heavy, ArmorBonus = 8, MaxDex = 1 }
        });
        registry.RegisterEquipment(new EquipmentDefinition
        {
            Id = "shield:heavy_steel",
            Name = "Heavy Steel Shield",
            Category = EquipmentCategory.Shield,
            WeightLbs = 15,
            Armor = new ArmorProfile { Kind = ArmorKind.Shield, ArmorBonus = 2 }
        });
        registry.RegisterEquipment(new EquipmentDefinition
        {
            Id = "wondrous:amulet_natural_armor_1",
            Name = "Amulet of Natural Armor +1",
            Category = EquipmentCategory.Wondrous,
            GrantedPermabuffs = new List<Permabuff>
            {
                new GrantTypedBonus { Target = BonusTarget.AC, BonusType = BonusType.NaturalEnhancement, Value = new Formula("1") }
            }
        });
        registry.RegisterEquipment(new EquipmentDefinition
        {
            Id = "ring:protection_2",
            Name = "Ring of Protection +2",
            Category = EquipmentCategory.Ring,
            GrantedPermabuffs = new List<Permabuff>
            {
                new GrantTypedBonus { Target = BonusTarget.AC, BonusType = BonusType.Deflection, Value = new Formula("2") }
            }
        });
        var engine = new ReplayStudio(registry);
        var character = BuildFighter(10, str: 18, dex: 14);
        character.Equipment.Add(new EquipmentEntry { ContentId = "armor:full_plate" });
        character.Equipment.Add(new EquipmentEntry { ContentId = "shield:heavy_steel" });
        character.Equipment.Add(new EquipmentEntry { ContentId = "wondrous:amulet_natural_armor_1" });
        character.Equipment.Add(new EquipmentEntry { ContentId = "ring:protection_2" });

        var state = engine.Evaluate(character);

        // 10 + armor 8 + shield 2 + natural-enh 1 + deflection 2 + dex 1 (capped) = 24
        Assert.Equal(8, state.AC.Components[BonusType.Armor]);
        Assert.Equal(2, state.AC.Components[BonusType.Shield]);
        Assert.Equal(1, state.AC.Components[BonusType.NaturalEnhancement]);
        Assert.Equal(2, state.AC.Components[BonusType.Deflection]);
        Assert.Equal(1, state.AC.DexContribution);
        Assert.Equal(24, state.AC.Total);
        Assert.Equal(13, state.AC.Touch);    // 10 + deflection 2 + dex 1
        Assert.Equal(23, state.AC.FlatFooted); // 10 + 8 + 2 + 1 + 2
    }

    [Fact]
    public void GauntletsOfOgrePower_AddEnhancementStr()
    {
        var registry = BuildRegistry();
        registry.RegisterEquipment(new EquipmentDefinition
        {
            Id = "wondrous:gauntlets_ogre_power",
            Name = "Gauntlets of Ogre Power",
            Category = EquipmentCategory.Wondrous,
            GrantedPermabuffs = new List<Permabuff>
            {
                new GrantTypedBonus { Target = BonusTarget.AbilityStr, BonusType = BonusType.Enhancement, Value = new Formula("2") }
            }
        });
        var engine = new ReplayStudio(registry);
        var character = BuildFighter(1, str: 14);
        character.Equipment.Add(new EquipmentEntry { ContentId = "wondrous:gauntlets_ogre_power" });

        var state = engine.Evaluate(character);

        Assert.Equal(16, state.AbilityScores.STR);
    }

    [Fact]
    public void TwoCloaksOfResistance_DoNotStackOnSaves()
    {
        var registry = BuildRegistry();
        var engine = new ReplayStudio(registry);
        var character = BuildFighter(1);
        character.Equipment.Add(new EquipmentEntry
        {
            ItemId = "Cloak +3",
            Permabuffs = new List<Permabuff>
            {
                new GrantTypedBonus { Target = BonusTarget.AllSaves, BonusType = BonusType.Resistance, Value = new Formula("3") }
            }
        });
        character.Equipment.Add(new EquipmentEntry
        {
            ItemId = "Cloak +2",
            Permabuffs = new List<Permabuff>
            {
                new GrantTypedBonus { Target = BonusTarget.AllSaves, BonusType = BonusType.Resistance, Value = new Formula("2") }
            }
        });

        var state = engine.Evaluate(character);

        // Base fighter L1 fort save = +2, +3 resistance (not +5).
        Assert.Equal(5, state.BaseSaves.Fort);
        Assert.Equal(3, state.BaseSaves.Ref);
        Assert.Equal(3, state.BaseSaves.Will);
    }

    [Fact]
    public void Iteratives_Bab6Yields6And1()
    {
        var registry = BuildRegistry();
        var engine = new ReplayStudio(registry);
        var character = BuildFighter(6, str: 16);
        character.Equipment.Add(new EquipmentEntry
        {
            ItemId = "Longsword",
            Permabuffs = new List<Permabuff>
            {
                new GrantWeaponLine
                {
                    Profile = new WeaponProfile { Damage = "1d8", DamageType = "slashing", CritRangeLow = 19, CritMultiplier = 2 },
                    DisplayName = "Longsword"
                }
            }
        });

        var state = engine.Evaluate(character);

        var line = Assert.Single(state.AttackLines);
        Assert.Equal(new[] { 9, 4 }, line.Bonuses); // BAB 6 + STR 3
    }

    [Fact]
    public void CatalogWeaponEnhancement_AppliesToAttackAndDamage()
    {
        var registry = BuildRegistry();
        registry.RegisterEquipment(new EquipmentDefinition
        {
            Id = "weapon:magic_longsword",
            Name = "+3 Longsword",
            Category = EquipmentCategory.Weapon,
            EnhancementBonus = 3,
            Weapon = new WeaponProfile
            {
                Damage = "1d8",
                CritRangeLow = 19,
                DamageType = "slashing",
                Proficiency = "martial"
            }
        });
        var engine = new ReplayStudio(registry);
        var character = BuildFighter(1, str: 14);
        character.Equipment.Add(new EquipmentEntry { ContentId = "weapon:magic_longsword" });

        var line = Assert.Single(engine.Evaluate(character).AttackLines);

        Assert.Equal(new[] { 6 }, line.Bonuses); // BAB 1 + STR 2 + enhancement 3
        Assert.Equal("1d8+5", line.Damage);      // STR 2 + enhancement 3
    }

    [Fact]
    public void Iteratives_Bab11Yields11_6_1()
    {
        var registry = BuildRegistry();
        var engine = new ReplayStudio(registry);
        var character = BuildFighter(11, str: 14);
        character.Equipment.Add(new EquipmentEntry
        {
            ItemId = "Greatsword",
            TwoHanded = true,
            Permabuffs = new List<Permabuff>
            {
                new GrantWeaponLine
                {
                    Profile = new WeaponProfile { Damage = "2d6", DamageType = "slashing", TwoHanded = true },
                    TwoHanded = true,
                    DisplayName = "Greatsword"
                }
            }
        });

        var state = engine.Evaluate(character);

        var line = Assert.Single(state.AttackLines);
        Assert.Equal(new[] { 13, 8, 3 }, line.Bonuses); // BAB 11 + STR 2
    }

    [Fact]
    public void Twf_WithFeatLightOffHand_GivesMinus2BothHands()
    {
        var registry = BuildRegistry();
        var engine = new ReplayStudio(registry);
        var character = BuildFighter(6, str: 16);
        character.Equipment.Add(new EquipmentEntry
        {
            ItemId = "Longsword",
            MainHand = true,
            Permabuffs = new List<Permabuff>
            {
                new GrantWeaponLine
                {
                    Profile = new WeaponProfile { Damage = "1d8" },
                    MainHand = true,
                    DisplayName = "Longsword"
                }
            }
        });
        character.Equipment.Add(new EquipmentEntry
        {
            ItemId = "Short Sword",
            MainHand = false,
            Permabuffs = new List<Permabuff>
            {
                new GrantWeaponLine
                {
                    Profile = new WeaponProfile { Damage = "1d6", Light = true },
                    MainHand = false,
                    DisplayName = "Short Sword"
                }
            }
        });
        character.Ticks[0].Choices = new TickChoices { FeatIds = new List<string> { "feat:two_weapon_fighting" } };

        var state = engine.Evaluate(character);

        // TWF feat + light off-hand: -2/-2. Main: BAB 6 + STR 3 - 2 = 7 (iteratives 7/2).
        // Off: BAB 6 + STR 3 - 2 = 7 (single attack).
        Assert.Equal(2, state.AttackLines.Count);
        Assert.Equal(new[] { 7, 2 }, state.AttackLines[0].Bonuses);
        Assert.Equal(new[] { 7 }, state.AttackLines[1].Bonuses);
        Assert.True(state.AttackLines[1].IsOffHand);
    }

    [Fact]
    public void Twf_WithoutFeat_GivesHeavierPenalty()
    {
        var registry = BuildRegistry();
        var engine = new ReplayStudio(registry);
        var character = BuildFighter(6, str: 16);
        character.Equipment.Add(new EquipmentEntry
        {
            ItemId = "Longsword",
            MainHand = true,
            Permabuffs = new List<Permabuff>
            {
                new GrantWeaponLine { Profile = new WeaponProfile { Damage = "1d8" }, MainHand = true, DisplayName = "Longsword" }
            }
        });
        character.Equipment.Add(new EquipmentEntry
        {
            ItemId = "Short Sword",
            MainHand = false,
            Permabuffs = new List<Permabuff>
            {
                new GrantWeaponLine { Profile = new WeaponProfile { Damage = "1d6", Light = true }, MainHand = false, DisplayName = "Short Sword" }
            }
        });

        var state = engine.Evaluate(character);

        // No TWF feat + light off-hand: -4/-8. Main: 6+3-4=5/0. Off: 6+3-8=1.
        Assert.Equal(new[] { 5, 0 }, state.AttackLines[0].Bonuses);
        Assert.Equal(new[] { 1 }, state.AttackLines[1].Bonuses);
    }

    [Fact]
    public void Encumbrance_Str14_70lbsIsMedium()
    {
        var registry = BuildRegistry();
        registry.RegisterEquipment(new EquipmentDefinition
        {
            Id = "stuff",
            Name = "Stuff",
            Category = EquipmentCategory.Gear,
            WeightLbs = 70
        });
        var engine = new ReplayStudio(registry);
        var character = BuildFighter(1, str: 14);
        character.Equipment.Add(new EquipmentEntry { ContentId = "stuff" });

        var state = engine.Evaluate(character);

        // STR 14: light 58, medium 116, heavy 175
        Assert.Equal(58, state.Encumbrance.LightMax);
        Assert.Equal(116, state.Encumbrance.MediumMax);
        Assert.Equal(175, state.Encumbrance.HeavyMax);
        Assert.Equal(70d, state.Encumbrance.TotalWeightLbs);
        Assert.Equal(LoadCategory.Medium, state.Encumbrance.Load);
    }

    [Fact]
    public void EquipmentQuantityAndWeightOverride_DriveEncumbrance()
    {
        var registry = BuildRegistry();
        registry.RegisterEquipment(new EquipmentDefinition
        {
            Id = "gear:arrow",
            Name = "Arrow",
            Category = EquipmentCategory.Ammunition,
            WeightLbs = 1,
        });
        var character = BuildFighter(1);
        character.Equipment.Add(new EquipmentEntry
        {
            ContentId = "gear:arrow",
            Quantity = 5,
            WeightLbsOverride = 1.25,
        });

        var state = new ReplayStudio(registry).Evaluate(character);

        Assert.Equal(6.25, state.Encumbrance.TotalWeightLbs);
    }

    [Fact]
    public void CarriedEquipment_AddsWeightButDoesNotGrantEquippedEffects()
    {
        var registry = BuildRegistry();
        registry.RegisterEquipment(new EquipmentDefinition
        {
            Id = "wondrous:carried_strength",
            Name = "Carried Strength Item",
            Category = EquipmentCategory.Wondrous,
            WeightLbs = 5,
            GrantedPermabuffs =
            {
                new GrantTypedBonus
                {
                    Target = BonusTarget.AbilityStr,
                    BonusType = BonusType.Enhancement,
                    Value = new Formula("4"),
                },
            },
        });
        var character = BuildFighter(1, str: 10);
        character.Equipment.Add(new EquipmentEntry { ContentId = "wondrous:carried_strength", Slot = "carried" });

        var state = new ReplayStudio(registry).Evaluate(character);

        Assert.Equal(10, state.AbilityScores.STR);
        Assert.Equal(5d, state.Encumbrance.TotalWeightLbs);
    }

    [Fact]
    public void DoubleWeapon_ProducesMainAndOffHandAttackLines()
    {
        var registry = BuildRegistry();
        registry.RegisterEquipment(new EquipmentDefinition
        {
            Id = "weapon:double_test",
            Name = "Double Test Weapon",
            Category = EquipmentCategory.Weapon,
            Weapon = new WeaponProfile
            {
                Damage = "1d6",
                DoubleWeapon = true,
            },
        });
        var character = BuildFighter(1);
        character.Equipment.Add(new EquipmentEntry { ContentId = "weapon:double_test", DoubleWeapon = true });

        var state = new ReplayStudio(registry).Evaluate(character);

        Assert.Equal(2, state.AttackLines.Count);
        Assert.False(state.AttackLines[0].IsOffHand);
        Assert.True(state.AttackLines[1].IsOffHand);
    }

    [Fact]
    public void EquipmentAffectsOnlyPostTickAc()
    {
        // Asserting the post-tick design invariant: equipment changes AC vs. no-equipment
        // baseline, but the per-tick math (HP, BAB, base saves) is unchanged.
        var registry = BuildRegistry();
        registry.RegisterEquipment(new EquipmentDefinition
        {
            Id = "armor:full_plate",
            Name = "Full Plate",
            Category = EquipmentCategory.Armor,
            Armor = new ArmorProfile { Kind = ArmorKind.Heavy, ArmorBonus = 8, MaxDex = 1 }
        });
        var engine = new ReplayStudio(registry);

        var noEq = BuildFighter(5, dex: 14);
        var stateNoEq = engine.Evaluate(noEq);

        var withEq = BuildFighter(5, dex: 14);
        withEq.Equipment.Add(new EquipmentEntry { ContentId = "armor:full_plate" });
        var stateEq = engine.Evaluate(withEq);

        // AC differs.
        Assert.Equal(12, stateNoEq.AC.Total);
        Assert.Equal(19, stateEq.AC.Total);
        // Per-tick math identical.
        Assert.Equal(stateNoEq.BaseBAB, stateEq.BaseBAB);
        Assert.Equal(stateNoEq.BaseSaves.Fort, stateEq.BaseSaves.Fort);
        Assert.Equal(stateNoEq.HP, stateEq.HP);
    }
}
