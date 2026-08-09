using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.PcGen;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests.PcGen;

public class PcgEquipmentImportTests
{
    private static ContentRegistry CreateRegistryWithEquipment()
    {
        var r = new ContentRegistry();
        r.RegisterEquipment(new EquipmentDefinition
        {
            Id = "armor:full_plate",
            Name = "Full Plate",
            Category = EquipmentCategory.Armor,
            Slot = "body",
        });
        r.RegisterEquipment(new EquipmentDefinition
        {
            Id = "wondrous:belt_giant_strength_6",
            Name = "Belt of Giant Strength +6",
            Category = EquipmentCategory.Wondrous,
            Slot = "waist",
        });
        r.RegisterEquipment(new EquipmentDefinition
        {
            Id = "weapon:longsword",
            Name = "Longsword",
            Category = EquipmentCategory.Weapon,
        });
        r.RegisterEquipment(new EquipmentDefinition
        {
            Id = "wondrous:cloak_of_resistance_3",
            Name = "Cloak of Resistance +3",
            Category = EquipmentCategory.Wondrous,
            Slot = "shoulders",
        });
        return r;
    }

    // --- Parser ---

    [Fact]
    public void Parser_NaturalAttackPseudoEquipment_IsSkipped()
    {
        var pcg = """
            EQUIPNAME:Longsword|COST:1500|WT:4|QUANTITY:1
            EQUIPNAME:Bite (Natural/Primary)|COST:0|WT:0|QUANTITY:1
            EQUIPNAME:Claw (Natural/Secondary)|COST:0|WT:0|QUANTITY:1
            EQUIPNAME:Tail Slap (Natural/Secondary)|COST:0|WT:0|QUANTITY:1
            """;
        var data = PcgParser.ParseText(pcg);

        var item = Assert.Single(data.Equipment);
        Assert.Equal("Longsword", item.Name);
    }

    [Fact]
    public void Parser_EquipName_CapturesNameQuantityWeightCost()
    {
        var pcg = "EQUIPNAME:Belt of Giant Strength +6|OUTPUTORDER:7|COST:36000|WT:1.0|QUANTITY:1.0|NOTE:";
        var data = PcgParser.ParseText(pcg);

        var item = Assert.Single(data.Equipment);
        Assert.Equal("Belt of Giant Strength +6", item.Name);
        Assert.Equal(1.0, item.Quantity);
        Assert.Equal(1.0, item.WeightLbs);
        Assert.Equal(3_600_000L, item.PriceCp); // 36000 gp × 100 cp/gp
        Assert.Null(item.SlotName);
        Assert.False(item.InActiveSet);
    }

    [Fact]
    public void Parser_CustomEquipment_PreservesCustomizationAndBaseItem()
    {
        const string pcg = "EQUIPNAME:Belly Chain|CUSTOMIZATION:[BASEITEM:Belt|DATA:NAME=Belly Chain$EQMOD=EPIC_ABILITY_BONUS_ENHANCE&pipe;CHA=+12.BNS_SKL_CMP&pipe;Bluff=+5$SPROP=custom]";

        var item = Assert.Single(PcgParser.ParseText(pcg).Equipment);

        Assert.Equal("Belt", item.BaseItemName);
        Assert.Contains("EPIC_ABILITY_BONUS_ENHANCE", item.Customization);
    }

    [Fact]
    public void Parser_EquipSet_AssignsSlotAndMarksActiveSet()
    {
        var pcg = """
            EQUIPNAME:Belt of Giant Strength +6|COST:36000|WT:1.0|QUANTITY:1.0
            EQUIPSET:Default Set|ID:0.1|USETEMPMODS:Y
            EQUIPSET:Waist|ID:0.1.04|VALUE:Belt of Giant Strength +6|QUANTITY:1.0|USETEMPMODS:Y
            CALCEQUIPSET:0.1
            """;
        var data = PcgParser.ParseText(pcg);

        var item = Assert.Single(data.Equipment);
        Assert.Equal("Waist", item.SlotName);
        Assert.True(item.InActiveSet);
    }

    [Fact]
    public void Parser_CalcEquipSet_AfterEquipSets_ResolvesActiveCorrectly()
    {
        // Real .pcg files emit CALCEQUIPSET *after* all EQUIPSET lines. The parser must
        // defer the active-set determination until end-of-file.
        var pcg = """
            EQUIPNAME:Item A|COST:0|WT:0|QUANTITY:1
            EQUIPNAME:Item B|COST:0|WT:0|QUANTITY:1
            EQUIPSET:Head|ID:0.1.01|VALUE:Item A|QUANTITY:1
            EQUIPSET:Head|ID:0.2.01|VALUE:Item B|QUANTITY:1
            CALCEQUIPSET:0.2
            """;
        var data = PcgParser.ParseText(pcg);

        var a = data.Equipment.Single(e => e.Name == "Item A");
        var b = data.Equipment.Single(e => e.Name == "Item B");
        Assert.False(a.InActiveSet);
        Assert.True(b.InActiveSet);
        Assert.Equal("Head", a.SlotName);
        Assert.Equal("Head", b.SlotName);
    }

    // --- Mapper ---

    [Fact]
    public void Mapper_MapEquipment_FindsByExactName()
    {
        var mapper = new PcgIdMapper();
        var registry = CreateRegistryWithEquipment();

        Assert.Equal("wondrous:belt_giant_strength_6",
            mapper.MapEquipment("Belt of Giant Strength +6", registry));
    }

    [Fact]
    public void Mapper_MapEquipment_StripsPlusSuffix()
    {
        // Catalog has "Cloak of Resistance +3"; PCGen could emit "Cloak of Resistance +3" or
        // sometimes a +N variant not in the catalog. Verify the stripped fallback finds the base.
        var mapper = new PcgIdMapper();
        var registry = CreateRegistryWithEquipment();

        Assert.Equal("wondrous:cloak_of_resistance_3",
            mapper.MapEquipment("Cloak of Resistance +3", registry));
    }

    [Fact]
    public void Mapper_MapEquipment_UnknownItem_ReturnsNull()
    {
        var mapper = new PcgIdMapper();
        var registry = CreateRegistryWithEquipment();

        Assert.Null(mapper.MapEquipment("Mystery Trinket of Whimsy", registry));
    }

    [Fact]
    public void Mapper_ArmsSlot_NormalizesToWrists()
    {
        Assert.Equal("wrists", new PcgIdMapper().MapSlot("Arms"));
    }

    // --- Registry ---

    [Fact]
    public void Registry_TryGetEquipmentByName_IsCaseInsensitive()
    {
        var registry = CreateRegistryWithEquipment();

        Assert.True(registry.TryGetEquipmentByName("FULL PLATE", out var def));
        Assert.Equal("armor:full_plate", def!.Id);
    }

    [RequiresPrivatePacksFact]
    public void PrivateCatalog_HarpBow_MatchesMalhavocLstAndReplaysAsWeapon()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        var harpBow = registry.GetEquipment("weapon:harp_bow");

        Assert.Equal("Harp Bow", harpBow.Name);
        Assert.Equal(EquipmentCategory.Weapon, harpBow.Category);
        Assert.Equal("weapon", harpBow.Slot);
        Assert.Equal(333_000, harpBow.PriceCp);
        Assert.Equal(5, harpBow.WeightLbs);
        Assert.NotNull(harpBow.Weapon);
        Assert.Equal("1d6", harpBow.Weapon.Damage);
        Assert.Equal(20, harpBow.Weapon.CritRangeLow);
        Assert.Equal(3, harpBow.Weapon.CritMultiplier);
        Assert.Equal(60, harpBow.Weapon.RangeFt);
        Assert.Equal("piercing", harpBow.Weapon.DamageType);
        Assert.True(harpBow.Weapon.Ranged);
        Assert.True(harpBow.Weapon.TwoHanded);
        Assert.Equal("martial", harpBow.Weapon.Proficiency);
        Assert.Contains("+2 enhancement bonus on attack rolls only", harpBow.Description);

        var mapper = new PcgIdMapper();
        Assert.Equal(harpBow.Id, mapper.MapEquipment("Harp Bow (Small)", registry));
        Assert.Equal(harpBow.Id, mapper.MapEquipment("Harp Bow (Medium)", registry));

        var character = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10,
                DEX = 10,
                CON = 10,
                INT = 10,
                WIS = 10,
                CHA = 10,
            },
            Ticks = { new Tick { DriverId = "class:fighter" } },
            Equipment =
            {
                new EquipmentEntry
                {
                    ItemId = "Harp Bow",
                    ContentId = harpBow.Id,
                    MainHand = true,
                    TwoHanded = true,
                }
            }
        };

        var state = new ReplayStudio(registry).Evaluate(character);
        var attack = Assert.Single(state.AttackLines);
        Assert.Equal("Harp Bow", attack.Name);
        Assert.Equal("1d6", attack.Damage);
        Assert.Equal("x3", attack.Crit);
        Assert.True(attack.IsRanged);
    }

    // --- Converter ---

    private static PcgCharacterData MinimalCharacter(params PcgEquipmentRaw[] equipment)
    {
        return new PcgCharacterData
        {
            CharacterName = "Test",
            Race = "Human",
            Alignment = "N",
            Classes = { new PcgClassEntry { Name = "Fighter", Level = 1 } },
            Levels = { new PcgLevelEntry { ClassName = "Fighter", ClassLevel = 1 } },
            Equipment = equipment.ToList(),
        };
    }

    [Fact]
    public void Converter_KnownEquipment_EmitsEntryWithContentIdAndSlot()
    {
        var data = MinimalCharacter(new PcgEquipmentRaw
        {
            Name = "Belt of Giant Strength +6",
            SlotName = "Waist",
            InActiveSet = true,
        });
        var registry = CreateRegistryWithEquipment();
        var mapper = new PcgIdMapper();

        var result = PcgConverter.Convert(data, mapper, registry);

        var entry = Assert.Single(result.Character.Equipment);
        Assert.Equal("wondrous:belt_giant_strength_6", entry.ContentId);
        Assert.Equal("Belt of Giant Strength +6", entry.ItemId);
        Assert.Equal("waist", entry.Slot);
        Assert.Empty(result.DroppedEquipment);
    }

    [Fact]
    public void Converter_UnmappedEquipment_WarnsAndSkips()
    {
        var data = MinimalCharacter(new PcgEquipmentRaw
        {
            Name = "Bag of Holding (Nonexistent Type)",
            SlotName = "Body",
            InActiveSet = true,
        });
        var registry = CreateRegistryWithEquipment();
        var mapper = new PcgIdMapper();

        var result = PcgConverter.Convert(data, mapper, registry);

        Assert.Empty(result.Character.Equipment);
        Assert.Contains("Bag of Holding (Nonexistent Type)", result.DroppedEquipment);
        Assert.Contains(result.Warnings, w => w.Contains("Bag of Holding"));
    }

    [Fact]
    public void Converter_TwoHandSlot_SetsTwoHandedFlag()
    {
        var data = MinimalCharacter(new PcgEquipmentRaw
        {
            Name = "Longsword",
            SlotName = "Two Hand",
            InActiveSet = true,
        });
        var registry = CreateRegistryWithEquipment();
        var mapper = new PcgIdMapper();

        var result = PcgConverter.Convert(data, mapper, registry);

        var entry = Assert.Single(result.Character.Equipment);
        Assert.Equal("weapon:longsword", entry.ContentId);
        Assert.True(entry.MainHand);
        Assert.True(entry.TwoHanded);
        Assert.Empty(entry.Slot); // weapons leave the body-slot field empty
    }

    [Fact]
    public void Converter_SecondaryWeaponSlot_SetsMainHandFalse()
    {
        var data = MinimalCharacter(new PcgEquipmentRaw
        {
            Name = "Longsword",
            SlotName = "Secondary Weapon",
            InActiveSet = true,
        });
        var registry = CreateRegistryWithEquipment();
        var mapper = new PcgIdMapper();

        var result = PcgConverter.Convert(data, mapper, registry);

        var entry = Assert.Single(result.Character.Equipment);
        Assert.False(entry.MainHand);
        Assert.False(entry.TwoHanded);
    }

    [Theory]
    [InlineData("Primary Hand", true)]
    [InlineData("Secondary Hand", false)]
    public void Converter_ActualPcgenHandLabels_SetTheCorrectHand(string slot, bool expectedMainHand)
    {
        var data = MinimalCharacter(new PcgEquipmentRaw
        {
            Name = "Longsword",
            SlotName = slot,
            InActiveSet = true,
        });

        var result = PcgConverter.Convert(data, new PcgIdMapper(), CreateRegistryWithEquipment());

        Assert.Equal(expectedMainHand, Assert.Single(result.Character.Equipment).MainHand);
    }

    [Fact]
    public void Converter_QuantityAndCharacterSpecificWeightAndPrice_ArePreserved()
    {
        var data = MinimalCharacter(new PcgEquipmentRaw
        {
            Name = "Longsword",
            Quantity = 5,
            WeightLbs = 1.25,
            PriceCp = 166_500,
        });

        var entry = Assert.Single(PcgConverter.Convert(data, new PcgIdMapper(), CreateRegistryWithEquipment())
            .Character.Equipment);

        Assert.Equal(5, entry.Quantity);
        Assert.Equal(1.25, entry.WeightLbsOverride);
        Assert.Equal(166_500, entry.PriceCpOverride);
    }

    [Fact]
    public void Converter_DoubleWeaponLabel_IsExplicitlyPreserved()
    {
        var data = MinimalCharacter(new PcgEquipmentRaw
        {
            Name = "Longsword",
            SlotName = "Double Weapon",
            InActiveSet = true,
        });

        var entry = Assert.Single(PcgConverter.Convert(data, new PcgIdMapper(), CreateRegistryWithEquipment())
            .Character.Equipment);

        Assert.True(entry.DoubleWeapon);
        Assert.False(entry.TwoHanded);
    }

    [Fact]
    public void Converter_InactiveSetItem_GetsCarriedSlot()
    {
        var data = MinimalCharacter(new PcgEquipmentRaw
        {
            Name = "Belt of Giant Strength +6",
            SlotName = "Waist",
            InActiveSet = false,
        });
        var registry = CreateRegistryWithEquipment();
        var mapper = new PcgIdMapper();

        var result = PcgConverter.Convert(data, mapper, registry);

        var entry = Assert.Single(result.Character.Equipment);
        Assert.Equal("carried", entry.Slot);
    }

    [Fact]
    public void Converter_CustomLillyItems_ApplyTypedBonusesWithoutStackingCompetence()
    {
        var registry = TestContentHelper.LoadBundledPacks();
        registry.RegisterEquipment(new EquipmentDefinition
        {
            Id = "test:belly_chain", Name = "Belly Chain", Category = EquipmentCategory.Wondrous,
        });
        registry.RegisterEquipment(new EquipmentDefinition
        {
            Id = "test:emeralds_best_creation", Name = "Emerald's Best Creation", Category = EquipmentCategory.Wondrous,
        });
        var data = MinimalCharacter(
            new PcgEquipmentRaw
            {
                Name = "Belly Chain", BaseItemName = "Belt", SlotName = "Waist", InActiveSet = true,
                Customization = "BASEITEM:Belt$EQMOD=EPIC_ABILITY_BONUS_ENHANCE&pipe;CHA=+12.BNS_SKL_CMP&pipe;Bluff=+5$SPROP=custom",
            },
            new PcgEquipmentRaw
            {
                Name = "Emerald's Best Creation", BaseItemName = "Robe of Useful Items", SlotName = "Body", InActiveSet = true,
                Customization = "BASEITEM:Robe of Useful Items$EQMOD=EPIC_NATURAL_ARMR_ENHANCE&pipe;+10.BNS_SKL_CMP&pipe;Perform ~ Dance=+20&pipe;Bluff=+5&pipe;Disguise=+5&pipe;Gather Information=+5&pipe;Diplomacy=+5$SPROP=custom",
            });
        foreach (var stat in new[] { "STR", "DEX", "CON", "INT", "WIS", "CHA" })
            data.BaseStats[stat] = 10;

        var result = PcgConverter.Convert(data, new PcgIdMapper(), registry);
        var state = new ReplayStudio(registry).Evaluate(result.Character);

        Assert.Equal(22, state.AbilityScores.CHA);
        Assert.Equal(10, state.AC.Components[BonusType.NaturalEnhancement]);
        Assert.Equal(5, state.SkillBonuses["skill:bluff"]); // two +5 competence bonuses do not stack
        Assert.Equal(20, state.SkillBonuses["skill:perform_dance"]);
        Assert.Equal(5, state.SkillBonuses["skill:disguise"]);
        Assert.Equal(5, state.SkillBonuses["skill:gather_information"]);
        Assert.Equal(5, state.SkillBonuses["skill:diplomacy"]);
        Assert.Empty(result.UnsupportedCustomEquipmentModifiers);
    }

    [Fact]
    public void Converter_CustomWeapon_UsesMechanicalBaseAndPreservesEnhancementOverride()
    {
        var registry = TestContentHelper.LoadBundledPacks();
        registry.RegisterEquipment(new EquipmentDefinition
        {
            Id = "test:infernal_sting", Name = "Infernal Sting", Category = EquipmentCategory.Weapon,
        });
        var data = MinimalCharacter(new PcgEquipmentRaw
        {
            Name = "Infernal Sting", BaseItemName = "Whip", SlotName = "Primary Hand", InActiveSet = true,
            Customization = "BASEITEM:Whip$EQMOD=PLUS_10_WEAP.DREAD_MELEE$SPROP=custom",
        });

        var result = PcgConverter.Convert(data, new PcgIdMapper(), registry);
        var entry = Assert.Single(result.Character.Equipment);

        Assert.Equal("weapon:whip", entry.ContentId);
        Assert.Equal(10, entry.EnhancementBonusOverride);
        Assert.Contains(result.UnsupportedCustomEquipmentModifiers, warning => warning.EndsWith(": DREAD_MELEE"));
    }

    [RequiresPcgFixturesFact]
    public void ArchfiendLilly_CustomItemsLanguagesShadowAndFollowers_ArePreserved()
    {
        var path = TestContentHelper.PcgFixture("Archfiend Lilly.pcg");
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        var data = PcgParser.ParseText(File.ReadAllText(path), Path.GetFileName(path));

        var result = PcgConverter.Convert(data, new PcgIdMapper(), registry);
        var state = new ReplayStudio(registry).Evaluate(result.Character);

        Assert.Equal(6, result.Character.SourceLanguageIds.Count);
        Assert.Contains("template:archfiend_ascended", result.Character.TemplateIds);
        Assert.Contains("domain:charm", state.Domains);
        Assert.Equal("", state.DomainOwners["domain:charm"]);
        Assert.Equal(9, state.SLAs.Count(sla => sla.Id.StartsWith("domain_sla_", StringComparison.Ordinal)));
        Assert.Equal(43, state.AbilityScores.CHA); // 31 before equipment, +12 from Belly Chain
        Assert.Equal(10, state.AC.Components[BonusType.NaturalEnhancement]);
        Assert.Equal(45, state.SkillTotals["skill:bluff"]);
        Assert.Equal(60, state.SkillTotals["skill:perform_dance"]);
        Assert.Equal(90, state.Speeds[MovementMode.Fly]);
        Assert.Equal(FlightManeuverability.Good, state.FlyManeuverability);

        var shadowSlot = state.CompanionSlots.Single(slot => slot.LinkType == "shadow_companion");
        Assert.Equal("race:companion_shadow", shadowSlot.SelectedSpecies);
        Assert.Equal(7, shadowSlot.EffectiveLevel);
        Assert.Equal(6, result.Character.CompanionLinks.Count(link => link.LinkType == "leadership_follower"));
        Assert.True(state.Followers.Level1 > 0);
    }
}
