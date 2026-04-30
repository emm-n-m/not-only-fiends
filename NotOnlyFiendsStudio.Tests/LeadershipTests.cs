using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// PR 4 end-to-end tests for the Leadership feat using the real SRD content pack.
/// Unit tests for LeadershipTables + ModifyLeadershipScore live in CompanionTests.
/// </summary>
public class LeadershipTests
{
    // ---------- feat:leadership grants a cohort slot ----------

    [Fact]
    public void Leadership_GrantsCohortSlot_WhenFeatTaken()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var fighter = BuildFighterWithLeadership(levels: 6, cha: 14);
        var state = engine.Evaluate(fighter);

        Assert.Contains("leadership", state.Feats);
        // 6 HD + Mod(CHA 14)=+2 = 8 → no followers (<10).
        Assert.Equal(8, state.LeadershipScore);
        Assert.Equal(4, state.MaxCohortLevel); // min(8-2, 6-2) = 4
        Assert.Equal(0, state.Followers.Level1);

        // Cohort slot granted by the feat.
        var slot = Assert.Single(state.CompanionSlots);
        Assert.Equal("leadership_cohort", slot.LinkType);
        Assert.Equal("feat:leadership", slot.Granter);
        Assert.Equal(4, slot.EffectiveLevel); // min(6-2, 8-2) = 4
    }

    [Fact]
    public void Leadership_ScoreAt10_GrantsFiveFirstLevelFollowers()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // HD 8 + CHA 14 (+2) = 10 → 5 followers at level 1 per DMG.
        var fighter = BuildFighterWithLeadership(levels: 8, cha: 14);
        var state = engine.Evaluate(fighter);

        Assert.Equal(10, state.LeadershipScore);
        Assert.Equal(5, state.Followers.Level1);
        Assert.Equal(0, state.Followers.Level2);
    }

    [Fact]
    public void Leadership_HighScore_PopulatesMultiLevelFollowers()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // HD 14 + CHA 20 (+5) = 19 → Level1=40, Level2=4, Level3=2, Level4=1, Level5=1.
        var fighter = BuildFighterWithLeadership(levels: 14, cha: 20);
        var state = engine.Evaluate(fighter);

        Assert.Equal(19, state.LeadershipScore);
        Assert.Equal(40, state.Followers.Level1);
        Assert.Equal(4, state.Followers.Level2);
        Assert.Equal(2, state.Followers.Level3);
        Assert.Equal(1, state.Followers.Level4);
        Assert.Equal(1, state.Followers.Level5);
        Assert.Equal(0, state.Followers.Level6);
    }

    [Fact]
    public void Leadership_Prerequisite_Warns_WhenTakenTooEarly()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // HD 3 — below MinHD(6). Feat takes the L3 slot but prereq check fires a warning.
        var fighter = new Character
        {
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 14 },
            Ticks = Enumerable.Range(0, 3).Select(i =>
                i == 2
                    ? new Tick
                        {
                            DriverId = "class:fighter",
                            Choices = new TickChoices { FeatIds = new List<string> { "leadership" } }
                        }
                    : new Tick { DriverId = "class:fighter" }).ToList()
        };

        var state = engine.Evaluate(fighter);
        Assert.Contains(state.Warnings, w =>
            w.Contains("leadership", StringComparison.OrdinalIgnoreCase)
            && w.Contains("prerequisite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoLeadership_NoScoreOrSlot()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var fighter = new Character
        {
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 14 },
            Ticks = Enumerable.Range(0, 7).Select(_ => new Tick { DriverId = "class:fighter" }).ToList()
        };

        var state = engine.Evaluate(fighter);

        Assert.DoesNotContain("leadership", state.Feats);
        Assert.Equal(0, state.LeadershipScore);
        Assert.Equal(0, state.MaxCohortLevel);
        Assert.Empty(state.CompanionSlots);
    }

    // ---------- Cohort validation via CompanionResolver ----------

    [Fact]
    public void CompanionResolver_CohortUnderCap_NoWarning()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // Fighter 10 + CHA 16 (+3) = score 13 → MaxCohortLevel = min(11, 8) = 8.
        var master = BuildFighterWithLeadership(levels: 10, cha: 16);
        master.CompanionLinks = new List<CompanionLink>
        {
            new()
            {
                LinkType = "leadership_cohort",
                CompanionId = "cohort",
                EffectiveLevelFormula = new Formula("min(TotalHD - 2, LeadershipScore - 2)")
            }
        };

        // Cohort is a level-5 fighter — well under the cap of 8.
        var cohort = new Character
        {
            Name = "Squire",
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 12, CON = 12, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 5).Select(_ => new Tick { DriverId = "class:fighter" }).ToList()
        };

        var result = new CompanionResolver(engine, _ => cohort).Build(master);

        Assert.Equal(13, result.MasterState.LeadershipScore);
        Assert.Equal(8, result.MasterState.MaxCohortLevel);
        Assert.DoesNotContain(result.MasterState.Warnings, w =>
            w.Contains("exceeds max cohort level", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompanionResolver_CohortOverCap_WarnsOnMaster()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // Fighter 7 + CHA 14 (+2) = score 9 → MaxCohortLevel = min(7, 5) = 5.
        var master = BuildFighterWithLeadership(levels: 7, cha: 14);
        master.CompanionLinks = new List<CompanionLink>
        {
            new()
            {
                LinkType = "leadership_cohort",
                CompanionId = "overleveled",
                EffectiveLevelFormula = new Formula("min(TotalHD - 2, LeadershipScore - 2)")
            }
        };

        // Cohort is a level-6 fighter — exceeds the cap of 5.
        var cohort = new Character
        {
            Name = "OverleveledCohort",
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 12, CON = 12, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 6).Select(_ => new Tick { DriverId = "class:fighter" }).ToList()
        };

        var result = new CompanionResolver(engine, _ => cohort).Build(master);

        Assert.Equal(5, result.MasterState.MaxCohortLevel);
        Assert.Contains(result.MasterState.Warnings, w =>
            w.Contains("exceeds max cohort level", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompanionResolver_CohortWithLevelAdjustment_UsesECLForCap()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // Fighter 10 + CHA 16 (+3) = 13 → cap 8.
        var master = BuildFighterWithLeadership(levels: 10, cha: 16);
        master.CompanionLinks = new List<CompanionLink>
        {
            new()
            {
                LinkType = "leadership_cohort",
                CompanionId = "erinyes-cohort",
                EffectiveLevelFormula = new Formula("min(TotalHD - 2, LeadershipScore - 2)")
            }
        };

        // Erinyes cohort (LA +7 per srd_companions.json) with 1 outsider HD → ECL = 1 + 7 = 8.
        // This equals the cap exactly — no warning.
        var cohort = new Character
        {
            Name = "Erinyes",
            RaceId = "devil_erinyes",
            BaseAbilityScores = new AbilityScoreSet { STR = 0, DEX = 0, CON = 0, INT = 0, WIS = 0, CHA = 0 },
            Ticks = new List<Tick> { new() { DriverId = "racial_hd:outsider" } }
        };

        var result = new CompanionResolver(engine, _ => cohort).Build(master);

        // ECL == cap → no warning.
        Assert.Equal(8, result.MasterState.MaxCohortLevel);
        Assert.Equal(8, result.Companions[0].State.ECL);
        Assert.DoesNotContain(result.MasterState.Warnings, w =>
            w.Contains("exceeds max cohort level", StringComparison.OrdinalIgnoreCase));
    }

    // ---------- helper ----------

    private static Character BuildFighterWithLeadership(int levels, int cha)
    {
        // Feat schedule: fighter gets bonus feats at L1, L2, L4, L6, L8, ...
        // and the standard feat slot arrives at HD 1, 3, 6, 9, 12, 15, 18.
        // We attach leadership to the HD-6 standard feat slot.
        if (levels < 6)
            throw new ArgumentException("Need at least 6 levels to take Leadership.", nameof(levels));

        return new Character
        {
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = cha },
            Ticks = Enumerable.Range(0, levels).Select(i =>
                i == 5 // HD 6 — standard feat slot
                    ? new Tick
                        {
                            DriverId = "class:fighter",
                            Choices = new TickChoices { FeatIds = new List<string> { "leadership" } }
                        }
                    : new Tick { DriverId = "class:fighter" }).ToList()
        };
    }
}
