using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public class DriverTests
{
    private HDDriver CreateFighterDriver()
    {
        return new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:fighter",
            Name = "Fighter",
            HitDie = 10,
            SkillPointsPerLevel = 2,
            ClassSkills = new List<string> { "climb", "craft", "handle_animal", "intimidate", "jump", "ride", "swim" },
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
        };
    }

    private HDDriver CreateOutsiderDriver()
    {
        return new HDDriver
        {
            Kind = DriverKind.RacialHD,
            Id = "racial_hd:outsider",
            Name = "Outsider",
            HitDie = 8,
            SkillPointsPerLevel = 8,
            ClassSkills = new List<string>
            {
                "bluff", "craft", "knowledge_planes", "listen", "search",
                "sense_motive", "spot", "survival"
            },
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Good,
                Ref = ProgressionRate.Good,
                Will = ProgressionRate.Good
            },
            Prerequisites = new List<Prerequisite>
            {
                new HasRace { RaceId = "outsider" }
            }
        };
    }

    [Fact]
    public void ClassDriver_Fighter_Level1_ReturnsCorrectBuffs()
    {
        var fighter = CreateFighterDriver();
        var state = new CharacterState
        {
            TotalHD = 1,
            AbilityScores = new AbilityScoreSet { STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8 }
        };

        var buffs = fighter.GetPermabuffs(state, 1);

        Assert.Contains(buffs, b => b is AddHitDie hd && hd.DieSize == 10);
        Assert.Contains(buffs, b => b is GrantSkillPoints sp && sp.BasePoints == 2);
        Assert.Contains(buffs, b => b is AddClassSkills);
        Assert.Contains(buffs, b => b is AddBAB bab && bab.Progression == BABProgression.Good && bab.ClassLevel == 1);
        Assert.Contains(buffs, b => b is AddSaves);
        Assert.Contains(buffs, b => b is GrantFeatSlot fs && fs.Restriction == "fighter_bonus");
    }

    [Fact]
    public void ClassDriver_Fighter_Level3_NoBonusFeat()
    {
        var fighter = CreateFighterDriver();
        var state = new CharacterState
        {
            TotalHD = 3,
            AbilityScores = new AbilityScoreSet { STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8 }
        };

        var buffs = fighter.GetPermabuffs(state, 3);

        // Level 3 has no fighter bonus feat
        Assert.DoesNotContain(buffs, b => b is GrantFeatSlot);
    }

    [Fact]
    public void ClassDriver_Fighter_SkipsBABAndSaves_PostEpic()
    {
        var fighter = CreateFighterDriver();
        var state = new CharacterState
        {
            TotalHD = 21,
            AbilityScores = new AbilityScoreSet { STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8 }
        };

        var buffs = fighter.GetPermabuffs(state, 21);

        Assert.Contains(buffs, b => b is AddHitDie);
        Assert.Contains(buffs, b => b is GrantSkillPoints);
        Assert.DoesNotContain(buffs, b => b is AddBAB);
        Assert.DoesNotContain(buffs, b => b is AddSaves);
    }

    [Fact]
    public void ClassDriver_Fighter_ApplyBuffsToState_Level1()
    {
        var fighter = CreateFighterDriver();
        var state = new CharacterState
        {
            TotalHD = 1,
            AbilityScores = new AbilityScoreSet { STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8 }
        };

        var buffs = fighter.GetPermabuffs(state, 1);
        foreach (var buff in buffs)
            buff.Apply(state);

        Assert.Equal(12, state.HP);           // 10 (max d10) + 2 (CON mod)
        Assert.Equal(1, state.BaseBAB);        // Good BAB level 1
        Assert.Equal(2, state.BaseSaves.Fort); // Good Fort level 1
        Assert.Equal(0, state.BaseSaves.Ref);  // Poor Ref level 1
        Assert.Equal(0, state.BaseSaves.Will); // Poor Will level 1
        Assert.Equal(8, state.UnspentSkillPoints); // (2 + 0) * 4 = 8
        Assert.Contains("climb", state.ClassSkills);
        Assert.Equal(1, state.PendingBonusFeatSlots); // fighter bonus feat
    }

    [Fact]
    public void ClassDriver_Fighter_Levels1to3_CumulativeStats()
    {
        var fighter = CreateFighterDriver();
        var state = new CharacterState
        {
            AbilityScores = new AbilityScoreSet { STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8 }
        };

        for (int lvl = 1; lvl <= 3; lvl++)
        {
            state.TotalHD = lvl;
            var buffs = fighter.GetPermabuffs(state, lvl);
            foreach (var buff in buffs)
                buff.Apply(state);
        }

        Assert.Equal(3, state.BaseBAB);         // Good BAB 3 levels
        Assert.Equal(3, state.BaseSaves.Fort);   // Good Fort at 3: 2+3/2 = 3
        Assert.Equal(1, state.BaseSaves.Ref);    // Poor Ref at 3: 3/3 = 1
        Assert.Equal(1, state.BaseSaves.Will);   // Poor Will at 3: 3/3 = 1
        // HP: 12 (max d10+2) + 8 (avg d10+2) + 8 = 28
        Assert.Equal(28, state.HP);
        // Skills: 8 (first HD x4) + 2 + 2 = 12
        Assert.Equal(12, state.UnspentSkillPoints);
    }

    [Fact]
    public void RacialHDDriver_Outsider_Level1()
    {
        var outsider = CreateOutsiderDriver();
        var state = new CharacterState
        {
            TotalHD = 1,
            RaceId = "outsider",
            AbilityScores = new AbilityScoreSet { STR = 16, DEX = 14, CON = 14, INT = 14, WIS = 12, CHA = 10 }
        };

        var buffs = outsider.GetPermabuffs(state, 1);
        foreach (var buff in buffs)
            buff.Apply(state);

        Assert.Equal(10, state.HP);            // 8 (max d8) + 2 (CON mod)
        Assert.Equal(1, state.BaseBAB);         // Good BAB level 1
        Assert.Equal(2, state.BaseSaves.Fort);  // Good all saves level 1
        Assert.Equal(2, state.BaseSaves.Ref);
        Assert.Equal(2, state.BaseSaves.Will);
        Assert.Equal(40, state.UnspentSkillPoints); // (8 + 2) * 4 = 40
        Assert.Contains("bluff", state.ClassSkills);
    }

    [Fact]
    public void RacialHDDriver_Outsider_8Levels()
    {
        var outsider = CreateOutsiderDriver();
        var state = new CharacterState
        {
            RaceId = "outsider",
            AbilityScores = new AbilityScoreSet { STR = 16, DEX = 14, CON = 14, INT = 14, WIS = 12, CHA = 10 }
        };

        for (int lvl = 1; lvl <= 8; lvl++)
        {
            state.TotalHD = lvl;
            var buffs = outsider.GetPermabuffs(state, lvl);
            foreach (var buff in buffs)
                buff.Apply(state);
        }

        Assert.Equal(8, state.BaseBAB);         // Good BAB 8 levels
        Assert.Equal(6, state.BaseSaves.Fort);   // Good: 2+8/2 = 6
        Assert.Equal(6, state.BaseSaves.Ref);    // All good for outsider
        Assert.Equal(6, state.BaseSaves.Will);
        // HP: 10 (max d8+2) + 7*7 (avg d8 is 5, +2 CON) = 10 + 49 = 59
        Assert.Equal(59, state.HP);
        // Skills: 40 (first HD) + 7*10 = 40 + 70 = 110
        Assert.Equal(110, state.UnspentSkillPoints);
    }

    [Fact]
    public void RacialHDDriver_Prerequisite_FailsForWrongRace()
    {
        var outsider = CreateOutsiderDriver();
        var state = new CharacterState
        {
            RaceId = "human",
            TotalHD = 1
        };

        foreach (var prereq in outsider.Prerequisites)
        {
            Assert.False(prereq.IsMet(state));
        }
    }
}
