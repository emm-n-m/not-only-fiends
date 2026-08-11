using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// A template acquired mid-career applies at its acquisition HD, forward only: skill points
/// banked before it stay as they were earned, while the SRD's "increase all current and
/// future Hit Dice" rule restates banked die sizes at the moment of transformation.
/// Evaluating the timeline below the acquisition HD must show the untransformed creature.
/// </summary>
public class AcquiredTemplateTests
{
    private const string TemplateId = "template:test_undeath";
    private const string ChainedTemplateId = "template:test_augment";

    private static ContentRegistry CreateContentRegistry()
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

        // A prestige class whose 2nd level transforms the character — the capstone shape.
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:transformer",
            Name = "Transformer",
            HitDie = 8,
            SkillPointsPerLevel = 2,
            BABProgression = BABProgression.Average,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Poor,
                Ref = ProgressionRate.Poor,
                Will = ProgressionRate.Good
            },
            LevelPermabuffs = new Dictionary<int, List<Permabuff>>
            {
                { 2, new List<Permabuff> { new ApplyTemplate { TemplateId = TemplateId } } }
            }
        });

        // Lich-shaped: undead type, d12 floor, +2 Int, and a chained consequence template —
        // plus a final-state prerequisite the test characters never meet.
        registry.RegisterTemplate(new TemplateDriver
        {
            Id = TemplateId,
            Name = "Test Undeath",
            TypeOverride = CreatureType.Undead,
            HitDieSizeFloor = 12,
            SpellResistanceFloor = 12,
            AbilityModifiers = new AbilityScoreSet { INT = 2 },
            LevelAdjustment = 2,
            Prerequisites = new List<Prerequisite> { new MinHD { Value = 20 } },
            CreationPermabuffs = new List<Permabuff>
            {
                new ApplyTemplate { TemplateId = ChainedTemplateId }
            }
        });

        registry.RegisterTemplate(new TemplateDriver
        {
            Id = ChainedTemplateId,
            Name = "Test Augment",
            SubtypeAdditions = new List<string> { "augmented" }
        });

        return registry;
    }

    private static Character FighterFive(int acquisitionHD = 0) => new()
    {
        Name = "Test Subject",
        RaceId = "race:human",
        // Int 14 (+2); the template's +2 makes it 16 (+3) from its acquisition tick on.
        BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 14, WIS = 10, CHA = 10 },
        TemplateIds = new List<string> { TemplateId },
        TemplateAcquisitionHD = acquisitionHD > 0
            ? new Dictionary<string, int> { [TemplateId] = acquisitionHD }
            : new Dictionary<string, int>(),
        Ticks = Enumerable.Range(0, 5).Select(_ => new Tick { DriverId = "class:fighter" }).ToList()
    };

    [Fact]
    public void AcquiredTemplate_AppliesForwardFromItsTick()
    {
        var engine = new ReplayStudio(CreateContentRegistry());
        var character = FighterFive(acquisitionHD: 4);

        // Below the acquisition HD the character is untransformed in every respect.
        var at3 = engine.Evaluate(character, upToHD: 3);
        Assert.Equal(CreatureType.Humanoid, at3.Type);
        Assert.True(at3.IsLiving);
        Assert.Equal(14, at3.AbilityScores.INT);
        Assert.Equal(0, at3.LevelAdjustment);
        Assert.DoesNotContain(TemplateId, at3.TemplateIds);
        Assert.All(at3.HitDice, die => Assert.Equal(10, die.DieSize));
        // 1st HD (2 base + 2 Int) ×4, then 4 per level.
        Assert.Equal(new[] { 16, 4, 4 }, at3.SkillPointAccruals.Select(a => a.Points));

        // From the acquisition HD on, the template is live — including for that tick's
        // skill points — and banked dice are restated to the floor.
        var at5 = engine.Evaluate(character, upToHD: 5);
        Assert.Equal(CreatureType.Undead, at5.Type);
        Assert.False(at5.IsLiving);
        Assert.Equal(16, at5.AbilityScores.INT);
        Assert.Equal(2, at5.LevelAdjustment);
        Assert.Contains(TemplateId, at5.TemplateIds);
        Assert.All(at5.HitDice, die => Assert.Equal(12, die.DieSize));
        Assert.Equal(new[] { 16, 4, 4, 5, 5 }, at5.SkillPointAccruals.Select(a => a.Points));
        Assert.Equal(new[] { 2, 2, 2, 3, 3 }, at5.SkillPointAccruals.Select(a => a.IntelligenceModifier));
    }

    [Fact]
    public void AcquiredTemplate_ChainedConsequenceTemplate_FollowsTheSameTick()
    {
        var engine = new ReplayStudio(CreateContentRegistry());
        var character = FighterFive(acquisitionHD: 4);

        var at3 = engine.Evaluate(character, upToHD: 3);
        Assert.DoesNotContain(ChainedTemplateId, at3.TemplateIds);
        Assert.DoesNotContain("augmented", at3.Subtypes);

        var at5 = engine.Evaluate(character, upToHD: 5);
        Assert.Contains(ChainedTemplateId, at5.TemplateIds);
        Assert.Contains("augmented", at5.Subtypes);
    }

    [Fact]
    public void AcquiredTemplate_RestatementPreservesSavedRollsAndRederivesHP()
    {
        var engine = new ReplayStudio(CreateContentRegistry());
        var character = FighterFive(acquisitionHD: 4);
        // The 12 at HD 3 exceeds the fighter's d10 but fits the d12 the timeline ends on:
        // a valid source input, not a data error.
        var rolls = new[] { 10, 8, 12, 7, 3 };
        for (var i = 0; i < rolls.Length; i++)
            character.Ticks[i].Choices.HitPointsRolled = rolls[i];

        var full = engine.Evaluate(character);
        Assert.Equal(rolls.Select(r => (int?)r), full.HitDice.Select(die => die.SavedRoll));
        Assert.All(full.HitDice, die => Assert.Equal(12, die.DieSize));
        // Con 10 throughout, so HP is the plain sum of the preserved rolls.
        Assert.Equal(rolls.Sum(), full.HP);
        Assert.DoesNotContain(full.Warnings, w => w.Message.Contains("outside"));

        // Truncated below the acquisition, the dice are still d10 and the over-die roll
        // still does not warn: it is judged against the full timeline's eventual ceiling.
        var at3 = engine.Evaluate(character, upToHD: 3);
        Assert.All(at3.HitDice, die => Assert.Equal(10, die.DieSize));
        Assert.Equal(30, at3.HP);
        Assert.DoesNotContain(at3.Warnings, w => w.Message.Contains("outside"));
    }

    [Fact]
    public void SavedRoll_BeyondEveryDieTheTimelineReaches_StillWarns()
    {
        var engine = new ReplayStudio(CreateContentRegistry());
        var character = FighterFive(acquisitionHD: 4);
        character.Ticks[1].Choices.HitPointsRolled = 13;

        var state = engine.Evaluate(character);
        Assert.Contains(state.Warnings, w => w.Message.Contains("outside"));
    }

    [Fact]
    public void AcquiredTemplate_PastTheLastTick_AppliesInTheTailOnly()
    {
        var engine = new ReplayStudio(CreateContentRegistry());
        var character = FighterFive(acquisitionHD: 6); // one past the 5-tick timeline

        var full = engine.Evaluate(character);
        Assert.Equal(CreatureType.Undead, full.Type);
        Assert.All(full.HitDice, die => Assert.Equal(12, die.DieSize));
        // Every tick was lived at Int 14; the tail template feeds no level's skill points.
        Assert.Equal(new[] { 16, 4, 4, 4, 4 }, full.SkillPointAccruals.Select(a => a.Points));
        // The unmet final-state prerequisite (MinHD 20) is reported once the template applied…
        Assert.Contains(full.Warnings, w => w.Message.Contains("prerequisite not met for template Test Undeath"));

        // …but a truncated evaluation never applies it and must not warn about it.
        var at4 = engine.Evaluate(character, upToHD: 4);
        Assert.Equal(CreatureType.Humanoid, at4.Type);
        Assert.DoesNotContain(TemplateId, at4.TemplateIds);
        Assert.DoesNotContain(at4.Warnings, w => w.Message.Contains("prerequisite not met for template"));
    }

    [Fact]
    public void MissingAcquisitionEntry_MeansCreation_ExactlyAsBefore()
    {
        var engine = new ReplayStudio(CreateContentRegistry());
        var withoutEntry = engine.Evaluate(FighterFive());
        var atOne = engine.Evaluate(FighterFive(acquisitionHD: 1)); // ≤ 1 is creation too

        foreach (var state in new[] { withoutEntry, atOne })
        {
            Assert.Equal(CreatureType.Undead, state.Type);
            Assert.Equal(16, state.AbilityScores.INT);
            Assert.All(state.HitDice, die => Assert.Equal(12, die.DieSize));
            // Int 16 from HD 1: (2+3)×4, then 5 per level — today's creation arithmetic.
            Assert.Equal(new[] { 20, 5, 5, 5, 5 }, state.SkillPointAccruals.Select(a => a.Points));
        }
    }

    [Fact]
    public void CapstoneApplyTemplate_FiresWhenTheClassLevelIsReached()
    {
        var engine = new ReplayStudio(CreateContentRegistry());
        var character = new Character
        {
            Name = "Capstone Subject",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 14, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:transformer" },
                new() { DriverId = "class:transformer" }
            }
        };

        var at3 = engine.Evaluate(character, upToHD: 3); // transformer level 1
        Assert.Equal(CreatureType.Humanoid, at3.Type);
        Assert.DoesNotContain(TemplateId, at3.TemplateIds);

        var full = engine.Evaluate(character); // transformer level 2 at HD 4
        Assert.Equal(CreatureType.Undead, full.Type);
        Assert.Contains(TemplateId, full.TemplateIds);
        Assert.All(full.HitDice, die => Assert.Equal(12, die.DieSize));
        // The transforming level's own skill points precede the transformation: the level
        // completes, then you transform. HD 4 still accrues at Int 14 (+2).
        Assert.Equal(new[] { 2, 2, 2, 2 }, full.SkillPointAccruals.Select(a => a.IntelligenceModifier));
    }

    [Fact]
    public void TemplateGrantedTwice_AppliesOnce()
    {
        var engine = new ReplayStudio(CreateContentRegistry());
        // On the character from creation AND granted by the transformer capstone.
        var character = new Character
        {
            Name = "Double Grant",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 14, WIS = 10, CHA = 10 },
            TemplateIds = new List<string> { TemplateId },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:transformer" },
                new() { DriverId = "class:transformer" }
            }
        };

        var state = engine.Evaluate(character);
        Assert.Equal(16, state.AbilityScores.INT); // +2 once, not twice
        Assert.Equal(2, state.LevelAdjustment);
        Assert.Single(state.TemplateIds, id => id == TemplateId);
    }

    private const string HeritageTemplateId = "template:test_heritage";
    private const string AscensionTemplateId = "template:test_ascension";

    private static void RegisterAscensionPair(ContentRegistry registry)
    {
        // Alu-fiend-shaped heritage: inherited, so it applies at creation.
        registry.RegisterTemplate(new TemplateDriver
        {
            Id = HeritageTemplateId,
            Name = "Test Heritage",
            TypeOverride = CreatureType.Outsider,
            AbilityModifiers = new AbilityScoreSet { INT = 4 },
            LevelAdjustment = 4,
            CreationPermabuffs = new List<Permabuff> { new GrantImmunity { Immunity = "poison" } },
            ScalingFormulas = new List<ScalingFormula>
            {
                new() { Target = AttributeTarget.SpellResistance, Formula = new Formula("TotalHD + 10") }
            }
        });

        // Archfiend-shaped ascension: acquiring it consumes the heritage.
        registry.RegisterTemplate(new TemplateDriver
        {
            Id = AscensionTemplateId,
            Name = "Test Ascension",
            TypeOverride = CreatureType.Outsider,
            AbilityModifiers = new AbilityScoreSet { INT = 6 },
            LevelAdjustment = 8,
            CreationPermabuffs = new List<Permabuff> { new RevokeTemplate { TemplateId = HeritageTemplateId } }
        });
    }

    [Fact]
    public void Ascension_RevokesTheHeritage_ForwardFromItsTick()
    {
        var registry = CreateContentRegistry();
        RegisterAscensionPair(registry);
        var engine = new ReplayStudio(registry);

        var character = FighterFive();
        character.TemplateIds = new List<string> { HeritageTemplateId, AscensionTemplateId };
        character.TemplateAcquisitionHD = new Dictionary<string, int> { [AscensionTemplateId] = 4 };

        // Through HD 3 she is the heritage creature: Int 18, LA 4, poison-immune, SR HD+10.
        var at3 = engine.Evaluate(character, upToHD: 3);
        Assert.Equal(CreatureType.Outsider, at3.Type);
        Assert.Equal(18, at3.AbilityScores.INT);
        Assert.Equal(4, at3.LevelAdjustment);
        Assert.Contains("poison", at3.Immunities);
        Assert.Equal(13, at3.SpellResistance);
        Assert.DoesNotContain(AscensionTemplateId, at3.TemplateIds);

        // From HD 4 the ascension replaces it: +6 Int on the base 14 (the heritage's +4 is
        // gone), LA 8 net, no inherited immunity, and the heritage's SR formula stops.
        var at5 = engine.Evaluate(character, upToHD: 5);
        Assert.Equal(CreatureType.Outsider, at5.Type);
        Assert.Equal(20, at5.AbilityScores.INT);
        Assert.Equal(8, at5.LevelAdjustment);
        Assert.DoesNotContain("poison", at5.Immunities);
        Assert.Null(at5.SpellResistance);
        Assert.DoesNotContain(HeritageTemplateId, at5.TemplateIds);
        Assert.Contains(AscensionTemplateId, at5.TemplateIds);

        // Skill points follow the Int of the level that earned them: +4 mod through the
        // swap, +5 from the ascension tick on. Fighter 2 base: (2+4)×4, 6, 6, then 7, 7.
        Assert.Equal(new[] { 24, 6, 6, 7, 7 }, at5.SkillPointAccruals.Select(a => a.Points));
    }

    [Fact]
    public void Ascension_EndsRacialBonusSkillPoints_AfterItsTick()
    {
        var registry = CreateContentRegistry();
        RegisterAscensionPair(registry);
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:bonus_human",
            Name = "Bonus Human",
            Type = CreatureType.Humanoid,
            Size = Size.Medium,
            Speeds = new Dictionary<MovementMode, int> { { MovementMode.Land, 30 } },
            BonusSkillPointsPerHD = 1
        });
        registry.RegisterTemplate(new TemplateDriver
        {
            Id = "template:test_ascension_racial_end",
            Name = "Test Ascension (Racial End)",
            AbilityModifiers = new AbilityScoreSet { INT = 6 },
            CreationPermabuffs = new List<Permabuff> { new EndRacialBonusSkillPoints() }
        });
        var engine = new ReplayStudio(registry);

        var character = FighterFive(acquisitionHD: 0);
        character.RaceId = "race:bonus_human";
        character.TemplateIds = new List<string> { "template:test_ascension_racial_end" };
        character.TemplateAcquisitionHD = new Dictionary<string, int> { ["template:test_ascension_racial_end"] = 4 };

        var state = engine.Evaluate(character);
        // The racial +1 pays through the acquisition tick — the level completes, then you
        // transform — and stops after it: 4 racial accruals, not 5.
        var racial = state.SkillPointAccruals.Where(a => a.Source == "race:bonus_human").ToList();
        Assert.Equal(4, racial.Count);
        // Driver points still switch Int at the acquisition tick: base Int 14 (+2) gives
        // (2+2)×4 then 4/level; the ascension's +6 makes it 20 (+5) → 7/level from HD 4.
        Assert.Equal(new[] { 16, 4, 4, 7, 7 },
            state.SkillPointAccruals.Where(a => a.Source == "class:fighter").Select(a => a.Points));
    }

    [Fact]
    public void RevokeTemplate_NeverAppliedTemplate_IsANoOp()
    {
        var registry = CreateContentRegistry();
        RegisterAscensionPair(registry);
        var engine = new ReplayStudio(registry);

        var character = FighterFive();
        character.TemplateIds = new List<string> { AscensionTemplateId }; // no heritage at all
        character.TemplateAcquisitionHD = new Dictionary<string, int> { [AscensionTemplateId] = 4 };

        var state = engine.Evaluate(character);
        Assert.Equal(20, state.AbilityScores.INT);
        Assert.Equal(8, state.LevelAdjustment);
        Assert.DoesNotContain(state.Warnings, w => w.Message.Contains("Revoke"));
    }

    [Fact]
    public void SpellResistanceFloor_NeverStacks_BestSourceWins()
    {
        var engine = new ReplayStudio(CreateContentRegistry());

        // No other SR source: the template's floor is the value.
        var plain = engine.Evaluate(FighterFive(acquisitionHD: 4));
        Assert.Equal(12, plain.SpellResistance);

        // A higher SR from before the transformation survives it — SR from overlapping
        // sources does not stack, the best applies.
        var resistant = FighterFive(acquisitionHD: 4);
        resistant.PermanentEvents.Add(new PermanentEvent
        {
            BeforeTick = 0,
            Permabuffs = new List<Permabuff>
            {
                new ModifyAttribute { Target = AttributeTarget.SpellResistance, Value = 25 }
            }
        });
        Assert.Equal(25, engine.Evaluate(resistant).SpellResistance);
    }

    [Fact]
    public void FindEarliestAcquisitionHD_ChronologicalPrerequisite_ReturnsFirstLegalTick()
    {
        var registry = CreateContentRegistry();
        registry.RegisterTemplate(new TemplateDriver
        {
            Id = "template:hd_gated",
            Name = "HD Gated",
            Prerequisites = new List<Prerequisite> { new MinHD { Value = 3 } }
        });
        var engine = new ReplayStudio(registry);

        var character = FighterFive();
        character.TemplateIds = new List<string> { "template:hd_gated" };

        // Met after tick 3 completes, so the template can apply at the start of HD 4.
        Assert.Equal(4, engine.FindEarliestAcquisitionHD(character, "template:hd_gated"));
    }

    [Fact]
    public void FindEarliestAcquisitionHD_FeatPrerequisite_ChecksTheFinalState()
    {
        var registry = CreateContentRegistry();
        registry.RegisterFeat(new FeatDefinition { Id = "feat:key_ritual", Name = "Key Ritual" });
        registry.RegisterTemplate(new TemplateDriver
        {
            Id = "template:ritual_gated",
            Name = "Ritual Gated",
            Prerequisites = new List<Prerequisite>
            {
                new HasFeat { FeatId = "feat:key_ritual" },
                new MinHD { Value = 3 }
            }
        });
        var engine = new ReplayStudio(registry);

        var character = FighterFive();
        character.TemplateIds = new List<string> { "template:ritual_gated" };
        // Imported characters store every feat on the last tick; a chronological feat check
        // would wrongly push acquisition to HD 6.
        character.Ticks[^1].Choices.FeatIds = new List<string> { "feat:key_ritual" };

        Assert.Equal(4, engine.FindEarliestAcquisitionHD(character, "template:ritual_gated"));
    }

    [Fact]
    public void FindEarliestAcquisitionHD_NeverMet_ReturnsNull()
    {
        var engine = new ReplayStudio(CreateContentRegistry());
        var character = FighterFive(); // 5 HD, template requires MinHD 20

        Assert.Null(engine.FindEarliestAcquisitionHD(character, TemplateId));
    }

    [Fact]
    public void ApplyTemplate_UnknownTemplateId_WarnsAndSkips()
    {
        var engine = new ReplayStudio(CreateContentRegistry());
        var character = new Character
        {
            Name = "Bad Chain",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick> { new() { DriverId = "class:fighter" } },
            PermanentEvents = new List<PermanentEvent>
            {
                new()
                {
                    BeforeTick = 0,
                    Permabuffs = new List<Permabuff> { new ApplyTemplate { TemplateId = "template:nonexistent" } }
                }
            }
        };

        var state = engine.Evaluate(character);
        Assert.Contains(state.Warnings, w => w.Message.Contains("could not resolve template template:nonexistent"));
        Assert.Empty(state.TemplateIds);
    }
}
