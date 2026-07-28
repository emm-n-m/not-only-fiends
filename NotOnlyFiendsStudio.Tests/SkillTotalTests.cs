using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// The skill tail pass: totals (ranks + key ability modifier + granted bonuses + synergies) and
/// the 5-rank synergy rule. Before this existed, <c>SkillDefinition.Synergies</c> and
/// <c>CharacterState.SkillBonuses</c> had no consumer anywhere in the solution — every racial and
/// class skill bonus in every pack affected nothing observable, and the sheet showed bare ranks.
///
/// The synthetic registry mirrors the real SRD synergy graph for the skills under test, so the
/// mechanics are asserted in isolation; <see cref="BundledContent_SynergyDataIsConsumed"/> then
/// checks the shipped content actually flows through the same pass.
/// </summary>
public class SkillTotalTests
{
    private const int NoRanks = 0;

    private static ContentRegistry CreateRegistry()
    {
        var registry = new ContentRegistry();

        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:human",
            Name = "Human",
            Type = CreatureType.Humanoid,
            Size = Size.Medium,
        });

        // A generous skill-point class so allocations in these tests are legal and produce no
        // "spent more skill points than available" warnings to reason around.
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:rogue",
            Name = "Rogue",
            HitDie = 6,
            SkillPointsPerLevel = 20,
            ClassSkills = new List<string>
            {
                "skill:balance", "skill:bluff", "skill:diplomacy", "skill:intimidate", "skill:jump",
                "skill:knowledge_nobility", "skill:sense_motive", "skill:sleight_of_hand",
                "skill:spot", "skill:tumble",
            },
            BABProgression = BABProgression.Average,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Poor,
                Ref = ProgressionRate.Good,
                Will = ProgressionRate.Poor,
            },
        });

        void Skill(string id, string name, string keyAbility, params (string Target, int Bonus)[] synergies) =>
            registry.RegisterSkill(new SkillDefinition
            {
                Id = id,
                Name = name,
                KeyAbility = keyAbility,
                Synergies = synergies
                    .Select(s => new SkillSynergy { TargetSkillId = s.Target, Bonus = s.Bonus })
                    .ToList(),
            });

        // Same shape as srd_core/skills/srd.json for these entries.
        Skill("skill:spot", "Spot", "wis");
        Skill("skill:balance", "Balance", "dex");
        Skill("skill:sleight_of_hand", "Sleight of Hand", "dex");
        Skill("skill:intimidate", "Intimidate", "cha");
        Skill("skill:diplomacy", "Diplomacy", "cha");
        Skill("skill:bluff", "Bluff", "cha",
            ("skill:sleight_of_hand", 2), ("skill:diplomacy", 2), ("skill:intimidate", 2));
        Skill("skill:sense_motive", "Sense Motive", "wis", ("skill:diplomacy", 2));
        Skill("skill:knowledge_nobility", "Knowledge (nobility and royalty)", "int", ("skill:diplomacy", 2));
        Skill("skill:jump", "Jump", "str", ("skill:tumble", 2));
        Skill("skill:tumble", "Tumble", "dex", ("skill:balance", 2), ("skill:jump", 2));

        return registry;
    }

    /// <summary>
    /// One rogue level with every ability at 10 (modifier +0) unless overridden, and the given
    /// whole-rank allocations. Ranks are passed as whole ranks and doubled — state stores
    /// half-ranks. MaxHalfRanks is not a concern here: the tests use ≤ 6 ranks at HD 1 only where
    /// the resulting warning is irrelevant to the assertion.
    /// </summary>
    private static CharacterState Evaluate(
        ContentRegistry registry,
        Dictionary<string, int> wholeRanks,
        AbilityScoreSet? abilities = null,
        List<Permabuff>? extraGrants = null,
        int levels = 1)
    {
        var character = new Character
        {
            Name = "Skill Test",
            RaceId = "race:human",
            BaseAbilityScores = abilities ?? new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10,
            },
        };

        for (int i = 0; i < levels; i++)
            character.Ticks.Add(new Tick { DriverId = "class:rogue" });

        // All ranks on the last tick, so MaxHalfRanks is at its highest.
        character.Ticks[^1].Choices.SkillAllocations = wholeRanks
            .Select(kv => new SkillAllocation { SkillId = kv.Key, HalfRanks = kv.Value * 2 })
            .ToList();

        if (extraGrants != null)
            character.PermanentEvents.Add(new PermanentEvent { BeforeTick = 0, Permabuffs = extraGrants });

        return new ReplayStudio(registry).Evaluate(character);
    }

    [Fact]
    public void RanksOnly_TotalEqualsRanks()
    {
        var state = Evaluate(CreateRegistry(), new() { ["skill:spot"] = 4 });

        Assert.Equal(4, state.SkillTotals["skill:spot"]);
    }

    [Fact]
    public void RanksPlusAbilityModifier_AreSummed()
    {
        var abilities = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 16, CHA = 10 };

        var state = Evaluate(CreateRegistry(), new() { ["skill:spot"] = 4 }, abilities);

        // Spot is Wisdom-keyed: 4 ranks + 3 (WIS 16).
        Assert.Equal(7, state.SkillTotals["skill:spot"]);
    }

    [Fact]
    public void NegativeAbilityModifier_LowersTheTotal()
    {
        var abilities = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 7, CHA = 10 };

        var state = Evaluate(CreateRegistry(), new() { ["skill:spot"] = 4 }, abilities);

        // 4 ranks - 2 (WIS 7).
        Assert.Equal(2, state.SkillTotals["skill:spot"]);
    }

    [Fact]
    public void GrantSkillBonus_ReachesTheTotal()
    {
        var grants = new List<Permabuff> { new GrantSkillBonus { SkillId = "skill:spot", Value = 2 } };

        var state = Evaluate(CreateRegistry(), new() { ["skill:spot"] = 4 }, extraGrants: grants);

        Assert.Equal(2, state.SkillBonuses["skill:spot"]);
        Assert.Equal(6, state.SkillTotals["skill:spot"]);
    }

    [Fact]
    public void GrantSkillBonus_WithNoRanks_StillProducesATotal()
    {
        var grants = new List<Permabuff> { new GrantSkillBonus { SkillId = "skill:spot", Value = 2 } };

        var state = Evaluate(CreateRegistry(), new(), extraGrants: grants);

        Assert.Equal(NoRanks, state.SkillHalfRanks.GetValueOrDefault("skill:spot"));
        Assert.Equal(2, state.SkillTotals["skill:spot"]);
    }

    [Fact]
    public void HalfRanks_AreTruncatedNotRounded()
    {
        var registry = CreateRegistry();
        var character = new Character
        {
            Name = "Cross-class",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
        };
        character.Ticks.Add(new Tick
        {
            DriverId = "class:rogue",
            Choices = new TickChoices
            {
                // 5 half-ranks = 2.5 ranks; only whole ranks count toward the roll.
                SkillAllocations = new() { new SkillAllocation { SkillId = "skill:spot", HalfRanks = 5 } },
            },
        });

        var state = new ReplayStudio(registry).Evaluate(character);

        Assert.Equal(2, state.SkillTotals["skill:spot"]);
    }

    [Fact]
    public void SynergyFires_AtExactlyFiveRanks()
    {
        var state = Evaluate(CreateRegistry(), new() { ["skill:bluff"] = 5 }, levels: 2);

        Assert.Equal(2, state.SkillSynergyBonuses["skill:diplomacy"]);
        Assert.Equal(2, state.SkillTotals["skill:diplomacy"]);
    }

    [Fact]
    public void SynergyDoesNotFire_AtFourRanks()
    {
        var state = Evaluate(CreateRegistry(), new() { ["skill:bluff"] = 4 }, levels: 2);

        Assert.False(state.SkillSynergyBonuses.ContainsKey("skill:diplomacy"));
        Assert.False(state.SkillTotals.ContainsKey("skill:diplomacy"));
    }

    [Fact]
    public void ThreeSynergiesIntoDiplomacy_Stack()
    {
        // Bluff, Sense Motive and Knowledge (nobility) each grant Diplomacy +2. They are three
        // separate sources and legitimately stack, so the pass must not deduplicate them.
        var state = Evaluate(
            CreateRegistry(),
            new()
            {
                ["skill:bluff"] = 5,
                ["skill:sense_motive"] = 5,
                ["skill:knowledge_nobility"] = 5,
            },
            levels: 5);

        Assert.Equal(6, state.SkillSynergyBonuses["skill:diplomacy"]);
        Assert.Equal(6, state.SkillTotals["skill:diplomacy"]);
    }

    [Fact]
    public void SynergyBonus_DoesNotCountTowardAnotherSynergysThreshold()
    {
        // Jump 5 ranks → Tumble +2. Tumble sits at 4 *ranks*, and Tumble's own synergies key off
        // ranks, not the total — so its +2 into Balance and Jump must not fire. Were synergies
        // chained (or iterated to a fixed point), Tumble would read as 6 and cascade.
        var state = Evaluate(
            CreateRegistry(),
            new() { ["skill:jump"] = 5, ["skill:tumble"] = 4 },
            levels: 5);

        Assert.Equal(2, state.SkillSynergyBonuses["skill:tumble"]);
        Assert.Equal(6, state.SkillTotals["skill:tumble"]);

        Assert.False(state.SkillSynergyBonuses.ContainsKey("skill:balance"));
        Assert.Equal(5, state.SkillTotals["skill:jump"]);
    }

    [Fact]
    public void SynergyChain_FiresOnceTheSourceItselfReachesFiveRanks()
    {
        // Same setup as above but with Tumble at 5 ranks: now its own synergies do fire. This is
        // the positive control that the previous test is measuring the threshold and not just a
        // missing edge.
        var state = Evaluate(
            CreateRegistry(),
            new() { ["skill:jump"] = 5, ["skill:tumble"] = 5 },
            levels: 5);

        Assert.Equal(2, state.SkillSynergyBonuses["skill:balance"]);
        Assert.Equal(2, state.SkillSynergyBonuses["skill:jump"]);
        Assert.Equal(7, state.SkillTotals["skill:jump"]);
    }

    [Fact]
    public void GrantedBonusAndSynergy_AreBothInTheTotalButTrackedApart()
    {
        // SkillBonuses stays "what content granted" so provenance survives on the sheet and in
        // assertions that read it (e.g. Hellbred's +2 racial Intimidate).
        var grants = new List<Permabuff> { new GrantSkillBonus { SkillId = "skill:diplomacy", Value = 3 } };

        var state = Evaluate(
            CreateRegistry(),
            new() { ["skill:bluff"] = 5, ["skill:diplomacy"] = 2 },
            extraGrants: grants,
            levels: 3);

        Assert.Equal(3, state.SkillBonuses["skill:diplomacy"]);
        Assert.Equal(2, state.SkillSynergyBonuses["skill:diplomacy"]);
        Assert.Equal(7, state.SkillTotals["skill:diplomacy"]);
    }

    [Fact]
    public void BundledContent_SynergyDataIsConsumed()
    {
        // The shipped srd_core skill list carries 12 synergy entries across 9 skills that nothing
        // read before this pass existed. Asserted against real content so a future re-extraction
        // that drops the structured synergies is caught.
        var registry = TestContentHelper.LoadBundledPacks();

        var bluff = registry.GetAllSkills().Single(s => s.Id == "skill:bluff");
        Assert.Contains(bluff.Synergies, s => s.TargetSkillId == "skill:diplomacy" && s.Bonus == 2);

        var character = new Character
        {
            Name = "Bundled Synergy",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
        };
        for (int i = 0; i < 5; i++)
            character.Ticks.Add(new Tick { DriverId = "class:rogue" });
        character.Ticks[^1].Choices.SkillAllocations = new()
        {
            new SkillAllocation { SkillId = "skill:bluff", HalfRanks = 10 },
        };

        var state = new ReplayStudio(registry).Evaluate(character);

        Assert.Equal(5, state.SkillTotals["skill:bluff"]);
        Assert.Equal(2, state.SkillSynergyBonuses["skill:diplomacy"]);
        Assert.Equal(2, state.SkillSynergyBonuses["skill:intimidate"]);
        Assert.Equal(2, state.SkillSynergyBonuses["skill:sleight_of_hand"]);
    }

    [Fact]
    public void Totals_AreRecomputedNotAccumulated_AcrossRepeatedEvaluation()
    {
        // The sheet's HD slider calls Evaluate repeatedly against the same Character. A pass that
        // appended rather than replaced would inflate every total on the second look.
        var registry = CreateRegistry();
        var character = new Character
        {
            Name = "Slider",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
        };
        for (int i = 0; i < 3; i++)
            character.Ticks.Add(new Tick { DriverId = "class:rogue" });
        character.Ticks[^1].Choices.SkillAllocations = new()
        {
            new SkillAllocation { SkillId = "skill:bluff", HalfRanks = 10 },
        };

        var engine = new ReplayStudio(registry);
        var first = engine.Evaluate(character);
        var second = engine.Evaluate(character);

        Assert.Equal(first.SkillTotals["skill:bluff"], second.SkillTotals["skill:bluff"]);
        Assert.Equal(2, second.SkillSynergyBonuses["skill:diplomacy"]);
    }
}
