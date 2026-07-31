using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public class ReplayStudioTests
{
    private ContentRegistry CreateContentRegistry()
    {
        var registry = new ContentRegistry();

        // Human race
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:human",
            Name = "Human",
            Type = CreatureType.Humanoid,
            Size = Size.Medium,
            Speeds = new Dictionary<MovementMode, int> { { MovementMode.Land, 30 } },
            BonusFeats = 1
        });

        // Fighter class
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:fighter",
            Name = "Fighter",
            HitDie = 10,
            SkillPointsPerLevel = 2,
            ClassSkills = new List<string> { "skill:climb", "craft", "skill:handle_animal", "skill:intimidate", "jump", "skill:ride", "skill:swim" },
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Good,
                Ref = ProgressionRate.Poor,
                Will = ProgressionRate.Poor
            },
            LevelPermabuffs = new Dictionary<int, List<Permabuff>>
            {
                { 1, new List<Permabuff> { new GrantFeatSlot { Restriction = "fighter_bonus" } } },
                { 2, new List<Permabuff> { new GrantFeatSlot { Restriction = "fighter_bonus" } } },
                { 4, new List<Permabuff> { new GrantFeatSlot { Restriction = "fighter_bonus" } } },
                { 6, new List<Permabuff> { new GrantFeatSlot { Restriction = "fighter_bonus" } } },
                { 8, new List<Permabuff> { new GrantFeatSlot { Restriction = "fighter_bonus" } } },
                { 10, new List<Permabuff> { new GrantFeatSlot { Restriction = "fighter_bonus" } } }
            }
        });

        // Rogue class
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:rogue",
            Name = "Rogue",
            HitDie = 6,
            SkillPointsPerLevel = 8,
            ClassSkills = new List<string>
            {
                "skill:appraise", "skill:balance", "skill:bluff", "skill:climb", "craft", "skill:decipher_script",
                "skill:diplomacy", "skill:disable_device", "skill:disguise", "skill:escape_artist", "skill:forgery",
                "skill:gather_information", "skill:hide", "skill:intimidate", "jump", "skill:knowledge_local",
                "skill:listen", "skill:move_silently", "skill:open_lock", "perform", "profession",
                "skill:search", "skill:sense_motive", "skill:sleight_of_hand", "skill:spot", "skill:swim",
                "skill:tumble", "skill:use_magic_device", "skill:use_rope"
            },
            BABProgression = BABProgression.Average,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Poor,
                Ref = ProgressionRate.Good,
                Will = ProgressionRate.Poor
            },
            LevelPermabuffs = new Dictionary<int, List<Permabuff>>
            {
                { 1, new List<Permabuff>
                    {
                        new GrantAbility { Ability = new GrantedAbility { Id = "sneak_attack", Name = "Sneak Attack" } },
                        new ModifyCounter { CounterId = "sneak_attack_dice", Value = 1 },
                        new GrantAbility { Ability = new GrantedAbility { Id = "trapfinding", Name = "Trapfinding" } }
                    }
                },
                { 3, new List<Permabuff>
                    {
                        new ModifyCounter { CounterId = "sneak_attack_dice", Value = 1 }
                    }
                }
            }
        });

        // Skills referenced by these tests. Allocations against an unregistered id now warn,
        // so the fixture has to declare the ones it exercises.
        foreach (var (id, ability) in new[]
                 {
                     ("skill:climb", "str"), ("jump", "str"), ("skill:swim", "str"),
                     ("skill:bluff", "cha"), ("skill:hide", "dex"), ("skill:move_silently", "dex"),
                     ("skill:intimidate", "cha"), ("skill:ride", "dex"), ("skill:spot", "wis"),
                     ("skill:listen", "wis"), ("skill:tumble", "dex"), ("skill:concentration", "con"),
                     ("skill:spellcraft", "int"), ("skill:knowledge_arcana", "int"),
                 })
        {
            registry.RegisterSkill(new SkillDefinition { Id = id, Name = id, KeyAbility = ability });
        }

        return registry;
    }

    [Fact]
    public void HumanFighter5_FullEvaluation()
    {
        var registry = CreateContentRegistry();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Test Fighter",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter", Choices = new TickChoices { AbilityIncrease = Ability.STR } },
                new() { DriverId = "class:fighter" },
            }
        };

        var state = engine.Evaluate(character);

        // Identity
        Assert.Equal("race:human", state.RaceId);
        Assert.Equal(CreatureType.Humanoid, state.Type);
        Assert.Equal(Size.Medium, state.Size);

        // Ability Scores (16 base STR + 1 at HD 4 = 17)
        Assert.Equal(17, state.AbilityScores.STR);
        Assert.Equal(14, state.AbilityScores.DEX);
        Assert.Equal(14, state.AbilityScores.CON);

        // Progression
        Assert.Equal(5, state.TotalHD);
        Assert.Equal(5, state.ClassLevels["class:fighter"]);

        // BAB: Good BAB at 5 = +5
        Assert.Equal(5, state.BaseBAB);
        Assert.Equal(5, state.EffectiveBAB);

        // Saves: Fort good at 5 = 4, Ref/Will poor at 5 = 1
        Assert.Equal(4, state.BaseSaves.Fort);
        Assert.Equal(1, state.BaseSaves.Ref);
        Assert.Equal(1, state.BaseSaves.Will);

        // HP: d10 + CON(2): max(12) + 4*avg(8) = 12 + 32 = 44
        Assert.Equal(44, state.HP);

        // Skills: (2+0)*4 at HD 1 = 8, then 2 per level for 4 more = 8 + 8 = 16
        Assert.Equal(16, state.UnspentSkillPoints);

        // Feat slots: standard (HD 1, 3) = 2 + Human bonus = 3
        // Fighter bonus feats (levels 1, 2, 4) = 3
        // Total: 3 pending standard + 3 pending bonus = 6
        Assert.Equal(3, state.PendingFeatSlots);
        Assert.Equal(3, state.PendingBonusFeatSlots);

        // Movement
        Assert.Equal(30, state.Speeds[MovementMode.Land]);

        // No warnings
        Assert.Empty(state.Warnings);
    }

    [Fact]
    public void HumanFighter5_UpToHD3()
    {
        var registry = CreateContentRegistry();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Test Fighter",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter", Choices = new TickChoices { AbilityIncrease = Ability.STR } },
                new() { DriverId = "class:fighter" },
            }
        };

        var state = engine.Evaluate(character, upToHD: 3);

        Assert.Equal(3, state.TotalHD);
        Assert.Equal(3, state.ClassLevels["class:fighter"]);
        Assert.Equal(3, state.BaseBAB); // Good BAB at 3
        Assert.Equal(16, state.AbilityScores.STR); // No increase yet (that's at HD 4)
    }

    [Fact]
    public void ConstitutionIncrease_RecalculatesAllExistingHitDice()
    {
        var engine = new ReplayStudio(CreateContentRegistry());
        var character = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 13, INT = 10, WIS = 10, CHA = 10
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" },
                new()
                {
                    DriverId = "class:fighter",
                    Choices = new TickChoices { AbilityIncrease = Ability.CON }
                }
            }
        };

        var state = engine.Evaluate(character);

        Assert.Equal(14, state.AbilityScores.CON);
        Assert.Equal(36, state.HP); // 10 + 2 on first HD, then three times 6 + 2.
    }

    [Fact]
    public void ClassFeatureOptionRequirements_ProduceWarnings()
    {
        var registry = CreateContentRegistry();
        ((HDDriver)registry.GetDriver("class:fighter")).LevelPermabuffs[1].Add(
            new GrantClassFeatureSelection { FeatureType = "class_feature:test" });
        registry.RegisterClassFeature(new ClassFeatureDefinition
        {
            Id = "class_feature:test",
            Name = "Test Options",
            Options = new List<ClassFeatureOption>
            {
                new()
                {
                    Id = "option:restricted",
                    Name = "Restricted Option",
                    MinEffectiveLevel = 5,
                    RequiredCasterLevel = 5,
                    RequiredAlignment = "good"
                }
            }
        });
        var character = new Character
        {
            RaceId = "race:human",
            Alignment = Alignment.CE,
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10
            },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:fighter",
                    Choices = new TickChoices
                    {
                        ClassFeatureChoices = new Dictionary<string, List<string>>
                        {
                            ["class_feature:test"] = new() { "option:restricted" }
                        }
                    }
                }
            }
        };

        var state = new ReplayStudio(registry).Evaluate(character);

        Assert.Contains(state.Warnings, w => w.Message.Contains("requires effective level 5"));
        Assert.Contains(state.Warnings, w => w.Message.Contains("requires caster level 5"));
        Assert.Contains(state.Warnings, w => w.Message.Contains("requires alignment good"));
        Assert.Contains("option:restricted", state.ClassFeatureSelections["class_feature:test"]);
    }

    [Fact]
    public void Multiclass_Fighter2Rogue1()
    {
        var registry = CreateContentRegistry();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Multiclass Test",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 14, DEX = 16, CON = 12, INT = 14, WIS = 10, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:rogue" },
            }
        };

        var state = engine.Evaluate(character);

        Assert.Equal(3, state.TotalHD);
        Assert.Equal(2, state.ClassLevels["class:fighter"]);
        Assert.Equal(1, state.ClassLevels["class:rogue"]);

        // BAB: Fighter 2 (Good=2) + Rogue 1 (Average=0) = 2
        Assert.Equal(2, state.BaseBAB);

        // Saves: Fighter 2 Fort good(3) + Rogue 1 Fort poor(0) = 3
        //        Fighter 2 Ref poor(0) + Rogue 1 Ref good(2) = 2
        //        Fighter 2 Will poor(0) + Rogue 1 Will poor(0) = 0
        Assert.Equal(3, state.BaseSaves.Fort);
        Assert.Equal(2, state.BaseSaves.Ref);
        Assert.Equal(0, state.BaseSaves.Will);

        // HP: Fighter HD1: max d10+1=11, Fighter HD2: avg d10+1=7, Rogue HD3: avg d6+1=5
        // Total: 11 + 7 + 5 = 23
        Assert.Equal(23, state.HP);

        // Rogue gets sneak attack and trapfinding at level 1
        Assert.Contains(state.Abilities, a => a.Id == "sneak_attack");
        Assert.Equal(1, state.Counters["sneak_attack_dice"]);
        Assert.Contains(state.Abilities, a => a.Id == "trapfinding");

        // Class skills from both classes should be merged
        Assert.Contains("skill:climb", state.ClassSkills);      // both
        Assert.Contains("skill:tumble", state.ClassSkills);      // rogue only
        Assert.Contains("skill:handle_animal", state.ClassSkills); // fighter only

        // Feat slots: standard at HD 1, 3 = 2 + Human bonus = 3
        // Fighter bonus: levels 1, 2 = 2
        Assert.Equal(3, state.PendingFeatSlots);
        Assert.Equal(2, state.PendingBonusFeatSlots);
    }

    [Fact]
    public void FeatSelection_ReducesPendingSlots()
    {
        var registry = CreateContentRegistry();
        registry.RegisterFeat(new FeatDefinition
        {
            Id = "feat:power_attack",
            Name = "Power Attack",
            Type = FeatType.General,
            // Fighter-bonus eligibility is a tag, not a type: Power Attack is a general feat
            // that a fighter may also take with a bonus slot.
            Tags = new List<string> { ReplayStudio.FighterBonusTag },
            Prerequisites = new List<Prerequisite> { new MinAbility { Ability = Ability.STR, Value = 13 } }
        });

        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Feat Test",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:fighter",
                    Choices = new TickChoices
                    {
                        FeatIds = new List<string> { "feat:power_attack" }
                    }
                },
            }
        };

        var state = engine.Evaluate(character);

        Assert.Contains("feat:power_attack", state.Feats);
        // HD 1 gives 1 standard feat + 1 Human bonus = 2 standard
        // Fighter level 1 gives 1 bonus feat (fighter_bonus restriction)
        // Power Attack carries the fighter_bonus tag → consumes the bonus slot first
        Assert.Equal(2, state.PendingFeatSlots);
        Assert.Equal(0, state.PendingBonusFeatSlots);
    }

    [Fact]
    public void PermanentEvent_TomeOfINT_AffectsSubsequentSkills()
    {
        var registry = CreateContentRegistry();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Tome Test",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter" }, // HD 1: INT 10, skills = (2+0)*4 = 8
                new() { DriverId = "class:fighter" }, // HD 2: INT 10, skills = 2+0 = 2
                new() { DriverId = "class:fighter" }, // HD 3: INT 12 (tome applied before), skills = 2+1 = 3
                new() { DriverId = "class:fighter" }, // HD 4: INT 12, skills = 2+1 = 3
            },
            PermanentEvents = new List<PermanentEvent>
            {
                new()
                {
                    BeforeTick = 2, // before HD 3 (0-indexed)
                    Permabuffs = new List<Permabuff>
                    {
                        new ModifyAttribute { Target = AttributeTarget.AbilityScore, AbilityScore = Ability.INT, Value = 2 }
                    }
                }
            }
        };

        var state = engine.Evaluate(character);

        Assert.Equal(12, state.AbilityScores.INT);
        // Skills: HD1 (2+0)*4=8, HD2 2+0=2, HD3 2+1=3, HD4 2+1=3 = 16
        Assert.Equal(16, state.UnspentSkillPoints);
    }

    [Fact]
    public void NoWarnings_ValidBuild()
    {
        var registry = CreateContentRegistry();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Valid Build",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter" },
            }
        };

        var state = engine.Evaluate(character);
        Assert.Empty(state.Warnings);
    }

    [Fact]
    public void SkillAllocation_ClassSkill_CostsOnePointPerRank()
    {
        var registry = CreateContentRegistry();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Skill Test",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:fighter",
                    Choices = new TickChoices
                    {
                        SkillAllocations = new List<SkillAllocation>
                        {
                            new() { SkillId = "skill:climb", HalfRanks = 2 }  // 1 rank, class skill
                        }
                    }
                },
            }
        };

        var state = engine.Evaluate(character);

        // Fighter HD 1: (2+0)*4 = 8 skill points. Spent 1 (class skill: 2 half-ranks costs 1 pt)
        Assert.Equal(7, state.UnspentSkillPoints);
        Assert.Equal(2, state.SkillHalfRanks["skill:climb"]);
        Assert.Empty(state.Warnings);
    }

    [Fact]
    public void SkillAllocation_CrossClassSkill_CostsOnePointPerHalfRank()
    {
        var registry = CreateContentRegistry();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Cross-Class Test",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:fighter",
                    Choices = new TickChoices
                    {
                        SkillAllocations = new List<SkillAllocation>
                        {
                            new() { SkillId = "skill:bluff", HalfRanks = 1 }  // 0.5 rank, cross-class
                        }
                    }
                },
            }
        };

        var state = engine.Evaluate(character);

        // Cross-class costs 1 point per half-rank
        Assert.Equal(7, state.UnspentSkillPoints);
        Assert.Equal(1, state.SkillHalfRanks["skill:bluff"]);
        Assert.Empty(state.Warnings);
    }

    [Fact]
    public void SkillAllocation_ExceedingMaxRanks_WarnsButApplies()
    {
        var registry = CreateContentRegistry();
        var engine = new ReplayStudio(registry);

        // MaxHalfRanks at HD 1 = (1+3)*2 = 8 (i.e. 4 ranks)
        var character = new Character
        {
            Name = "Max Rank Test",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:fighter",
                    Choices = new TickChoices
                    {
                        SkillAllocations = new List<SkillAllocation>
                        {
                            new() { SkillId = "skill:climb", HalfRanks = 10 }  // 5 ranks, exceeds max 4
                        }
                    }
                },
            }
        };

        var state = engine.Evaluate(character);

        Assert.Equal(10, state.SkillHalfRanks["skill:climb"]);
        Assert.Contains(state.Warnings, w => w.Message.Contains("skill:climb") && w.Message.Contains("exceeding max"));
    }

    [Fact]
    public void SkillAllocation_Overspend_WarnsNegativePoints()
    {
        var registry = CreateContentRegistry();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Overspend Test",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:fighter",
                    Choices = new TickChoices
                    {
                        SkillAllocations = new List<SkillAllocation>
                        {
                            // Fighter HD1 human: (2+0)*4 = 8 pts. Spend 8 half-ranks class = 4 pts each
                            new() { SkillId = "skill:climb", HalfRanks = 8 },  // 4 pts
                            new() { SkillId = "skill:swim", HalfRanks = 8 },   // 4 pts
                            new() { SkillId = "skill:jump", HalfRanks = 4 },   // 2 pts → total 10, but only 8 available
                        }
                    }
                },
            }
        };

        var state = engine.Evaluate(character);

        Assert.True(state.UnspentSkillPoints < 0);
        Assert.Contains(state.Warnings, w => w.Message.Contains("more skill points than available"));
    }

    [Fact]
    public void AbilityIncrease_AtWrongHD_DoesNotApply()
    {
        var registry = CreateContentRegistry();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Wrong HD Ability Increase",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter", Choices = new TickChoices { AbilityIncrease = Ability.STR } }, // HD 1 — not a valid HD for increase
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter", Choices = new TickChoices { AbilityIncrease = Ability.STR } }, // HD 4 — valid
            }
        };

        var state = engine.Evaluate(character);

        // Only HD 4 should apply the increase (HD 1 should be ignored since 1 % 4 != 0)
        Assert.Equal(11, state.AbilityScores.STR);
    }

    // --- M3: Equipment Tests ---

    [Fact]
    public void Equipment_AbilityBonus_AppliedAfterTicks()
    {
        var registry = CreateContentRegistry();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Equipped Fighter",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 16, DEX = 10, CON = 14, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter" }
            },
            Equipment = new List<EquipmentEntry>
            {
                new()
                {
                    ItemId = "Belt of Giant Strength +4",
                    Slot = "waist",
                    Permabuffs = new List<Permabuff>
                    {
                        new ModifyAttribute { Target = AttributeTarget.AbilityScore, AbilityScore = Ability.STR, Value = 4 }
                    }
                }
            }
        };

        var state = engine.Evaluate(character);
        Assert.Equal(20, state.AbilityScores.STR); // 16 base + 4 equipment
    }

    [Fact]
    public void Equipment_MultipleItems_AllApplied()
    {
        var registry = CreateContentRegistry();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Geared Fighter",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 14, CON = 14, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter" }
            },
            Equipment = new List<EquipmentEntry>
            {
                new()
                {
                    ItemId = "Amulet of Natural Armor +2",
                    Slot = "neck",
                    Permabuffs = new List<Permabuff>
                    {
                        new ModifyAttribute { Target = AttributeTarget.NaturalArmor, Value = 2 }
                    }
                },
                new()
                {
                    ItemId = "Cloak of Resistance +3",
                    Slot = "shoulders",
                    Permabuffs = new List<Permabuff>
                    {
                        new ModifyAttribute { Target = AttributeTarget.SpellResistance, Value = 3 }
                    }
                }
            }
        };

        var state = engine.Evaluate(character);
        Assert.Equal(2, state.NaturalArmor);
        Assert.Equal(3, state.SpellResistance);
    }

    // --- M3: Permanent Events Tests ---

    [Fact]
    public void PermanentEvent_BeforeFirstTick_ModifiesAbility()
    {
        var registry = CreateContentRegistry();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Tome Reader",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 16, DEX = 10, CON = 14, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" }
            },
            PermanentEvents = new List<PermanentEvent>
            {
                new()
                {
                    BeforeTick = 0, // Before HD 1
                    Permabuffs = new List<Permabuff>
                    {
                        new ModifyAttribute { Target = AttributeTarget.AbilityScore, AbilityScore = Ability.STR, Value = 5 }
                    }
                }
            }
        };

        var state = engine.Evaluate(character);
        Assert.Equal(21, state.AbilityScores.STR); // 16 + 5
    }

    [Fact]
    public void PermanentEvent_BetweenTicks_AppliesAtCorrectTime()
    {
        var registry = CreateContentRegistry();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Mid-Level Tome",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" }
            },
            PermanentEvents = new List<PermanentEvent>
            {
                new()
                {
                    BeforeTick = 2, // Before HD 3
                    Permabuffs = new List<Permabuff>
                    {
                        new ModifyAttribute { Target = AttributeTarget.AbilityScore, AbilityScore = Ability.CON, Value = 2 }
                    }
                }
            }
        };

        // At HD 2, event hasn't fired yet
        var stateAt2 = engine.Evaluate(character, upToHD: 2);
        Assert.Equal(10, stateAt2.AbilityScores.CON);

        // At HD 3, event has fired (before tick 2 = before HD 3)
        var stateAt3 = engine.Evaluate(character, upToHD: 3);
        Assert.Equal(12, stateAt3.AbilityScores.CON);
    }

    [Fact]
    public void PermanentEvent_UpToHD_RespectsEventTiming()
    {
        var registry = CreateContentRegistry();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Tome at End",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" }
            },
            PermanentEvents = new List<PermanentEvent>
            {
                new()
                {
                    BeforeTick = 4, // Before HD 5
                    Permabuffs = new List<Permabuff>
                    {
                        new ModifyAttribute { Target = AttributeTarget.NaturalArmor, Value = 3 }
                    }
                }
            }
        };

        // At HD 4, event hasn't fired
        var at4 = engine.Evaluate(character, upToHD: 4);
        Assert.Equal(0, at4.NaturalArmor);

        // At HD 5, event has fired
        var at5 = engine.Evaluate(character, upToHD: 5);
        Assert.Equal(3, at5.NaturalArmor);
    }

    [Fact]
    public void EffectiveLevel_RacialHD_BoostsRangerFeatures()
    {
        var registry = new ContentRegistry();

        // Human race
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:human",
            Name = "Human",
            Type = CreatureType.Humanoid,
            Size = Size.Medium,
            Speeds = new Dictionary<MovementMode, int> { { MovementMode.Land, 30 } }
        });

        // Outsider race (for racial HD)
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:outsider",
            Name = "Outsider",
            Type = CreatureType.Outsider,
            Size = Size.Medium,
            Speeds = new Dictionary<MovementMode, int> { { MovementMode.Land, 30 } }
        });

        // Racial HD driver
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.RacialHD,
            Id = "racial_hd:outsider",
            Name = "Outsider",
            HitDie = 8,
            SkillPointsPerLevel = 8,
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Good,
                Ref = ProgressionRate.Good,
                Will = ProgressionRate.Good
            }
        });

        // Ranger class with level-specific features
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:ranger",
            Name = "Ranger",
            HitDie = 8,
            SkillPointsPerLevel = 6,
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Good,
                Ref = ProgressionRate.Good,
                Will = ProgressionRate.Poor
            },
            LevelPermabuffs = new Dictionary<int, List<Permabuff>>
            {
                { 1, new List<Permabuff> { new GrantAbility { Ability = new GrantedAbility { Id = "favored_enemy_1", Name = "Favored Enemy (1st)" } } } },
                { 2, new List<Permabuff> { new GrantAbility { Ability = new GrantedAbility { Id = "combat_style", Name = "Combat Style" } } } },
                { 5, new List<Permabuff> { new GrantAbility { Ability = new GrantedAbility { Id = "favored_enemy_2", Name = "Favored Enemy (2nd)" } } } },
                { 10, new List<Permabuff> { new GrantAbility { Ability = new GrantedAbility { Id = "favored_enemy_3", Name = "Favored Enemy (3rd)" } } } },
            }
        });

        // Template: "racial HD count as ranger levels"
        registry.RegisterTemplate(new TemplateDriver
        {
            Id = "template:ranger_affinity",
            Name = "Ranger Affinity",
            CreationPermabuffs = new List<Permabuff>
            {
                new GrantEffectiveLevels
                {
                    TargetDriverId = "class:ranger",
                    BonusFormula = new Formula("RacialHD()")
                }
            }
        });

        var engine = new ReplayStudio(registry);

        // 4 racial HD + 1 ranger level, with template
        var character = new Character
        {
            Name = "Outsider Ranger",
            RaceId = "race:outsider",
            TemplateIds = new List<string> { "template:ranger_affinity" },
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 14, DEX = 14, CON = 14, INT = 10, WIS = 14, CHA = 10
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "racial_hd:outsider" }, // HD 1
                new() { DriverId = "racial_hd:outsider" }, // HD 2
                new() { DriverId = "racial_hd:outsider" }, // HD 3
                new() { DriverId = "racial_hd:outsider" }, // HD 4
                new() { DriverId = "class:ranger" },        // HD 5 — actual ranger 1, effective ranger 5 (1 + 4 racial)
            }
        };

        var state = engine.Evaluate(character);

        // Class level should still be 1 (actual ranger levels taken)
        Assert.Equal(1, state.ClassLevels["class:ranger"]);
        Assert.Equal(5, state.TotalHD);

        // Effective level for ranger = 1 actual + 4 racial HD = 5
        // So ranger should get level 1 AND level 5 features
        Assert.Contains(state.Abilities, a => a.Id == "favored_enemy_1"); // ranger level 1
        Assert.Contains(state.Abilities, a => a.Id == "favored_enemy_2"); // ranger effective level 5

        // Should NOT get level 10 feature (effective level is only 5)
        Assert.DoesNotContain(state.Abilities, a => a.Id == "favored_enemy_3");

        // BAB should use actual driverLevel (1), not effective level
        // 4 outsider HD (good BAB): +4, 1 ranger HD (good BAB at level 1): +1 = 5
        Assert.Equal(5, state.BaseBAB);

        // Effective level rule should be recorded on state
        Assert.Single(state.EffectiveLevelRules);
        Assert.Equal("class:ranger", state.EffectiveLevelRules[0].TargetDriverId);
    }

    [Fact]
    public void TemplatePrerequisites_ValidatedAgainstFinishedState()
    {
        var registry = new ContentRegistry();

        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:outsider",
            Name = "Outsider",
            Type = CreatureType.Outsider,
            Size = Size.Medium,
            Speeds = new Dictionary<MovementMode, int> { { MovementMode.Land, 30 } }
        });

        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.RacialHD,
            Id = "racial_hd:outsider",
            Name = "Outsider",
            HitDie = 8,
            SkillPointsPerLevel = 8,
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Good, Ref = ProgressionRate.Good, Will = ProgressionRate.Good }
        });

        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:ranger",
            Name = "Ranger",
            HitDie = 8,
            SkillPointsPerLevel = 6,
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Good, Ref = ProgressionRate.Good, Will = ProgressionRate.Poor }
        });

        // Template gated on a class level that only exists AFTER the tick loop —
        // the check must therefore run against the finished state, not at creation.
        registry.RegisterTemplate(new TemplateDriver
        {
            Id = "template:unseelie_champion",
            Name = "Unseelie Champion",
            Prerequisites = new List<Prerequisite>
            {
                new HasCreatureType { Type = CreatureType.Outsider },
                new AnyOf
                {
                    Options = new List<Prerequisite>
                    {
                        new MinClassLevel { ClassId = "class:ranger", Value = 1 },
                        new MinClassLevel { ClassId = "class:planar_ranger", Value = 1 },
                    }
                }
            }
        });

        var engine = new ReplayStudio(registry);

        var qualified = new Character
        {
            Name = "Qualified",
            RaceId = "race:outsider",
            TemplateIds = new List<string> { "template:unseelie_champion" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "racial_hd:outsider" },
                new() { DriverId = "class:ranger" },
            }
        };
        var qualifiedState = engine.Evaluate(qualified);
        Assert.DoesNotContain(qualifiedState.Warnings, w => w.Message.Contains("template Unseelie Champion"));

        var unqualified = new Character
        {
            Name = "Unqualified",
            RaceId = "race:outsider",
            TemplateIds = new List<string> { "template:unseelie_champion" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "racial_hd:outsider" },
                new() { DriverId = "racial_hd:outsider" },
            }
        };
        var unqualifiedState = engine.Evaluate(unqualified);
        var warning = Assert.Single(unqualifiedState.Warnings, w => w.Message.Contains("template Unseelie Champion"));
        Assert.Contains("class:ranger level 1+ or class:planar_ranger level 1+", warning.Message);
    }

    [Fact]
    public void EffectiveLevel_WithoutTemplate_NoBoost()
    {
        var registry = new ContentRegistry();

        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:outsider",
            Name = "Outsider",
            Type = CreatureType.Outsider,
            Size = Size.Medium,
            Speeds = new Dictionary<MovementMode, int> { { MovementMode.Land, 30 } }
        });

        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.RacialHD,
            Id = "racial_hd:outsider",
            Name = "Outsider",
            HitDie = 8,
            SkillPointsPerLevel = 8,
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Good, Ref = ProgressionRate.Good, Will = ProgressionRate.Good }
        });

        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:ranger",
            Name = "Ranger",
            HitDie = 8,
            SkillPointsPerLevel = 6,
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Good, Ref = ProgressionRate.Good, Will = ProgressionRate.Poor },
            LevelPermabuffs = new Dictionary<int, List<Permabuff>>
            {
                { 1, new List<Permabuff> { new GrantAbility { Ability = new GrantedAbility { Id = "favored_enemy_1", Name = "Favored Enemy (1st)" } } } },
                { 5, new List<Permabuff> { new GrantAbility { Ability = new GrantedAbility { Id = "favored_enemy_2", Name = "Favored Enemy (2nd)" } } } },
            }
        });

        var engine = new ReplayStudio(registry);

        // Same setup but NO template — ranger 1 should only get level 1 features
        var character = new Character
        {
            Name = "Plain Outsider Ranger",
            RaceId = "race:outsider",
            BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 14, CON = 14, INT = 10, WIS = 14, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "racial_hd:outsider" },
                new() { DriverId = "racial_hd:outsider" },
                new() { DriverId = "racial_hd:outsider" },
                new() { DriverId = "racial_hd:outsider" },
                new() { DriverId = "class:ranger" },
            }
        };

        var state = engine.Evaluate(character);

        Assert.Contains(state.Abilities, a => a.Id == "favored_enemy_1"); // actual level 1
        Assert.DoesNotContain(state.Abilities, a => a.Id == "favored_enemy_2"); // no boost, no level 5
        Assert.Empty(state.EffectiveLevelRules);
    }

    [Fact]
    public void EffectiveLevel_ClassLevel_BoostsAnotherClass()
    {
        // Test that ClassLevel() formula works for cross-class boosting
        // e.g. "sorcerer levels count as bard levels"
        var registry = new ContentRegistry();

        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:human", Name = "Human", Type = CreatureType.Humanoid,
            Size = Size.Medium, Speeds = new Dictionary<MovementMode, int> { { MovementMode.Land, 30 } }
        });

        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class, Id = "class:sorcerer", Name = "Sorcerer",
            HitDie = 4, SkillPointsPerLevel = 2,
            BABProgression = BABProgression.Poor,
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Poor, Ref = ProgressionRate.Poor, Will = ProgressionRate.Good }
        });

        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class, Id = "class:bard", Name = "Bard",
            HitDie = 6, SkillPointsPerLevel = 6,
            BABProgression = BABProgression.Average,
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Poor, Ref = ProgressionRate.Good, Will = ProgressionRate.Good },
            LevelPermabuffs = new Dictionary<int, List<Permabuff>>
            {
                { 1, new List<Permabuff> { new GrantAbility { Ability = new GrantedAbility { Id = "bardic_music", Name = "Bardic Music" } } } },
                { 3, new List<Permabuff> { new GrantAbility { Ability = new GrantedAbility { Id = "inspire_competence", Name = "Inspire Competence" } } } },
            }
        });

        // Template: sorcerer levels count as bard levels
        registry.RegisterTemplate(new TemplateDriver
        {
            Id = "template:arcane_affinity",
            Name = "Arcane Affinity",
            CreationPermabuffs = new List<Permabuff>
            {
                new GrantEffectiveLevels
                {
                    TargetDriverId = "class:bard",
                    BonusFormula = new Formula("ClassLevel(sorcerer)")
                }
            }
        });

        var engine = new ReplayStudio(registry);

        // 2 sorcerer + 1 bard, with template
        var character = new Character
        {
            Name = "Arcane Prodigy",
            RaceId = "race:human",
            TemplateIds = new List<string> { "template:arcane_affinity" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 12, CON = 12, INT = 14, WIS = 10, CHA = 16 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:sorcerer" },
                new() { DriverId = "class:sorcerer" },
                new() { DriverId = "class:bard" }, // actual bard 1, effective bard 3 (1 + 2 sorc)
            }
        };

        var state = engine.Evaluate(character);

        Assert.Equal(1, state.ClassLevels["class:bard"]);
        Assert.Equal(2, state.ClassLevels["class:sorcerer"]);

        // Bard effective level 3 → gets inspire_competence (level 3 feature)
        Assert.Contains(state.Abilities, a => a.Id == "bardic_music");
        Assert.Contains(state.Abilities, a => a.Id == "inspire_competence");
    }

    [Fact]
    public void SneakAttack_StacksAcrossClasses()
    {
        var registry = CreateContentRegistry();

        // Register a mini Assassin with sneak attack at levels 1 and 3
        registry.RegisterDriver(new HDDriver
        {
            Id = "class:assassin",
            Name = "Assassin",
            Kind = DriverKind.Class,
            HitDie = 6,
            SkillPointsPerLevel = 4,
            ClassSkills = new List<string> { "skill:hide", "skill:move_silently" },
            BABProgression = BABProgression.Average,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Poor,
                Ref = ProgressionRate.Good,
                Will = ProgressionRate.Poor
            },
            MaxLevel = 10,
            LevelPermabuffs = new Dictionary<int, List<Permabuff>>
            {
                { 1, new List<Permabuff>
                    {
                        new ModifyCounter { CounterId = "sneak_attack_dice", Value = 1 }
                    }
                },
                { 3, new List<Permabuff>
                    {
                        new ModifyCounter { CounterId = "sneak_attack_dice", Value = 1 }
                    }
                }
            }
        });

        var engine = new ReplayStudio(registry);

        // Rogue 3 / Assassin 2: Rogue gives SA at L1,L3 (2 dice) + Assassin gives SA at L1 (1 die) = 3 total
        var character = new Character
        {
            Name = "Multiclass Sneak",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 16, CON = 12, INT = 14, WIS = 10, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:rogue" },   // Rogue 1: sneak_attack + 1 die
                new() { DriverId = "class:rogue" },   // Rogue 2
                new() { DriverId = "class:rogue" },   // Rogue 3: +1 die (total 2 from rogue)
                new() { DriverId = "class:assassin" }, // Assassin 1: +1 die (total 3)
                new() { DriverId = "class:assassin" }, // Assassin 2
            }
        };

        var state = engine.Evaluate(character);

        // Sneak attack ability should exist
        Assert.Contains(state.Abilities, a => a.Id == "sneak_attack");
        // Total sneak attack dice: 2 (Rogue L1,L3) + 1 (Assassin L1) = 3
        Assert.Equal(3, state.Counters["sneak_attack_dice"]);
    }

    [Fact]
    public void RacialClassSkillDelta_AddsAndRemovesSkills()
    {
        var registry = new ContentRegistry();

        // "Imp" race: outsider with custom class skills (adds hide/spellcraft, removes survival)
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:imp",
            Name = "Imp",
            Type = CreatureType.Outsider,
            Subtypes = new List<string> { "evil", "lawful" },
            Size = Size.Tiny,
            Speeds = new Dictionary<MovementMode, int> { { MovementMode.Land, 20 }, { MovementMode.Fly, 50 } },
            RacialHDDriverId = "racial_hd:outsider",
            RacialClassSkillAdditions = new List<string> { "skill:hide", "skill:spellcraft" },
            RacialClassSkillRemovals = new List<string> { "skill:survival" }
        });

        // Generic outsider racial HD driver with base class skills
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.RacialHD,
            Id = "racial_hd:outsider",
            Name = "Outsider",
            HitDie = 8,
            SkillPointsPerLevel = 8,
            ClassSkills = new List<string> { "skill:bluff", "craft", "skill:knowledge_planes", "skill:listen", "skill:search", "skill:sense_motive", "skill:spot", "skill:survival" },
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Good,
                Ref = ProgressionRate.Good,
                Will = ProgressionRate.Good
            }
        });

        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Test Imp",
            RaceId = "race:imp",
            BaseAbilityScores = new AbilityScoreSet { STR = 6, DEX = 16, CON = 10, INT = 10, WIS = 12, CHA = 14 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "racial_hd:outsider" },
                new() { DriverId = "racial_hd:outsider" },
                new() { DriverId = "racial_hd:outsider" },
            }
        };

        var state = engine.Evaluate(character);

        // Base outsider skills should be present
        Assert.Contains("skill:bluff", state.ClassSkills);
        Assert.Contains("skill:listen", state.ClassSkills);
        Assert.Contains("skill:spot", state.ClassSkills);

        // Added skills should be present
        Assert.Contains("skill:hide", state.ClassSkills);
        Assert.Contains("skill:spellcraft", state.ClassSkills);

        // Removed skills should NOT be present
        Assert.DoesNotContain("skill:survival", state.ClassSkills);

        // CurrentTickClassSkills on last tick should also reflect the delta
        Assert.Contains("skill:hide", state.CurrentTickClassSkills);
        Assert.Contains("skill:spellcraft", state.CurrentTickClassSkills);
        Assert.DoesNotContain("skill:survival", state.CurrentTickClassSkills);
    }

    [Fact]
    public void RacialClassSkillDelta_EmptyDelta_UsesDriverSkillsUnchanged()
    {
        var registry = new ContentRegistry();

        // Race with no delta — should use driver skills as-is
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:generic_outsider",
            Name = "Generic Outsider",
            Type = CreatureType.Outsider,
            Size = Size.Medium,
            Speeds = new Dictionary<MovementMode, int> { { MovementMode.Land, 30 } },
            RacialHDDriverId = "racial_hd:outsider"
        });

        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.RacialHD,
            Id = "racial_hd:outsider",
            Name = "Outsider",
            HitDie = 8,
            SkillPointsPerLevel = 8,
            ClassSkills = new List<string> { "skill:bluff", "skill:listen", "skill:spot", "skill:survival" },
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Good,
                Ref = ProgressionRate.Good,
                Will = ProgressionRate.Good
            }
        });

        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Default Outsider",
            RaceId = "race:generic_outsider",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "racial_hd:outsider" },
            }
        };

        var state = engine.Evaluate(character);

        // All driver skills present, nothing added or removed
        Assert.Contains("skill:bluff", state.ClassSkills);
        Assert.Contains("skill:listen", state.ClassSkills);
        Assert.Contains("skill:spot", state.ClassSkills);
        Assert.Contains("skill:survival", state.ClassSkills);
    }
}
