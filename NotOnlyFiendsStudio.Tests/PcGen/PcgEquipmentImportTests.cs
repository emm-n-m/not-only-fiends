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

    // --- Registry ---

    [Fact]
    public void Registry_TryGetEquipmentByName_IsCaseInsensitive()
    {
        var registry = CreateRegistryWithEquipment();

        Assert.True(registry.TryGetEquipmentByName("FULL PLATE", out var def));
        Assert.Equal("armor:full_plate", def!.Id);
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
            Name = "Robe of the Archmagi (Black)",
            SlotName = "Body",
            InActiveSet = true,
        });
        var registry = CreateRegistryWithEquipment();
        var mapper = new PcgIdMapper();

        var result = PcgConverter.Convert(data, mapper, registry);

        Assert.Empty(result.Character.Equipment);
        Assert.Contains("Robe of the Archmagi (Black)", result.DroppedEquipment);
        Assert.Contains(result.Warnings, w => w.Contains("Robe of the Archmagi"));
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
}
