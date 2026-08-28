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
        Assert.NotNull(registry.GetFeat("feat:power_attack"));
        Assert.NotNull(registry.GetFeat("feat:cleave"));
        Assert.NotNull(registry.GetFeat("feat:great_cleave"));
        Assert.NotNull(registry.GetFeat("feat:improved_initiative"));

        // Fighter bonus feats
        Assert.NotNull(registry.GetFeat("feat:weapon_specialization"));
    }

    [Fact]
    public void SpellMastery_UsesOneFeatSlotForIntelligenceModifierSelections()
    {
        var (_, engine) = CreateStudio();
        var character = new Character
        {
            Name = "Spell Mastery selections",
            RaceId = "race:dwarf",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 16, WIS = 10, CHA = 10
            },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:wizard",
                    Choices = new TickChoices
                    {
                        FeatIds = new List<string>
                        {
                            "feat:spell_mastery:magic_missile",
                            "feat:spell_mastery:shield",
                            "feat:spell_mastery:detect_magic",
                            "feat:spell_mastery:identify"
                        }
                    }
                }
            }
        };

        var state = engine.Evaluate(character);

        Assert.Equal(3, state.Feats.Count(feat => feat.StartsWith("feat:spell_mastery:", StringComparison.Ordinal)));
        Assert.DoesNotContain("feat:spell_mastery:identify", state.Feats);
        Assert.Equal(0, state.PendingFeatSlots);
        Assert.Contains(state.Warnings, warning => warning.Message.Contains("Intelligence modifier limit (3)"));
    }

    [Fact]
    public void GrantedFeats_FromRaceTemplateAndClass_AreNotPlayerSelections()
    {
        var (_, engine) = CreateStudio();
        var character = new Character
        {
            Name = "Granted Feats",
            RaceId = "race:pixie",
            TemplateIds = new List<string> { "template:vampire" },
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 16, CON = 10, INT = 14, WIS = 12, CHA = 14
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:wizard" }
            }
        };

        var state = engine.Evaluate(character);

        // Pixie's racial permabuffs, vampire's template permabuffs, and wizard's class feature
        // all grant feats directly. None should appear as a player choice on the tick.
        Assert.Contains("feat:weapon_finesse", state.Feats);
        Assert.Contains("feat:alertness", state.Feats);
        Assert.Contains("feat:scribe_scroll", state.Feats);
        Assert.Empty(character.Ticks[0].Choices.FeatIds ?? new List<string>());
    }

    [Fact]
    public void PrerequisiteChain_PowerAttack_Cleave_GreatCleave()
    {
        var (registry, engine) = CreateStudio();

        // Fighter with STR 16: can take Power Attack at level 1
        var character = new Character
        {
            Name = "Feat Chain Test",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter", Choices = new TickChoices
                    { FeatIds = new List<string> { "feat:power_attack", "feat:cleave", "feat:improved_initiative" } } },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter", Choices = new TickChoices
                    { FeatIds = new List<string> { "feat:great_cleave" } } },
            }
        };

        var state = engine.Evaluate(character);

        Assert.Contains("feat:power_attack", state.Feats);
        Assert.Contains("feat:cleave", state.Feats);
        Assert.Contains("feat:improved_initiative", state.Feats);
        Assert.Contains("feat:great_cleave", state.Feats);
        Assert.Empty(state.Warnings);
    }

    [Fact]
    public void PrerequisiteViolation_CleaveWithoutPowerAttack()
    {
        var (registry, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Bad Feat Test",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter", Choices = new TickChoices
                    { FeatIds = new List<string> { "feat:cleave" } } },
            }
        };

        var state = engine.Evaluate(character);

        // Should have a warning about missing power_attack prereq
        Assert.NotEmpty(state.Warnings);
        Assert.Contains(state.Warnings, w => w.Message.Contains("feat:power_attack") || w.Message.Contains("Power Attack"));
    }

    [Fact]
    public void GreatCleave_RequiresBAB4()
    {
        var (registry, engine) = CreateStudio();

        // Try to take Great Cleave at HD 1 (BAB +1 only)
        var character = new Character
        {
            Name = "BAB Test",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter", Choices = new TickChoices
                    { FeatIds = new List<string> { "feat:power_attack", "feat:cleave", "feat:great_cleave" } } },
            }
        };

        var state = engine.Evaluate(character);

        // Should have warning about BAB requirement
        Assert.Contains(state.Warnings, w => w.Message.Contains("BAB"));
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
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 14, CON = 14, INT = 16, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:wizard", Choices = new TickChoices
                    { FeatIds = new List<string> { "feat:improved_initiative", "feat:lightning_reflexes", "feat:iron_will" } } },
            }
        };

        var state = engine.Evaluate(character);

        // state.Feats also includes scribe_scroll granted by class. The two picks land; the third is dropped.
        Assert.Contains("feat:improved_initiative", state.Feats);
        Assert.Contains("feat:lightning_reflexes", state.Feats);
        Assert.DoesNotContain("feat:iron_will", state.Feats);
        Assert.Contains(state.Warnings, w => w.Message.Contains("feat:iron_will") && w.Message.Contains("no available feat slot"));
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
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter", Choices = new TickChoices
                    { FeatIds = new List<string> { "feat:power_attack", "feat:weapon_focus:longsword" } } },
            }
        };

        var state = engine.Evaluate(character);
        Assert.Empty(state.Warnings);
        Assert.Contains("feat:power_attack", state.Feats);
        Assert.Contains("feat:weapon_focus:longsword", state.Feats);
        // 3 slots granted at HD 1, 2 picked → 1 remaining.
        Assert.Single(state.FeatSlots);
    }

    [Fact]
    public void FeatSlotEnforcement_UsesRestrictedSlotBeforeUnrestrictedSlot()
    {
        var (_, engine) = CreateStudio();
        var character = new Character
        {
            Name = "Restricted Precedence",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter", Choices = new TickChoices
                {
                    FeatIds = new List<string> { "feat:improved_initiative", "feat:power_attack" }
                } }
            }
        };

        var state = engine.Evaluate(character);

        // The general feat must use an unrestricted slot; Power Attack is a fighter-bonus feat
        // and should consume the restricted slot. The remaining slot proves the precedence.
        Assert.Single(state.FeatSlots);
        Assert.Null(state.FeatSlots[0].Restriction);
        Assert.Empty(state.Warnings);
    }

    [Fact]
    public void InvalidFeatId_ProducesWarning()
    {
        var (_, engine) = CreateStudio();
        var character = new Character
        {
            Name = "Invalid Feat Test",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 14, CON = 14, INT = 16, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:wizard",
                    Choices = new TickChoices { FeatIds = new List<string> { "feat:not_in_catalog" } }
                }
            }
        };

        var state = engine.Evaluate(character);

        Assert.Contains(state.Warnings, w => w.Message.Contains("unknown feat 'feat:not_in_catalog'"));
    }

    [Fact]
    public void Fighter6_CorrectFeatSlots()
    {
        var (registry, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Slot Count Test",
            RaceId = "race:human",
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
        Assert.Contains(available, f => f.Id == "feat:power_attack");
        Assert.Contains(available, f => f.Id == "feat:improved_initiative");
        Assert.Contains(available, f => f.Id == "feat:weapon_focus"); // BAB 1+

        // Should NOT include Cleave (requires Power Attack feat)
        Assert.DoesNotContain(available, f => f.Id == "feat:cleave");

        // Should NOT include Combat Expertise (requires INT 13+, we have INT 10)
        Assert.DoesNotContain(available, f => f.Id == "feat:combat_expertise");
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
            Feats = new List<string> { "feat:power_attack" }
        };

        var available = engine.GetAvailableFeats(state);

        Assert.Contains(available, f => f.Id == "feat:cleave");
        Assert.Contains(available, f => f.Id == "feat:improved_bull_rush");
        Assert.Contains(available, f => f.Id == "feat:improved_sunder");

        // Great Cleave still needs BAB +4 and Cleave
        Assert.DoesNotContain(available, f => f.Id == "feat:great_cleave");
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
            Feats = new List<string> { "feat:weapon_focus" }
        };

        var available = engine.GetAvailableFeats(state, restriction: "fighter_bonus");

        // Fighter bonus restriction should include general + fighter bonus feats
        Assert.Contains(available, f => f.Id == "feat:power_attack");        // general
        Assert.Contains(available, f => f.Id == "feat:weapon_specialization"); // fighter bonus, needs weapon_focus + fighter 4

        // Should not include already taken feats (unless repeatable)
        // weapon_focus is repeatable, so it should still appear
        Assert.Contains(available, f => f.Id == "feat:weapon_focus");
    }

    [Fact]
    public void FeatCascade_GrantBonusFeat_AppliesPermabuffs()
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
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Good, Ref = ProgressionRate.Poor, Will = ProgressionRate.Poor },
            LevelPermabuffs = new Dictionary<int, List<Permabuff>>
            {
                { 1, new List<Permabuff> { new GrantBonusFeat { FeatId = "feat:toughness_bonus" } } }
            }
        });

        // Register a feat with a GrantedPermabuff and a tag
        registry.RegisterFeat(new FeatDefinition
        {
            Id = "feat:toughness_bonus",
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
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick> { new() { DriverId = "class:fighter" } }
        };

        var state = engine.Evaluate(character);

        // GrantBonusFeat adds the feat and cascades its permabuffs
        Assert.Contains("feat:toughness_bonus", state.Feats);
        Assert.Equal(1, state.NaturalArmor);

        // Granted bonus feats also count toward type/tag totals so downstream
        // HasFeatOfType / HasFeatWithTag prerequisites see them.
        Assert.Equal(1, state.FeatTypeCounts.GetValueOrDefault(FeatType.General));
        Assert.Equal(1, state.FeatTagCounts.GetValueOrDefault("defensive"));
    }

    private static Character FighterWithFeats(params string[] featIds) => new()
    {
        Name = "Selection Feats",
        RaceId = "race:human",
        BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
        Ticks = new List<Tick>
        {
            new()
            {
                DriverId = "class:fighter",
                Choices = new TickChoices { FeatIds = featIds.ToList() }
            }
        }
    };

    [Fact]
    public void SkillFocus_GrantsPlusThreeToTheSelectedSkill()
    {
        var (_, engine) = CreateStudio();
        var state = engine.Evaluate(FighterWithFeats("feat:skill_focus:concentration"));

        Assert.Contains("feat:skill_focus:concentration", state.Feats);
        Assert.Equal(3, state.SkillBonuses.GetValueOrDefault("skill:concentration"));
        Assert.Equal(3, state.SkillTotals.GetValueOrDefault("skill:concentration"));
        Assert.DoesNotContain(state.Warnings, w => w.Message.Contains("skill_focus"));
    }

    [Theory]
    [InlineData("feat:skill_focus_concentration")]        // old PCGen-import dialect
    [InlineData("feat:skill_focus_skill:concentration")]  // old builder-UI dialect
    public void SkillFocus_LegacyDialects_NormalizeToTheCanonicalId(string legacyId)
    {
        // Saved characters are never rewritten, so every legacy spelling must replay to the
        // same canonical state the new form produces.
        var (_, engine) = CreateStudio();
        var state = engine.Evaluate(FighterWithFeats(legacyId));

        Assert.Contains("feat:skill_focus:concentration", state.Feats);
        Assert.Equal(3, state.SkillBonuses.GetValueOrDefault("skill:concentration"));
        Assert.DoesNotContain(state.Warnings, w => w.Message.Contains("skill_focus"));
    }

    [Fact]
    public void SkillFocus_WithoutSelection_WarnsAndGrantsNoBonus()
    {
        var (_, engine) = CreateStudio();
        var state = engine.Evaluate(FighterWithFeats("feat:skill_focus"));

        // The feat is kept (legacy saves must replay), but it has no target and says so.
        Assert.Contains("feat:skill_focus", state.Feats);
        Assert.Contains(state.Warnings, w => w.Message.Contains("requires a skill selection"));
        Assert.Empty(state.SkillBonuses);
    }

    [Fact]
    public void SkillFocus_UnknownSkillSelection_Warns()
    {
        var (_, engine) = CreateStudio();
        var state = engine.Evaluate(FighterWithFeats("feat:skill_focus:xyzzy"));

        Assert.Contains(state.Warnings, w => w.Message.Contains("unknown skill 'skill:xyzzy'"));
    }

    [Fact]
    public void SkillFocus_SkillFamilySelection_IsValid()
    {
        // Prestige prerequisites use family selections (loremaster: feat:skill_focus:knowledge),
        // so a family name is legal even though no single skill carries that id.
        var (_, engine) = CreateStudio();
        var state = engine.Evaluate(FighterWithFeats("feat:skill_focus:knowledge"));

        Assert.DoesNotContain(state.Warnings, w => w.Message.Contains("unknown skill"));
    }

    [Fact]
    public void SpellFocus_SchoolSelections_ValidateAgainstTheEightSchools()
    {
        var (_, engine) = CreateStudio();

        var valid = engine.Evaluate(FighterWithFeats("feat:spell_focus:conjuration"));
        Assert.DoesNotContain(valid.Warnings, w => w.Message.Contains("unknown school"));

        var invalid = engine.Evaluate(FighterWithFeats("feat:spell_focus:hairdressing"));
        Assert.Contains(invalid.Warnings, w => w.Message.Contains("unknown school 'hairdressing'"));

        var bare = engine.Evaluate(FighterWithFeats("feat:spell_focus"));
        Assert.Contains(bare.Warnings, w => w.Message.Contains("requires a school selection"));
    }

    [Fact]
    public void SkillFocus_PrestigePrerequisites_MatchAnyDialect()
    {
        // Archmage requires feat:skill_focus:spellcraft; a legacy save spelling it the old
        // builder way must still qualify after normalization.
        var (_, engine) = CreateStudio();
        var state = engine.Evaluate(FighterWithFeats("feat:skill_focus_skill:spellcraft"));

        var prereq = new HasFeat { FeatId = "feat:skill_focus:spellcraft" };
        Assert.True(prereq.IsMet(state));
    }
}
