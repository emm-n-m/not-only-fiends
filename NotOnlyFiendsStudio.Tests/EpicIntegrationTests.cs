using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public class EpicIntegrationTests
{
    private (ContentRegistry registry, ReplayStudio engine) CreateStudio()
    {
        var registry = TestContentHelper.LoadAllPacks();
        return (registry, new ReplayStudio(registry));
    }

    [Fact]
    public void EpicProgression_Fighter25_BABAndSavesCorrect()
    {
        var (_, engine) = CreateStudio();

        var ticks = Enumerable.Range(0, 25)
            .Select(i => new Tick
            {
                DriverId = "class:fighter",
                Choices = i == 3 || i == 7 || i == 11 || i == 15 || i == 19 || i == 23
                    ? new TickChoices { AbilityIncrease = Ability.STR }
                    : new TickChoices()
            }).ToList();

        var character = new Character
        {
            Name = "Epic Fighter",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = ticks
        };

        var state = engine.Evaluate(character);

        Assert.Equal(25, state.TotalHD);
        Assert.Equal(25, state.ClassLevels["class:fighter"]);

        // Pre-epic BAB (Good, 20 levels): 20
        Assert.Equal(20, state.BaseBAB);

        // Epic attack bonus: +1 at odd HD past 20 → HD 21, 23, 25 = +3
        Assert.Equal(3, state.EpicAttackBonus);
        Assert.Equal(23, state.EffectiveBAB);

        // Epic save bonus: +1 at even HD past 20 → HD 22, 24 = +2
        Assert.Equal(2, state.EpicSaveBonus);

        // Pre-epic saves (Fighter 20): Fort good(2+10=12), Ref poor(20/3=6), Will poor(20/3=6)
        Assert.Equal(12, state.BaseSaves.Fort);
        Assert.Equal(6, state.BaseSaves.Ref);
        Assert.Equal(6, state.BaseSaves.Will);

        // Effective saves (base + epic + ability mod: CON+2, DEX+2, WIS+1)
        Assert.Equal(16, state.EffectiveSaves.Fort);
        Assert.Equal(10, state.EffectiveSaves.Ref);
        Assert.Equal(9, state.EffectiveSaves.Will);

        // Ability score increases at HD 4,8,12,16,20,24 = 6 increases
        Assert.Equal(22, state.AbilityScores.STR); // 16 + 6

        // Epic feat slots: HD 21, 24 = 2
        // Standard feat slots: HD 1,3,6,9,12,15,18 = 7
        // Human bonus: 1
        // Fighter bonus: at levels 1,2,4,6,8,10,12,14,16,18,20 = 11
        // Total standard: 7 + 1 + 2 = 10
        // Total bonus: 11
    }

    [Fact]
    public void EpicProgression_BABFreezes_AtHD20()
    {
        var (_, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Pre-Post Epic",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10
            },
            Ticks = Enumerable.Range(0, 22).Select(_ => new Tick { DriverId = "class:fighter" }).ToList()
        };

        // Check at HD 20
        var state20 = engine.Evaluate(character, upToHD: 20);
        Assert.Equal(20, state20.BaseBAB); // Good BAB at 20 = 20
        Assert.Equal(0, state20.EpicAttackBonus);

        // Check at HD 21
        var state21 = engine.Evaluate(character, upToHD: 21);
        Assert.Equal(20, state21.BaseBAB); // Still 20 — class BAB frozen
        Assert.Equal(1, state21.EpicAttackBonus); // +1 at HD 21 (odd)
        Assert.Equal(21, state21.EffectiveBAB);

        // Check at HD 22
        var state22 = engine.Evaluate(character, upToHD: 22);
        Assert.Equal(20, state22.BaseBAB);
        Assert.Equal(1, state22.EpicAttackBonus); // Still 1 — HD 22 is even
        Assert.Equal(1, state22.EpicSaveBonus); // +1 save at HD 22 (even)
    }

    [Fact]
    public void EpicFeatSlots_AtCorrectHD()
    {
        var (_, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Epic Feat Slots",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10
            },
            Ticks = Enumerable.Range(0, 30).Select(_ => new Tick { DriverId = "class:fighter" }).ToList()
        };

        // Standard feat slots (HD 1-18): HD 1,3,6,9,12,15,18 = 7
        // Human bonus: 1
        // Epic feat slots: HD 21, 24, 27, 30 = 4
        // Fighter bonus feats (levels 1-20 even): 1,2,4,6,8,10,12,14,16,18,20 = 11
        // Total standard: 7 + 1 + 4 = 12
        // Total bonus: 11
        var state = engine.Evaluate(character);
        Assert.Equal(12, state.PendingFeatSlots);
        Assert.Equal(11, state.PendingBonusFeatSlots);
    }

    [Fact]
    public void PermanentEvent_TomeOfINT_AffectsSkillPointsFromSubsequentHD()
    {
        var (_, engine) = CreateStudio();

        // Fighter with INT 10 (mod +0, 2 skill points per level)
        // Tome of INT +2 applied before HD 9 (index 8)
        // HD 1-8: 2 skill points each (except HD 1: 2*4=8)
        // HD 9+: 3 skill points each (2 base + 1 INT mod)

        var character = new Character
        {
            Name = "Tome Test",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10
            },
            Ticks = Enumerable.Range(0, 12).Select(_ => new Tick { DriverId = "class:fighter" }).ToList(),
            PermanentEvents = new List<PermanentEvent>
            {
                new()
                {
                    BeforeTick = 8, // before HD 9
                    Permabuffs = new List<Permabuff>
                    {
                        new ModifyAttribute { Target = AttributeTarget.AbilityScore, AbilityScore = Ability.INT, Value = 2 }
                    }
                }
            }
        };

        var state = engine.Evaluate(character);

        Assert.Equal(12, state.AbilityScores.INT);

        // Human: +1 skill point per HD (x4 at first HD)
        // HD 1: (2+0)*4=8 + 1*4=4 = 12
        // HD 2-8: (2+0+1)*7 = 21
        // HD 9-12: (2+1+1)*4 = 16  (INT now 12, mod +1)
        // Total: 12 + 21 + 16 = 49
        Assert.Equal(49, state.UnspentSkillPoints);
    }

    [Fact]
    public void ComplexBuild_Outsider8_HalfFiend_Fighter5()
    {
        var (_, engine) = CreateStudio();

        var ticks = new List<Tick>();
        // 8 outsider racial HD
        for (int i = 0; i < 8; i++)
            ticks.Add(new Tick
            {
                DriverId = "racial_hd:outsider",
                Choices = (i + 1) % 4 == 0
                    ? new TickChoices { AbilityIncrease = Ability.STR }
                    : new TickChoices()
            });
        // 5 fighter levels (HD 9-13). HD 12 = ability increase
        for (int i = 0; i < 5; i++)
            ticks.Add(new Tick
            {
                DriverId = "class:fighter",
                Choices = (8 + i + 1) % 4 == 0
                    ? new TickChoices { AbilityIncrease = Ability.STR }
                    : new TickChoices()
            });

        var character = new Character
        {
            Name = "Complex Build",
            RaceId = "race:outsider",
            TemplateIds = new List<string> { "template:half_fiend" },
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 14, WIS = 12, CHA = 10
            },
            Ticks = ticks
        };

        var state = engine.Evaluate(character);

        Assert.Equal(13, state.TotalHD); // 8 outsider + 5 fighter
        Assert.Equal(5, state.ClassLevels["class:fighter"]);

        // Ability scores: base + half-fiend mods + 3 increases (HD 4,8,12)
        Assert.Equal(23, state.AbilityScores.STR); // 16 + 4 (HF) + 3 (increases)

        // BAB: Outsider Good 8 = 8, Fighter Good 5 = 5, total = 13
        Assert.Equal(13, state.BaseBAB);

        // Saves: Outsider all-good 8 = 6, Fighter Fort good 5 = 4, Ref poor 5 = 1, Will poor 5 = 1
        Assert.Equal(10, state.BaseSaves.Fort); // 6 + 4
        Assert.Equal(7, state.BaseSaves.Ref);   // 6 + 1
        Assert.Equal(7, state.BaseSaves.Will);  // 6 + 1

        // LA = 4 (half-fiend), ECL = 13 + 4 = 17
        Assert.Equal(4, state.LevelAdjustment);
        Assert.Equal(17, state.ECL);

        // SR = TotalHD + 10 = 23
        Assert.Equal(23, state.SpellResistance);

        // SLAs at HD 13: darkness(1), desecrate(3), unholy_blight(5), poison(7), contagion(9), blasphemy(11), unholy_aura(13)
        Assert.Equal(7, state.SLAs.Count);
        Assert.Contains(state.SLAs, s => s.Id == "hf_sla_unholy_aura");
        Assert.DoesNotContain(state.SLAs, s => s.Id == "hf_sla_haste"); // HD 15 needed

        // Natural armor, attacks from half-fiend
        Assert.Equal(1, state.NaturalArmor);
        Assert.Equal(2, state.NaturalAttacks.Count);

        // Movement
        Assert.Equal(30, state.Speeds[MovementMode.Land]);
        Assert.Equal(60, state.Speeds[MovementMode.Fly]);
    }

    [Fact]
    public void EvaluateUpToHD_SnapshotsProgressionCorrectly()
    {
        var (_, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Snapshot Test",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = Enumerable.Range(0, 10).Select(_ => new Tick { DriverId = "class:fighter" }).ToList()
        };

        var state1 = engine.Evaluate(character, upToHD: 1);
        var state5 = engine.Evaluate(character, upToHD: 5);
        var state10 = engine.Evaluate(character, upToHD: 10);

        Assert.Equal(1, state1.TotalHD);
        Assert.Equal(1, state1.BaseBAB);

        Assert.Equal(5, state5.TotalHD);
        Assert.Equal(5, state5.BaseBAB);

        Assert.Equal(10, state10.TotalHD);
        Assert.Equal(10, state10.BaseBAB);
    }
}
