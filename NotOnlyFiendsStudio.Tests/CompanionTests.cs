using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// PR 1 scaffolding tests. Covers the new model fields, the GrantCompanionSlot
/// and ModifyLeadershipScore permabuffs, the ReplayStudio tail pass (slot
/// recompute, Leadership score, companion-side template scaling), and
/// CompanionResolver wiring. No content content required — registries are
/// hand-built per test.
/// </summary>
public class CompanionTests
{
    // ---------- GrantCompanionSlot permabuff ----------

    [Fact]
    public void GrantCompanionSlot_AddsSlotAndPendingSelections()
    {
        var state = new CharacterState { TotalHD = 1 };
        var ctx = new PermabuffContext(state, GameRules.Standard35e());
        ctx.CurrentDriverId = "class:druid";

        new GrantCompanionSlot
        {
            LinkType = "animal_companion",
            ClassFeatureType = "class_feature:animal_companion_options",
            EffectiveLevelFormula = new Formula("ClassLevel(druid)")
        }.Apply(ctx);

        Assert.Single(state.CompanionSlots);
        var slot = state.CompanionSlots[0];
        Assert.Equal("animal_companion", slot.LinkType);
        Assert.Equal("class:druid", slot.Granter);
        Assert.Equal("class_feature:animal_companion_options", slot.ClassFeatureType);
        Assert.Equal(1, state.PendingCompanionSelections["animal_companion"]);
        Assert.Equal(1, state.PendingClassFeatureSelections["class_feature:animal_companion_options"]);
    }

    [Fact]
    public void GrantCompanionSlot_SecondGrant_UpdatesFormulaWithoutNewSelection()
    {
        // Druid grants AC at L1 (formula = ClassLevel(druid)).
        // Ranger grants AC upgrade at L4 (formula = ClassLevel(druid)+ClassLevel(ranger)-3).
        var state = new CharacterState { TotalHD = 1 };
        var ctx = new PermabuffContext(state, GameRules.Standard35e());

        new GrantCompanionSlot
        {
            LinkType = "animal_companion",
            ClassFeatureType = "class_feature:animal_companion_options",
            EffectiveLevelFormula = new Formula("ClassLevel(druid)")
        }.Apply(ctx);

        new GrantCompanionSlot
        {
            LinkType = "animal_companion",
            ClassFeatureType = "class_feature:animal_companion_options",
            EffectiveLevelFormula = new Formula("ClassLevel(druid) + ClassLevel(ranger) - 3"),
            UpgradeOnly = true
        }.Apply(ctx);

        Assert.Single(state.CompanionSlots);
        Assert.Equal(1, state.PendingCompanionSelections["animal_companion"]);
        Assert.Equal(1, state.PendingClassFeatureSelections["class_feature:animal_companion_options"]);
        Assert.Equal("ClassLevel(druid) + ClassLevel(ranger) - 3",
                     state.CompanionSlots[0].EffectiveLevelFormula.Expression);
    }

    [Fact]
    public void GrantCompanionSlot_UpgradeOnlyWithoutExistingSlot_DoesNothing()
    {
        var state = new CharacterState { TotalHD = 1 };
        var ctx = new PermabuffContext(state, GameRules.Standard35e());

        new GrantCompanionSlot
        {
            LinkType = "animal_companion",
            ClassFeatureType = "class_feature:animal_companion_options",
            EffectiveLevelFormula = new Formula("ClassLevel(ranger) - 3"),
            UpgradeOnly = true
        }.Apply(ctx);

        Assert.Empty(state.CompanionSlots);
        Assert.Empty(state.PendingCompanionSelections);
        Assert.Empty(state.PendingClassFeatureSelections);
    }

    // ---------- ModifyLeadershipScore permabuff ----------

    [Fact]
    public void ModifyLeadershipScore_AccumulatesModifier()
    {
        var state = new CharacterState();
        var ctx = new PermabuffContext(state, GameRules.Standard35e());

        new ModifyLeadershipScore { Value = 2, Reason = "fame" }.Apply(ctx);
        new ModifyLeadershipScore { Value = -1, Reason = "cruelty" }.Apply(ctx);

        Assert.Equal(1, state.LeadershipScoreModifier);
    }

    // ---------- Formula attribute additions ----------

    [Fact]
    public void Formula_MasterLevel_ReadsEffectiveMasterLevel()
    {
        var state = new CharacterState { EffectiveMasterLevel = 7 };
        Assert.Equal(7, new Formula("MasterLevel").Evaluate(state));
        Assert.Equal(4, new Formula("MasterLevel - 3").Evaluate(state));
    }

    [Fact]
    public void Formula_LeadershipScore_ReadsState()
    {
        var state = new CharacterState { LeadershipScore = 12 };
        Assert.Equal(10, new Formula("LeadershipScore - 2").Evaluate(state));
    }

    // ---------- Template CompanionScalingPermabuffs ----------

    [Fact]
    public void Template_GetCompanionScalingPermabuffs_FiresEntriesUpToMasterLevel()
    {
        var template = new TemplateDriver
        {
            Id = "template:test_companion",
            CompanionScalingPermabuffs = new SortedDictionary<int, List<Permabuff>>
            {
                { 1, new List<Permabuff> { new ModifyAttribute { Target = AttributeTarget.NaturalArmor, Value = 1 } } },
                { 4, new List<Permabuff> { new ModifyAttribute { Target = AttributeTarget.NaturalArmor, Value = 2 } } },
                { 7, new List<Permabuff> { new ModifyAttribute { Target = AttributeTarget.NaturalArmor, Value = 3 } } }
            }
        };

        var atLevel0 = template.GetCompanionScalingPermabuffs(0);
        var atLevel5 = template.GetCompanionScalingPermabuffs(5);
        var atLevel20 = template.GetCompanionScalingPermabuffs(20);

        Assert.Empty(atLevel0);
        Assert.Equal(2, atLevel5.Count);   // tiers 1 and 4
        Assert.Equal(3, atLevel20.Count);  // all three
    }

    // ---------- ReplayStudio tail pass: slot effective-level recompute ----------

    [Fact]
    public void TailPass_RecomputesCompanionSlotEffectiveLevel()
    {
        var registry = BuildBasicRegistry();

        // Druid driver grants the slot at level 1.
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:druid",
            Name = "Druid",
            HitDie = 8,
            SkillPointsPerLevel = 4,
            BABProgression = BABProgression.Average,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Good,
                Ref = ProgressionRate.Poor,
                Will = ProgressionRate.Good
            },
            LevelPermabuffs = new Dictionary<int, List<Permabuff>>
            {
                { 1, new List<Permabuff>
                    {
                        new GrantCompanionSlot
                        {
                            LinkType = "animal_companion",
                            ClassFeatureType = "class_feature:animal_companion_options",
                            EffectiveLevelFormula = new Formula("ClassLevel(druid)")
                        }
                    }
                }
            }
        });

        var engine = new ReplayStudio(registry);
        var character = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 14, CHA = 10 },
            Ticks = Enumerable.Range(0, 5).Select(_ => new Tick { DriverId = "class:druid" }).ToList()
        };

        var state = engine.Evaluate(character);

        Assert.Single(state.CompanionSlots);
        Assert.Equal(5, state.CompanionSlots[0].EffectiveLevel);
        Assert.Equal(1, state.PendingClassFeatureSelections["class_feature:animal_companion_options"]);
    }

    [Fact]
    public void TailPass_BindsSelectedSpeciesFromClassFeatureSelections()
    {
        var registry = BuildBasicRegistry();
        registry.RegisterDriver(BuildDruidDriver());
        registry.RegisterClassFeature(new ClassFeatureDefinition
        {
            Id = "class_feature:animal_companion_options",
            Name = "Animal Companion Options",
            Options = new List<ClassFeatureOption>
            {
                new() { Id = "leopard", Name = "Leopard" },
                new() { Id = "wolf", Name = "Wolf" }
            }
        });

        var engine = new ReplayStudio(registry);
        var character = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 14, CHA = 10 },
            Ticks =
            {
                new Tick
                {
                    DriverId = "class:druid",
                    Choices = new TickChoices
                    {
                        ClassFeatureChoices = new Dictionary<string, List<string>>
                        {
                            ["class_feature:animal_companion_options"] = new() { "leopard" }
                        }
                    }
                }
            }
        };

        var state = engine.Evaluate(character);

        Assert.Single(state.CompanionSlots);
        Assert.Equal("leopard", state.CompanionSlots[0].SelectedSpecies);
    }

    // ---------- ReplayStudio tail pass: Leadership ----------

    [Fact]
    public void TailPass_LeadershipScoreComputed_WhenFeatPresent()
    {
        var registry = BuildBasicRegistry();
        registry.RegisterDriver(BuildFighterDriver());
        registry.RegisterFeat(new FeatDefinition
        {
            Id = "feat:leadership",
            Name = "Leadership",
            Type = FeatType.General,
            Prerequisites = new List<Prerequisite> { new MinHD { Value = 6 } }
        });

        var engine = new ReplayStudio(registry);
        var character = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 14 },
            // 7 fighter levels, take Leadership at L7 via the standard L6 feat slot (HD 6).
            Ticks = Enumerable.Range(0, 7).Select(i =>
                i == 5
                    ? new Tick
                        {
                            DriverId = "class:fighter",
                            Choices = new TickChoices { FeatIds = new List<string> { "feat:leadership" } }
                        }
                    : new Tick { DriverId = "class:fighter" }).ToList()
        };

        var state = engine.Evaluate(character);

        Assert.Contains("feat:leadership", state.Feats);
        // 7 HD + Mod(CHA 14)=+2 + 0 modifier = 9
        Assert.Equal(9, state.LeadershipScore);
        Assert.Equal(5, state.MaxCohortLevel); // min(9-2, 7-2) = 5
        Assert.Equal(0, state.Followers.Level1); // score 9 → no followers
    }

    [Fact]
    public void TailPass_LeadershipFollowers_PopulatedAtThreshold()
    {
        var registry = BuildBasicRegistry();
        registry.RegisterDriver(BuildFighterDriver());
        registry.RegisterFeat(new FeatDefinition
        {
            Id = "feat:leadership",
            Name = "Leadership",
            Type = FeatType.General,
            Prerequisites = new List<Prerequisite> { new MinHD { Value = 6 } }
        });

        var engine = new ReplayStudio(registry);
        // Fighter 8 with CHA 18 → score = 8 + 4 = 12 → 8 first-level followers per DMG.
        var character = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 18 },
            Ticks = Enumerable.Range(0, 8).Select(i =>
                i == 5
                    ? new Tick
                        {
                            DriverId = "class:fighter",
                            Choices = new TickChoices { FeatIds = new List<string> { "feat:leadership" } }
                        }
                    : new Tick { DriverId = "class:fighter" }).ToList()
        };

        var state = engine.Evaluate(character);

        Assert.Equal(12, state.LeadershipScore);
        Assert.Equal(8, state.Followers.Level1);
    }

    [Fact]
    public void LeadershipTables_ScoreBelow10_NoFollowers()
    {
        var counts = LeadershipTables.LookupFollowerCounts(9);
        Assert.Equal(0, counts.Level1);
        Assert.Equal(0, counts.Level6);
    }

    [Fact]
    public void LeadershipTables_Score25_MaxFollowers()
    {
        var counts = LeadershipTables.LookupFollowerCounts(30);
        Assert.Equal(135, counts.Level1);
        Assert.Equal(13, counts.Level2);
        Assert.Equal(1, counts.Level6);
    }

    // ---------- ReplayStudio tail pass: companion-side template scaling ----------

    [Fact]
    public void TailPass_CompanionTemplate_FiresScalingUpToMasterLevel()
    {
        var registry = BuildBasicRegistry();
        // Animal racial HD driver — minimal definition.
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.RacialHD,
            Id = "racial_hd:animal",
            Name = "Animal HD",
            HitDie = 8,
            SkillPointsPerLevel = 1,
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Good,
                Ref = ProgressionRate.Good,
                Will = ProgressionRate.Poor
            }
        });

        // Companion creature race.
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:companion_test_beast",
            Name = "Test Beast",
            Type = CreatureType.Animal,
            Size = Size.Medium,
            RacialHDDriverId = "racial_hd:animal"
        });

        // Animal companion progression template.
        registry.RegisterTemplate(new TemplateDriver
        {
            Id = "template:animal_companion_standard",
            Name = "Animal Companion (standard)",
            CompanionScalingPermabuffs = new SortedDictionary<int, List<Permabuff>>
            {
                { 1, new List<Permabuff> { new ModifyAttribute { Target = AttributeTarget.NaturalArmor, Value = 0 } } },
                { 2, new List<Permabuff>
                    {
                        new ModifyAttribute { Target = AttributeTarget.NaturalArmor, Value = 2 },
                        new GrantAbility { Ability = new GrantedAbility { Id = "ac_link", Name = "Link" } }
                    }
                },
                { 5, new List<Permabuff>
                    {
                        new ModifyAttribute { Target = AttributeTarget.NaturalArmor, Value = 1 },
                        new GrantAbility { Ability = new GrantedAbility { Id = "ac_evasion", Name = "Evasion" } }
                    }
                },
                { 9, new List<Permabuff>
                    {
                        new GrantAbility { Ability = new GrantedAbility { Id = "ac_multiattack", Name = "Multiattack" } }
                    }
                }
            }
        });

        var engine = new ReplayStudio(registry);
        var companion = new Character
        {
            Name = "Shadow",
            RaceId = "race:companion_test_beast",
            TemplateIds = new List<string> { "template:animal_companion_standard" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 2, WIS = 10, CHA = 6 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "racial_hd:animal" },
                new() { DriverId = "racial_hd:animal" },
                new() { DriverId = "racial_hd:animal" }
            },
            CompanionOrigin = new CompanionOrigin
            {
                LinkType = "animal_companion",
                EffectiveMasterLevel = 5
            }
        };

        var state = engine.Evaluate(companion);

        Assert.Equal(5, state.EffectiveMasterLevel);
        // NA tiers 1 (+0) + 2 (+2) + 5 (+1) = 3.
        Assert.Equal(3, state.NaturalArmor);
        Assert.Contains(state.Abilities, a => a.Id == "ac_link");
        Assert.Contains(state.Abilities, a => a.Id == "ac_evasion");
        Assert.DoesNotContain(state.Abilities, a => a.Id == "ac_multiattack");
    }

    [Fact]
    public void TailPass_NoCompanionOrigin_TemplateScalingNotFired()
    {
        var registry = BuildBasicRegistry();
        registry.RegisterTemplate(new TemplateDriver
        {
            Id = "template:animal_companion_standard",
            CompanionScalingPermabuffs = new SortedDictionary<int, List<Permabuff>>
            {
                { 1, new List<Permabuff> { new ModifyAttribute { Target = AttributeTarget.NaturalArmor, Value = 99 } } }
            }
        });

        var engine = new ReplayStudio(registry);
        var character = new Character
        {
            RaceId = "race:human",
            TemplateIds = new List<string> { "template:animal_companion_standard" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 }
            // No CompanionOrigin → not a companion build → scaling must NOT fire.
        };

        var state = engine.Evaluate(character);
        Assert.Equal(0, state.NaturalArmor);
    }

    // ---------- CompanionResolver ----------

    [Fact]
    public void CompanionResolver_BuildsMasterAndCompanion_InjectsMasterLevel()
    {
        var registry = BuildBasicRegistry();
        registry.RegisterDriver(BuildDruidDriver());

        // Animal racial HD + companion race + template (minimal).
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.RacialHD,
            Id = "racial_hd:animal",
            Name = "Animal HD",
            HitDie = 8,
            SkillPointsPerLevel = 1,
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Good, Ref = ProgressionRate.Good, Will = ProgressionRate.Poor
            }
        });
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:companion_wolf",
            Name = "Wolf",
            Type = CreatureType.Animal,
            Size = Size.Medium,
            RacialHDDriverId = "racial_hd:animal"
        });
        registry.RegisterTemplate(new TemplateDriver
        {
            Id = "template:animal_companion_standard",
            CompanionScalingPermabuffs = new SortedDictionary<int, List<Permabuff>>
            {
                { 1, new List<Permabuff> { new ModifyAttribute { Target = AttributeTarget.NaturalArmor, Value = 1 } } },
                { 5, new List<Permabuff> { new ModifyAttribute { Target = AttributeTarget.NaturalArmor, Value = 2 } } }
            }
        });

        var engine = new ReplayStudio(registry);

        var wolf = new Character
        {
            Name = "Wolf",
            RaceId = "race:companion_wolf",
            TemplateIds = new List<string> { "template:animal_companion_standard" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 2, WIS = 10, CHA = 6 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "racial_hd:animal" },
                new() { DriverId = "racial_hd:animal" }
            }
        };

        var druid = new Character
        {
            Name = "Daenra",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 14, CHA = 10 },
            Ticks = Enumerable.Range(0, 5).Select(_ => new Tick { DriverId = "class:druid" }).ToList(),
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "animal_companion",
                    CompanionId = "wolf",
                    EffectiveLevelFormula = new Formula("ClassLevel(druid)")
                }
            }
        };

        var resolver = new CompanionResolver(engine, id => id == "wolf" ? wolf : null);
        var result = resolver.Build(druid);

        Assert.Equal(5, result.MasterState.CompanionSlots[0].EffectiveLevel);
        Assert.Single(result.Companions);
        var companion = result.Companions[0];
        Assert.Equal(5, companion.State.EffectiveMasterLevel);
        Assert.Equal(3, companion.State.NaturalArmor); // tiers 1 (+1) and 5 (+2)
    }

    [Fact]
    public void CompanionResolver_MissingCompanion_EmitsWarning()
    {
        var registry = BuildBasicRegistry();
        registry.RegisterDriver(BuildDruidDriver());

        var druid = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 14, CHA = 10 },
            Ticks = new List<Tick> { new() { DriverId = "class:druid" } },
            CompanionLinks = new List<CompanionLink>
            {
                new() { LinkType = "animal_companion", CompanionId = "missing" }
            }
        };

        var engine = new ReplayStudio(registry);
        var resolver = new CompanionResolver(engine, _ => null);
        var result = resolver.Build(druid);

        Assert.Empty(result.Companions);
        Assert.Contains(result.MasterState.Warnings, w => w.Message.Contains("missing companion"));
    }

    // ---------- helpers ----------

    private static ContentRegistry BuildBasicRegistry()
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
        return registry;
    }

    private static HDDriver BuildDruidDriver() => new()
    {
        Kind = DriverKind.Class,
        Id = "class:druid",
        Name = "Druid",
        HitDie = 8,
        SkillPointsPerLevel = 4,
        BABProgression = BABProgression.Average,
        SaveProgression = new SaveProgression
        {
            Fort = ProgressionRate.Good,
            Ref = ProgressionRate.Poor,
            Will = ProgressionRate.Good
        },
        LevelPermabuffs = new Dictionary<int, List<Permabuff>>
        {
            { 1, new List<Permabuff>
                {
                    new GrantCompanionSlot
                    {
                        LinkType = "animal_companion",
                        ClassFeatureType = "class_feature:animal_companion_options",
                        EffectiveLevelFormula = new Formula("ClassLevel(druid)")
                    }
                }
            }
        }
    };

    private static HDDriver BuildFighterDriver() => new()
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
    };

    // ---------- PR 2 end-to-end (real SRD content) ----------

    [Fact]
    public void Druid5_GrantsAnimalCompanionSlot_FromContentPack()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var druid = new Character
        {
            Name = "Daenra",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 14, CHA = 10 },
            Ticks = Enumerable.Range(0, 5).Select(_ => new Tick { DriverId = "class:druid" }).ToList()
        };

        var state = engine.Evaluate(druid);

        var slot = Assert.Single(state.CompanionSlots);
        Assert.Equal("animal_companion", slot.LinkType);
        Assert.Equal("class:druid", slot.Granter);
        Assert.Equal(5, slot.EffectiveLevel);
        Assert.Equal(1, state.PendingClassFeatureSelections["class_feature:animal_companion_options"]);
    }

    [Fact]
    public void Druid5_PicksWolf_BindsSelectedSpeciesAndScalesCompanion()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var druid = new Character
        {
            Name = "Daenra",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 14, CHA = 10 },
            Ticks = Enumerable.Range(0, 5).Select(i =>
                i == 0
                    ? new Tick
                        {
                            DriverId = "class:druid",
                            Choices = new TickChoices
                            {
                                ClassFeatureChoices = new Dictionary<string, List<string>>
                                {
                                    ["class_feature:animal_companion_options"] = new() { "race:companion_wolf" }
                                }
                            }
                        }
                    : new Tick { DriverId = "class:druid" }).ToList(),
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "animal_companion",
                    CompanionId = "wolf-companion",
                    SelectedSpecies = "race:companion_wolf",
                    EffectiveLevelFormula = new Formula("ClassLevel(druid)")
                }
            }
        };

        // Wolf has 2 base HD per SRD. At druid 5 (effective AC level 5), the wolf
        // gains the level-3 tier from the standard AC progression: NA +2, Str+1, Dex+1,
        // ability ac_evasion. Link/Share Spells from level 1 tier as well.
        var wolf = new Character
        {
            Name = "Lupa",
            RaceId = "race:companion_wolf",
            TemplateIds = new List<string> { "template:animal_companion_standard" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "racial_hd:animal" },
                new() { DriverId = "racial_hd:animal" }
            }
        };

        var resolver = new CompanionResolver(engine, id => id == "wolf-companion" ? wolf : null);
        var result = resolver.Build(druid);

        // Master side
        Assert.Equal("race:companion_wolf", result.MasterState.CompanionSlots[0].SelectedSpecies);
        Assert.Equal(5, result.MasterState.CompanionSlots[0].EffectiveLevel);

        // Companion side
        var wolfBuild = Assert.Single(result.Companions);
        Assert.Equal(5, wolfBuild.State.EffectiveMasterLevel);

        // Wolf base NA = 2; AC tier-3 adds +2 → 4.
        Assert.Equal(4, wolfBuild.State.NaturalArmor);

        // Abilities: link, share spells, evasion (tier 1 + tier 3). Multiattack NOT yet (tier 9).
        Assert.Contains(wolfBuild.State.Abilities, a => a.Id == "ac_link");
        Assert.Contains(wolfBuild.State.Abilities, a => a.Id == "ac_share_spells");
        Assert.Contains(wolfBuild.State.Abilities, a => a.Id == "ac_evasion");
        Assert.DoesNotContain(wolfBuild.State.Abilities, a => a.Id == "ac_devotion");
        Assert.DoesNotContain(wolfBuild.State.Abilities, a => a.Id == "ac_multiattack");

        // Bonus tricks counter: 1 trick at tier 3.
        Assert.Equal(1, wolfBuild.State.Counters["ac_bonus_tricks"]);

        // Wolf base Str (10 + 3 = 13) + AC tier-3 (+1) = 14.
        Assert.Equal(14, wolfBuild.State.AbilityScores.STR);
        // Wolf base Dex (10 + 5 = 15) + AC tier-3 (+1) = 16.
        Assert.Equal(16, wolfBuild.State.AbilityScores.DEX);
    }

    [Fact]
    public void Wizard1_GrantsFamiliarSlot_AndScalesCatFromContentPack()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var wizard = new Character
        {
            Name = "Vex",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 16, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:wizard",
                    Choices = new TickChoices
                    {
                        ClassFeatureChoices = new Dictionary<string, List<string>>
                        {
                            ["class_feature:familiar_options"] = new() { "race:familiar_cat" }
                        }
                    }
                }
            },
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "familiar",
                    CompanionId = "cat-familiar",
                    SelectedSpecies = "race:familiar_cat",
                    EffectiveLevelFormula = new Formula("CasterLevel(wizard) + CasterLevel(sorcerer)")
                }
            }
        };

        // Cat has 1 racial HD (1/2 HD in SRD, modeled here as 1 tick).
        var cat = new Character
        {
            Name = "Whiskers",
            RaceId = "race:familiar_cat",
            TemplateIds = new List<string> { "template:familiar_standard" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick> { new() { DriverId = "racial_hd:animal" } }
        };

        var resolver = new CompanionResolver(engine, id => id == "cat-familiar" ? cat : null);
        var result = resolver.Build(wizard);

        // Master side
        var slot = Assert.Single(result.MasterState.CompanionSlots);
        Assert.Equal("familiar", slot.LinkType);
        Assert.Equal(1, slot.EffectiveLevel);
        Assert.Equal("race:familiar_cat", slot.SelectedSpecies);

        // Companion side
        var catBuild = Assert.Single(result.Companions);
        Assert.Equal(1, catBuild.State.EffectiveMasterLevel);

        // Cat base NA = 2 (from racialPermabuffs); familiar tier 1 adds +1 → 3.
        Assert.Equal(3, catBuild.State.NaturalArmor);

        // Familiar template SetAttribute INT = 6 at master level 1.
        Assert.Equal(6, catBuild.State.AbilityScores.INT);

        // Tier 1 abilities all present.
        Assert.Contains(catBuild.State.Abilities, a => a.Id == "fam_alertness");
        Assert.Contains(catBuild.State.Abilities, a => a.Id == "fam_improved_evasion");
        Assert.Contains(catBuild.State.Abilities, a => a.Id == "fam_share_spells");
        Assert.Contains(catBuild.State.Abilities, a => a.Id == "fam_empathic_link");
        // Higher-tier abilities NOT present at master level 1.
        Assert.DoesNotContain(catBuild.State.Abilities, a => a.Id == "fam_deliver_touch_spells");
    }

    [Fact]
    public void Sorcerer1_GrantsFamiliarSlot_FromContentPack()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var sorcerer = new Character
        {
            Name = "Mira",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 16 },
            Ticks = new List<Tick> { new() { DriverId = "class:sorcerer" } }
        };

        var state = engine.Evaluate(sorcerer);

        var slot = Assert.Single(state.CompanionSlots);
        Assert.Equal("familiar", slot.LinkType);
        Assert.Equal(1, slot.EffectiveLevel);
        Assert.Equal(1, state.PendingClassFeatureSelections["class_feature:familiar_options"]);
    }

    // ---------- PR 3: Paladin mount + Ranger AC ----------

    [Fact]
    public void Paladin4_NoMountSlotYet()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var paladin = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 10, CON = 12, INT = 10, WIS = 12, CHA = 14 },
            Ticks = Enumerable.Range(0, 4).Select(_ => new Tick { DriverId = "class:paladin" }).ToList()
        };

        var state = engine.Evaluate(paladin);

        Assert.Empty(state.CompanionSlots);
        Assert.False(state.PendingClassFeatureSelections.ContainsKey("class_feature:paladin_mount_options"));
    }

    [Fact]
    public void Paladin5_GrantsMountSlot_FromContentPack()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var paladin = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 10, CON = 12, INT = 10, WIS = 12, CHA = 14 },
            Ticks = Enumerable.Range(0, 5).Select(_ => new Tick { DriverId = "class:paladin" }).ToList()
        };

        var state = engine.Evaluate(paladin);

        var slot = Assert.Single(state.CompanionSlots);
        Assert.Equal("special_mount", slot.LinkType);
        Assert.Equal("class:paladin", slot.Granter);
        Assert.Equal(5, slot.EffectiveLevel);
        Assert.Equal(1, state.PendingClassFeatureSelections["class_feature:paladin_mount_options"]);
    }

    [Fact]
    public void Paladin5_HeavyWarhorse_FullPipeline()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var paladin = new Character
        {
            Name = "Sir Aldric",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 10, CON = 12, INT = 10, WIS = 12, CHA = 14 },
            Ticks = Enumerable.Range(0, 5).Select(i =>
                i == 4
                    ? new Tick
                        {
                            DriverId = "class:paladin",
                            Choices = new TickChoices
                            {
                                ClassFeatureChoices = new Dictionary<string, List<string>>
                                {
                                    ["class_feature:paladin_mount_options"] = new() { "race:companion_heavy_warhorse" }
                                }
                            }
                        }
                    : new Tick { DriverId = "class:paladin" }).ToList(),
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "special_mount",
                    CompanionId = "warhorse",
                    SelectedSpecies = "race:companion_heavy_warhorse",
                    EffectiveLevelFormula = new Formula("ClassLevel(paladin)")
                }
            }
        };

        // Heavy warhorse: 4 base HD per SRD (modeled here).
        var warhorse = new Character
        {
            Name = "Daystar",
            RaceId = "race:companion_heavy_warhorse",
            TemplateIds = new List<string> { "template:special_mount_standard" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 4).Select(_ => new Tick { DriverId = "racial_hd:animal" }).ToList()
        };

        var resolver = new CompanionResolver(engine, id => id == "warhorse" ? warhorse : null);
        var result = resolver.Build(paladin);

        // Master side
        Assert.Equal("race:companion_heavy_warhorse", result.MasterState.CompanionSlots[0].SelectedSpecies);
        Assert.Equal(5, result.MasterState.CompanionSlots[0].EffectiveLevel);

        // Companion side
        var mount = Assert.Single(result.Companions);
        Assert.Equal(5, mount.State.EffectiveMasterLevel);

        // Heavy warhorse base NA = 4; mount tier-5 adds +4 → 8.
        Assert.Equal(8, mount.State.NaturalArmor);

        // Heavy warhorse base Str = 18 (10 + 8); mount tier-5 adds +1 → 19.
        Assert.Equal(19, mount.State.AbilityScores.STR);

        // Mount tier-5 sets Int to 6.
        Assert.Equal(6, mount.State.AbilityScores.INT);

        // Tier-5 abilities present.
        Assert.Contains(mount.State.Abilities, a => a.Id == "mount_empathic_link");
        Assert.Contains(mount.State.Abilities, a => a.Id == "mount_improved_evasion");
        Assert.Contains(mount.State.Abilities, a => a.Id == "mount_share_spells");
        Assert.Contains(mount.State.Abilities, a => a.Id == "mount_share_saving_throws");

        // Higher-tier abilities NOT present.
        Assert.DoesNotContain(mount.State.Abilities, a => a.Id == "mount_improved_speed");
        Assert.DoesNotContain(mount.State.Abilities, a => a.Id == "mount_command");
        Assert.DoesNotContain(mount.State.Abilities, a => a.Id == "mount_spell_resistance");
    }

    [Fact]
    public void Paladin11_HeavyWarhorse_ScalesToCommandTier()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var paladin = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 10, CON = 12, INT = 10, WIS = 12, CHA = 14 },
            Ticks = Enumerable.Range(0, 11).Select(_ => new Tick { DriverId = "class:paladin" }).ToList(),
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "special_mount",
                    CompanionId = "warhorse",
                    EffectiveLevelFormula = new Formula("ClassLevel(paladin)")
                }
            }
        };
        var warhorse = new Character
        {
            RaceId = "race:companion_heavy_warhorse",
            TemplateIds = new List<string> { "template:special_mount_standard" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 4).Select(_ => new Tick { DriverId = "racial_hd:animal" }).ToList()
        };

        var result = new CompanionResolver(engine, _ => warhorse).Build(paladin);
        var mount = result.Companions[0];

        Assert.Equal(11, mount.State.EffectiveMasterLevel);
        // NA: base 4 + tier 5 (+4) + tier 8 (+2) + tier 11 (+2) = 12.
        Assert.Equal(12, mount.State.NaturalArmor);
        // Str: base 18 + 1 + 1 + 1 = 21.
        Assert.Equal(21, mount.State.AbilityScores.STR);
        // Int set last by tier 11 = 8.
        Assert.Equal(8, mount.State.AbilityScores.INT);
        Assert.Contains(mount.State.Abilities, a => a.Id == "mount_command");
        Assert.DoesNotContain(mount.State.Abilities, a => a.Id == "mount_spell_resistance");
    }

    [Fact]
    public void Ranger4_GrantsAnimalCompanionSlot_AtRangerMinus3()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var ranger = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 12, DEX = 14, CON = 10, INT = 10, WIS = 14, CHA = 10 },
            Ticks = Enumerable.Range(0, 6).Select(_ => new Tick { DriverId = "class:ranger" }).ToList()
        };

        var state = engine.Evaluate(ranger);

        var slot = Assert.Single(state.CompanionSlots);
        Assert.Equal("animal_companion", slot.LinkType);
        // Pure ranger 6: max(0, 0+6-3) = 3.
        Assert.Equal(3, slot.EffectiveLevel);
    }

    [Fact]
    public void Ranger3_NoAnimalCompanionSlotYet()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var ranger = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 12, DEX = 14, CON = 10, INT = 10, WIS = 14, CHA = 10 },
            Ticks = Enumerable.Range(0, 3).Select(_ => new Tick { DriverId = "class:ranger" }).ToList()
        };

        var state = engine.Evaluate(ranger);
        Assert.Empty(state.CompanionSlots);
    }

    [Fact]
    public void Druid3Ranger6_StacksAnimalCompanionLevel()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 14, CON = 12, INT = 10, WIS = 14, CHA = 10 },
            Ticks =
                Enumerable.Range(0, 3).Select(_ => new Tick { DriverId = "class:druid" })
                    .Concat(Enumerable.Range(0, 6).Select(_ => new Tick { DriverId = "class:ranger" }))
                    .ToList()
        };

        var state = engine.Evaluate(character);

        var slot = Assert.Single(state.CompanionSlots);
        // max(3, 3 + 6 - 3) = max(3, 6) = 6.
        Assert.Equal(6, slot.EffectiveLevel);
    }

    [Fact]
    public void Ranger6Druid3_StacksRegardlessOfOrder()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 14, CON = 12, INT = 10, WIS = 14, CHA = 10 },
            // Reverse order: ranger first, then druid.
            Ticks =
                Enumerable.Range(0, 6).Select(_ => new Tick { DriverId = "class:ranger" })
                    .Concat(Enumerable.Range(0, 3).Select(_ => new Tick { DriverId = "class:druid" }))
                    .ToList()
        };

        var state = engine.Evaluate(character);

        var slot = Assert.Single(state.CompanionSlots);
        // Same as above — order-independent thanks to max-formula on both grants.
        Assert.Equal(6, slot.EffectiveLevel);
    }

    [Fact]
    public void Druid5Ranger3_DruidLevelDominates()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // When ranger < 4 in PHB (no AC granted by ranger), the stacking formula
        // would give druid+ranger-3 = 5+3-3 = 5, which equals druid alone — ok either way.
        // When ranger == 1: max(5, 5+1-3=3) = 5 → druid wins. This is the right behavior:
        // adding a low-level ranger dip should not reduce the AC progression.
        var character = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 14, CON = 12, INT = 10, WIS = 14, CHA = 10 },
            Ticks =
                Enumerable.Range(0, 5).Select(_ => new Tick { DriverId = "class:druid" })
                    .Concat(Enumerable.Range(0, 1).Select(_ => new Tick { DriverId = "class:ranger" }))
                    .ToList()
        };

        var state = engine.Evaluate(character);
        var slot = Assert.Single(state.CompanionSlots);
        // max(5, 5+1-3=3) = 5.
        Assert.Equal(5, slot.EffectiveLevel);
    }

    // ---------- PR 5: Improved Familiar + Wild Cohort ----------

    [Fact]
    public void ImprovedFamiliar_Feat_QueuesImprovedSelectionPool()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // Wizard 7 — meets the highest-tier improved familiar CL prereq.
        // Take Improved Familiar at HD 6 standard feat slot.
        var wizard = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 16, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 7).Select(i =>
                i == 5
                    ? new Tick
                        {
                            DriverId = "class:wizard",
                            Choices = new TickChoices { FeatIds = new List<string> { "feat:improved_familiar" } }
                        }
                    : new Tick { DriverId = "class:wizard" }).ToList()
        };

        var state = engine.Evaluate(wizard);

        Assert.Contains("feat:improved_familiar", state.Feats);
        // Basic familiar pool from wizard L1 + improved familiar pool from the feat → both pending.
        Assert.Equal(1, state.PendingClassFeatureSelections["class_feature:familiar_options"]);
        Assert.Equal(1, state.PendingClassFeatureSelections["class_feature:improved_familiar_options"]);
    }

    [Fact]
    public void ImprovedFamiliar_Pseudodragon_ScalesAndBindsSelection()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // Wizard 7 with Improved Familiar; pick pseudodragon (CL 7 prereq, good alignment).
        var wizard = new Character
        {
            Name = "Aldric",
            Alignment = Alignment.NG,
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 16, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 7).Select(i =>
                i == 5
                    ? new Tick
                        {
                            DriverId = "class:wizard",
                            Choices = new TickChoices
                            {
                                FeatIds = new List<string> { "feat:improved_familiar" },
                                ClassFeatureChoices = new Dictionary<string, List<string>>
                                {
                                    ["class_feature:improved_familiar_options"] = new() { "race:familiar_pseudodragon" }
                                }
                            }
                        }
                    : new Tick { DriverId = "class:wizard" }).ToList(),
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "familiar",
                    CompanionId = "psd",
                    SelectedSpecies = "race:familiar_pseudodragon",
                    EffectiveLevelFormula = new Formula("CasterLevel(wizard) + CasterLevel(sorcerer)")
                }
            }
        };

        var pseudo = new Character
        {
            Name = "Verdant",
            RaceId = "race:familiar_pseudodragon",
            TemplateIds = new List<string> { "template:familiar_standard" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "racial_hd:dragon" },
                new() { DriverId = "racial_hd:dragon" }
            }
        };

        var result = new CompanionResolver(engine, _ => pseudo).Build(wizard);

        // Master side
        Assert.Equal(7, result.MasterState.CompanionSlots[0].EffectiveLevel);

        // Companion side
        var ps = Assert.Single(result.Companions);
        Assert.Equal(7, ps.State.EffectiveMasterLevel);
        // Pseudodragon base NA = 4; familiar tiers 1+3+5+7 add +1 each → 8.
        Assert.Equal(8, ps.State.NaturalArmor);
        // Familiar template SetAttribute INT 6→7→8→9 by tier 7. Last write wins.
        Assert.Equal(9, ps.State.AbilityScores.INT);
        // Tier-7 ability granted.
        Assert.Contains(ps.State.Abilities, a => a.Id == "fam_speak_with_animals");
        // Pseudodragon racial telepathy preserved.
        Assert.Contains(ps.State.Abilities, a => a.Id == "psd_telepathy");
    }

    [Fact]
    public void ImprovedFamiliarPick_WithoutFeat_Warns()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // Wizard 7 — does NOT take improved_familiar feat, but tries to select from
        // the improved pool. Should warn: no pending selection for improved_familiar_options.
        var wizard = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 16, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 7).Select(i =>
                i == 0
                    ? new Tick
                        {
                            DriverId = "class:wizard",
                            Choices = new TickChoices
                            {
                                ClassFeatureChoices = new Dictionary<string, List<string>>
                                {
                                    ["class_feature:improved_familiar_options"] = new() { "race:familiar_pseudodragon" }
                                }
                            }
                        }
                    : new Tick { DriverId = "class:wizard" }).ToList()
        };

        var state = engine.Evaluate(wizard);
        Assert.Contains(state.Warnings, w =>
            w.Message.Contains("class_feature:improved_familiar_options", StringComparison.OrdinalIgnoreCase)
            && w.Message.Contains("no pending", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WildCohort_Fighter6_GrantsCompanionSlotAtEffectiveLevel3()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var fighter = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 12, CON = 12, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 6).Select(i =>
                i == 5
                    ? new Tick
                        {
                            DriverId = "class:fighter",
                            Choices = new TickChoices { FeatIds = new List<string> { "feat:wild_cohort" } }
                        }
                    : new Tick { DriverId = "class:fighter" }).ToList()
        };

        var state = engine.Evaluate(fighter);

        Assert.Contains("feat:wild_cohort", state.Feats);
        var slot = Assert.Single(state.CompanionSlots);
        Assert.Equal("wild_cohort", slot.LinkType);
        Assert.Equal("feat:wild_cohort", slot.Granter);
        // max(1, 6 - 3) = 3.
        Assert.Equal(3, slot.EffectiveLevel);
        Assert.Equal(1, state.PendingClassFeatureSelections["class_feature:wild_cohort_options"]);
    }

    [Fact]
    public void WildCohort_Fighter1_FloorsAtEffectiveLevel1()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var fighter = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 12, CON = 12, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:fighter",
                    Choices = new TickChoices { FeatIds = new List<string> { "feat:wild_cohort" } }
                }
            }
        };

        var state = engine.Evaluate(fighter);

        var slot = Assert.Single(state.CompanionSlots);
        // max(1, 1 - 3) = max(1, -2) = 1.
        Assert.Equal(1, slot.EffectiveLevel);
    }

    [Fact]
    public void WildCohort_Fighter6_PicksWolf_FullPipeline()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var fighter = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 12, CON = 12, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 6).Select(i =>
                i == 5
                    ? new Tick
                        {
                            DriverId = "class:fighter",
                            Choices = new TickChoices
                            {
                                FeatIds = new List<string> { "feat:wild_cohort" },
                                ClassFeatureChoices = new Dictionary<string, List<string>>
                                {
                                    ["class_feature:wild_cohort_options"] = new() { "race:companion_wolf" }
                                }
                            }
                        }
                    : new Tick { DriverId = "class:fighter" }).ToList(),
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "wild_cohort",
                    CompanionId = "wolf",
                    SelectedSpecies = "race:companion_wolf",
                    EffectiveLevelFormula = new Formula("max(1, TotalHD - 3)")
                }
            }
        };

        // Wild Cohort uses the standard animal-companion progression per CAd.
        var wolf = new Character
        {
            Name = "Lupa",
            RaceId = "race:companion_wolf",
            TemplateIds = new List<string> { "template:animal_companion_standard" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "racial_hd:animal" },
                new() { DriverId = "racial_hd:animal" }
            }
        };

        var result = new CompanionResolver(engine, _ => wolf).Build(fighter);

        // Master side
        Assert.Equal("race:companion_wolf", result.MasterState.CompanionSlots[0].SelectedSpecies);
        Assert.Equal(3, result.MasterState.CompanionSlots[0].EffectiveLevel);

        // Companion side: AC progression at effective level 3 = tier 3.
        var w = Assert.Single(result.Companions);
        Assert.Equal(3, w.State.EffectiveMasterLevel);
        // Wolf base NA = 2; AC tier 3 adds +2 → 4.
        Assert.Equal(4, w.State.NaturalArmor);
        Assert.Contains(w.State.Abilities, a => a.Id == "ac_link");
        Assert.Contains(w.State.Abilities, a => a.Id == "ac_share_spells");
        Assert.Contains(w.State.Abilities, a => a.Id == "ac_evasion");
    }

    // ---------- PR 6: Level 4+ animal companion pool ----------

    [Fact]
    public void Druid7_PicksDireWolf_TierAbilitiesGranted()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // Druid 7 picks a tier-7 dire wolf. Effective AC level for a dire wolf
        // companion would normally be reduced by 6 in PHB scoring, but the engine
        // treats minEffectiveLevel as a hint only — the slot's effective level is
        // still ClassLevel(druid) = 7. The companion file is responsible for
        // adjusting its own ticks to match the intended power tier.
        var druid = new Character
        {
            Name = "Wynn",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 14, CHA = 10 },
            Ticks = Enumerable.Range(0, 7).Select(i =>
                i == 0
                    ? new Tick
                        {
                            DriverId = "class:druid",
                            Choices = new TickChoices
                            {
                                ClassFeatureChoices = new Dictionary<string, List<string>>
                                {
                                    ["class_feature:animal_companion_options"] = new() { "race:companion_dire_wolf" }
                                }
                            }
                        }
                    : new Tick { DriverId = "class:druid" }).ToList(),
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "animal_companion",
                    CompanionId = "fenris",
                    SelectedSpecies = "race:companion_dire_wolf",
                    EffectiveLevelFormula = new Formula("max(ClassLevel(druid), ClassLevel(druid) + ClassLevel(ranger) - 3)")
                }
            }
        };

        // Dire wolf has 6 base HD per SRD.
        var direWolf = new Character
        {
            Name = "Fenris",
            RaceId = "race:companion_dire_wolf",
            TemplateIds = new List<string> { "template:animal_companion_standard" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 6).Select(_ => new Tick { DriverId = "racial_hd:animal" }).ToList()
        };

        var result = new CompanionResolver(engine, _ => direWolf).Build(druid);

        Assert.Equal("race:companion_dire_wolf", result.MasterState.CompanionSlots[0].SelectedSpecies);
        Assert.Equal(7, result.MasterState.CompanionSlots[0].EffectiveLevel);

        var fen = Assert.Single(result.Companions);
        Assert.Equal(7, fen.State.EffectiveMasterLevel);
        // Dire wolf base NA = 4; AC tiers 3 (+2) + 6 (+2) = +4 → 8.
        Assert.Equal(8, fen.State.NaturalArmor);
        Assert.Contains(fen.State.Abilities, a => a.Id == "ac_devotion"); // tier 6
        Assert.Contains(fen.State.Abilities, a => a.Id == "direwolf_trip"); // racial
        Assert.DoesNotContain(fen.State.Abilities, a => a.Id == "ac_multiattack"); // tier 9 not yet
    }

    [Fact]
    public void Druid12_DireBear_ScalesThroughMidTier()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var druid = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 16, CHA = 10 },
            Ticks = Enumerable.Range(0, 12).Select(_ => new Tick { DriverId = "class:druid" }).ToList(),
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "animal_companion",
                    CompanionId = "ursus",
                    EffectiveLevelFormula = new Formula("max(ClassLevel(druid), ClassLevel(druid) + ClassLevel(ranger) - 3)")
                }
            }
        };

        // Dire bear has 12 base HD per SRD.
        var direBear = new Character
        {
            RaceId = "race:companion_bear_dire",
            TemplateIds = new List<string> { "template:animal_companion_standard" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 12).Select(_ => new Tick { DriverId = "racial_hd:animal" }).ToList()
        };

        var result = new CompanionResolver(engine, _ => direBear).Build(druid);
        var bear = result.Companions[0];

        Assert.Equal(12, bear.State.EffectiveMasterLevel);
        // Dire bear base NA = 7; AC tiers 3 (+2) + 6 (+2) + 9 (+2) + 12 (+2) = +8 → 15.
        Assert.Equal(15, bear.State.NaturalArmor);
        // Bonus tricks counter: 4 tricks at tier 12.
        Assert.Equal(4, bear.State.Counters["ac_bonus_tricks"]);
        // Multiattack granted at tier 9.
        Assert.Contains(bear.State.Abilities, a => a.Id == "ac_multiattack");
        // Improved evasion (tier 15) NOT yet.
        Assert.DoesNotContain(bear.State.Abilities, a => a.Id == "ac_improved_evasion");
        // Racial signature preserved.
        Assert.Contains(bear.State.Abilities, a => a.Id == "direbear_improved_grab");
    }

    [Fact]
    public void Druid20_DireTiger_TopTierAllAbilities()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // Druid 20 — full PHB AC progression should fire (tiers 1, 3, 6, 9, 12, 15, 18).
        var druid = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 18, CHA = 10 },
            Ticks = Enumerable.Range(0, 20).Select(_ => new Tick { DriverId = "class:druid" }).ToList(),
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "animal_companion",
                    CompanionId = "shere",
                    EffectiveLevelFormula = new Formula("max(ClassLevel(druid), ClassLevel(druid) + ClassLevel(ranger) - 3)")
                }
            }
        };

        // Dire tiger: 16 base HD.
        var direTiger = new Character
        {
            RaceId = "race:companion_tiger_dire",
            TemplateIds = new List<string> { "template:animal_companion_standard" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 16).Select(_ => new Tick { DriverId = "racial_hd:animal" }).ToList()
        };

        var result = new CompanionResolver(engine, _ => direTiger).Build(druid);
        var dt = result.Companions[0];

        Assert.Equal(20, dt.State.EffectiveMasterLevel);
        // Dire tiger base NA = 6; +2 each at 3/6/9/12/15/18 = +12 → 18.
        Assert.Equal(18, dt.State.NaturalArmor);
        // Bonus tricks: 6 at tier 18.
        Assert.Equal(6, dt.State.Counters["ac_bonus_tricks"]);
        // All AC abilities granted.
        Assert.Contains(dt.State.Abilities, a => a.Id == "ac_link");
        Assert.Contains(dt.State.Abilities, a => a.Id == "ac_share_spells");
        Assert.Contains(dt.State.Abilities, a => a.Id == "ac_evasion");
        Assert.Contains(dt.State.Abilities, a => a.Id == "ac_devotion");
        Assert.Contains(dt.State.Abilities, a => a.Id == "ac_multiattack");
        Assert.Contains(dt.State.Abilities, a => a.Id == "ac_improved_evasion");
        // Racial signature preserved.
        Assert.Contains(dt.State.Abilities, a => a.Id == "diretiger_pounce");
        Assert.Contains(dt.State.Abilities, a => a.Id == "diretiger_rake");
    }

    [Fact]
    public void Druid20_Elephant_HugeCompanionScalingWorks()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var druid = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 18, CHA = 10 },
            Ticks = Enumerable.Range(0, 20).Select(_ => new Tick { DriverId = "class:druid" }).ToList(),
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "animal_companion",
                    CompanionId = "ele",
                    EffectiveLevelFormula = new Formula("max(ClassLevel(druid), ClassLevel(druid) + ClassLevel(ranger) - 3)")
                }
            }
        };

        // Elephant has 11 HD per SRD; Huge size.
        var elephant = new Character
        {
            RaceId = "race:companion_elephant",
            TemplateIds = new List<string> { "template:animal_companion_standard" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 11).Select(_ => new Tick { DriverId = "racial_hd:animal" }).ToList()
        };

        var result = new CompanionResolver(engine, _ => elephant).Build(druid);
        var ele = result.Companions[0];

        Assert.Equal(Size.Huge, ele.State.Size);
        // Elephant base Str: 10 + 20 = 30; AC tiers 3,6,9,12,15,18 add +1 each = +6 → 36.
        Assert.Equal(36, ele.State.AbilityScores.STR);
        Assert.Contains(ele.State.Abilities, a => a.Id == "elephant_trample");
    }

    [Fact]
    public void Druid20_StackingFormula_OrderIndependentWithRanger()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // Sanity: druid 17 / ranger 4 → max(17, 17+4-3) = 18 effective AC level.
        // (ECL exceeds 20, but tests the formula at high levels.)
        var character = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 16, CHA = 10 },
            Ticks =
                Enumerable.Range(0, 17).Select(_ => new Tick { DriverId = "class:druid" })
                    .Concat(Enumerable.Range(0, 4).Select(_ => new Tick { DriverId = "class:ranger" }))
                    .ToList()
        };

        var state = engine.Evaluate(character);
        var slot = Assert.Single(state.CompanionSlots);
        Assert.Equal(18, slot.EffectiveLevel);
    }

    [Fact]
    public void Wizard9_FamiliarScales_HigherTierAbilities()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var wizard = new Character
        {
            Name = "Vex",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 16, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 9).Select(_ => new Tick { DriverId = "class:wizard" }).ToList(),
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "familiar",
                    CompanionId = "owl-familiar",
                    SelectedSpecies = "race:familiar_owl",
                    EffectiveLevelFormula = new Formula("CasterLevel(wizard) + CasterLevel(sorcerer)")
                }
            }
        };

        var owl = new Character
        {
            Name = "Hoot",
            RaceId = "race:familiar_owl",
            TemplateIds = new List<string> { "template:familiar_standard" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick> { new() { DriverId = "racial_hd:animal" } }
        };

        var resolver = new CompanionResolver(engine, id => id == "owl-familiar" ? owl : null);
        var result = resolver.Build(wizard);

        Assert.Equal(9, result.MasterState.CompanionSlots[0].EffectiveLevel);

        var owlBuild = Assert.Single(result.Companions);
        Assert.Equal(9, owlBuild.State.EffectiveMasterLevel);

        // Familiar template fires tiers 1, 3, 5, 7, 9 → NA delta = 5; owl base NA = 1; total = 6.
        Assert.Equal(6, owlBuild.State.NaturalArmor);
        // INT scales to 10 by tier 9 (set 6 → 7 → 8 → 9 → 10).
        Assert.Equal(10, owlBuild.State.AbilityScores.INT);
        // Tier 7 grants Speak with Animals of Its Kind.
        Assert.Contains(owlBuild.State.Abilities, a => a.Id == "fam_speak_with_animals");
        // SR tier (11) NOT yet reached.
        Assert.DoesNotContain(owlBuild.State.Abilities, a => a.Id == "fam_spell_resistance");
    }
}
