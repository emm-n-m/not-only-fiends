using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// A monster race whose hit dice are levels of a monster <em>class</em> — PCGen's
/// <c>MONSTERCLASS:&lt;class&gt;:&lt;hd&gt;</c>. The Archfiend race is the case in hand: 24 levels of
/// <c>class:archfiend</c> arrive with the race, and a character may buy more of that same class after.
///
/// Those free HD are not levels the character earned, so they do not carry the every-four-levels
/// ability increase — but they <em>are</em> levels of a class for everything the chassis computes,
/// which is why they stay one continuous driver run rather than being split off into a
/// <c>racial_hd:</c> driver. <see cref="FreeHD_AreOneClassRunForSaves_NotTwo"/> is that half.
/// </summary>
public class MonsterClassHDTests
{
    private const int FreeHD = 24;

    private static ContentRegistry Registry()
    {
        var registry = new ContentRegistry();
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:archfiend_like",
            Name = "Archfiend-like",
            Type = CreatureType.Outsider,
            Size = Size.Medium,
            Speeds = new Dictionary<MovementMode, int> { { MovementMode.Land, 40 } },
            MonsterClassDriverId = "class:monster",
            MonsterClassHD = FreeHD,
        });
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:small_monster",
            Name = "Small Monster",
            Type = CreatureType.Outsider,
            Size = Size.Medium,
            Speeds = new Dictionary<MovementMode, int> { { MovementMode.Land, 30 } },
            MonsterClassDriverId = "class:monster",
            MonsterClassHD = 8,
        });
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:plain",
            Name = "Plain",
            Type = CreatureType.Humanoid,
            Size = Size.Medium,
            Speeds = new Dictionary<MovementMode, int> { { MovementMode.Land, 30 } },
        });
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:monster",
            Name = "Monster Class",
            HitDie = 8,
            SkillPointsPerLevel = 8,
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Good,
                Ref = ProgressionRate.Poor,
                Will = ProgressionRate.Good,
            },
        });
        return registry;
    }

    private static Character Character(string raceId, int hd) => new()
    {
        Name = "Monster",
        RaceId = raceId,
        BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
        Ticks = Enumerable.Range(0, hd)
            .Select(_ => new Tick
            {
                DriverId = "class:monster",
                // Recorded on every tick: the rule, not the character file, decides which ones take.
                Choices = new TickChoices { AbilityIncrease = Ability.CHA },
            })
            .ToList(),
    };

    /// <summary>
    /// Ember's actual schedule. PCGen gave her three increases across 36 HD, at total HD 28, 32 and
    /// 36 — character levels 4, 8 and 12 once the 24 free HD are set aside. Counting all 36 offered
    /// nine, six of them inside the free block.
    /// </summary>
    [Theory]
    [InlineData(FreeHD + 3, 0)]
    [InlineData(FreeHD + 4, 1)]
    [InlineData(FreeHD + 8, 2)]
    [InlineData(FreeHD + 12, 3)]
    public void FreeHD_DoNotCountTowardTheAbilityIncreaseSchedule(int totalHD, int expectedIncreases)
    {
        var state = new ReplayStudio(Registry()).Evaluate(Character("race:archfiend_like", totalHD));

        // Every increase in this fixture is CHA, so the delta off the base 10 is the count taken.
        Assert.Equal(10 + expectedIncreases, state.AbilityScores.CHA);
    }

    /// <summary>Without a monster class, total HD is character level and every 4th takes.</summary>
    [Fact]
    public void OrdinaryRace_CountsEveryFourthHD()
    {
        var state = new ReplayStudio(Registry()).Evaluate(Character("race:plain", 12));

        Assert.Equal(10 + 3, state.AbilityScores.CHA);
    }

    /// <summary>
    /// Why the free HD are not modelled as a separate <c>racial_hd:</c> driver carrying the first
    /// N levels. Base saves are per class, taken once from that class's total level
    /// (<c>2 + level/2</c> for a good save), so a 12-level run is +8 — while splitting it into
    /// 8 free + 4 bought would pay the level-1 "+2" twice, (2 + 8/2) + (2 + 4/2) = 10.
    /// The free HD are levels of a class; only their <em>ownership</em> differs.
    /// </summary>
    [Fact]
    public void FreeHD_AreOneClassRunForSaves_NotTwo()
    {
        var state = new ReplayStudio(Registry()).Evaluate(Character("race:small_monster", 12));

        Assert.Equal(2 + 12 / 2, state.ProgressionBaseSaves.Fort);
        Assert.Equal(2 + 12 / 2, state.ProgressionBaseSaves.Will);
        Assert.Equal(12, state.BaseBAB);
    }
}
