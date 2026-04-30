using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public class CoreModelTests
{
    [Fact]
    public void Character_CanBeCreated()
    {
        var character = new Character
        {
            Name = "Test Fighter",
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter" }
            }
        };

        Assert.Equal("Test Fighter", character.Name);
        Assert.Single(character.Ticks);
        Assert.Equal("class:fighter", character.Ticks[0].DriverId);
    }

    [Fact]
    public void CharacterState_CanBeCreated()
    {
        var state = new CharacterState
        {
            RaceId = "human",
            Type = CreatureType.Humanoid,
            Size = Size.Medium,
            TotalHD = 1,
            BaseBAB = 1,
            HP = 12
        };

        Assert.Equal(CreatureType.Humanoid, state.Type);
        Assert.Equal(1, state.EffectiveBAB);
        Assert.Equal(12, state.HP);
    }

    [Fact]
    public void AbilityScoreSet_ModifierCalculation()
    {
        Assert.Equal(-1, AbilityScoreSet.Modifier(8));
        Assert.Equal(0, AbilityScoreSet.Modifier(10));
        Assert.Equal(0, AbilityScoreSet.Modifier(11));
        Assert.Equal(1, AbilityScoreSet.Modifier(12));
        Assert.Equal(2, AbilityScoreSet.Modifier(14));
        Assert.Equal(3, AbilityScoreSet.Modifier(16));
        Assert.Equal(5, AbilityScoreSet.Modifier(20));
        // Negative scores
        Assert.Equal(-5, AbilityScoreSet.Modifier(1));
    }

    [Fact]
    public void AbilityScoreSet_GetAndSetScore()
    {
        var scores = new AbilityScoreSet { STR = 10, DEX = 12, CON = 14, INT = 16, WIS = 18, CHA = 20 };

        Assert.Equal(10, scores.GetScore(Ability.STR));
        Assert.Equal(12, scores.GetScore(Ability.DEX));
        Assert.Equal(14, scores.GetScore(Ability.CON));
        Assert.Equal(16, scores.GetScore(Ability.INT));
        Assert.Equal(18, scores.GetScore(Ability.WIS));
        Assert.Equal(20, scores.GetScore(Ability.CHA));

        scores.SetScore(Ability.STR, 22);
        Assert.Equal(22, scores.GetScore(Ability.STR));
    }

    [Fact]
    public void CharacterState_ECL_Computed()
    {
        var state = new CharacterState { TotalHD = 8, LevelAdjustment = 4 };
        Assert.Equal(12, state.ECL);
    }

    [Fact]
    public void CharacterState_EffectiveSaves_IncludesEpicBonusAndAbilityMods()
    {
        var state = new CharacterState
        {
            BaseSaves = new SaveSet { Fort = 6, Ref = 2, Will = 2 },
            EpicSaveBonus = 3,
            AbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 14, CON = 16, INT = 10, WIS = 12, CHA = 10
            }
        };

        // Fort = 6 + 3 epic + 3 CON = 12
        Assert.Equal(12, state.EffectiveSaves.Fort);
        // Ref = 2 + 3 epic + 2 DEX = 7
        Assert.Equal(7, state.EffectiveSaves.Ref);
        // Will = 2 + 3 epic + 1 WIS = 6
        Assert.Equal(6, state.EffectiveSaves.Will);
    }
}
