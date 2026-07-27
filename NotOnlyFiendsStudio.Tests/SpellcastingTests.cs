using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public class SpellcastingTests
{
    private (ContentRegistry registry, ReplayStudio engine) CreateStudio()
    {
        var registry = TestContentHelper.LoadAllPacks();
        return (registry, new ReplayStudio(registry));
    }

    [Fact]
    public void Sorcerer10_CasterLevel10_MaxSpell5()
    {
        var (_, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Sorcerer 10",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 8, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 18
            },
            Ticks = Enumerable.Range(0, 10).Select(_ => new Tick { DriverId = "class:sorcerer" }).ToList()
        };

        var state = engine.Evaluate(character);

        Assert.Equal(10, state.TotalHD);
        Assert.True(state.Spellcasting.ContainsKey("class:sorcerer"));

        var sc = state.Spellcasting["class:sorcerer"];
        Assert.Equal(CastingType.Arcane, sc.CastingType);
        Assert.Equal(Ability.CHA, sc.CastingStat);
        Assert.Equal(10, sc.CasterLevel);
        Assert.Equal(5, sc.MaxSpellLevel);

        // Spells per day at level 10: 6/6/6/6/5/3
        Assert.Equal(6, sc.SpellsPerDay[0]);
        Assert.Equal(6, sc.SpellsPerDay[1]);
        Assert.Equal(6, sc.SpellsPerDay[2]);
        Assert.Equal(6, sc.SpellsPerDay[3]);
        Assert.Equal(5, sc.SpellsPerDay[4]);
        Assert.Equal(3, sc.SpellsPerDay[5]);

        // Spells known at level 10: 9/5/4/3/2/1
        Assert.NotNull(sc.SpellsKnown);
        Assert.Equal(9, sc.SpellsKnown![0]);
        Assert.Equal(5, sc.SpellsKnown[1]);
        Assert.Equal(4, sc.SpellsKnown[2]);
        Assert.Equal(3, sc.SpellsKnown[3]);
        Assert.Equal(2, sc.SpellsKnown[4]);
        Assert.Equal(1, sc.SpellsKnown[5]);
    }

    [Fact]
    public void Sorcerer5_EldritchKnight5_CasterLevel9()
    {
        var (registry, engine) = CreateStudio();

        // Need to add the martial weapon proficiency feat since EK requires it
        registry.RegisterFeat(new FeatDefinition
        {
            Id = "feat:weapon_proficiency_martial",
            Name = "Martial Weapon Proficiency",
            Type = FeatType.General
        });

        var ticks = new List<Tick>();
        // 5 levels of sorcerer
        for (int i = 0; i < 5; i++)
            ticks.Add(new Tick { DriverId = "class:sorcerer" });
        // 5 levels of eldritch knight (loses 1st level spellcasting, advances 2-10)
        for (int i = 0; i < 5; i++)
            ticks.Add(new Tick { DriverId = "class:eldritch_knight" });

        var character = new Character
        {
            Name = "Sorc5/EK5",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 14, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 16
            },
            Ticks = ticks
        };

        var state = engine.Evaluate(character);

        Assert.Equal(10, state.TotalHD);
        Assert.Equal(5, state.ClassLevels["class:sorcerer"]);
        Assert.Equal(5, state.ClassLevels["class:eldritch_knight"]);

        // Sorcerer base caster level: 5
        // EK advances from level 2 onwards (loses 1st): +4 advances from EK 2,3,4,5
        // Total caster level: 5 + 4 = 9
        var sc = state.Spellcasting["class:sorcerer"];
        Assert.Equal(9, sc.CasterLevel);

        // At caster level 9, sorcerer max spell level = 4 (from sorcerer level 9 progression)
        Assert.Equal(4, sc.MaxSpellLevel);
    }

    [Fact]
    public void Cleric5_DivineSpellcasting()
    {
        var (_, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Cleric 5",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 14, DEX = 10, CON = 14, INT = 10, WIS = 16, CHA = 8
            },
            Ticks = Enumerable.Range(0, 5).Select(_ => new Tick { DriverId = "class:cleric" }).ToList()
        };

        var state = engine.Evaluate(character);

        Assert.True(state.Spellcasting.ContainsKey("class:cleric"));
        var sc = state.Spellcasting["class:cleric"];
        Assert.Equal(CastingType.Divine, sc.CastingType);
        Assert.Equal(Ability.WIS, sc.CastingStat);
        Assert.Equal(5, sc.CasterLevel);
        Assert.Equal(3, sc.MaxSpellLevel);

        // Cleric 5: 5/3/2/1 spells per day (not counting bonus spells)
        Assert.Equal(5, sc.SpellsPerDay[0]);
        Assert.Equal(3, sc.SpellsPerDay[1]);
        Assert.Equal(2, sc.SpellsPerDay[2]);
        Assert.Equal(1, sc.SpellsPerDay[3]);

        // Cleric has no spells known table (prepared caster)
        Assert.Null(sc.SpellsKnown);

        // Cleric gets turn undead
        Assert.Contains(state.Abilities, a => a.Id == "turn_undead");
    }

    [Fact]
    public void Sorcerer1_HasBasicSpellcasting()
    {
        var (_, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Sorc 1",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 8, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 18
            },
            Ticks = new List<Tick> { new() { DriverId = "class:sorcerer" } }
        };

        var state = engine.Evaluate(character);

        var sc = state.Spellcasting["class:sorcerer"];
        Assert.Equal(1, sc.CasterLevel);
        Assert.Equal(1, sc.MaxSpellLevel);
        Assert.Equal(5, sc.SpellsPerDay[0]); // 5 cantrips
        Assert.Equal(3, sc.SpellsPerDay[1]); // 3 first-level
    }

    [Fact]
    public void SpellSelections_ArePersistedInSpellcastingState()
    {
        var (_, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Sorc 1",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 8, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 18
            },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:sorcerer",
                    Choices = new TickChoices
                    {
                        SpellSelections = new List<SpellSelection>
                        {
                            new() { ClassId = "class:sorcerer", SpellLevel = 0, SpellId = "spell:detect_magic" },
                            new() { ClassId = "class:sorcerer", SpellLevel = 1, SpellId = "spell:magic_missile" }
                        }
                    }
                }
            }
        };

        var state = engine.Evaluate(character);
        var sc = state.Spellcasting["class:sorcerer"];

        Assert.Collection(sc.SelectedSpells.OrderBy(s => s.SpellLevel).ThenBy(s => s.SpellId),
            spell =>
            {
                Assert.Equal("class:sorcerer", spell.ClassId);
                Assert.Equal(0, spell.SpellLevel);
                Assert.Equal("spell:detect_magic", spell.SpellId);
            },
            spell =>
            {
                Assert.Equal("class:sorcerer", spell.ClassId);
                Assert.Equal(1, spell.SpellLevel);
                Assert.Equal("spell:magic_missile", spell.SpellId);
            });
    }

    [Fact]
    public void SpellSelections_UnknownClass_AddsWarning()
    {
        var (_, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Sorc 1",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 8, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 18
            },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:sorcerer",
                    Choices = new TickChoices
                    {
                        SpellSelections = new List<SpellSelection>
                        {
                            new() { ClassId = "class:wizard", SpellLevel = 1, SpellId = "spell:magic_missile" }
                        }
                    }
                }
            }
        };

        var state = engine.Evaluate(character);
        var sc = state.Spellcasting["class:sorcerer"];

        Assert.Empty(sc.SelectedSpells);
        Assert.Contains(state.Warnings, w => w.Contains("unknown spellcasting class") && w.Contains("class:wizard"));
    }

    [Fact]
    public void NonCaster_NoSpellcasting()
    {
        var (_, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Fighter 1",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick> { new() { DriverId = "class:fighter" } }
        };

        var state = engine.Evaluate(character);
        Assert.Empty(state.Spellcasting);
    }

    [Fact]
    public void AdvanceSpellcasting_MultipleClasses_WithChoice_Resolves()
    {
        // Create a scenario with two arcane casters + a prestige class that advances arcane
        var registry = new ContentRegistry();
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:human", Name = "Human", Type = CreatureType.Humanoid,
            Size = Size.Medium, Speeds = new() { { MovementMode.Land, 30 } }
        });

        // Sorcerer (arcane)
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class, Id = "class:sorcerer", Name = "Sorcerer",
            HitDie = 4, SkillPointsPerLevel = 2,
            BABProgression = BABProgression.Poor,
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Poor, Ref = ProgressionRate.Poor, Will = ProgressionRate.Good },
            Spellcasting = new SpellcastingProgression
            {
                CastingType = CastingType.Arcane, CastingStat = Ability.CHA,
                SpellsPerDay = new() { { 1, new() { { 0, 5 }, { 1, 3 } } } },
                SpellsKnown = new() { { 1, new() { { 0, 4 }, { 1, 2 } } } }
            }
        });

        // Wizard (also arcane)
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class, Id = "class:wizard", Name = "Wizard",
            HitDie = 4, SkillPointsPerLevel = 2,
            BABProgression = BABProgression.Poor,
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Poor, Ref = ProgressionRate.Poor, Will = ProgressionRate.Good },
            Spellcasting = new SpellcastingProgression
            {
                CastingType = CastingType.Arcane, CastingStat = Ability.INT,
                SpellsPerDay = new() { { 1, new() { { 0, 3 }, { 1, 1 } } } }
            }
        });

        // Prestige class that advances arcane
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class, Id = "class:arcane_trickster", Name = "Arcane Trickster",
            HitDie = 4, SkillPointsPerLevel = 4,
            BABProgression = BABProgression.Poor,
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Poor, Ref = ProgressionRate.Good, Will = ProgressionRate.Good },
            PerLevelPermabuffs = new List<Permabuff>
            {
                new AdvanceSpellcasting { CastingType = CastingType.Arcane }
            }
        });

        var engine = new ReplayStudio(registry);

        // Sorc 1, Wizard 1, then Arcane Trickster 1 choosing to advance sorcerer
        var character = new Character
        {
            Name = "MultiArcane",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 8, DEX = 14, CON = 14, INT = 16, WIS = 12, CHA = 16 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:sorcerer" },
                new() { DriverId = "class:wizard" },
                new()
                {
                    DriverId = "class:arcane_trickster",
                    Choices = new TickChoices
                    {
                        ClassFeatureChoices = new Dictionary<string, List<string>>
                        {
                            ["advance_spellcasting"] = new() { "class:sorcerer" }
                        }
                    }
                }
            }
        };

        var state = engine.Evaluate(character);

        // Sorcerer should be advanced: CL 1 + 1 = 2
        Assert.Equal(2, state.Spellcasting["class:sorcerer"].CasterLevel);
        // Wizard should NOT be advanced: still CL 1
        Assert.Equal(1, state.Spellcasting["class:wizard"].CasterLevel);
        // No warning about needing selection since we provided it
        Assert.DoesNotContain(state.Warnings, w => w.Contains("multiple matching"));
    }

    [Fact]
    public void AdvanceSpellcasting_MultipleClasses_NoChoice_Warns()
    {
        var registry = new ContentRegistry();
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:human", Name = "Human", Type = CreatureType.Humanoid,
            Size = Size.Medium, Speeds = new() { { MovementMode.Land, 30 } }
        });

        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class, Id = "class:sorcerer", Name = "Sorcerer",
            HitDie = 4, SkillPointsPerLevel = 2,
            BABProgression = BABProgression.Poor,
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Poor, Ref = ProgressionRate.Poor, Will = ProgressionRate.Good },
            Spellcasting = new SpellcastingProgression
            {
                CastingType = CastingType.Arcane, CastingStat = Ability.CHA,
                SpellsPerDay = new() { { 1, new() { { 0, 5 }, { 1, 3 } } } }
            }
        });

        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class, Id = "class:wizard", Name = "Wizard",
            HitDie = 4, SkillPointsPerLevel = 2,
            BABProgression = BABProgression.Poor,
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Poor, Ref = ProgressionRate.Poor, Will = ProgressionRate.Good },
            Spellcasting = new SpellcastingProgression
            {
                CastingType = CastingType.Arcane, CastingStat = Ability.INT,
                SpellsPerDay = new() { { 1, new() { { 0, 3 }, { 1, 1 } } } }
            }
        });

        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class, Id = "class:prestige_arcane", Name = "Prestige Arcane",
            HitDie = 4, SkillPointsPerLevel = 4,
            BABProgression = BABProgression.Poor,
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Poor, Ref = ProgressionRate.Poor, Will = ProgressionRate.Good },
            PerLevelPermabuffs = new List<Permabuff>
            {
                new AdvanceSpellcasting { CastingType = CastingType.Arcane }
            }
        });

        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "MultiArcane NoChoice",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 8, DEX = 14, CON = 14, INT = 16, WIS = 12, CHA = 16 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:sorcerer" },
                new() { DriverId = "class:wizard" },
                new() { DriverId = "class:prestige_arcane" }  // no advance_spellcasting choice
            }
        };

        var state = engine.Evaluate(character);

        // Should warn about needing to select
        Assert.Contains(state.Warnings, w => w.Contains("multiple matching"));
        // Neither should be advanced
        Assert.Equal(1, state.Spellcasting["class:sorcerer"].CasterLevel);
        Assert.Equal(1, state.Spellcasting["class:wizard"].CasterLevel);
    }

    [Fact]
    public void DomainSelection_UnknownDomain_Warns()
    {
        var (_, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Cleric Bad Domain",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 10, CON = 14, INT = 10, WIS = 16, CHA = 8 },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:cleric",
                    Choices = new TickChoices
                    {
                        ClassFeatureChoices = new Dictionary<string, List<string>>
                        {
                            ["domains"] = new() { "domain:nonexistent" }
                        }
                    }
                }
            }
        };

        var state = engine.Evaluate(character);
        Assert.Contains(state.Warnings, w => w.Contains("unknown domain") && w.Contains("nonexistent"));
        Assert.Equal(2, state.PendingDomainSelections["class:cleric"]); // 2 granted, unknown doesn't decrement
    }

    [Fact]
    public void SpellSelection_AboveMaxLevel_Warns()
    {
        var (_, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Sorc 1 OverLevel",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 8, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 18 },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:sorcerer",
                    Choices = new TickChoices
                    {
                        SpellSelections = new List<SpellSelection>
                        {
                            new() { ClassId = "class:sorcerer", SpellLevel = 9, SpellId = "spell:wish" }
                        }
                    }
                }
            }
        };

        var state = engine.Evaluate(character);
        Assert.Contains(state.Warnings, w => w.Contains("exceeds max spell level"));
    }
}
