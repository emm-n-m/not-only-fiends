using System.Text.Json;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

public class IntelligentItemTests
{
    [Fact]
    public void Ego_UsesSrdComponents()
    {
        var item = new IntelligentItemDefinition
        {
            MentalAbilities = new IntelligentItemMentalAbilities
            {
                Intelligence = 18,
                Wisdom = 14,
                Charisma = 12
            },
            Communication = IntelligentItemCommunication.SpeechAndTelepathy,
            Senses = new IntelligentItemSenses { ReadsAllLanguages = true, ReadsMagic = true },
            SpecialPurpose = "Defeat arcane spellcasters",
            Powers =
            {
                new IntelligentItemPower { Kind = IntelligentItemPowerKind.Lesser, Name = "Detect magic" },
                new IntelligentItemPower { Kind = IntelligentItemPowerKind.Greater, Name = "Haste" }
            }
        };

        // 2 enhancement + 1 lesser + 2 greater + 4 purpose + telepathy + read languages
        // + read magic + INT/WIS/CHA bonuses (4+2+1) = 19.
        Assert.Equal(19, item.CalculateEgo(enhancementBonus: 2));
    }

    [Fact]
    public void AlignmentPenalty_ScalesAtEgoTwentyAndThirty()
    {
        var item = new IntelligentItemDefinition
        {
            Alignment = Alignment.LG,
            EgoOverride = 30
        };

        Assert.Equal(0, item.AlignmentNegativeLevels(Alignment.NG));
        Assert.Equal(3, item.AlignmentNegativeLevels(Alignment.CE));
        item.EgoOverride = 20;
        Assert.Equal(2, item.AlignmentNegativeLevels(Alignment.CE));
        item.EgoOverride = 1;
        Assert.Equal(1, item.AlignmentNegativeLevels(Alignment.CE));
    }

    [Fact]
    public void EquipmentDefinition_SerializesIntelligentItemData()
    {
        var equipment = new EquipmentDefinition
        {
            Id = "weapon:test_intelligent_sword",
            Name = "Test Intelligent Sword",
            Category = EquipmentCategory.Weapon,
            IntelligentItem = new IntelligentItemDefinition
            {
                Alignment = Alignment.CE,
                Communication = IntelligentItemCommunication.Telepathy,
                SpecialPurpose = "Defeat all",
                DedicatedPower = new IntelligentItemPower
                {
                    Kind = IntelligentItemPowerKind.Dedicated,
                    Name = "True resurrection",
                    Activation = "once per month",
                    BasePriceModifierGp = 200_000,
                    Description = "The item can use true resurrection on its wielder."
                }
            }
        };

        var json = JsonSerializer.Serialize(equipment, JsonOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<EquipmentDefinition>(json, JsonOptions.Default);

        Assert.NotNull(roundTrip?.IntelligentItem);
        Assert.Equal(Alignment.CE, roundTrip!.IntelligentItem!.Alignment);
        Assert.Equal("Defeat all", roundTrip.IntelligentItem.SpecialPurpose);
        Assert.Equal("True resurrection", roundTrip.IntelligentItem.DedicatedPower!.Name);
    }
}
