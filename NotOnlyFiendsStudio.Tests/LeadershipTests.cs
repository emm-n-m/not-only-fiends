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
        // SRD Leadership table: score 8 → cohort level 5th, held to 4th by "two or more levels
        // lower than himself" for a 6th-level character.
        Assert.Equal(4, state.MaxCohortLevel);
        Assert.Equal(0, state.Followers.Level1);

        // Cohort slot granted by the feat.
        var slot = Assert.Single(state.CompanionSlots);
        Assert.Equal("leadership_cohort", slot.LinkType);
        Assert.Equal("feat:leadership", slot.Granter);
        // The slot follows the same table-derived cap as the warning and companion resolver.
        Assert.Equal(state.MaxCohortLevel, slot.EffectiveLevel);
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

        // Fighter 10 + CHA 16 (+3) = score 13 → SRD table cohort level 9th, held to 8th by the
        // two-levels-lower cap.
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
        Assert.Equal(8, result.MasterState.MaxCohortLevel);
        Assert.DoesNotContain(result.MasterState.Warnings, w =>
            w.Message.Contains("exceeds max cohort level", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompanionResolver_CohortOverCap_WarnsOnMaster()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // Fighter 7 + CHA 14 (+2) = score 9 → SRD table cohort level 6th, held to 5th by the
        // two-levels-lower cap.
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
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 12, CON = 12, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 6).Select(_ => new Tick { DriverId = "class:fighter" }).ToList()
        };

        var result = new CompanionResolver(engine, _ => cohort).Build(master);

        Assert.Equal(5, result.MasterState.MaxCohortLevel);
        Assert.Contains(result.MasterState.Warnings, w =>
            w.Message.Contains("exceeds max cohort level", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompanionResolver_CohortWithLevelAdjustment_UsesECLForCap()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // Fighter 10 + CHA 16 (+3) = 13 → SRD table cohort level 9th, capped to 8th.
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
        // Level adjustment counts toward the cap: 1 HD would pass trivially, and ECL 8 sits exactly
        // on the cap of 8, which is allowed — "up to this level".
        var cohort = new Character
        {
            Name = "Erinyes",
            RaceId = "race:devil_erinyes",
            BaseAbilityScores = new AbilityScoreSet { STR = 0, DEX = 0, CON = 0, INT = 0, WIS = 0, CHA = 0 },
            Ticks = new List<Tick> { new() { DriverId = "racial_hd:outsider" } }
        };

        var result = new CompanionResolver(engine, _ => cohort).Build(master);

        Assert.Equal(8, result.MasterState.MaxCohortLevel);
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
        // Table row 30 gives 20th; Epic Leadership's own cap is level - 1 = 20, so both agree.
        Assert.Equal(20, withEpic.MaxCohortLevel);
    }

    // ---------- Leadership modifiers ----------

    /// <summary>
    /// SRD Leadership Modifiers. Reputation applies whoever the leader is recruiting; the other two
    /// groups apply to cohorts or to followers but not both, so a character has two effective
    /// scores, not one.
    /// </summary>
    [Fact]
    public void ReputationAppliesToBothScores_TheOtherGroupsToOne()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var master = BuildFighterWithLeadership(levels: 10, cha: 16);   // base score 13
        master.LeadershipModifiers = new LeadershipModifiers
        {
            GreatRenown = true,             // +2 both
            Cruelty = true,                 // -2 both
            HasStronghold = true,           // +2 followers only
            CohortDeathsCaused = 1,         // -2 cohorts only
        };

        var state = engine.Evaluate(master);

        Assert.Equal(13, state.LeadershipScore);            // base is unchanged
        Assert.Equal(13 + 2 - 2 - 2, state.LeadershipCohortScore);
        Assert.Equal(13 + 2 - 2 + 2, state.LeadershipFollowerScore);
    }

    /// <summary>"Caused the death of a cohort -2" is cumulative per cohort killed.</summary>
    [Fact]
    public void CohortDeathsAreCumulative()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var master = BuildFighterWithLeadership(levels: 10, cha: 16);
        master.LeadershipModifiers = new LeadershipModifiers { CohortDeathsCaused = 3 };

        var state = engine.Evaluate(master);

        Assert.Equal(13 - 6, state.LeadershipCohortScore);
        Assert.Equal(13, state.LeadershipFollowerScore);   // cohort-only group
    }

    /// <summary>
    /// "Has a familiar, special mount, or animal companion -2" is derivable, so it is computed
    /// rather than trusted as an input — and it is in the cohort-only group.
    /// </summary>
    [Fact]
    public void KeepingAFamiliarCostsTwoOnTheCohortScoreOnly()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // A wizard has a familiar slot from 1st level; a fighter has none.
        var fighter = BuildFighterWithLeadership(levels: 10, cha: 16);
        var withoutFamiliar = engine.Evaluate(fighter);
        Assert.Equal(withoutFamiliar.LeadershipScore, withoutFamiliar.LeadershipCohortScore);

        var wizard = new Character
        {
            Name = "Wizard",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 16, WIS = 10, CHA = 16 },
            Ticks = Enumerable.Range(0, 10).Select(i =>
                i == 5
                    ? new Tick
                    {
                        DriverId = "class:wizard",
                        Choices = new TickChoices { FeatIds = new List<string> { "feat:leadership" } }
                    }
                    : new Tick { DriverId = "class:wizard" }).ToList()
        };

        var state = engine.Evaluate(wizard);

        Assert.Contains(state.CompanionSlots, slot => slot.LinkType == "familiar");
        Assert.Equal(state.LeadershipScore - 2, state.LeadershipCohortScore);
        Assert.Equal(state.LeadershipScore, state.LeadershipFollowerScore);
        Assert.Contains(state.LeadershipModifierNotes, note => note.Contains("familiar"));
    }

    /// <summary>
    /// "Recruits a cohort of a different alignment -1" needs the cohort, so the engine cannot see
    /// it during replay — CompanionResolver applies it, and only to the cohort side.
    /// </summary>
    [Fact]
    public void ADifferentlyAlignedCohortCostsOneOnTheCohortScoreOnly()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var master = BuildFighterWithLeadership(levels: 10, cha: 16);
        master.Alignment = Alignment.LG;
        master.CompanionLinks = new List<CompanionLink>
        {
            new()
            {
                LinkType = "leadership_cohort",
                CompanionId = "cohort",
                EffectiveLevelFormula = new Formula("min(TotalHD - 2, LeadershipScore - 2)")
            }
        };

        Character Cohort(Alignment alignment) => new()
        {
            Name = "Cohort",
            RaceId = "race:human",
            Alignment = alignment,
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 5).Select(_ => new Tick { DriverId = "class:fighter" }).ToList()
        };

        var same = new CompanionResolver(engine, _ => Cohort(Alignment.LG)).Build(master).MasterState;
        var different = new CompanionResolver(engine, _ => Cohort(Alignment.CE)).Build(master).MasterState;

        Assert.Equal(same.LeadershipCohortScore - 1, different.LeadershipCohortScore);
        Assert.Equal(same.LeadershipFollowerScore, different.LeadershipFollowerScore);
        Assert.Contains(different.LeadershipModifierNotes, note => note.Contains("different alignment"));
    }

    /// <summary>
    /// The whole point of splitting the scores: the two tables get different inputs. A leader with
    /// every positive modifier and Epic Leadership fields the follower row for the higher score.
    /// </summary>
    [Fact]
    public void TheTwoScoresDriveTheirOwnTables()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var master = BuildFighterWithLeadership(levels: 21, cha: 28);   // base 21 + 9 = 30
        master.Ticks[^1].Choices.FeatIds = new List<string> { "feat:epic_leadership" };
        master.LeadershipModifiers = new LeadershipModifiers
        {
            GreatRenown = true,
            FairnessAndGenerosity = true,
            SpecialPower = true,
            HasStronghold = true,
        };

        var state = engine.Evaluate(master);

        Assert.Equal(30, state.LeadershipScore);
        Assert.Equal(34, state.LeadershipCohortScore);      // +4 reputation
        Assert.Equal(36, state.LeadershipFollowerScore);    // +4 reputation, +2 stronghold

        // Table: Epic Leadership row 36 — 660/66/33/17/9/5/3/2/1 — not row 34's.
        Assert.Equal(660, state.Followers.Level1);
        Assert.Equal(2, state.Followers.At(8));
        // Row 34 offers 22nd; Epic Leadership's cap of level - 1 = 20 is what binds.
        Assert.Equal(20, state.MaxCohortLevel);
    }

    // ---------- follower slots are filled by ECL ----------

    /// <summary>
    /// "Number of Followers by Level" counts a follower's level, and level adjustment is part of
    /// that — a 6 HD aranea with LA +4 fills a 10th-level slot, not a 6th. The stored
    /// CompanionLink.FollowerLevel cannot be used: PCGen writes HITDICE:0 on every FOLLOWER line,
    /// so every imported link claims level 0.
    /// </summary>
    [Fact]
    public void FollowersOccupyTheSlotMatchingTheirEcl()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var master = BuildFighterWithLeadership(levels: 20, cha: 20);
        master.CompanionLinks = new List<CompanionLink>
        {
            new()
            {
                LinkType = "leadership_follower",
                CompanionId = "drow",
                FollowerLevel = 0,          // what an import leaves behind
                EffectiveLevelFormula = new Formula("TotalHD")
            }
        };

        // A drow has LA +2, so 3 racial-free class levels reach ECL 5.
        var follower = new Character
        {
            Name = "Drow follower",
            RaceId = "race:drow",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 3).Select(_ => new Tick { DriverId = "class:fighter" }).ToList()
        };

        var result = new CompanionResolver(engine, _ => follower).Build(master);
        var followerState = result.Companions[0].State;

        Assert.Equal(3, followerState.TotalHD);
        Assert.Equal(5, followerState.ECL);            // 3 HD + LA 2
        // Filed under 5, not 3, and not the link's stored 0.
        Assert.Equal(1, result.MasterState.FollowerOccupancy.GetValueOrDefault(5));
        Assert.Equal(0, result.MasterState.FollowerOccupancy.GetValueOrDefault(3));
        Assert.Equal(0, result.MasterState.FollowerOccupancy.GetValueOrDefault(0));
    }

    /// <summary>Over-filling a follower level is reported against that level's capacity.</summary>
    [Fact]
    public void MoreFollowersThanCapacityAtALevelWarns()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // Fighter 10 + Cha 12 (+1) = score 11 → 6 first-level followers and nothing above.
        var master = BuildFighterWithLeadership(levels: 10, cha: 12);
        master.CompanionLinks = Enumerable.Range(0, 2).Select(i => new CompanionLink
        {
            LinkType = "leadership_follower",
            CompanionId = $"follower{i}",
            EffectiveLevelFormula = new Formula("TotalHD")
        }).ToList();

        // Two 2nd-level followers, where the table allows none.
        var follower = new Character
        {
            Name = "Follower",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 2).Select(_ => new Tick { DriverId = "class:fighter" }).ToList()
        };

        var result = new CompanionResolver(engine, _ => follower).Build(master);

        Assert.Equal(11, result.MasterState.LeadershipFollowerScore);
        Assert.Equal(0, result.MasterState.Followers.At(2));
        Assert.Equal(2, result.MasterState.FollowerOccupancy[2]);
        Assert.Contains(result.MasterState.Warnings, w =>
            w.Message.Contains("2 follower(s) of level 2 linked"));
    }

    /// <summary>
    /// The two feats state the cohort cap differently, and Epic Leadership's wording wins for a
    /// character who has it. Base: "he can only recruit a cohort who is two or more levels lower
    /// than himself." Table: Epic Leadership, under its own Cohort Level column: "he or she can't
    /// recruit a cohort of his or her level or higher." Epic Leadership also says it "in all other
    /// ways functions as the Leadership feat", which pulls the other way — but the epic wording is
    /// specific to the epic table, and specific beats general.
    /// </summary>
    [Fact]
    public void EpicLeadershipRelaxesTheCohortCapByOne()
    {
        var baseFeats = new[] { "feat:leadership" };
        var epicFeats = new[] { "feat:leadership", "feat:epic_leadership" };

        Assert.Equal(19, LeadershipTables.CohortLevelCap(baseFeats, 21));
        Assert.Equal(20, LeadershipTables.CohortLevelCap(epicFeats, 21));

        // The cap is character level — hit dice, not ECL — which is what keeps a 6 HD succubus
        // whose racial Charisma buys a Leadership score of 18 from fielding the 12th-level cohort
        // the table would otherwise offer her.
        Assert.Equal(12, LeadershipTables.LookupCohortLevel(18));
        Assert.Equal(4, LeadershipTables.CohortLevelCap(baseFeats, 6));
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
