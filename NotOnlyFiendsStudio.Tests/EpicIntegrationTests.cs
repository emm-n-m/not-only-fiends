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
    public void EpicSpellcasting_UsesKnowledgeRanksForOpenSlots_AndKeepsDevelopedSpells()
    {
        var (registry, engine) = CreateStudio();
        var ticks = Enumerable.Range(0, 22)
            .Select(_ => new Tick { DriverId = "class:sorcerer", Choices = new TickChoices() })
            .ToList();
        ticks[20].Choices = new TickChoices
        {
            FeatIds = new List<string> { "feat:epic_spellcasting" },
            SkillAllocations = new List<SkillAllocation>
            {
                new() { SkillId = "skill:knowledge_arcana", HalfRanks = 48 },
                new() { SkillId = "skill:spellcraft", HalfRanks = 48 },
            },
            SpellSelections = new List<SpellSelection>
            {
                new()
                {
                    ClassId = EpicSpellcasting.CharismaListId,
                    SpellLevel = 10,
                    SpellId = "spell:frog_mass",
                },
            },
        };
        var character = new Character
        {
            Name = "Epic Sorcerer",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 12, INT = 14, WIS = 10, CHA = 24,
            },
            Ticks = ticks,
        };

        var state = engine.Evaluate(character);
        var epic = state.Spellcasting[EpicSpellcasting.CharismaListId];

        Assert.Equal(SpellAcquisition.Developed, epic.Acquisition);
        Assert.Equal(10, epic.MaxSpellLevel);
        Assert.Equal(2, epic.SpellsPerDay[10]);
        Assert.Contains(epic.SelectedSpells, spell => spell.SpellId == "spell:frog_mass");
        Assert.DoesNotContain(state.Warnings, warning =>
            warning.Message.Contains("unknown spellcasting class", StringComparison.Ordinal));
        Assert.Contains(registry.GetSpellsForList(EpicSpellcasting.CharismaListId),
            spell => spell.Id == "spell:frog_mass");
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

        // Ability scores: racial-HD ticks do not grant selectable increases; the class tick at
        // total HD 12 does. Base 16 + half-fiend 4 + one class-tick increase = 21.
        Assert.Equal(21, state.AbilityScores.STR);

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
        Assert.Equal(30, state.Speeds[MovementMode.Fly]);
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

    [Fact]
    public void EpicAssassin_ContinuesSneakAttackAndBonusFeatsPastTenthLevel()
    {
        var (_, engine) = CreateStudio();

        var ticks = Enumerable.Repeat("class:rogue", 10)
            .Concat(Enumerable.Repeat("class:assassin", 20))
            .Select(driverId => new Tick { DriverId = driverId })
            .ToList();
        var character = new Character
        {
            Name = "Epic Assassin",
            RaceId = "race:human",
            Alignment = Alignment.NE,
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 16, CON = 12, INT = 14, WIS = 10, CHA = 10
            },
            Ticks = ticks
        };

        var state = engine.Evaluate(character);

        // Rogue 1-9 odd levels (5d6) plus assassin 1-19 odd levels (10d6): the epic assassin's
        // sneak attack keeps rising +1d6 every two levels after 9th.
        Assert.Equal(15, state.Counters["sneak_attack_dice"]);
        Assert.DoesNotContain(state.Warnings, warning =>
            warning.Message.Contains("exceeds max level", StringComparison.Ordinal));

        // Control: the same 30 character levels with the assassin capped at 10 and the rest in a
        // class that grants no slots of its own. The difference is the epic assassin's bonus feat
        // every four levels after 10th — assassin 14 and 18.
        character.Ticks = Enumerable.Repeat("class:rogue", 10)
            .Concat(Enumerable.Repeat("class:assassin", 10))
            .Concat(Enumerable.Repeat("class:sorcerer", 10))
            .Select(driverId => new Tick { DriverId = driverId })
            .ToList();
        var control = engine.Evaluate(character);

        Assert.Equal(10, control.Counters["sneak_attack_dice"]);
        Assert.Equal(control.FeatSlots.Count(slot => slot.Restriction == null) + 2,
            state.FeatSlots.Count(slot => slot.Restriction == null));
    }

    [Fact]
    public void PrestigeClassPastTenth_WarnsOnlyWhileTheCharacterIsStillNonEpic()
    {
        var (_, engine) = CreateStudio();

        // Assassin 11 is reached at HD 17 here, which the SRD forbids: "a ten-level prestige
        // class can progress beyond 10th level, but only if the character level is already 20th
        // or higher". Assassin 15 lands at HD 21 and is legal.
        var ticks = Enumerable.Repeat("class:rogue", 6)
            .Concat(Enumerable.Repeat("class:assassin", 20))
            .Select(driverId => new Tick { DriverId = driverId })
            .ToList();
        var character = new Character
        {
            Name = "Premature Epic Assassin",
            RaceId = "race:human",
            Alignment = Alignment.NE,
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 16, CON = 12, INT = 14, WIS = 10, CHA = 10
            },
            Ticks = ticks
        };

        var state = engine.Evaluate(character);

        var overruns = state.Warnings
            .Where(warning => warning.Message.Contains("exceeds max level", StringComparison.Ordinal))
            .Select(warning => warning.TickIndex)
            .ToList();
        Assert.Equal(new int?[] { 17, 18, 19, 20 }, overruns);
    }

    [Fact]
    public void PerfectWight_EntersOnlyOnceEpicAndScalesItsFourDailyPowers()
    {
        var (_, engine) = CreateStudio();

        // 24 ranks of Hide and Move Silently cannot exist before 21st level (max ranks = level + 3),
        // which is the SRD's own reason this class is epic-only. Buy 4 ranks of each at 1st level
        // and 1 more per level after, then take the class from HD 22 on.
        var ticks = new List<Tick>();
        for (var level = 1; level <= 21; level++)
        {
            var halfRanks = level == 1 ? 8 : 2;
            ticks.Add(new Tick
            {
                DriverId = "class:rogue",
                Choices = new TickChoices
                {
                    SkillAllocations = new List<SkillAllocation>
                    {
                        new() { SkillId = "skill:hide", HalfRanks = halfRanks },
                        new() { SkillId = "skill:move_silently", HalfRanks = halfRanks },
                    }
                }
            });
        }
        ticks.AddRange(Enumerable.Range(0, 10).Select(_ => new Tick { DriverId = "class:perfect_wight" }));
        ticks[^1].Choices = new TickChoices { FeatIds = new List<string> { "feat:self_concealment" } };

        var character = new Character
        {
            Name = "Perfect Wight",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 30, CON = 12, INT = 14, WIS = 10, CHA = 10
            },
            Ticks = ticks
        };

        var state = engine.Evaluate(character);

        Assert.Equal(10, state.ClassLevels["class:perfect_wight"]);
        Assert.Equal(24, state.SkillHalfRanks["skill:hide"] / 2);
        // Rogue 19 caps sneak attack at 10d6, which is the class's own entry requirement.
        Assert.Equal(10, state.Counters["sneak_attack_dice"]);
        Assert.DoesNotContain(state.Warnings, warning =>
            warning.Message.Contains("prerequisite not met for Perfect Wight", StringComparison.Ordinal));

        // 1st and 6th, 2nd and 7th, 3rd and 8th, 4th and 9th.
        Assert.Equal(2, state.Counters["perfect_wight_greater_invisibility_uses"]);
        Assert.Equal(2, state.Counters["perfect_wight_improved_legerdemain_uses"]);
        Assert.Equal(2, state.Counters["perfect_wight_incorporeal_uses"]);
        Assert.Equal(2, state.Counters["perfect_wight_shadow_form_uses"]);
        Assert.Contains(state.Abilities, ability => ability.Id == "perfect_wight_shadow_form");
    }

    [Fact]
    public void PerfectWight_TakenBeforeEpicWarnsOnEveryUnmetRequirement()
    {
        var (_, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Premature Wight",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 14, CON = 12, INT = 14, WIS = 10, CHA = 10
            },
            Ticks = new List<Tick> { new() { DriverId = "class:perfect_wight" } }
        };

        var state = engine.Evaluate(character);

        var unmet = state.Warnings
            .Where(warning => warning.Message.Contains("prerequisite not met for Perfect Wight", StringComparison.Ordinal))
            .Select(warning => warning.Message)
            .ToList();
        Assert.Contains(unmet, message => message.Contains("21", StringComparison.Ordinal));
        Assert.Contains(unmet, message => message.Contains("skill:hide", StringComparison.Ordinal));
        Assert.Contains(unmet, message => message.Contains("sneak_attack_dice", StringComparison.Ordinal));
        Assert.Contains(unmet, message => message.Contains("feat:self_concealment", StringComparison.Ordinal));
    }
}
