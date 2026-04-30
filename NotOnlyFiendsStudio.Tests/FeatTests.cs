using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public class FeatTests
{
    private (ContentRegistry registry, ReplayStudio engine) CreateStudio()
    {
        var registry = TestContentHelper.LoadAllPacks();
        return (registry, new ReplayStudio(registry));
    }

    [Fact]
    public void FeatsLoadFromJson()
    {
        var (registry, _) = CreateStudio();

        // General feats
        Assert.NotNull(registry.GetFeat("power_attack"));
        Assert.NotNull(registry.GetFeat("cleave"));
        Assert.NotNull(registry.GetFeat("great_cleave"));
        Assert.NotNull(registry.GetFeat("improved_initiative"));

        // Fighter bonus feats
        Assert.NotNull(registry.GetFeat("weapon_specialization"));
    }

    [Fact]
    public void PrerequisiteChain_PowerAttack_Cleave_GreatCleave()
    {
        var (registry, engine) = CreateStudio();

        // Fighter with STR 16: can take Power Attack at level 1
        var character = new Character
        {
            Name = "Feat Chain Test",
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter", Choices = new TickChoices
                    { FeatIds = new List<string> { "power_attack", "cleave", "improved_initiative" } } },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter", Choices = new TickChoices
                    { FeatIds = new List<string> { "great_cleave" } } },
            }
        };

        var state = engine.Evaluate(character);

        Assert.Contains("power_attack", state.Feats);
        Assert.Contains("cleave", state.Feats);
        Assert.Contains("improved_initiative", state.Feats);
        Assert.Contains("great_cleave", state.Feats);
        Assert.Empty(state.Warnings);
    }

    [Fact]
    public void PrerequisiteViolation_CleaveWithoutPowerAttack()
    {
        var (registry, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Bad Feat Test",
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter", Choices = new TickChoices
                    { FeatIds = new List<string> { "cleave" } } },
            }
        };

        var state = engine.Evaluate(character);

        // Should have a warning about missing power_attack prereq
        Assert.NotEmpty(state.Warnings);
        Assert.Contains(state.Warnings, w => w.Contains("power_attack") || w.Contains("Power Attack"));
    }

    [Fact]
    public void GreatCleave_RequiresBAB4()
    {
        var (registry, engine) = CreateStudio();

        // Try to take Great Cleave at HD 1 (BAB +1 only)
        var character = new Character
        {
            Name = "BAB Test",
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter", Choices = new TickChoices
                    { FeatIds = new List<string> { "power_attack", "cleave", "great_cleave" } } },
            }
        };

        var state = engine.Evaluate(character);

        // Should have warning about BAB requirement
        Assert.Contains(state.Warnings, w => w.Contains("BAB"));
    }

    [Fact]
    public void FeatSlotEnforcement_DropsExtraFeatWhenNoSlotAvailable()
    {
        // Wizard HD 1: 1 standard slot + 1 human bonus = 2 slots.
        // Picking 3 feats at HD 1 must drop the third and leave 2 in Feats.
        var (registry, engine) = CreateStudio();
        var character = new Character
        {
            Name = "Slot Enforcement Test",
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 14, CON = 14, INT = 16, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:wizard", Choices = new TickChoices
                    { FeatIds = new List<string> { "improved_initiative", "lightning_reflexes", "iron_will" } } },
            }
        };

        var state = engine.Evaluate(character);

        // state.Feats also includes scribe_scroll granted by class. The two picks land; the third is dropped.
        Assert.Contains("improved_initiative", state.Feats);
        Assert.Contains("lightning_reflexes", state.Feats);
        Assert.DoesNotContain("iron_will", state.Feats);
        Assert.Contains(state.Warnings, w => w.Contains("iron_will") && w.Contains("no available feat slot"));
    }

    [Fact]
    public void FeatSlotEnforcement_FighterBonusSlotConsumedByMatchingFeat()
    {
        // Fighter HD 1 picks power_attack (General) + a bonus fighter-restricted feat.
        // 1 standard + 1 human bonus (unrestricted) + 1 fighter_bonus = 3 slots available.
        // After two picks, exactly one bonus slot should remain.
        var (registry, engine) = CreateStudio();
        var character = new Character
        {
            Name = "Fighter Slot Test",
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter", Choices = new TickChoices
                    { FeatIds = new List<string> { "power_attack", "weapon_focus" } } },
            }
        };

        var state = engine.Evaluate(character);
        Assert.Empty(state.Warnings);
        Assert.Contains("power_attack", state.Feats);
        Assert.Contains("weapon_focus", state.Feats);
        // 3 slots granted at HD 1, 2 picked → 1 remaining.
        Assert.Single(state.FeatSlots);
    }

    [Fact]
    public void Fighter6_CorrectFeatSlots()
    {
        var (registry, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Slot Count Test",
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = Enumerable.Range(0, 6).Select(_ => new Tick { DriverId = "class:fighter" }).ToList()
        };

        var state = engine.Evaluate(character);

        // Standard feat slots: HD 1, 3, 6 = 3
        // Human bonus feat: 1
        // Fighter bonus feats: levels 1, 2, 4, 6 = 4
        // Total standard pending: 4 (3 standard + 1 human)
        // Total bonus pending: 4
        Assert.Equal(4, state.PendingFeatSlots);
        Assert.Equal(4, state.PendingBonusFeatSlots);
    }

    [Fact]
    public void GetAvailableFeats_FiltersCorrectly()
    {
        var (registry, engine) = CreateStudio();

        var state = new CharacterState
        {
            TotalHD = 1,
            BaseBAB = 1,
            AbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            }
        };

        var available = engine.GetAvailableFeats(state);

        // Should include Power Attack (STR 13+) and Improved Initiative (no prereqs)
        Assert.Contains(available, f => f.Id == "power_attack");
        Assert.Contains(available, f => f.Id == "improved_initiative");
        Assert.Contains(available, f => f.Id == "weapon_focus"); // BAB 1+

        // Should NOT include Cleave (requires Power Attack feat)
        Assert.DoesNotContain(available, f => f.Id == "cleave");

        // Should NOT include Combat Expertise (requires INT 13+, we have INT 10)
        Assert.DoesNotContain(available, f => f.Id == "combat_expertise");
    }

    [Fact]
    public void GetAvailableFeats_AfterTakingPowerAttack_IncludesCleave()
    {
        var (registry, engine) = CreateStudio();

        var state = new CharacterState
        {
            TotalHD = 1,
            BaseBAB = 1,
            AbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Feats = new List<string> { "power_attack" }
        };

        var available = engine.GetAvailableFeats(state);

        Assert.Contains(available, f => f.Id == "cleave");
        Assert.Contains(available, f => f.Id == "improved_bull_rush");
        Assert.Contains(available, f => f.Id == "improved_sunder");

        // Great Cleave still needs BAB +4 and Cleave
        Assert.DoesNotContain(available, f => f.Id == "great_cleave");
    }

    [Fact]
    public void GetAvailableFeats_FighterBonusRestriction()
    {
        var (registry, engine) = CreateStudio();

        var state = new CharacterState
        {
            TotalHD = 4,
            BaseBAB = 4,
            AbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            ClassLevels = new Dictionary<string, int> { { "class:fighter", 4 } },
            Feats = new List<string> { "weapon_focus" }
        };

        var available = engine.GetAvailableFeats(state, restriction: "fighter_bonus");

        // Fighter bonus restriction should include general + fighter bonus feats
        Assert.Contains(available, f => f.Id == "power_attack");        // general
        Assert.Contains(available, f => f.Id == "weapon_specialization"); // fighter bonus, needs weapon_focus + fighter 4

        // Should not include already taken feats (unless repeatable)
        // weapon_focus is repeatable, so it should still appear
        Assert.Contains(available, f => f.Id == "weapon_focus");
    }

    [Fact]
    public void FeatCascade_GrantBonusFeat_AppliesPermabuffs()
    {
        var registry = new ContentRegistry();
        registry.RegisterRace(new RaceDefinition
        {
            Id = "human",
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
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Good, Ref = ProgressionRate.Poor, Will = ProgressionRate.Poor },
            LevelPermabuffs = new Dictionary<int, List<Permabuff>>
            {
                { 1, new List<Permabuff> { new GrantBonusFeat { FeatId = "toughness_bonus" } } }
            }
        });

        // Register a feat with a GrantedPermabuff and a tag
        registry.RegisterFeat(new FeatDefinition
        {
            Id = "toughness_bonus",
            Name = "Toughness (Bonus)",
            Type = FeatType.General,
            Tags = new List<string> { "defensive" },
            GrantedPermabuffs = new List<Permabuff>
            {
                new ModifyAttribute { Target = AttributeTarget.NaturalArmor, Value = 1 }
            }
        });

        var engine = new ReplayStudio(registry);
        var character = new Character
        {
            Name = "Cascade Test",
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick> { new() { DriverId = "class:fighter" } }
        };

        var state = engine.Evaluate(character);

        // GrantBonusFeat adds the feat and cascades its permabuffs
        Assert.Contains("toughness_bonus", state.Feats);
        Assert.Equal(1, state.NaturalArmor);

        // Granted bonus feats also count toward type/tag totals so downstream
        // HasFeatOfType / HasFeatWithTag prerequisites see them.
        Assert.Equal(1, state.FeatTypeCounts.GetValueOrDefault(FeatType.General));
        Assert.Equal(1, state.FeatTagCounts.GetValueOrDefault("defensive"));
    }
}
