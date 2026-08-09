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

        Assert.Contains("feat:leadership", state.Feats);
        // 6 HD + Mod(CHA 14)=+2 = 8 → no followers (<10).
        Assert.Equal(8, state.LeadershipScore);
        // SRD Leadership table: score 8 → cohort level 5th. The separate cap is the character's
        // own level (6th) minus one, which is also 5.
        Assert.Equal(5, state.MaxCohortLevel);
        Assert.Equal(0, state.Followers.Level1);

        // Cohort slot granted by the feat.
        var slot = Assert.Single(state.CompanionSlots);
        Assert.Equal("leadership_cohort", slot.LinkType);
        Assert.Equal("feat:leadership", slot.Granter);
        // The slot's own formula is authored in content and still arithmetic — see KNOWN_ISSUES,
        // formulas cannot do the table lookup that MaxCohortLevel now uses.
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
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 14 },
            Ticks = Enumerable.Range(0, 3).Select(i =>
                i == 2
                    ? new Tick
                        {
                            DriverId = "class:fighter",
                            Choices = new TickChoices { FeatIds = new List<string> { "feat:leadership" } }
                        }
                    : new Tick { DriverId = "class:fighter" }).ToList()
        };

        var state = engine.Evaluate(fighter);
        Assert.Contains(state.Warnings, w =>
            w.Message.Contains("leadership", StringComparison.OrdinalIgnoreCase)
            && w.Message.Contains("prerequisite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoLeadership_NoScoreOrSlot()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var fighter = new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 14 },
            Ticks = Enumerable.Range(0, 7).Select(_ => new Tick { DriverId = "class:fighter" }).ToList()
        };

        var state = engine.Evaluate(fighter);

        Assert.DoesNotContain("feat:leadership", state.Feats);
        Assert.Equal(0, state.LeadershipScore);
        Assert.Equal(0, state.MaxCohortLevel);
        Assert.Empty(state.CompanionSlots);
    }

    [Fact]
    public void WarningStringifiesToItsMessage()
    {
        // Both the builder and the sheet interpolated the Warning instance rather than .Message
        // after it stopped being a plain string, so users were shown the type name and the cohort
        // ECL warning looked like it was never raised. Keep the default readable.
        Assert.Equal("over cap", new Warning { Message = "over cap" }.ToString());
        Assert.Equal("HD 7: over cap", new Warning { TickIndex = 7, Message = "over cap" }.ToString());
        Assert.DoesNotContain("Warning", new Warning { Message = "over cap" }.ToString());
    }

    // ---------- Cohort validation via CompanionResolver ----------

    [Fact]
    public void CompanionResolver_CohortUnderCap_NoWarning()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // Fighter 10 + CHA 16 (+3) = score 13 → SRD table cohort level 9th, under the
        // own-level-minus-one cap of 9.
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
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 12, CON = 12, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 5).Select(_ => new Tick { DriverId = "class:fighter" }).ToList()
        };

        var result = new CompanionResolver(engine, _ => cohort).Build(master);

        Assert.Equal(13, result.MasterState.LeadershipScore);
        Assert.Equal(9, result.MasterState.MaxCohortLevel);
        Assert.DoesNotContain(result.MasterState.Warnings, w =>
            w.Message.Contains("exceeds max cohort level", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompanionResolver_CohortOverCap_WarnsOnMaster()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // Fighter 7 + CHA 14 (+2) = score 9 → SRD table cohort level 6th, under the
        // own-level-minus-one cap of 6.
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

        // Cohort is a level-7 fighter — exceeds the cap of 6.
        var cohort = new Character
        {
            Name = "OverleveledCohort",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 12, CON = 12, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 7).Select(_ => new Tick { DriverId = "class:fighter" }).ToList()
        };

        var result = new CompanionResolver(engine, _ => cohort).Build(master);

        Assert.Equal(6, result.MasterState.MaxCohortLevel);
        Assert.Contains(result.MasterState.Warnings, w =>
            w.Message.Contains("exceeds max cohort level", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompanionResolver_CohortWithLevelAdjustment_UsesECLForCap()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // Fighter 10 + CHA 16 (+3) = 13 → SRD table cohort level 9th.
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
        // The point of the test is that level adjustment counts toward the cap at all: 1 HD would
        // pass trivially, ECL 8 is what must be compared against the cap of 9.
        var cohort = new Character
        {
            Name = "Erinyes",
            RaceId = "race:devil_erinyes",
            BaseAbilityScores = new AbilityScoreSet { STR = 0, DEX = 0, CON = 0, INT = 0, WIS = 0, CHA = 0 },
            Ticks = new List<Tick> { new() { DriverId = "racial_hd:outsider" } }
        };

        var result = new CompanionResolver(engine, _ => cohort).Build(master);

        Assert.Equal(9, result.MasterState.MaxCohortLevel);
        Assert.Equal(8, result.Companions[0].State.ECL);
        Assert.DoesNotContain(result.MasterState.Warnings, w =>
            w.Message.Contains("exceeds max cohort level", StringComparison.OrdinalIgnoreCase));
    }

    // ---------- helper ----------

    // ---------- Epic Leadership ----------

    /// <summary>
    /// SRD: "Normal: The Leadership feat provides no benefit for leadership scores beyond 25."
    /// Without the epic feat the base table's last row applies however high the score climbs.
    /// </summary>
    [Fact]
    public void BaseLeadershipTable_StopsAt25()
    {
        var at25 = LeadershipTables.LookupFollowerCounts(25);
        var at40 = LeadershipTables.LookupFollowerCounts(40);

        Assert.Equal(135, at25.Level1);
        Assert.Equal(at25.Level1, at40.Level1);
        Assert.Equal(0, at40.At(7));
        Assert.Equal(17, LeadershipTables.LookupCohortLevel(40));
    }

    /// <summary>Rows transcribed from Table: Epic Leadership in `epicFeats.html`.</summary>
    [Theory]
    // score, cohort, 1st,  2nd, 3rd, 4th, 5th, 6th, 7th, 8th, 9th, 10th
    [InlineData(25, 17, 135, 13, 7, 4, 2, 2, 1, 0, 0, 0)]
    [InlineData(30, 20, 300, 30, 15, 8, 4, 2, 1, 0, 0, 0)]
    [InlineData(31, 20, 350, 35, 18, 9, 5, 3, 2, 1, 0, 0)]
    [InlineData(36, 23, 660, 66, 33, 17, 9, 5, 3, 2, 1, 0)]
    [InlineData(40, 25, 1000, 100, 50, 25, 13, 7, 4, 2, 1, 0)]
    public void EpicLeadershipTable_MatchesTheSrdRows(
        int score, int cohort, int l1, int l2, int l3, int l4, int l5, int l6, int l7, int l8, int l9, int l10)
    {
        var counts = LeadershipTables.LookupEpicFollowerCounts(score);

        Assert.Equal(cohort, LeadershipTables.LookupEpicCohortLevel(score));
        Assert.Equal(
            new[] { l1, l2, l3, l4, l5, l6, l7, l8, l9, l10 },
            new[] { counts.Level1, counts.Level2, counts.Level3, counts.Level4, counts.Level5,
                    counts.Level6, counts.At(7), counts.At(8), counts.At(9), counts.At(10) });
    }

    /// <summary>
    /// Past 40 the SRD gives rules rather than rows: +100 1st-level followers per point, then
    /// one-tenth as many 2nd as 1st and half as many at each level after, rounding up except that
    /// a fraction below 1 rounds to 0. The cohort level rises by 1 per 2 points.
    /// </summary>
    [Fact]
    public void EpicLeadershipBeyond40_FollowsTheStatedRules()
    {
        var at42 = LeadershipTables.LookupEpicFollowerCounts(42);

        Assert.Equal(1200, at42.Level1);            // 1000 + 100 x 2
        Assert.Equal(120, at42.Level2);             // one-tenth of 1st
        Assert.Equal(60, at42.Level3);              // half of 2nd
        Assert.Equal(30, at42.Level4);
        Assert.Equal(15, at42.Level5);
        Assert.Equal(8, at42.Level6);               // 15/2 rounds up
        Assert.Equal(4, at42.At(7));
        Assert.Equal(2, at42.At(8));
        Assert.Equal(1, at42.At(9));
        Assert.Equal(0, at42.At(10));              // half of 1 is below 1, so none

        Assert.Equal(26, LeadershipTables.LookupEpicCohortLevel(42));   // 25 + 2/2
        Assert.Equal(30, LeadershipTables.LookupEpicCohortLevel(50));   // 25 + 10/2
    }

    /// <summary>
    /// "And so on" outruns the printed table. The epic table's last column is 10th level, but the
    /// halving rule keeps producing followers as the score climbs — a score of 60 fields 11th- and
    /// 12th-level followers, and only the SRD's "can't have a follower of higher than 20th level"
    /// stops it.
    /// </summary>
    [Fact]
    public void EpicLeadershipHalving_ContinuesPastThePrintedTable()
    {
        var at60 = LeadershipTables.LookupEpicFollowerCounts(60);

        Assert.Equal(3000, at60.Level1);            // 1000 + 100 x 20
        Assert.Equal(300, at60.Level2);
        Assert.Equal(150, at60.Level3);
        Assert.Equal(75, at60.Level4);
        Assert.Equal(38, at60.Level5);              // 75/2 rounds up
        Assert.Equal(19, at60.Level6);
        Assert.Equal(10, at60.At(7));
        Assert.Equal(5, at60.At(8));
        Assert.Equal(3, at60.At(9));
        Assert.Equal(2, at60.At(10));
        // Past the printed table.
        Assert.Equal(1, at60.At(11));
        Assert.Equal(0, at60.At(12));
        Assert.Equal(11, at60.HighestLevel);
    }

    /// <summary>No follower may be above 20th level, however extreme the score.</summary>
    [Fact]
    public void FollowerLevelsNeverExceedTwentieth()
    {
        var absurd = LeadershipTables.LookupEpicFollowerCounts(500);

        Assert.True(absurd.HighestLevel <= FollowerCounts.MaxFollowerLevel);
        Assert.Equal(0, absurd.At(FollowerCounts.MaxFollowerLevel + 1));
    }

    /// <summary>
    /// The feat has to actually switch tables during evaluation, not merely exist in the content.
    /// </summary>
    [Fact]
    public void EpicLeadershipFeat_SwitchesTheEngineToTheEpicTable()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var master = BuildFighterWithLeadership(levels: 21, cha: 28);
        // 21 HD + Mod(CHA 28) = +9 → score 30.
        var withoutEpic = engine.Evaluate(master);
        Assert.Equal(30, withoutEpic.LeadershipScore);
        Assert.Equal(135, withoutEpic.Followers.Level1);      // base table, capped at its 25 row
        Assert.Equal(0, withoutEpic.Followers.At(7));

        master.Ticks[^1].Choices.FeatIds =
            new List<string> { "feat:epic_leadership" };
        var withEpic = engine.Evaluate(master);

        Assert.Contains("feat:epic_leadership", withEpic.Feats);
        Assert.Equal(30, withEpic.LeadershipScore);
        Assert.Equal(300, withEpic.Followers.Level1);         // Table: Epic Leadership, score 30
        Assert.Equal(1, withEpic.Followers.At(7));
        Assert.Equal(20, withEpic.MaxCohortLevel);            // table 20th, under level 21 - 1
    }

    private static Character BuildFighterWithLeadership(int levels, int cha)
    {
        // Feat schedule: fighter gets bonus feats at L1, L2, L4, L6, L8, ...
        // and the standard feat slot arrives at HD 1, 3, 6, 9, 12, 15, 18.
        // We attach leadership to the HD-6 standard feat slot.
        if (levels < 6)
            throw new ArgumentException("Need at least 6 levels to take Leadership.", nameof(levels));

        return new Character
        {
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = cha },
            Ticks = Enumerable.Range(0, levels).Select(i =>
                i == 5 // HD 6 — standard feat slot
                    ? new Tick
                        {
                            DriverId = "class:fighter",
                            Choices = new TickChoices { FeatIds = new List<string> { "feat:leadership" } }
                        }
                    : new Tick { DriverId = "class:fighter" }).ToList()
        };
    }
}
