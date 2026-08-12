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

        Assert.Equal(3, item.AlignmentNegativeLevels(Alignment.NG));
        Assert.Equal(0, item.AlignmentNegativeLevels(Alignment.LG));
        Assert.Equal(3, item.AlignmentNegativeLevels(Alignment.CE));
        item.EgoOverride = 20;
        Assert.Equal(2, item.AlignmentNegativeLevels(Alignment.CE));
        item.EgoOverride = 1;
        Assert.Equal(1, item.AlignmentNegativeLevels(Alignment.CE));
    }

    [Theory]
    [InlineData(Alignment.LN, Alignment.LG, true)]
    [InlineData(Alignment.LN, Alignment.LE, true)]
    [InlineData(Alignment.NG, Alignment.CG, true)]
    [InlineData(Alignment.LG, Alignment.NG, false)]
    [InlineData(Alignment.CE, Alignment.NE, false)]
    [InlineData(Alignment.N, Alignment.N, true)]
    [InlineData(Alignment.N, Alignment.LN, false)]
    public void AlignmentCorrespondence_UsesOnlyTheSrdNeutralAxisException(
        Alignment itemAlignment, Alignment wielderAlignment, bool expected)
    {
        var item = new IntelligentItemDefinition { Alignment = itemAlignment };

        Assert.Equal(expected, item.AlignmentCorresponds(wielderAlignment));
    }

    [Fact]
    public void Replay_ReportsIntelligentItemAndAppliesAlignmentNegativeLevel()
    {
        var registry = TestContentHelper.LoadBundledPacks();
        registry.RegisterEquipment(new EquipmentDefinition
        {
            Id = "weapon:test_intelligent_longsword",
            Name = "Judgment",
            Category = EquipmentCategory.Weapon,
            Slot = "weapon",
            EnhancementBonus = 1,
            Weapon = new WeaponProfile { Damage = "1d8", DamageType = "slashing" },
            IntelligentItem = new IntelligentItemDefinition
            {
                Alignment = Alignment.LG,
                EgoOverride = 10,
            },
        });
        var character = new Character
        {
            RaceId = "race:human",
            Alignment = Alignment.NG,
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10,
            },
            Ticks = { new Tick { DriverId = "class:fighter" } },
            Equipment = { new EquipmentEntry { ContentId = "weapon:test_intelligent_longsword", Slot = "weapon" } },
        };

        var state = new ReplayStudio(registry).Evaluate(character);

        var item = Assert.Single(state.IntelligentItems);
        Assert.Equal(10, item.Ego);
        Assert.False(item.AlignmentCorresponds);
        Assert.Equal(1, state.EquipmentNegativeLevels);
        Assert.Equal(5, state.HP); // fighter d10 at first HD, then the sourced -5 hp negative-level penalty
        Assert.Equal(-1, state.EffectiveSaves.Will);
        Assert.Equal(1, Assert.Single(state.AttackLines).Bonuses[0]); // BAB +1, +1 weapon, -1 negative level

        var carried = character.Clone();
        carried.Equipment[0].Slot = "carried";
        var carriedState = new ReplayStudio(registry).Evaluate(carried);
        Assert.Empty(carriedState.IntelligentItems);
        Assert.Equal(0, carriedState.EquipmentNegativeLevels);
        Assert.Equal(10, carriedState.HP);
    }

    [Fact]
    public void CharacterClone_DeepCopiesIntelligentItemOverride()
    {
        var character = new Character
        {
            Equipment =
            {
                new EquipmentEntry
                {
                    ItemId = "Whisper",
                    IntelligentItemOverride = new IntelligentItemDefinition
                    {
                        LanguageIds = { "draconic" },
                        Powers = { new IntelligentItemPower { Kind = IntelligentItemPowerKind.Lesser, Name = "Detect magic" } },
                    },
                },
            },
        };

        var clone = character.Clone();
        clone.Equipment[0].IntelligentItemOverride!.LanguageIds.Add("infernal");
        clone.Equipment[0].IntelligentItemOverride!.Powers[0].Name = "Changed";

        Assert.Equal(new[] { "draconic" }, character.Equipment[0].IntelligentItemOverride!.LanguageIds);
        Assert.Equal("Detect magic", character.Equipment[0].IntelligentItemOverride!.Powers[0].Name);
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
