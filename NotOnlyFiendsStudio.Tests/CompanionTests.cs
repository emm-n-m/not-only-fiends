using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.PcGen;

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

    /// <summary>
    /// SRD: a druid's animal companion advances on her full level; "a ranger's effective druid
    /// level is one-half his ranger level", and the ability is gained at 4th level, so ranger
    /// levels below that contribute nothing.
    /// </summary>
    [Theory]
    // Druid: full level.
    [InlineData(1, 0, 0, 1)]
    [InlineData(5, 0, 0, 5)]
    [InlineData(20, 0, 0, 20)]
    // Ranger: nothing before 4th, half thereafter. The old expression counted ranger levels
    // one-for-one past 3rd, which put a ranger 20's companion at 17 instead of 10.
    [InlineData(0, 1, 0, 0)]
    [InlineData(0, 3, 0, 0)]
    [InlineData(0, 4, 0, 2)]
    [InlineData(0, 5, 0, 2)]
    [InlineData(0, 6, 0, 3)]
    [InlineData(0, 20, 0, 10)]
    // Planar ranger is a ranger for this purpose — Vzraella's snake resolved to level 0 and
    // gained no scaling at all while variant levels went uncounted.
    [InlineData(0, 0, 3, 0)]
    [InlineData(0, 0, 4, 2)]
    [InlineData(0, 0, 12, 6)]
    // Variants combine with max, not sum — a template that boosts "ranger level" grants its rule
    // to every ranger id, so summing would count the same bonus once per variant. Levels split
    // across two ranger variants therefore count only as the larger.
    [InlineData(0, 2, 2, 0)]
    [InlineData(0, 6, 4, 3)]
    // Druid and ranger levels stack.
    [InlineData(3, 6, 0, 6)]
    public void AnimalCompanionLevel_FollowsTheDruidAndHalfRangerRule(
        int druidLevels, int rangerLevels, int planarRangerLevels, int expected)
    {
        var master = new CharacterState();
        if (druidLevels > 0) master.ClassLevels["class:druid"] = druidLevels;
        if (rangerLevels > 0) master.ClassLevels["class:ranger"] = rangerLevels;
        if (planarRangerLevels > 0) master.ClassLevels["class:planar_ranger"] = planarRangerLevels;

        var actual = new Formula(CompanionResolver.AnimalCompanionLevelExpression).Evaluate(master);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// An animal companion is a class ability, so anything that raises the class level *for
    /// abilities* advances it. Vzraella's shape: planar ranger 3 with the Unseelie Champion
    /// template adding her 9 outsider HD to ranger level — which is why she has three favoured
    /// enemies and full combat style mastery. Effective ranger level 12, so the companion
    /// advances as a 6th-level druid's, not as a 3rd-level ranger's (which grants nothing).
    /// </summary>
    [Fact]
    public void AnimalCompanionLevel_CountsEffectiveLevelsGrantedByTemplates()
    {
        var master = new CharacterState();
        master.ClassLevels["class:planar_ranger"] = 3;
        // The template grants the bonus to both ranger ids, as the real one does.
        foreach (var rangerId in new[] { "class:ranger", "class:planar_ranger" })
        {
            master.EffectiveLevelRules.Add(new EffectiveLevelRule
            {
                TargetDriverId = rangerId,
                BonusFormula = new Formula("RacialHD()")
            });
        }
        master.HDList.AddRange(Enumerable.Repeat("racial_hd:outsider", 9));
        master.HDList.AddRange(Enumerable.Repeat("class:planar_ranger", 3));

        var formula = new Formula(CompanionResolver.AnimalCompanionLevelExpression);

        Assert.Equal(12, new Formula("EffectiveClassLevel(planar_ranger)").Evaluate(master));
        // Raw class level is untouched — only the ability-facing accessor stacks the bonus.
        Assert.Equal(3, new Formula("ClassLevel(planar_ranger)").Evaluate(master));
        Assert.Equal(6, formula.Evaluate(master));

        // The template grants its rule to class:ranger as well, since it cannot know which ranger
        // variant the character took. Combining the two with max keeps that from counting the
        // nine outsider HD twice, which would advance the companion as a 10th-level druid's.
        Assert.Equal(9, new Formula("EffectiveClassLevel(ranger)").Evaluate(master));
    }

    /// <summary>
    /// Casting as an Nth-level druid is not being one. A nymph casts as a 7th-level druid, and
    /// that grant registers an effective-level rule so class levels stack onto her caster level —
    /// but it must not advance her animal companion. The Nymph Archdruid is druid 6, so her
    /// companion advances as a 6th-level druid's, not a 13th's.
    /// </summary>
    [Fact]
    public void AnimalCompanionLevel_IgnoresRacialSpellcastingGrants()
    {
        var master = new CharacterState();
        master.ClassLevels["class:druid"] = 6;
        master.EffectiveLevelRules.Add(new EffectiveLevelRule
        {
            TargetDriverId = "class:druid",
            BonusFormula = new Formula("7"),
            Scope = EffectiveLevelScope.SpellcastingOnly
        });

        Assert.Equal(6, new Formula("EffectiveClassLevel(druid)").Evaluate(master));
        Assert.Equal(6, new Formula(CompanionResolver.AnimalCompanionLevelExpression).Evaluate(master));
    }

    /// <summary>
    /// The SRD alternative animal companion lists, transcribed from the druid page. The
    /// adjustment is a property of the species, applied to the master's effective druid level
    /// before any companion scaling is read.
    /// </summary>
    [Theory]
    [InlineData("race:companion_wolf", 0)]           // base list
    [InlineData("race:companion_snake_viper_medium", 0)]
    [InlineData("race:companion_snake_viper_large", -3)]  // 4th level or higher
    [InlineData("race:companion_leopard", -3)]
    [InlineData("race:companion_wolverine", -3)]
    [InlineData("race:companion_dire_wolf", -6)]         // 7th level or higher
    [InlineData("race:companion_tiger", -6)]
    [InlineData("race:companion_lion", -6)]
    [InlineData("race:companion_bear_polar", -9)]        // 10th level or higher
    [InlineData("race:companion_dire_lion", -9)]
    [InlineData("race:companion_bear_dire", -12)]        // 13th level or higher
    [InlineData("race:companion_elephant", -12)]
    [InlineData("race:companion_tiger_dire", -15)]       // 16th level or higher
    public void CompanionRaces_CarryTheirAlternativeListAdjustment(string raceId, int expected)
    {
        var registry = TestContentHelper.LoadAllPacks();
        Assert.Equal(expected, registry.GetRace(raceId).CompanionLevelModifier);
    }

    /// <summary>
    /// Vzraella end to end: planar ranger 3, Unseelie Champion adding her 9 outsider HD, and a
    /// Large viper. Effective ranger level 12 → effective druid level 6 → fielded at 6 − 3 = 3,
    /// which is the tier her imported snake was actually built at.
    /// </summary>
    [Fact]
    public void AdvancedCompanion_AppliesSpeciesAdjustmentToTheMastersEffectiveLevel()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var snake = new Character
        {
            Name = "Pet Snake",
            RaceId = "race:companion_snake_viper_large",
            TemplateIds = new List<string> { "template:animal_companion_standard" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 3).Select(_ => new Tick { DriverId = "racial_hd:animal" }).ToList(),
            CompanionOrigin = new CompanionOrigin
            {
                LinkType = "animal_companion",
                MasterCharacterId = "vzraella",
                EffectiveMasterLevel = 6
            }
        };

        var state = engine.Evaluate(snake);

        Assert.Equal(3, state.EffectiveMasterLevel);
        // Tier 3 fires, tier 6 does not.
        Assert.Contains(state.Abilities, a => a.Id == "ac_evasion");
        Assert.DoesNotContain(state.Abilities, a => a.Id == "ac_devotion");
        Assert.Equal(1, state.Counters["ac_bonus_tricks"]);
    }

    /// <summary>
    /// A save written before the fix still carries the old expression. It is migrated at replay
    /// time — the same treatment the legacy familiar formula gets — so existing characters are
    /// corrected without a re-import.
    /// </summary>
    [Fact]
    public void CompanionResolver_MigratesLegacyAnimalCompanionFormula()
    {
        var registry = BuildBasicRegistry();
        registry.RegisterDriver(BuildDruidDriver());

        var companion = new Character
        {
            Name = "Snake",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 2, WIS = 10, CHA = 6 },
            Ticks = new List<Tick> { new() { DriverId = "class:druid" } }
        };

        var master = new Character
        {
            Name = "Ranger",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 14, CHA = 10 },
            Ticks = Enumerable.Range(0, 8).Select(_ => new Tick { DriverId = "class:druid" }).ToList(),
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "animal_companion",
                    CompanionId = "snake",
                    // The pre-fix expression, as written into saves by older imports.
                    EffectiveLevelFormula = new Formula(
                        "max(ClassLevel(druid), ClassLevel(druid) + ClassLevel(ranger) - 3)")
                }
            }
        };

        var resolver = new CompanionResolver(new ReplayStudio(registry), id => id == "snake" ? companion : null);
        var result = resolver.Build(master);

        // Eight druid levels: both expressions agree at 8, so the migration is safe for druids.
        Assert.Equal(8, result.Companions[0].State.EffectiveMasterLevel);
    }

    /// <summary>
    /// Vzraella's shape: a planar ranger 3 with an animal companion. Rangers gain the ability at
    /// 4th, so the link resolves to level 0 and the companion gains nothing from it. The companion
    /// still builds — the save records a character that was played — but the master must say so.
    /// </summary>
    [Fact]
    public void CompanionResolver_LinkTheMasterDoesNotQualifyFor_Warns()
    {
        var registry = BuildBasicRegistry();
        registry.RegisterDriver(BuildDruidDriver());

        var snake = new Character
        {
            Name = "Snake",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 2, WIS = 10, CHA = 6 },
            Ticks = new List<Tick> { new() { DriverId = "class:druid" } }
        };

        // No druid or ranger levels at all, so the animal companion expression yields 0.
        var master = new Character
        {
            Name = "Vzraella",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>(),
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "animal_companion",
                    CompanionId = "snake",
                    EffectiveLevelFormula = new Formula(CompanionResolver.AnimalCompanionLevelExpression)
                }
            }
        };

        var resolver = new CompanionResolver(new ReplayStudio(registry), id => id == "snake" ? snake : null);
        var result = resolver.Build(master);

        // The companion is still built, not dropped.
        Assert.Single(result.Companions);
        Assert.Equal(0, result.Companions[0].State.EffectiveMasterLevel);

        var warning = Assert.Single(
            result.MasterState.Warnings, w => w.Message.Contains("does not qualify for it"));
        Assert.Contains("animal_companion", warning.Message);
        Assert.Contains("snake", warning.Message);
    }

    /// <summary>A master who does qualify draws no such warning.</summary>
    [Fact]
    public void CompanionResolver_QualifyingMaster_DoesNotWarn()
    {
        var registry = BuildBasicRegistry();
        registry.RegisterDriver(BuildDruidDriver());

        var snake = new Character
        {
            Name = "Snake",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 2, WIS = 10, CHA = 6 },
            Ticks = new List<Tick> { new() { DriverId = "class:druid" } }
        };

        var master = new Character
        {
            Name = "Druid",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 14, CHA = 10 },
            Ticks = Enumerable.Range(0, 4).Select(_ => new Tick { DriverId = "class:druid" }).ToList(),
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "animal_companion",
                    CompanionId = "snake",
                    EffectiveLevelFormula = new Formula(CompanionResolver.AnimalCompanionLevelExpression)
                }
            }
        };

        var resolver = new CompanionResolver(new ReplayStudio(registry), id => id == "snake" ? snake : null);
        var result = resolver.Build(master);

        Assert.Equal(4, result.Companions[0].State.EffectiveMasterLevel);
        Assert.DoesNotContain(result.MasterState.Warnings, w => w.Message.Contains("does not qualify"));
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
        // SRD Leadership table: score 9 → cohort level 6th, held to 5th by "two or more levels
        // lower than himself" for a 7th-level character.
        Assert.Equal(5, state.MaxCohortLevel);
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
        // Base table's last row is "25 or higher — 17th — 135 13 7 4 2 2". Without Epic
        // Leadership the table stops there however high the score goes.
        var counts = LeadershipTables.LookupFollowerCounts(30);
        Assert.Equal(135, counts.Level1);
        Assert.Equal(13, counts.Level2);
        Assert.Equal(7, counts.Level3);
        Assert.Equal(4, counts.Level4);
        Assert.Equal(2, counts.Level5);
        Assert.Equal(2, counts.Level6);
        Assert.Equal(0, counts.At(7));
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

    /// <summary>
    /// SRD wizard.html, Familiar Special: "Toad — Master gains +3 hit points" and
    /// "Rat — Master gains a +2 bonus on Fortitude saves". The benefit runs from familiar to
    /// master, which nothing in the engine did before.
    /// </summary>
    [Fact]
    public void CompanionResolver_Familiar_GrantsItsSpecialAbilityToTheMaster()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        Character Master(string companionId) => new()
        {
            Name = "Master",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 12, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:wizard", Choices = new TickChoices { HitPointsRolled = 4 } }
            },
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "familiar",
                    CompanionId = companionId,
                    EffectiveLevelFormula = new Formula("ClassLevel(wizard)")
                }
            }
        };

        Character Animal(string raceId) => new()
        {
            Name = raceId,
            RaceId = raceId,
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick> { new() { DriverId = "racial_hd:animal" } }
        };

        var unbound = engine.Evaluate(Master("none"));

        var withToad = new CompanionResolver(engine, _ => Animal("race:familiar_toad"))
            .Build(Master("toad")).MasterState;
        Assert.Equal(unbound.HP + 3, withToad.HP);

        var withRat = new CompanionResolver(engine, _ => Animal("race:familiar_rat"))
            .Build(Master("rat")).MasterState;
        Assert.Equal(unbound.EffectiveSaves.Fort + 2, withRat.EffectiveSaves.Fort);
        Assert.Equal(unbound.EffectiveSaves.Ref, withRat.EffectiveSaves.Ref);

        var withCat = new CompanionResolver(engine, _ => Animal("race:familiar_cat"))
            .Build(Master("cat")).MasterState;
        Assert.Equal(3, withCat.SkillBonuses["skill:move_silently"]);
        Assert.Equal(3, withCat.SkillTotals["skill:move_silently"]);
    }

    /// <summary>
    /// The special is a familiar benefit, so the same animal taken as an animal companion gives
    /// its master nothing.
    /// </summary>
    [Fact]
    public void CompanionResolver_AnimalCompanion_DoesNotGrantTheFamiliarSpecial()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);
        var master = new Character
        {
            Name = "Druid",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 12, WIS = 12, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:druid", Choices = new TickChoices { HitPointsRolled = 4 } }
            },
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "animal_companion",
                    CompanionId = "toad",
                    EffectiveLevelFormula = new Formula("ClassLevel(druid)")
                }
            }
        };
        var toad = new Character
        {
            Name = "Toad",
            RaceId = "race:familiar_toad",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick> { new() { DriverId = "racial_hd:animal" } }
        };

        var bound = new CompanionResolver(engine, _ => toad).Build(master).MasterState;
        var unbound = engine.Evaluate(new Character
        {
            Name = "Druid",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 12, WIS = 12, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:druid", Choices = new TickChoices { HitPointsRolled = 4 } }
            }
        });

        Assert.Equal(unbound.HP, bound.HP);
    }

    /// <summary>
    /// SRD familiar skills: "For each skill in which either the master or the familiar has ranks,
    /// use either the normal skill ranks for an animal of that type or the master's skill ranks,
    /// whichever are better. In either case, the familiar uses its own ability modifiers."
    ///
    /// Per-skill maximum, not wholesale replacement — the familiar keeps whatever it is better at,
    /// which is what it would use if dismissed.
    /// </summary>
    [Fact]
    public void CompanionResolver_Familiar_TakesTheBetterOfItsOwnAndItsMastersSkillRanks()
    {
        var registry = TestContentHelper.LoadAllPacks();

        // Master: a rogue with 8 ranks of Hide and 1 of Listen, and a Dexterity of 10 (+0) so the
        // familiar's own modifier is visibly different from the master's.
        var master = new Character
        {
            Name = "Master",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 12, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:rogue",
                    Choices = new TickChoices
                    {
                        SkillAllocations = new List<SkillAllocation>
                        {
                            new() { SkillId = "skill:hide", HalfRanks = 16 },
                            new() { SkillId = "skill:listen", HalfRanks = 2 },
                        }
                    }
                }
            },
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "familiar",
                    CompanionId = "toad",
                    EffectiveLevelFormula = new Formula("ClassLevel(rogue)")
                }
            }
        };

        // Familiar: a toad with 1 rank of Hide (worse than the master) and 2 of Spot, which the
        // master does not have at all.
        var familiar = new Character
        {
            Name = "Toad",
            RaceId = "race:familiar_toad",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 14, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "racial_hd:animal",
                    Choices = new TickChoices
                    {
                        SkillAllocations = new List<SkillAllocation>
                        {
                            new() { SkillId = "skill:hide", HalfRanks = 2 },
                            new() { SkillId = "skill:spot", HalfRanks = 4 },
                        }
                    }
                }
            }
        };

        var result = new CompanionResolver(new ReplayStudio(registry), _ => familiar).Build(master);
        var familiarState = Assert.Single(result.Companions).State;

        // Master is better at Hide (8 ranks vs 1) — the familiar takes the master's.
        Assert.Equal(16, familiarState.SkillHalfRanks["skill:hide"]);
        // Familiar is better at Spot (2 ranks vs none) — it keeps its own.
        Assert.Equal(4, familiarState.SkillHalfRanks["skill:spot"]);
        // A skill only the master has ranks in still transfers.
        Assert.Equal(2, familiarState.SkillHalfRanks["skill:listen"]);

        // "In either case, the familiar uses its own ability modifiers": the toad's Dexterity is
        // 14 base +2 racial = 16 (+3), the master's is 10 (+0), so the borrowed Hide ranks are
        // totalled with the toad's modifier.
        var toadDexMod = AbilityScoreSet.Modifier(familiarState.AbilityScores.DEX);
        Assert.Equal(3, toadDexMod);
        // Plus the toad's Diminutive +12 Hide size bonus, which the master (Medium) does not get.
        Assert.Equal(Size.Diminutive, familiarState.Size);
        Assert.Equal(
            8 + toadDexMod + 12 + familiarState.SkillBonuses.GetValueOrDefault("skill:hide")
              + familiarState.SkillSynergyBonuses.GetValueOrDefault("skill:hide"),
            familiarState.SkillTotals["skill:hide"]);
        Assert.NotEqual(result.MasterState.SkillTotals["skill:hide"], familiarState.SkillTotals["skill:hide"]);
    }

    [Theory]
    [InlineData("familiar")]
    [InlineData("improved_familiar")]
    public void CompanionResolver_Familiar_InheritsMasterHpBabAndProgressionSaves(string linkType)
    {
        var registry = BuildBasicRegistry();
        registry.RegisterDriver(BuildFighterDriver());
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.RacialHD,
            Id = "racial_hd:test_familiar",
            Name = "Familiar HD",
            HitDie = 8,
            SkillPointsPerLevel = 2,
            BABProgression = BABProgression.Average,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Poor,
                Ref = ProgressionRate.Good,
                Will = ProgressionRate.Poor
            }
        });
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:test_familiar",
            Name = "Test Familiar",
            Type = CreatureType.MagicalBeast,
            Size = Size.Small,
            RacialHDDriverId = "racial_hd:test_familiar",
            RacialPermabuffs = new List<Permabuff>
            {
                new ModifyAttribute { Target = AttributeTarget.AllSaves, Value = 1 }
            }
        });

        var master = new Character
        {
            Name = "Master",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 14, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 4).Select(_ => new Tick
            {
                DriverId = "class:fighter",
                Choices = new TickChoices { HitPointsRolled = 5 }
            }).ToList(),
            PermanentEvents = new List<PermanentEvent>
            {
                new()
                {
                    BeforeTick = 0,
                    Permabuffs = new List<Permabuff>
                    {
                        new ModifyAttribute { Target = AttributeTarget.AllSaves, Value = 5 }
                    }
                }
            },
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = linkType,
                    CompanionId = "familiar",
                    EffectiveLevelFormula = new Formula("ClassLevel(wizard) + ClassLevel(sorcerer)")
                }
            }
        };
        var familiar = new Character
        {
            Name = "Familiar",
            RaceId = "race:test_familiar",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 18, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 2).Select(_ => new Tick
            {
                DriverId = "racial_hd:test_familiar",
                Choices = new TickChoices { HitPointsRolled = 8 }
            }).ToList()
        };

        var result = new CompanionResolver(new ReplayStudio(registry), _ => familiar).Build(master);
        var familiarState = Assert.Single(result.Companions).State;

        Assert.Equal(28, result.MasterState.HP);
        Assert.Equal(14, familiarState.HP);
        Assert.Equal(result.MasterState.BaseBAB, familiarState.EffectiveBAB);
        Assert.Equal(9, result.MasterState.BaseSaves.Fort); // progression 4 + master's own +5 bonus
        Assert.Equal(5, familiarState.BaseSaves.Fort); // master's progression 4 + familiar's own +1
        Assert.Equal(2, familiarState.BaseSaves.Ref);  // master's progression 1 + familiar's own +1
        Assert.Equal(2, familiarState.BaseSaves.Will);
        Assert.Equal(6, familiarState.EffectiveSaves.Ref); // base 2 + familiar Dex modifier 4
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

    /// <summary>
    /// Celestial and Fiendish share one progression table:
    ///
    ///   HD 1–3   resistance 5,  no DR
    ///   HD 4–7   resistance 5,  DR 5/magic
    ///   HD 8–11  resistance 10, DR 5/magic
    ///   HD 12+   resistance 10, DR 10/magic
    ///
    /// with "Spell resistance equal to the creature's HD + 5 (maximum 25)".
    /// </summary>
    [Theory]
    [InlineData("template:celestial", 3, 5, 0, 8)]
    [InlineData("template:celestial", 4, 5, 5, 9)]
    [InlineData("template:celestial", 8, 10, 5, 13)]
    [InlineData("template:celestial", 12, 10, 10, 17)]
    [InlineData("template:celestial", 25, 10, 10, 25)]   // SR capped
    [InlineData("template:fiendish", 3, 5, 0, 8)]
    [InlineData("template:fiendish", 8, 10, 5, 13)]
    [InlineData("template:fiendish", 25, 10, 10, 25)]
    public void CelestialAndFiendish_ShareTheSameProgressionTable(
        string templateId, int hitDice, int resistance, int damageReduction, int spellResistance)
    {
        var registry = TestContentHelper.LoadAllPacks();
        var element = templateId == "template:celestial" ? "acid" : "fire";

        var state = new ReplayStudio(registry).Evaluate(new Character
        {
            Name = "Templated animal",
            RaceId = "race:companion_snake_viper_tiny",
            Alignment = Alignment.N,
            TemplateIds = new List<string> { templateId },
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, hitDice)
                .Select(_ => new Tick { DriverId = "racial_hd:animal" }).ToList()
        });

        Assert.Equal(resistance, state.Resistances.GetValueOrDefault(element));
        Assert.Equal(damageReduction, state.DamageReduction.FirstOrDefault()?.Value ?? 0);
        Assert.Equal(spellResistance, state.SpellResistance);
        // "Abilities: Same as the base creature, but Intelligence is at least 3." A viper has 1.
        Assert.Equal(3, state.AbilityScores.INT);
    }

    /// <summary>
    /// The celestial counterpart of template:fiendish, so a nonevil planar ranger has a companion
    /// to take. Structurally identical apart from the element set, the smite, and the base-type
    /// list — the celestial one omits ooze, which the fiendish one allows.
    /// </summary>
    [Fact]
    public void CelestialTemplate_MirrorsFiendishWithItsOwnElementsAndSmite()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        Character Snake(params string[] templates) => new()
        {
            Name = "Snake",
            RaceId = "race:companion_snake_viper_tiny",
            Alignment = Alignment.NG,
            TemplateIds = templates.ToList(),
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 4)
                .Select(_ => new Tick { DriverId = "racial_hd:animal" }).ToList()
        };

        var celestial = engine.Evaluate(Snake("template:celestial", "template:celestial_animal"));

        // Animal → magical beast, the same shift that makes a celestial companion illegal for
        // anyone but a planar ranger.
        Assert.Equal(CreatureType.MagicalBeast, celestial.Type);
        Assert.Contains("augmented", celestial.Subtypes);
        Assert.Equal(2, celestial.LevelAdjustment);

        // Acid, cold and electricity — where fiendish takes cold and fire.
        Assert.Equal(5, celestial.Resistances["acid"]);
        Assert.Equal(5, celestial.Resistances["cold"]);
        Assert.Equal(5, celestial.Resistances["electricity"]);
        Assert.False(celestial.Resistances.ContainsKey("fire"));

        Assert.Contains(celestial.DamageReduction, dr => dr.Value == 5 && dr.BypassedBy == "magic");
        Assert.Equal(celestial.TotalHD + 5, celestial.SpellResistance);
        Assert.Contains(celestial.SpecialAttacks, a => a.Name == "Smite Evil");

        var fiendish = engine.Evaluate(Snake("template:fiendish", "template:fiendish_animal"));
        Assert.Contains(fiendish.SpecialAttacks, a => a.Name == "Smite Good");
        Assert.Equal(celestial.LevelAdjustment, fiendish.LevelAdjustment);
        Assert.Equal(celestial.SpellResistance, fiendish.SpellResistance);
    }

    /// <summary>An evil creature cannot carry the celestial template.</summary>
    [Fact]
    public void CelestialTemplate_IsRefusedToAnEvilCreature()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var state = new ReplayStudio(registry).Evaluate(new Character
        {
            Name = "Snake",
            RaceId = "race:companion_snake_viper_tiny",
            Alignment = Alignment.NE,
            TemplateIds = new List<string> { "template:celestial" },
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick> { new() { DriverId = "racial_hd:animal" } }
        });

        Assert.Contains(state.Warnings, w =>
            w.Message.Contains("Celestial", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// An animal companion must be an animal. The celestial and fiendish templates move the type to
    /// magical beast, so a druid or ordinary ranger cannot field one — the planar ranger's "may have
    /// a celestial/fiendish version" is the exception that makes it legal, and only in the direction
    /// its alignment allows.
    /// </summary>
    [Fact]
    public void ANonAnimalCompanionIsRejectedUnlessTheMasterIsAPlanarRanger()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        Character Master(string classId, int levels, Alignment alignment) => new()
        {
            Name = classId,
            RaceId = "race:human",
            Alignment = alignment,
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 14, CHA = 10 },
            Ticks = Enumerable.Range(0, levels).Select(_ => new Tick { DriverId = classId }).ToList(),
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "animal_companion",
                    CompanionId = "pet",
                    EffectiveLevelFormula = new Formula(CompanionResolver.AnimalCompanionLevelExpression)
                }
            }
        };

        // A fiendish wolf: the template moves animal → magical beast.
        var fiendishPet = new Character
        {
            Name = "Fiendish wolf",
            RaceId = "race:companion_bat",
            TemplateIds = new List<string> { "template:fiendish" },
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick> { new() { DriverId = "racial_hd:animal" } }
        };

        var druid = new CompanionResolver(engine, _ => fiendishPet)
            .Build(Master("class:druid", 5, Alignment.N)).MasterState;
        Assert.Contains(druid.Warnings, w =>
            w.Message.Contains("not an animal") && w.Message.Contains("planar ranger's option"));

        // A nongood planar ranger may have exactly this.
        var planar = new CompanionResolver(engine, _ => fiendishPet)
            .Build(Master("class:planar_ranger", 5, Alignment.LE)).MasterState;
        Assert.DoesNotContain(planar.Warnings, w => w.Message.Contains("animal companion"));

        // A good one may not.
        var goodPlanar = new CompanionResolver(engine, _ => fiendishPet)
            .Build(Master("class:planar_ranger", 5, Alignment.LG)).MasterState;
        Assert.Contains(goodPlanar.Warnings, w =>
            w.Message.Contains("fiendish, which only a nongood"));
    }

    /// <summary>
    /// SRD Unearthed Arcana: "The planar ranger has all the standard ranger class features, except
    /// as noted below", and Animal Companion is one of the entries it keeps (in a celestial or
    /// fiendish variant). Its content carried the feature as a description with no mechanics, so a
    /// planar ranger reached 4th level and the builder reported that no HD granted a companion
    /// slot at all.
    /// </summary>
    [Fact]
    public void PlanarRanger4_GrantsAnAnimalCompanionSlotLikeARanger()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        Character Build(int levels) => new()
        {
            Name = "Planar ranger",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 12, CHA = 10 },
            Ticks = Enumerable.Range(0, levels)
                .Select(_ => new Tick { DriverId = "class:planar_ranger" }).ToList()
        };

        // The ability arrives at 4th, exactly as for a ranger.
        Assert.DoesNotContain(engine.Evaluate(Build(3)).CompanionSlots,
            slot => slot.LinkType == "animal_companion");

        var state = engine.Evaluate(Build(4));
        var slot = Assert.Single(state.CompanionSlots, s => s.LinkType == "animal_companion");
        // "the ranger's effective druid level is one-half his ranger level" — 4/2 = 2.
        Assert.Equal(2, slot.EffectiveLevel);
    }

    /// <summary>A planar ranger keeps the ranger's combat style choice too.</summary>
    [Fact]
    public void PlanarRanger2_OffersTheRangerCombatStyleChoice()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var state = engine.Evaluate(new Character
        {
            Name = "Planar ranger",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 12, CHA = 10 },
            Ticks = Enumerable.Range(0, 2)
                .Select(_ => new Tick { DriverId = "class:planar_ranger" }).ToList()
        });

        Assert.True(state.PendingClassFeatureSelections
            .ContainsKey("class_feature:ranger_combat_style"));
    }

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
                    EffectiveLevelFormula = new Formula("ClassLevel(wizard) + ClassLevel(sorcerer)")
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
                    EffectiveLevelFormula = new Formula("ClassLevel(wizard) + ClassLevel(sorcerer)")
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

        // Druid 7 picks a dire wolf, which is on the 7th-level alternative list at –6. The slot
        // still reports the master's own druid level, but the companion is fielded at 7 − 6 = 1:
        // link and share spells, and none of the tier-3-and-up scaling.
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
        Assert.Equal(7 - 6, fen.State.EffectiveMasterLevel);
        // Dire wolf base NA = 4, and no tier reaches 3, so nothing is added.
        Assert.Equal(4, fen.State.NaturalArmor);
        Assert.Contains(fen.State.Abilities, a => a.Id == "ac_link"); // tier 1
        Assert.Contains(fen.State.Abilities, a => a.Id == "ac_share_spells"); // tier 1
        Assert.Contains(fen.State.Abilities, a => a.Id == "direwolf_trip"); // racial
        Assert.DoesNotContain(fen.State.Abilities, a => a.Id == "ac_evasion"); // tier 3
        Assert.DoesNotContain(fen.State.Abilities, a => a.Id == "ac_devotion"); // tier 6
    }

    [Fact]
    public void Druid18_DireBear_ScalesThroughMidTier()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // A dire bear is on the 13th-level list at –12, so a druid 12 cannot field one at all.
        // Druid 18 puts it at 18 − 12 = 6 — the mid tier this test is about.
        var druid = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 16, CHA = 10 },
            Ticks = Enumerable.Range(0, 18).Select(_ => new Tick { DriverId = "class:druid" }).ToList(),
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

        Assert.Equal(18 - 12, bear.State.EffectiveMasterLevel);
        // Dire bear base NA = 7; AC tiers 3 (+2) + 6 (+2) = +4 → 11.
        Assert.Equal(11, bear.State.NaturalArmor);
        // Bonus tricks counter: 2 tricks by tier 6.
        Assert.Equal(2, bear.State.Counters["ac_bonus_tricks"]);
        // Devotion arrives at tier 6.
        Assert.Contains(bear.State.Abilities, a => a.Id == "ac_devotion");
        // Multiattack (tier 9) NOT yet.
        Assert.DoesNotContain(bear.State.Abilities, a => a.Id == "ac_multiattack");
        // Racial signature preserved.
        Assert.Contains(bear.State.Abilities, a => a.Id == "direbear_improved_grab");
    }

    [Fact]
    public void Druid20_DireTiger_TopTierAllAbilities()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // Druid 20 with a dire tiger, the top alternative list at –15: fielded at 20 − 15 = 5,
        // so only tiers 1 and 3 fire. The heaviest companions are deliberately the weakest
        // relative to the druid's level — that is what the alternative lists price.
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

        Assert.Equal(20 - 15, dt.State.EffectiveMasterLevel);
        // Dire tiger base NA = 6; only tier 3 adds (+2) → 8.
        Assert.Equal(8, dt.State.NaturalArmor);
        // Bonus tricks: 1, from tier 3.
        Assert.Equal(1, dt.State.Counters["ac_bonus_tricks"]);
        Assert.Contains(dt.State.Abilities, a => a.Id == "ac_link");
        Assert.Contains(dt.State.Abilities, a => a.Id == "ac_share_spells");
        Assert.Contains(dt.State.Abilities, a => a.Id == "ac_evasion");
        // Everything from tier 6 up is out of reach even for a 20th-level druid.
        Assert.DoesNotContain(dt.State.Abilities, a => a.Id == "ac_devotion");
        Assert.DoesNotContain(dt.State.Abilities, a => a.Id == "ac_multiattack");
        Assert.DoesNotContain(dt.State.Abilities, a => a.Id == "ac_improved_evasion");
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
        // Elephant is on the 13th-level list at –12, so a druid 20 fields it at 8: tiers 3 and 6
        // add +1 Str each. Base Str 10 + 20 racial = 30, → 32.
        Assert.Equal(20 - 12, ele.State.EffectiveMasterLevel);
        Assert.Equal(32, ele.State.AbilityScores.STR);
        Assert.Contains(ele.State.Abilities, a => a.Id == "elephant_trample");
    }

    [Fact]
    public void Druid20_StackingFormula_OrderIndependentWithRanger()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // SRD ranger: "the ranger's effective druid level is one-half his ranger level", and the
        // ability only arrives at 4th. So druid 17 + ranger 4/2 = 19, not the 17+4-3 = 18 the
        // legacy expression produced by counting ranger levels one-for-one past 3rd.
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
        Assert.Equal(19, slot.EffectiveLevel);
    }

    /// <summary>
    /// AEG "Imp" feat: FOLLOWERS:Familiar|1 with FamiliarLVL = total level (BONUS:VAR|
    /// FamiliarLVL|TL). A divine caster with no wizard or sorcerer levels fields the imp at
    /// her full level: the feat grants the slot with its own progression, and a link still
    /// wearing the importer's generic arcane default takes the slot's level instead of
    /// reading 0 and warning that the master "does not qualify".
    /// </summary>
    [RequiresPrivatePacksFact]
    public void FeatGrantedFamiliar_DefaultLinkFormula_UsesTheSlotProgression()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        var engine = new ReplayStudio(registry);

        var priestess = new Character
        {
            Name = "Priestess",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 16, CHA = 10 },
            Ticks = Enumerable.Range(0, 7).Select(_ => new Tick { DriverId = "class:cleric" }).ToList(),
            CompanionLinks = new List<CompanionLink>
            {
                new()
                {
                    LinkType = "familiar",
                    CompanionId = "pact-imp",
                    SelectedSpecies = "race:companion_devil_imp",
                    EffectiveLevelFormula = new Formula("ClassLevel(wizard) + ClassLevel(sorcerer)")
                }
            }
        };
        priestess.Ticks[0].Choices.FeatIds = new List<string> { "feat:infernal_pact", "feat:imp" };

        var imp = new Character
        {
            Name = "Pact Imp",
            RaceId = "race:companion_devil_imp",
            TemplateIds = new List<string> { "template:familiar_standard" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 11, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 3).Select(_ => new Tick { DriverId = "racial_hd:outsider" }).ToList()
        };

        var resolver = new CompanionResolver(engine, id => id == "pact-imp" ? imp : null);
        var result = resolver.Build(priestess);

        var slot = Assert.Single(result.MasterState.CompanionSlots);
        Assert.Equal("feat:imp", slot.Granter);
        Assert.Equal(7, slot.EffectiveLevel);

        var impBuild = Assert.Single(result.Companions);
        Assert.Equal(7, impBuild.State.EffectiveMasterLevel);
        Assert.DoesNotContain(result.MasterState.Warnings, w => w.Message.Contains("does not qualify"));
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
                    EffectiveLevelFormula = new Formula("ClassLevel(wizard) + ClassLevel(sorcerer)")
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

    [RequiresPcgFixturesFact]
    public void DuchessRoseElite_LillietteUsesMasterDerivedFamiliarStats()
    {
        var rosePath = TestContentHelper.PcgFixture("Duchess Rose, Elite Succubus.pcg");
        var lilliettePath = TestContentHelper.PcgFixture("Lilly.pcg");
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        var mapper = new PcgIdMapper();
        var rose = PcgConverter.Convert(
            PcgParser.ParseText(File.ReadAllText(rosePath), Path.GetFileName(rosePath)),
            mapper,
            registry).Character;
        var lilliette = PcgConverter.Convert(
            PcgParser.ParseText(File.ReadAllText(lilliettePath), Path.GetFileName(lilliettePath)),
            mapper,
            registry).Character;

        Assert.Contains("template:familiar_standard", lilliette.TemplateIds);

        // Exercise migration of a character save imported before this fix.
        Assert.Single(rose.CompanionLinks).EffectiveLevelFormula =
            new Formula("CasterLevel(wizard) + CasterLevel(sorcerer)");

        var result = new CompanionResolver(
            new ReplayStudio(registry),
            id => id == "lilly" ? lilliette : null).Build(rose);
        var familiarState = Assert.Single(result.Companions).State;

        Assert.Equal(23, result.MasterState.Spellcasting["class:sorcerer"].CasterLevel);
        Assert.Equal(6, result.MasterState.ClassLevels["class:sorcerer"]);
        Assert.Equal(50, result.MasterState.Speeds[MovementMode.Fly]);
        Assert.Equal(FlightManeuverability.Average, result.MasterState.FlyManeuverability);
        Assert.Equal(6, familiarState.EffectiveMasterLevel);
        Assert.Equal(199, result.MasterState.HP);
        Assert.Equal(99, familiarState.HP);
        Assert.Equal(Math.Max(1, result.MasterState.HP / 2), familiarState.HP);
        Assert.Equal(10, result.MasterState.BaseBAB);
        Assert.Equal(5, result.MasterState.EpicAttackBonus);
        Assert.Equal(15, result.MasterState.EffectiveBAB);
        Assert.Equal(10, familiarState.BaseBAB);
        Assert.Equal(0, familiarState.EpicAttackBonus);
        Assert.Equal(result.MasterState.BaseBAB, familiarState.EffectiveBAB);
        Assert.Equal(6, familiarState.BaseSaves.Fort);
        Assert.Equal(12, familiarState.BaseSaves.Ref);
        Assert.Equal(15, familiarState.BaseSaves.Will);
        Assert.Equal(result.MasterState.ProgressionBaseSaves.Fort, familiarState.BaseSaves.Fort);
        Assert.Equal(result.MasterState.ProgressionBaseSaves.Ref, familiarState.BaseSaves.Ref);
        Assert.Equal(result.MasterState.ProgressionBaseSaves.Will, familiarState.BaseSaves.Will);
        Assert.Equal(4, result.MasterState.EpicSaveBonus);
        Assert.Equal(0, familiarState.EpicSaveBonus);
        Assert.Equal(6, familiarState.NaturalArmor); // Elemental base +3, familiar adjustment +3.
        Assert.Equal(8, familiarState.AbilityScores.INT);
        Assert.Equal(CreatureType.Elemental, familiarState.Type);
        Assert.Contains(familiarState.Abilities, ability => ability.Id == "fam_alertness");
        Assert.Contains(familiarState.Abilities, ability => ability.Id == "fam_improved_evasion");
        Assert.Contains(familiarState.Abilities, ability => ability.Id == "fam_share_spells");
        Assert.Contains(familiarState.Abilities, ability => ability.Id == "fam_empathic_link");
        Assert.Contains(familiarState.Abilities, ability => ability.Id == "fam_deliver_touch_spells");
        Assert.Contains(familiarState.Abilities, ability => ability.Id == "fam_speak_with_master");
        Assert.DoesNotContain(familiarState.Abilities, ability => ability.Id == "fam_speak_with_animals");
        Assert.Contains(familiarState.SpecialAttacks, attack => attack.Id == "air_elem_whirlwind");
    }
}
