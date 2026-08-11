using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// Regression coverage for rules-accuracy gaps found by driving the REST API as an agent:
/// silently-accepted illegal choices, and prestige/class content that was missing the
/// prerequisites and proficiencies the 3.5e SRD specifies.
/// </summary>
public class RulesAccuracyTests
{
    private static readonly Lazy<ContentRegistry> Content = new(TestContentHelper.LoadAllPacks);

    private static Character Human(params Tick[] ticks) => new()
    {
        Name = "Rules Test",
        RaceId = "race:human",
        BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 14, CON = 14, INT = 16, WIS = 12, CHA = 14 },
        Ticks = ticks.ToList()
    };

    private static CharacterState Evaluate(Character character) =>
        new ReplayStudio(Content.Value).Evaluate(character);

    [Fact]
    public void MonkPerfectSelf_ChangesTypeAndGrantsPrintedDamageReduction()
    {
        var character = Human(Enumerable.Range(0, 20)
            .Select(_ => new Tick { DriverId = "class:monk" })
            .ToArray());
        character.Alignment = Alignment.LN;

        var state = Evaluate(character);

        Assert.Equal(CreatureType.Outsider, state.Type);
        Assert.True(state.IsLiving);
        var damageReduction = Assert.Single(state.DamageReduction,
            entry => entry.BypassedBy == "magic");
        Assert.Equal(10, damageReduction.Value);
    }

    [Fact]
    public void MonkDiamondSoul_GrantsSpellResistanceFromCurrentMonkLevel()
    {
        var character = Human(Enumerable.Range(0, 13)
            .Select(_ => new Tick { DriverId = "class:monk" })
            .ToArray());
        character.Alignment = Alignment.LN;

        var state = Evaluate(character);

        // SRD Monk: "spell resistance equal to her current monk level + 10."
        Assert.Equal(23, state.SpellResistance);
    }

    [Fact]
    public void MonkACBonus_AppliesOnlyWhenUnarmoredAndUnencumbered()
    {
        var unarmored = Human(Enumerable.Range(0, 5)
            .Select(_ => new Tick { DriverId = "class:monk" })
            .ToArray());
        unarmored.BaseAbilityScores.WIS = 18;

        var unarmoredState = Evaluate(unarmored);

        // Wisdom +4 and the monk's +1 AC bonus at monk level 5.
        Assert.Equal(5, unarmoredState.AC.Components[BonusType.Untyped]);
        Assert.Equal(10 + 5 + 2, unarmoredState.AC.Total);

        unarmored.Equipment.Add(new EquipmentEntry { ContentId = "armor:chain_shirt" });
        var armoredState = Evaluate(unarmored);

        Assert.Equal(0, armoredState.AC.Components.GetValueOrDefault(BonusType.Untyped));
        Assert.Equal(10 + 4 + AbilityScoreSet.Modifier(14), armoredState.AC.Total);
    }

    [Fact]
    public void NymphUnearthlyGrace_AddsCharismaModifierAsDeflectionAC()
    {
        var state = Evaluate(new Character
        {
            Name = "Nymph",
            RaceId = "race:nymph",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 6)
                .Select(_ => new Tick { DriverId = "racial_hd:fey" })
                .ToList()
        });

        // The race's +8 Charisma makes the deflection bonus +4.
        Assert.Equal(4, state.AbilityModifier(Ability.CHA));
        Assert.Equal(4, state.AC.Components[BonusType.Deflection]);
    }

    [Fact]
    public void ShamblingMound_ExposesPrintedFireResistanceAndPlantImmunities()
    {
        var state = Evaluate(new Character
        {
            Name = "Shambling Mound",
            RaceId = "race:shambling_mound",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 8)
                .Select(_ => new Tick { DriverId = "racial_hd:plant" })
                .ToList()
        });

        // SRD Shambling Mound special qualities: immunity to electricity and resistance to fire 10.
        Assert.Equal(10, state.Resistances["fire"]);
        Assert.Contains("electricity", state.Immunities);
        Assert.Contains("mind-affecting", state.Immunities);
        Assert.Contains("critical hits", state.Immunities);
    }

    [Fact]
    public void Grimlock_ExposesPrintedSightBasedImmunities()
    {
        var state = Evaluate(new Character
        {
            Name = "Grimlock",
            RaceId = "race:grimlock",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new()
            {
                new Tick { DriverId = "racial_hd:monstrous_humanoid" },
                new Tick { DriverId = "racial_hd:monstrous_humanoid" }
            }
        });

        Assert.Contains("gaze attacks", state.Immunities);
        Assert.Contains("visual effects", state.Immunities);
        Assert.Contains("illusions", state.Immunities);
    }

    [Fact]
    public void CreatureState_ExposesFastHealingAndTurnResistance()
    {
        var imp = Evaluate(new Character
        {
            Name = "Imp",
            RaceId = "race:companion_devil_imp",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new() { new Tick { DriverId = "racial_hd:outsider" } }
        });
        Assert.Equal(2, imp.FastHealing);

        var shadow = Evaluate(new Character
        {
            Name = "Shadow",
            RaceId = "race:companion_shadow",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new() { new Tick { DriverId = "racial_hd:undead" } }
        });
        Assert.Equal(2, shadow.TurnResistance);

        var vampire = Evaluate(new Character
        {
            Name = "Vampire",
            RaceId = "race:human",
            TemplateIds = new() { "template:vampire" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new() { new Tick { DriverId = "class:fighter" } }
        });
        Assert.Equal(5, vampire.FastHealing);
        Assert.Equal(4, vampire.TurnResistance);
    }

    [Theory]
    [InlineData("template:familiar_standard", 11, 16)]
    [InlineData("template:special_mount_standard", 15, 20)]
    public void CompanionProgression_ExposesPrintedSpellResistance(string templateId, int masterLevel, int expectedSr)
    {
        var state = Evaluate(new Character
        {
            Name = "Companion",
            RaceId = "race:familiar_toad",
            TemplateIds = new() { templateId },
            CompanionOrigin = new CompanionOrigin { LinkType = "test", EffectiveMasterLevel = masterLevel },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new() { new Tick { DriverId = "racial_hd:animal" } }
        });

        Assert.Equal(expectedSr, state.SpellResistance);
    }

    [Fact]
    public void BladeproofSkin_GrantsPrintedDamageReduction()
    {
        var state = Evaluate(Human(new Tick
        {
            DriverId = "class:fighter",
            Choices = new TickChoices { FeatIds = new List<string> { "feat:bladeproof_skin" } }
        }));

        var damageReduction = Assert.Single(state.DamageReduction,
            entry => entry.BypassedBy == "bludgeoning");
        Assert.Equal(3, damageReduction.Value);
    }

    [Fact]
    public void DevilErinyes_ReplayAndSheet_ExposeFireImmunityAlongsideResistances()
    {
        var state = Evaluate(new Character
        {
            Name = "Erinyes",
            RaceId = "race:devil_erinyes",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new() { new Tick { DriverId = "racial_hd:outsider" } }
        });

        Assert.Contains("fire", state.Immunities);
        Assert.Equal(10, state.Resistances["acid"]);
        Assert.Equal(10, state.Resistances["cold"]);

        var sheet = CharacterSheet.FromState(state);
        Assert.Contains("fire", sheet.Immunities);
    }

    // ---- familiar / companion animal skills ----

    /// <summary>
    /// An animal's class skills are the ones named in its own statblock — the Animal type grants no
    /// list of its own. Without them every rank an animal buys is cross-class at double cost, which
    /// made a legally-built 1-HD familiar report "spent 4 more skill points than available".
    ///
    /// SRD toad: "Skills: Hide +21, Listen +4, Spot +4" and "A toad's coloration gives it a +4
    /// racial bonus on Hide checks."
    /// </summary>
    [Fact]
    public void ToadFamiliar_HasItsStatblockSkillsAsClassSkillsAndItsRacialHideBonus()
    {
        var character = new Character
        {
            Name = "Toad",
            RaceId = "race:familiar_toad",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new()
            {
                new Tick
                {
                    DriverId = "racial_hd:animal",
                    Choices = new TickChoices
                    {
                        // 4 ranks of Hide — exactly what a 1-HD animal can afford at class-skill
                        // cost, and double that cross-class.
                        SkillAllocations = new List<SkillAllocation>
                        {
                            new() { SkillId = "skill:hide", HalfRanks = 8 }
                        }
                    }
                }
            }
        };

        var state = Evaluate(character);

        Assert.DoesNotContain(state.Warnings, w => w.Message.Contains("more skill points than available"));
        Assert.Equal(4, state.SkillBonuses["skill:hide"]);

        // 4 ranks + the toad's own Dexterity + racial +4 + Diminutive's +12 Hide size bonus.
        // Cross-check against the SRD's printed toad, Hide +21: it has Dex 12 (+1), so
        // 4 + 1 + 4 + 12 = 21 exactly.
        Assert.Equal(Size.Diminutive, state.Size);
        var dexMod = AbilityScoreSet.Modifier(state.AbilityScores.DEX);
        Assert.Equal(4 + dexMod + 4 + 12, state.SkillTotals["skill:hide"]);
    }

    /// <summary>
    /// SRD Hide: "A creature larger or smaller than Medium takes a size bonus or penalty on Hide
    /// checks depending on its size category: Fine +16, Diminutive +12, Tiny +8, Small +4,
    /// Large –4, Huge –8, Gargantuan –12, Colossal –16." Four times the AC/attack table, and it
    /// applies to Hide alone.
    /// </summary>
    [Theory]
    [InlineData(Size.Fine, 16)]
    [InlineData(Size.Diminutive, 12)]
    [InlineData(Size.Tiny, 8)]
    [InlineData(Size.Small, 4)]
    [InlineData(Size.Medium, 0)]
    [InlineData(Size.Large, -4)]
    [InlineData(Size.Huge, -8)]
    [InlineData(Size.Gargantuan, -12)]
    [InlineData(Size.Colossal, -16)]
    public void HideTakesItsOwnSizeModifier_NotTheAcAndAttackOne(Size size, int expected)
    {
        var rules = GameRules.Standard35e();

        // The AC/attack table runs 8/4/2/1/0/-1/-2/-4/-8 — it coincides with a quarter of this one
        // through the middle of the range but not at either end, so Hide needs its own table.
        Assert.Equal(expected, rules.CalculateHideSizeModifier(size));
    }

    /// <summary>The size modifier applies to Hide only — Move Silently and Spot take none.</summary>
    [Fact]
    public void OtherSkillsTakeNoSizeModifier()
    {
        var state = Evaluate(new Character
        {
            Name = "Toad",
            RaceId = "race:familiar_toad",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new()
            {
                new Tick
                {
                    DriverId = "racial_hd:animal",
                    Choices = new TickChoices
                    {
                        SkillAllocations = new List<SkillAllocation>
                        {
                            new() { SkillId = "skill:spot", HalfRanks = 4 }
                        }
                    }
                }
            }
        });

        var wisMod = AbilityScoreSet.Modifier(state.AbilityScores.WIS);
        Assert.Equal(2 + wisMod, state.SkillTotals["skill:spot"]);
    }

    /// <summary>
    /// The same omission affected every animal familiar, not just the toad. SRD hawk:
    /// "Hawks have a +8 racial bonus on Spot checks."
    /// </summary>
    [Fact]
    public void HawkCompanion_HasItsRacialSpotBonus()
    {
        var state = Evaluate(new Character
        {
            Name = "Hawk",
            RaceId = "race:companion_hawk",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new() { new Tick { DriverId = "racial_hd:animal" } }
        });

        Assert.Equal(8, state.SkillBonuses["skill:spot"]);
    }

    [Fact]
    public void AnimalCompanions_ApplyFlatRacialSkillBonuses()
    {
        var expected = new Dictionary<string, Dictionary<string, int>>
        {
            ["race:companion_ape"] = new() { ["skill:climb"] = 8 },
            ["race:companion_badger"] = new() { ["skill:escape_artist"] = 4 },
            ["race:companion_bear_black"] = new() { ["skill:swim"] = 4 },
            ["race:companion_bear_brown"] = new() { ["skill:swim"] = 4 },
            ["race:companion_dog"] = new() { ["skill:jump"] = 4 },
            ["race:companion_riding_dog"] = new() { ["skill:jump"] = 4 },
            ["race:companion_leopard"] = new()
            {
                ["skill:balance"] = 8,
                ["skill:climb"] = 8,
                ["skill:hide"] = 4,
                ["skill:jump"] = 8,
                ["skill:move_silently"] = 4,
            },
            ["race:companion_lion"] = new()
            {
                ["skill:balance"] = 4,
                ["skill:hide"] = 4,
                ["skill:move_silently"] = 4,
            },
            ["race:companion_monkey"] = new()
            {
                ["skill:balance"] = 8,
                ["skill:climb"] = 8,
            },
            ["race:companion_tiger"] = new()
            {
                ["skill:balance"] = 4,
                ["skill:hide"] = 4,
                ["skill:move_silently"] = 4,
            },
            ["race:companion_dire_lion"] = new()
            {
                ["skill:hide"] = 4,
                ["skill:move_silently"] = 4,
            },
            ["race:companion_tiger_dire"] = new()
            {
                ["skill:hide"] = 4,
                ["skill:move_silently"] = 4,
            },
            ["race:companion_dire_wolf"] = new()
            {
                ["skill:hide"] = 2,
                ["skill:listen"] = 2,
                ["skill:move_silently"] = 2,
                ["skill:spot"] = 2,
            },
            ["race:companion_wolverine"] = new() { ["skill:climb"] = 8 },
        };

        foreach (var (raceId, bonuses) in expected)
        {
            var state = Evaluate(new Character
            {
                Name = raceId,
                RaceId = raceId,
                BaseAbilityScores = new AbilityScoreSet
                    { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
                Ticks = new() { new Tick { DriverId = "racial_hd:animal" } }
            });

            Assert.Equal(bonuses, state.SkillBonuses);
        }
    }

    [Fact]
    public void NonAnimalCompanions_ApplyPrintedMovementAttacksAndFlatBonuses()
    {
        var air = Evaluate(new Character
        {
            Name = "Small air elemental",
            RaceId = "race:companion_elemental_air_small",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new() { new Tick { DriverId = "racial_hd:elemental_air" } }
        });
        Assert.Equal(FlightManeuverability.Perfect, air.FlyManeuverability);
        Assert.Equal("1d4", Assert.Single(air.NaturalAttacks, attack => attack.Name == "Slam").Damage);
        Assert.Contains(air.SpecialAttacks, attack => attack.Id == "air_elem_whirlwind");

        var water = Evaluate(new Character
        {
            Name = "Small water elemental",
            RaceId = "race:companion_elemental_water_small",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new() { new Tick { DriverId = "racial_hd:elemental_water" } }
        });
        Assert.Equal("1d6", Assert.Single(water.NaturalAttacks, attack => attack.Name == "Slam").Damage);
        Assert.Contains(water.SpecialAttacks, attack => attack.Id == "water_elem_vortex");

        var shadow = Evaluate(new Character
        {
            Name = "Shadow",
            RaceId = "race:companion_shadow",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new() { new Tick { DriverId = "racial_hd:undead" } }
        });
        Assert.Equal(FlightManeuverability.Good, shadow.FlyManeuverability);
        Assert.Equal(1, shadow.AC.Components[BonusType.Deflection]);
        Assert.Equal(2, shadow.SkillBonuses["skill:listen"]);
        Assert.Equal(2, shadow.SkillBonuses["skill:spot"]);
        Assert.Equal(4, shadow.SkillBonuses["skill:search"]);
        Assert.Contains(shadow.SpecialAttacks, attack => attack.Id == "shadow_str_damage");
        Assert.Contains(shadow.SpecialAttacks, attack => attack.Id == "shadow_create_spawn");
    }

    // ---- ability-modifier save bonuses (Divine Grace / Dark Blessing) ----

    /// <summary>
    /// SRD paladin: "she applies her Charisma modifier (if positive) as a bonus on all saving
    /// throws." The feature arrives at 2nd level and is worth nothing at 1st.
    /// </summary>
    [Fact]
    public void Paladin_DivineGrace_AddsCharismaToEverySaveFromSecondLevel()
    {
        var level1 = Evaluate(Human(new Tick { DriverId = "class:paladin" }));
        Assert.Equal(0, level1.AbilitySaveBonusTotal);

        var level2 = Evaluate(Human(
            new Tick { DriverId = "class:paladin" },
            new Tick { DriverId = "class:paladin" }));

        // Human() sets CHA 14 → +2 on every save, over and above the WIS/DEX/CON modifiers.
        var charisma = AbilityScoreSet.Modifier(level2.AbilityScores.CHA);
        Assert.Equal(2, charisma);
        Assert.Equal(charisma, level2.AbilitySaveBonusTotal);
        Assert.Equal(level2.BaseSaves.Fort + AbilityScoreSet.Modifier(level2.AbilityScores.CON) + charisma,
            level2.EffectiveSaves.Fort);
        Assert.Equal(level2.BaseSaves.Ref + AbilityScoreSet.Modifier(level2.AbilityScores.DEX) + charisma,
            level2.EffectiveSaves.Ref);
        Assert.Equal(level2.BaseSaves.Will + AbilityScoreSet.Modifier(level2.AbilityScores.WIS) + charisma,
            level2.EffectiveSaves.Will);
    }

    /// <summary>
    /// The bonus is the *current* Charisma modifier, not the one at the level that granted the
    /// feature. Banking a number at the granting tick was the original bug: it silently ignored
    /// every later ability increase, tome and worn item.
    /// </summary>
    [Fact]
    public void DivineGrace_TracksCharismaGainedAfterTheGrantingLevel()
    {
        var character = Human(
            new Tick { DriverId = "class:paladin" },
            new Tick { DriverId = "class:paladin" },
            new Tick { DriverId = "class:paladin" },
            new Tick
            {
                DriverId = "class:paladin",
                Choices = new TickChoices { AbilityIncrease = Ability.CHA }
            });

        var state = Evaluate(character);

        // CHA 14 + 1 from the 4th-level increase = 15, still +2.
        Assert.Equal(15, state.AbilityScores.CHA);
        Assert.Equal(2, state.AbilitySaveBonusTotal);

        // Tome of Leadership and Influence +5, read just before the 4th level.
        character.PermanentEvents.Add(new PermanentEvent
        {
            BeforeTick = 3,
            Permabuffs = new List<Permabuff>
            {
                new ModifyAttribute
                {
                    Target = AttributeTarget.AbilityScore,
                    AbilityScore = Ability.CHA,
                    Value = 5
                }
            }
        });

        var boosted = Evaluate(character);
        Assert.Equal(20, boosted.AbilityScores.CHA);
        Assert.Equal(5, boosted.AbilitySaveBonusTotal);
        Assert.Equal(state.EffectiveSaves.Will + 3, boosted.EffectiveSaves.Will);
    }

    /// <summary>SRD blackguard Dark Blessing is the same rule under a different name.</summary>
    [Fact]
    public void Blackguard_DarkBlessing_AddsCharismaToEverySave()
    {
        var state = Evaluate(Human(
            new Tick { DriverId = "class:blackguard" },
            new Tick { DriverId = "class:blackguard" }));

        Assert.Contains(state.AbilitySaveBonuses, bonus => bonus.SourceId == "dark_blessing");
        Assert.Equal(AbilityScoreSet.Modifier(state.AbilityScores.CHA), state.AbilitySaveBonusTotal);
    }

    /// <summary>
    /// A negative Charisma modifier is not carried onto saves — both features say "if positive".
    /// </summary>
    [Fact]
    public void DivineGrace_IgnoresANegativeCharismaModifier()
    {
        var character = Human(
            new Tick { DriverId = "class:paladin" },
            new Tick { DriverId = "class:paladin" });
        character.BaseAbilityScores.CHA = 6;

        var state = Evaluate(character);

        Assert.Equal(-2, AbilityScoreSet.Modifier(state.AbilityScores.CHA));
        Assert.Equal(0, state.AbilitySaveBonusTotal);
    }

    /// <summary>
    /// Epic ring of universal energy immunity: "the wearer takes no damage from energy of any of
    /// these types" — fire, cold, electricity, acid and sonic.
    /// </summary>
    [Fact]
    public void RingOfUniversalEnergyImmunity_GrantsAllFiveEnergyImmunities()
    {
        var character = Human(new Tick { DriverId = "class:fighter" });
        character.Equipment.Add(new EquipmentEntry
        {
            ItemId = "Ring of Universal Energy Immunity",
            ContentId = "ring:universal_energy_immunity",
            Slot = "ring"
        });

        var state = Evaluate(character);

        Assert.Equal(
            new[] { "acid", "cold", "electricity", "fire", "sonic" },
            state.Immunities.OrderBy(immunity => immunity, StringComparer.Ordinal));
    }

    // ---- validation gaps -------------------------------------------------

    [Fact]
    public void UnknownSkillId_Warns()
    {
        var state = Evaluate(Human(new Tick
        {
            DriverId = "class:fighter",
            Choices = new TickChoices
            {
                SkillAllocations = new List<SkillAllocation>
                {
                    new() { SkillId = "skill:underwater_basketweaving", HalfRanks = 8 }
                }
            }
        }));

        Assert.Contains(state.Warnings, w => w.Message.Contains("unknown skill 'skill:underwater_basketweaving'"));
        Assert.DoesNotContain("skill:underwater_basketweaving", state.SkillHalfRanks.Keys);
    }

    [Fact]
    public void DuplicateNonRepeatableFeat_WarnsAndIsNotApplied()
    {
        var state = Evaluate(Human(new Tick
        {
            DriverId = "class:fighter",
            Choices = new TickChoices { FeatIds = new List<string> { "feat:dodge", "feat:dodge" } }
        }));

        Assert.Contains(state.Warnings, w => w.Message.Contains("duplicate feat 'feat:dodge'"));
        Assert.Single(state.Feats, f => f == "feat:dodge");
    }

    [Fact]
    public void RepeatableFeat_TakenTwice_DoesNotWarn()
    {
        var state = Evaluate(Human(new Tick
        {
            DriverId = "class:fighter",
            Choices = new TickChoices
            {
                // Weapon Focus is repeatable (once per weapon).
                FeatIds = new List<string> { "feat:weapon_focus", "feat:weapon_focus" }
            }
        }));

        Assert.DoesNotContain(state.Warnings, w => w.Message.Contains("duplicate feat"));
    }

    [Fact]
    public void UnknownSpellId_Warns()
    {
        var state = Evaluate(Human(new Tick
        {
            DriverId = "class:wizard",
            Choices = new TickChoices
            {
                SpellSelections = new List<SpellSelection>
                {
                    new() { ClassId = "class:wizard", SpellLevel = 1, SpellId = "spell:fake_spell_xyz" }
                }
            }
        }));

        Assert.Contains(state.Warnings, w => w.Message.Contains("unknown spell 'spell:fake_spell_xyz'"));
    }

    [Fact]
    public void SpellNotOnClassList_Warns()
    {
        // cure_light_wounds is a cleric spell, not a wizard one.
        var state = Evaluate(Human(new Tick
        {
            DriverId = "class:wizard",
            Choices = new TickChoices
            {
                SpellSelections = new List<SpellSelection>
                {
                    new() { ClassId = "class:wizard", SpellLevel = 1, SpellId = "spell:cure_light_wounds" }
                }
            }
        }));

        Assert.Contains(state.Warnings, w => w.Message.Contains("not on the class:wizard spell list"));
    }

    [Fact]
    public void SpellAtWrongLevelForClass_Warns()
    {
        var state = Evaluate(Human(new Tick
        {
            DriverId = "class:wizard",
            Choices = new TickChoices
            {
                // magic_missile is a 1st-level wizard spell.
                SpellSelections = new List<SpellSelection>
                {
                    new() { ClassId = "class:wizard", SpellLevel = 0, SpellId = "spell:magic_missile" }
                }
            }
        }));

        Assert.Contains(state.Warnings, w => w.Message.Contains("is level 1 for class:wizard, not 0"));
    }

    [Fact]
    public void SpontaneousCaster_ExceedingSpellsKnown_Warns()
    {
        // A 1st-level sorcerer knows 2 first-level spells.
        var state = Evaluate(Human(new Tick
        {
            DriverId = "class:sorcerer",
            Choices = new TickChoices
            {
                SpellSelections = new List<SpellSelection>
                {
                    new() { ClassId = "class:sorcerer", SpellLevel = 1, SpellId = "spell:magic_missile" },
                    new() { ClassId = "class:sorcerer", SpellLevel = 1, SpellId = "spell:shield" },
                    new() { ClassId = "class:sorcerer", SpellLevel = 1, SpellId = "spell:mage_armor" },
                }
            }
        }));

        Assert.Contains(state.Warnings, w => w.Message.Contains("knows 3 level-1 spells, exceeding 2"));
    }

    [Fact]
    public void PreparedCaster_HasNoSpellsKnownCap()
    {
        // A wizard's spellbook is unbounded — scribing many spells is legal.
        var many = new[] { "spell:magic_missile", "spell:shield", "spell:mage_armor", "spell:burning_hands", "spell:true_strike" }
            .Select(id => new SpellSelection { ClassId = "class:wizard", SpellLevel = 1, SpellId = id })
            .ToList();

        var state = Evaluate(Human(new Tick
        {
            DriverId = "class:wizard",
            Choices = new TickChoices { SpellSelections = many }
        }));

        Assert.DoesNotContain(state.Warnings, w => w.Message.Contains("exceeding"));
    }

    // ---- fighter bonus feat restriction ----------------------------------

    [Fact]
    public void FighterBonusSlot_ExcludesNonCombatGeneralFeats()
    {
        var studio = new ReplayStudio(Content.Value);
        var state = Evaluate(Human(new Tick { DriverId = "class:fighter" }));

        var options = studio.GetAvailableFeats(state, "fighter_bonus").Select(f => f.Id).ToHashSet();

        // On the SRD fighter bonus list...
        Assert.Contains("feat:power_attack", options);
        Assert.Contains("feat:combat_reflexes", options);
        Assert.Contains("feat:weapon_finesse", options);
        // ...and emphatically not.
        Assert.DoesNotContain("feat:skill_focus", options);
        Assert.DoesNotContain("feat:acrobatic", options);
        Assert.DoesNotContain("feat:negotiator", options);
        Assert.DoesNotContain("feat:self_sufficient", options);
    }

    [Fact]
    public void FighterBonusSlot_IsMuchNarrowerThanStandardSlot()
    {
        var studio = new ReplayStudio(Content.Value);
        var state = Evaluate(Human(new Tick { DriverId = "class:fighter" }));

        var bonus = studio.GetAvailableFeats(state, "fighter_bonus").Count;
        var standard = studio.GetAvailableFeats(state).Count;

        Assert.True(bonus < standard / 2,
            $"fighter_bonus offered {bonus} of {standard} feats — restriction is not being applied");
    }

    [Fact]
    public void GrantOnlyFeat_ChosenWithASlot_Warns()
    {
        // A wizard gets no martial proficiency, so this pick is not caught as a duplicate —
        // it has to be rejected on its own merits.
        var state = Evaluate(Human(new Tick
        {
            DriverId = "class:wizard",
            Choices = new TickChoices { FeatIds = new List<string> { "feat:weapon_proficiency_martial" } }
        }));

        Assert.Contains(state.Warnings, w => w.Message.Contains("cannot be selected"));
        Assert.DoesNotContain("feat:weapon_proficiency_martial", state.Feats);
    }

    [Fact]
    public void GrantOnlyFeats_AreNotSelectable()
    {
        var studio = new ReplayStudio(Content.Value);
        var state = Evaluate(Human(new Tick { DriverId = "class:fighter" }));

        // The blanket "all martial weapons" proficiency is granted by classes, never chosen.
        Assert.DoesNotContain("feat:weapon_proficiency_martial",
            studio.GetAvailableFeats(state).Select(f => f.Id));
        // The real SRD feat (one weapon at a time) stays selectable.
        Assert.Contains("feat:martial_weapon_proficiency",
            studio.GetAvailableFeats(state).Select(f => f.Id));
    }

    // ---- class proficiencies ---------------------------------------------

    [Theory]
    [InlineData("class:fighter", "feat:simple_weapon_proficiency", "feat:weapon_proficiency_martial", "feat:armor_proficiency_heavy", "feat:tower_shield_proficiency")]
    [InlineData("class:barbarian", "feat:simple_weapon_proficiency", "feat:weapon_proficiency_martial", "feat:armor_proficiency_medium", "feat:shield_proficiency")]
    [InlineData("class:cleric", "feat:simple_weapon_proficiency", "feat:armor_proficiency_heavy", "feat:shield_proficiency", "feat:armor_proficiency_light")]
    [InlineData("class:rogue", "feat:simple_weapon_proficiency", "feat:armor_proficiency_light", "feat:armor_proficiency_light", "feat:armor_proficiency_light")]
    public void MartialClasses_GrantTheirProficiencies(string driverId, params string[] expected)
    {
        var state = Evaluate(Human(new Tick { DriverId = driverId }));

        foreach (var featId in expected.Distinct())
            Assert.Contains(featId, state.Feats);
    }

    [Fact]
    public void ProficiencyGrants_DoNotConsumeFeatSlots()
    {
        var plain = Evaluate(Human(new Tick { DriverId = "class:wizard" }));
        var martial = Evaluate(Human(new Tick { DriverId = "class:fighter" }));

        // Fighter gets an extra fighter-bonus slot, but the proficiency grants themselves
        // must not eat into the standard slots a wizard also receives.
        Assert.Equal(plain.PendingFeatSlots, martial.PendingFeatSlots);
    }

    [Fact]
    public void Wizard_DoesNotGetBlanketWeaponProficiency()
    {
        var state = Evaluate(Human(new Tick { DriverId = "class:wizard" }));

        Assert.DoesNotContain("feat:simple_weapon_proficiency", state.Feats);
        Assert.DoesNotContain("feat:weapon_proficiency_martial", state.Feats);
        Assert.DoesNotContain("feat:armor_proficiency_light", state.Feats);
    }

    // ---- SRD audit findings ----------------------------------------------
    // Each of the following was found by diffing content against the SRD text and carries
    // the quote that settles it.

    [Fact]
    public void Expert_IsNotProficientWithShields()
    {
        // "The expert is proficient in the use of all simple weapons and with light armor
        //  but not shields." (npcClasses.html)
        var state = Evaluate(Human(new Tick { DriverId = "class:expert" }));

        Assert.Contains("feat:simple_weapon_proficiency", state.Feats);
        Assert.Contains("feat:armor_proficiency_light", state.Feats);
        Assert.DoesNotContain("feat:shield_proficiency", state.Feats);
    }

    [Fact]
    public void CloisteredCleric_IsProficientWithLightArmor()
    {
        // "Cloistered clerics are proficient with simple weapons and with light armor."
        var state = Evaluate(Human(new Tick { DriverId = "class:cloistered_cleric" }));

        Assert.Contains("feat:simple_weapon_proficiency", state.Feats);
        Assert.Contains("feat:armor_proficiency_light", state.Feats);
    }

    [Fact]
    public void CloisteredCleric_HasAllKnowledgeSkillsAsClassSkills()
    {
        // UA: class skill list "includes ... all Knowledge skills".
        var state = Evaluate(Human(new Tick { DriverId = "class:cloistered_cleric" }));

        foreach (var knowledge in new[]
                 {
                     "skill:knowledge_arcana", "skill:knowledge_architecture", "skill:knowledge_dungeoneering",
                     "skill:knowledge_geography", "skill:knowledge_history", "skill:knowledge_local",
                     "skill:knowledge_nature", "skill:knowledge_nobility", "skill:knowledge_planes",
                     "skill:knowledge_religion"
                 })
        {
            Assert.Contains(knowledge, state.ClassSkills);
        }
    }

    [Theory]
    // UA: both variants have "all the standard <base> class features, except as noted below",
    // and weapon/armour proficiency is not among the exceptions.
    [InlineData("class:paladin_of_tyranny", "feat:weapon_proficiency_martial", "feat:armor_proficiency_heavy")]
    [InlineData("class:planar_ranger", "feat:weapon_proficiency_martial", "feat:armor_proficiency_light")]
    public void UnearthedArcanaVariants_InheritBaseClassProficiencies(
        string driverId, string a, string b)
    {
        var state = Evaluate(Human(new Tick { DriverId = driverId }));

        Assert.Contains("feat:simple_weapon_proficiency", state.Feats);
        Assert.Contains(a, state.Feats);
        Assert.Contains(b, state.Feats);
    }

    [Fact]
    public void Giant_HasAverageBaseAttackBonus()
    {
        // "Base attack bonus equal to 3/4 total Hit Dice (as cleric)." (monsterTypes.html)
        var driver = (HDDriver)Content.Value.GetDriver("racial_hd:giant");

        Assert.Equal(BABProgression.Average, driver.BABProgression);
    }

    [Fact]
    public void Humanoid_HasGoodReflexSaves()
    {
        // "Good Reflex saves (usually; a humanoid's good save varies)." (monsterTypes.html)
        var driver = (HDDriver)Content.Value.GetDriver("racial_hd:humanoid");

        Assert.Equal(ProgressionRate.Good, driver.SaveProgression.Ref);
        Assert.Equal(ProgressionRate.Poor, driver.SaveProgression.Fort);
    }

    [Fact]
    public void AirElemental_HasGoodReflexSave()
    {
        // "Good saves depend on the element: Fortitude (earth, water) or Reflex (air, fire)."
        var driver = (HDDriver)Content.Value.GetDriver("racial_hd:elemental_air");

        Assert.Equal(ProgressionRate.Good, driver.SaveProgression.Ref);
        Assert.Equal(ProgressionRate.Poor, driver.SaveProgression.Fort);
    }

    [Fact]
    public void WaterElemental_HasGoodFortitudeSave()
    {
        // "Good saves depend on the element: Fortitude (earth, water) or Reflex (air, fire)."
        var driver = (HDDriver)Content.Value.GetDriver("racial_hd:elemental_water");

        Assert.Equal(ProgressionRate.Good, driver.SaveProgression.Fort);
        Assert.Equal(ProgressionRate.Poor, driver.SaveProgression.Ref);
    }

    [Theory]
    // "+1 level of existing class" — neither is restricted to one casting type in the SRD,
    // so a divine Loremaster and an arcane Thaumaturgist must both advance.
    [InlineData("class:loremaster")]
    [InlineData("class:thaumaturgist")]
    public void AdvancementIsNotRestrictedToOneCastingType(string driverId)
    {
        var driver = (HDDriver)Content.Value.GetDriver(driverId);

        var advance = driver.PerLevelPermabuffs.OfType<AdvanceSpellcasting>().Single();
        Assert.Null(advance.CastingType);
    }

    [Fact]
    public void DivineLoremaster_AdvancesItsDivineCasting()
    {
        // The concrete consequence of the above: a cleric Loremaster used to gain nothing.
        var ticks = new List<Tick>();
        for (var i = 0; i < 10; i++) ticks.Add(new Tick { DriverId = "class:cleric" });
        var beforeEntry = Evaluate(Human(ticks.ToArray()));
        var clericLevel = beforeEntry.Spellcasting["class:cleric"].CasterLevel;

        ticks.Add(new Tick { DriverId = "class:loremaster" });
        var after = Evaluate(Human(ticks.ToArray()));

        Assert.Equal(clericLevel + 1, after.Spellcasting["class:cleric"].CasterLevel);
    }

    [Fact]
    public void Hierophant_AdvancesCasterLevelButNotSpellsPerDay()
    {
        // "Levels in the hierophant prestige class, even though they do not advance spell
        // progression in the character's base class, still stack with the character's base
        // spellcasting levels to determine caster level." Caster level should advance;
        // spells per day should not.
        var ticks = new List<Tick>();
        for (var i = 0; i < 13; i++) ticks.Add(new Tick { DriverId = "class:cleric" });
        var before = Evaluate(Human(ticks.ToArray()));
        var clericLevel = before.Spellcasting["class:cleric"].CasterLevel;
        var spellsPerDayBefore = before.Spellcasting["class:cleric"].SpellsPerDay;

        ticks.Add(new Tick { DriverId = "class:hierophant" });
        var after = Evaluate(Human(ticks.ToArray()));

        Assert.Equal(clericLevel + 1, after.Spellcasting["class:cleric"].CasterLevel);
        Assert.Equal(spellsPerDayBefore, after.Spellcasting["class:cleric"].SpellsPerDay);
    }

    // ---- prestige class prerequisites -------------------------------------

    /// <summary>
    /// Every SRD prestige class. These are the ones a character opts into, so an empty
    /// prerequisite list silently permits an illegal build — the defect this whole audit
    /// was chasing. Alignment alone is not enough for classes whose SRD Requirements
    /// section also lists skills, feats, BAB or spellcasting.
    /// </summary>
    public static TheoryData<string, int> SrdPrestigeClasses() => new()
    {
        { "class:arcane_archer", 6 },
        { "class:arcane_trickster", 7 },
        { "class:archmage", 6 },
        { "class:assassin", 4 },
        { "class:blackguard", 7 },
        { "class:cosmic_descryer", 4 },
        { "class:dragon_disciple", 4 },
        { "class:duelist", 6 },
        { "class:dwarven_defender", 6 },
        { "class:eldritch_knight", 2 },
        { "class:hierophant", 3 },
        { "class:horizon_walker", 2 },
        { "class:loremaster", 4 },
        { "class:mystic_theurge", 4 },
        { "class:shadowdancer", 6 },
        { "class:thaumaturgist", 2 },
    };

    [Theory]
    [MemberData(nameof(SrdPrestigeClasses))]
    public void PrestigeClasses_DeclareTheirSrdPrerequisites(string driverId, int expectedCount)
    {
        var driver = (HDDriver)Content.Value.GetDriver(driverId);

        Assert.Equal(expectedCount, driver.Prerequisites.Count);
    }

    [Theory]
    [MemberData(nameof(SrdPrestigeClasses))]
    public void PrestigeClasses_RejectAFreshFirstLevelCharacter(string driverId, int _)
    {
        // A 1st-level human fighter qualifies for none of them.
        var state = Evaluate(Human(new Tick { DriverId = "class:fighter" }));
        var driver = (HDDriver)Content.Value.GetDriver(driverId);

        Assert.False(driver.Prerequisites.All(p => p.IsMet(state)),
            $"{driverId} is available to a 1st-level fighter");
    }

    [Fact]
    public void Shadowdancer_GrantsCorrectProficiencies()
    {
        // Shadowdancer gains a weapon/armor list, not "no new proficiencies."
        var driver = (HDDriver)Content.Value.GetDriver("class:shadowdancer");
        var prof = driver.LevelPermabuffs[1].OfType<GrantAbility>()
            .Single(a => a.Ability.Id == "weapon_and_armor_proficiency");

        Assert.DoesNotContain("no new", prof.Ability.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("light armor", prof.Ability.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not with shields", prof.Ability.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(9)]
    public void Shadowdancer_HasLevelPermabuffsAtGapLevels(int level)
    {
        var driver = (HDDriver)Content.Value.GetDriver("class:shadowdancer");

        Assert.True(driver.LevelPermabuffs.TryGetValue(level, out var buffs) && buffs.Count > 0,
            $"shadowdancer has no levelPermabuffs at level {level}");
    }

    [Fact]
    public void Shadowdancer_ShadowJumpDistanceDoublesEveryTwoLevels()
    {
        // "At 4th level, a shadowdancer can jump up to a total of 20 feet each day this
        // way; this total distance increases by 20 feet at 6th level and every two levels
        // thereafter" — 20/40/80/160 ft. at 4th/6th/8th/10th.
        var driver = (HDDriver)Content.Value.GetDriver("class:shadowdancer");

        Assert.Contains(driver.LevelPermabuffs[6].OfType<GrantAbility>(), a => a.Ability.Id == "shadow_jump_40");
        Assert.Contains(driver.LevelPermabuffs[8].OfType<GrantAbility>(), a => a.Ability.Id == "shadow_jump_80");
        Assert.Contains(driver.LevelPermabuffs[10].OfType<GrantAbility>(), a => a.Ability.Id == "shadow_jump_160");
    }

    [Fact]
    public void Shadowdancer_ShadowCompanionGainsHDAtSixthAndNinthLevel()
    {
        // "Every third level gained by the shadowdancer adds +2 HD ... to her shadow
        // companion" — companion granted at 3rd, so the increases land on 6th and 9th
        // (10th is maxLevel, so there's no 12th to worry about).
        var driver = (HDDriver)Content.Value.GetDriver("class:shadowdancer");

        Assert.Contains(driver.LevelPermabuffs[6].OfType<GrantAbility>(), a => a.Ability.Id == "shadow_companion_hd_increase");
        Assert.Contains(driver.LevelPermabuffs[9].OfType<GrantAbility>(), a => a.Ability.Id == "shadow_companion_hd_increase");
    }

    [Fact]
    public void Shadowdancer_SummonShadowGrantsFixedScalingCompanionSlot()
    {
        var driver = (HDDriver)Content.Value.GetDriver("class:shadowdancer");
        var slot = driver.LevelPermabuffs[3].OfType<GrantCompanionSlot>().Single();

        Assert.Equal("shadow_companion", slot.LinkType);
        Assert.Equal("race:companion_shadow", slot.SelectedSpecies);
        var state = new CharacterState();
        state.ClassLevels["class:shadowdancer"] = 3;
        Assert.Equal(3, slot.EffectiveLevelFormula.Evaluate(state));
        state.ClassLevels["class:shadowdancer"] = 6;
        Assert.Equal(5, slot.EffectiveLevelFormula.Evaluate(state));
        state.ClassLevels["class:shadowdancer"] = 9;
        Assert.Equal(7, slot.EffectiveLevelFormula.Evaluate(state));
    }

    [Fact]
    public void DuelistRequiresPerformRanks_ViaAnyPerformSubskill()
    {
        // "Skills: Perform 3 ranks" — Perform is an umbrella, so any single sub-skill counts.
        var driver = (HDDriver)Content.Value.GetDriver("class:duelist");
        var perform = driver.Prerequisites.OfType<MinSkillRanksAcross>().Single();

        var state = new CharacterState();
        state.SkillHalfRanks["skill:perform_dance"] = 4;   // 2 ranks — short
        Assert.False(perform.IsMet(state));

        state.SkillHalfRanks["skill:perform_dance"] = 6;   // 3 ranks
        Assert.True(perform.IsMet(state));
    }

    [Fact]
    public void Loremaster_RequiresSkillFocusInAKnowledgeSkill_NotJustAnySkillFocus()
    {
        // "Feats: Any three metamagic or item creation feats, plus Skill Focus
        //  (Knowledge [any individual Knowledge skill])."
        // Selection ids are {feat}_{skill} and every Knowledge skill id starts with
        // "knowledge_", so HasFeat's prefix match discriminates exactly.
        var driver = (HDDriver)Content.Value.GetDriver("class:loremaster");
        var skillFocus = driver.Prerequisites.OfType<HasFeat>()
            .Single(f => f.FeatId.StartsWith("feat:skill_focus", StringComparison.Ordinal));

        var wrongSkill = new CharacterState();
        wrongSkill.Feats.Add("feat:skill_focus_spellcraft");
        Assert.False(skillFocus.IsMet(wrongSkill));

        var knowledge = new CharacterState();
        knowledge.Feats.Add("feat:skill_focus_knowledge_arcana");
        Assert.True(skillFocus.IsMet(knowledge));

        var otherKnowledge = new CharacterState();
        otherKnowledge.Feats.Add("feat:skill_focus_knowledge_religion");
        Assert.True(skillFocus.IsMet(otherKnowledge));
    }

    [Fact]
    public void Loremaster_GrantsBonusLanguageAtFourthAndEighthLevel()
    {
        // "Bonus Languages: At 4th and 8th level..."
        var driver = (HDDriver)Content.Value.GetDriver("class:loremaster");

        Assert.Contains(driver.LevelPermabuffs[4].OfType<GrantAbility>(), a => a.Ability.Id == "bonus_languages");
        Assert.Contains(driver.LevelPermabuffs[8].OfType<GrantAbility>(), a => a.Ability.Id == "bonus_languages");
    }

    [Fact]
    public void DwarvenDefender_UsesPrintedAcAndDamageReductionLevels()
    {
        var driver = (HDDriver)Content.Value.GetDriver("class:dwarven_defender");

        Assert.Contains(driver.LevelPermabuffs[1].OfType<GrantTypedBonus>(), bonus =>
            bonus.Target == BonusTarget.AC && bonus.BonusType == BonusType.Dodge
            && bonus.Value.Expression == "1");
        Assert.Contains(driver.LevelPermabuffs[4].OfType<GrantTypedBonus>(), bonus =>
            bonus.Target == BonusTarget.AC && bonus.BonusType == BonusType.Dodge);
        Assert.Contains(driver.LevelPermabuffs[7].OfType<GrantTypedBonus>(), bonus =>
            bonus.Target == BonusTarget.AC && bonus.BonusType == BonusType.Dodge);
        Assert.Contains(driver.LevelPermabuffs[10].OfType<GrantTypedBonus>(), bonus =>
            bonus.Target == BonusTarget.AC && bonus.BonusType == BonusType.Dodge);

        Assert.Equal(3, Assert.Single(driver.LevelPermabuffs[6].OfType<GrantDR>()).Value);
        Assert.Equal(6, Assert.Single(driver.LevelPermabuffs[10].OfType<GrantDR>()).Value);
    }

    [Theory]
    [InlineData(Alignment.N, "template:celestial")]
    [InlineData(Alignment.N, "template:fiendish")]
    [InlineData(Alignment.NG, "template:celestial")]
    [InlineData(Alignment.NE, "template:fiendish")]
    public void PlanarRanger_CompanionTemplateChoiceIsCarriedByTheSlot(
        Alignment alignment,
        string templateId)
    {
        var character = new Character
        {
            Name = "Planar companion choice",
            Alignment = alignment,
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet
                { STR = 12, DEX = 14, CON = 12, INT = 10, WIS = 12, CHA = 10 },
            Ticks = Enumerable.Range(0, 4)
                .Select(_ => new Tick { DriverId = "class:planar_ranger" })
                .ToList()
        };
        character.Ticks[^1].Choices.CompanionTemplateChoices =
            new Dictionary<string, string> { ["animal_companion"] = templateId };

        var state = Evaluate(character);
        var slot = Assert.Single(state.CompanionSlots, s => s.LinkType == "animal_companion");

        Assert.Equal(templateId, slot.SelectedTemplateId);
        Assert.DoesNotContain(state.Warnings,
            warning => warning.Message.Contains("companion template", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WeaponFocus_AppliesOnlyToTheSelectedEquippedWeapon()
    {
        var character = Human(new Tick
        {
            DriverId = "class:fighter",
            Choices = new TickChoices
            {
                FeatIds = new List<string> { "feat:weapon_focus_weapon:longsword" }
            }
        });
        character.Equipment.Add(new EquipmentEntry { ContentId = "weapon:longsword" });

        var state = Evaluate(character);
        var attack = Assert.Single(state.AttackLines);

        Assert.Equal("Longsword", attack.Name);
        Assert.Equal(new[] { 4 }, attack.Bonuses); // BAB 1 + Str 2 + Weapon Focus 1 + size 0
        Assert.Contains("feat:weapon_focus_weapon:longsword", state.Feats);
    }

    [Fact]
    public void SelectedWeaponFeatBonuses_UseTheirPrintedValues()
    {
        var selectedWeaponFeats = new[]
        {
            "feat:weapon_focus_weapon:longsword",
            "feat:weapon_specialization_weapon:longsword",
            "feat:greater_weapon_focus_weapon:longsword",
            "feat:greater_weapon_specialization_weapon:longsword",
            "feat:epic_weapon_focus_weapon:longsword",
            "feat:epic_weapon_specialization_weapon:longsword",
        };
        var character = Human();
        character.Ticks = Enumerable.Range(1, 24)
            .Select(level => new Tick
            {
                DriverId = "class:fighter",
                Choices = level switch
                {
                    1 => new TickChoices { FeatIds = new() { selectedWeaponFeats[0] } },
                    3 => new TickChoices { FeatIds = new() { selectedWeaponFeats[1] } },
                    6 => new TickChoices { FeatIds = new() { selectedWeaponFeats[2] } },
                    9 => new TickChoices { FeatIds = new() { selectedWeaponFeats[3] } },
                    21 => new TickChoices { FeatIds = new() { selectedWeaponFeats[4] } },
                    24 => new TickChoices { FeatIds = new() { selectedWeaponFeats[5] } },
                    _ => new TickChoices()
                }
            })
            .ToList();
        character.Equipment.Add(new EquipmentEntry { ContentId = "weapon:longsword" });

        var state = Evaluate(character);
        var attack = Assert.Single(state.AttackLines);

        Assert.Equal(3, state.WeaponBonusContributions.Count(c => c.Target == BonusTarget.Attack));
        Assert.Equal(3, state.WeaponBonusContributions.Count(c => c.Target == BonusTarget.Damage));
        Assert.Equal(new[] { 28, 23, 18, 13 }, attack.Bonuses); // BAB 20 + Str 2 + weapon feats 4 + epic attack 2
        Assert.Equal("1d8+10", attack.Damage); // Str 2 + specialization 2 + greater specialization 2 + epic specialization 4
    }

    [Fact]
    public void Thaumaturgist_RequiresSpellFocusInConjurationSpecifically()
    {
        // "Feats: Spell Focus (conjuration)." — one named school, so pin it exactly.
        var driver = (HDDriver)Content.Value.GetDriver("class:thaumaturgist");
        var spellFocus = driver.Prerequisites.OfType<HasFeat>().Single();

        var wrongSchool = new CharacterState();
        wrongSchool.Feats.Add("feat:spell_focus_evocation");
        Assert.False(spellFocus.IsMet(wrongSchool));

        var right = new CharacterState();
        right.Feats.Add("feat:spell_focus_conjuration");
        Assert.True(spellFocus.IsMet(right));
    }

    [Fact]
    public void CosmicDescryer_RequiresAnEnergyResistanceEpicFeat()
    {
        // "Feats: ... Epic Feats: Energy Resistance." No generic "energy_resistance" feat
        // exists — there are five, one per element (energy_resistance_acid/cold/electricity/
        // fire/sonic). HasFeatSelections' FeatId-or-FeatId+"_" prefix match already covers
        // "any one of them" without a new Prerequisite primitive.
        // "Ability to cast gate" is covered separately by CanCastSpellLevel (see
        // CosmicDescryer_RequiresAbilityToCastGate below) rather than a dedicated
        // KnowsSpell primitive — see that test for why.
        var driver = (HDDriver)Content.Value.GetDriver("class:cosmic_descryer");
        var energyResistance = driver.Prerequisites.OfType<HasFeatSelections>()
            .Single(f => f.FeatId == "feat:energy_resistance");

        var noFeat = new CharacterState();
        Assert.False(energyResistance.IsMet(noFeat));

        var withFeat = new CharacterState();
        withFeat.Feats.Add("feat:energy_resistance_fire");
        Assert.True(energyResistance.IsMet(withFeat));
    }

    [Fact]
    public void CosmicDescryer_RequiresAbilityToCastGate()
    {
        // "Feats: ... Ability to cast gate." No KnowsSpell primitive was built for this:
        // the data to track known spell identities exists (SpellcastingState.SelectedSpells)
        // but is populated only from TickChoices.SpellSelections, which the REST API never
        // exposes — an agent-built character could never satisfy an identity check. Instead
        // this reuses CanCastSpellLevel, the codebase's existing idiom for "ability to cast
        // [a specific spell]" (already used the same way for Arcane Trickster/Thaumaturgist).
        // gate is a 9th-level spell for cleric/sorcerer/wizard; Cosmic Descryer advances
        // arcane casting via its own AdvanceSpellcasting entries.
        var driver = (HDDriver)Content.Value.GetDriver("class:cosmic_descryer");

        Assert.Contains(driver.Prerequisites.OfType<CanCastSpellLevel>(),
            p => p.SpellLevel == 9 && p.CastingType == CastingType.Arcane);
    }

    [Fact]
    public void ArcaneArcher_RequiresElfOrHalfElf()
    {
        // "Race: Elf or half-elf."
        var driver = (HDDriver)Content.Value.GetDriver("class:arcane_archer");
        var race = driver.Prerequisites.OfType<HasAnyRace>().Single();

        Assert.Equal(new[] { "race:elf", "race:half_elf" }, race.RaceIds);

        var elf = new CharacterState { RaceId = "race:elf" };
        Assert.True(race.IsMet(elf));

        var dwarf = new CharacterState { RaceId = "race:dwarf" };
        Assert.False(race.IsMet(dwarf));
    }

    [Fact]
    public void DragonDisciple_RequiresDraconicAndExcludesHalfDragon()
    {
        // "Any nondragon (cannot already be a half-dragon)" and "Languages: Draconic."
        // Both halves are now reachable by a real character: draconic is granted by content and
        // by PCG import (see PcgConverterTests), and "template:half_dragon" was extracted from the
        // SRD mirror on 2026-07-29, so the exclusion finally gates against a template that exists.
        // Asserted against the registry below rather than a hand-built id, which is what the
        // earlier version of this test could not do.
        var driver = (HDDriver)Content.Value.GetDriver("class:dragon_disciple");
        var language = driver.Prerequisites.OfType<HasLanguage>().Single();
        var noDragon = driver.Prerequisites.OfType<LacksTemplate>().Single();

        Assert.Equal("draconic", language.LanguageId);
        Assert.Equal("template:half_dragon", noDragon.TemplateId);

        var withLanguage = new CharacterState();
        withLanguage.Languages.Add("draconic");
        Assert.True(language.IsMet(withLanguage));
        Assert.False(language.IsMet(new CharacterState()));

        // The excluded template resolves in the registry — the gap this test used to document.
        Assert.NotNull(Content.Value.GetTemplate(noDragon.TemplateId));

        var withTemplate = new CharacterState();
        withTemplate.TemplateIds.Add("template:half_dragon");
        Assert.False(noDragon.IsMet(withTemplate));
        Assert.True(noDragon.IsMet(new CharacterState()));
    }

    [Fact]
    public void DragonDisciple_EncodesPrintedAbilityAndNaturalAttackProgression()
    {
        // Dragon Disciple table/features: Str +2 at 2nd and 4th, Con +2 at 6th, Int +2 at 8th;
        // apotheosis adds Str +4 and Cha +2, raises natural armor to +4, and grants claws and a
        // bite if the character does not already have them. The SRD's size table gives a Medium
        // disciple 1d4 claws and a 1d6 bite.
        var character = Human(Enumerable.Range(0, 10)
            .Select(_ => new Tick { DriverId = "class:dragon_disciple" })
            .ToArray());

        var state = Evaluate(character);

        Assert.Equal(22, state.AbilityScores.STR);
        Assert.Equal(16, state.AbilityScores.CON);
        Assert.Equal(18, state.AbilityScores.INT);
        Assert.Equal(16, state.AbilityScores.CHA);
        Assert.Equal(4, state.NaturalArmor);

        var claws = Assert.Single(state.NaturalAttacks, attack => attack.Name == "Claw");
        Assert.Equal("1d4", claws.Damage);
        Assert.Equal(2, claws.Count);
        var bite = Assert.Single(state.NaturalAttacks, attack => attack.Name == "Bite");
        Assert.Equal("1d6", bite.Damage);
    }

    [Fact]
    public void HalfDragon_MatchesSrdTemplate()
    {
        // SRD "Creating a Half-Dragon" (monstersHtoI.html). Type becomes dragon, natural armor
        // improves by +4, Str +8/Con +2/Int +2/Cha +2, level adjustment +3.
        var template = Content.Value.GetTemplate("template:half_dragon");

        Assert.Equal(CreatureType.Dragon, template.TypeOverride);
        Assert.Equal(4, template.NaturalArmor);
        Assert.Equal(3, template.LevelAdjustment);

        Assert.Equal(8, template.AbilityModifiers!.STR);
        Assert.Equal(0, template.AbilityModifiers.DEX);
        Assert.Equal(2, template.AbilityModifiers.CON);
        Assert.Equal(2, template.AbilityModifiers.INT);
        Assert.Equal(0, template.AbilityModifiers.WIS);
        Assert.Equal(2, template.AbilityModifiers.CHA);

        // "two claw attacks and a bite attack, and the claws are the primary natural weapon."
        var claw = template.NaturalAttacks.Single(a => a.Name == "Claw");
        Assert.Equal(2, claw.Count);
        Assert.True(claw.IsPrimary);

        var bite = template.NaturalAttacks.Single(a => a.Name == "Bite");
        Assert.Equal(1, bite.Count);
        Assert.False(bite.IsPrimary);

        // "immunity to sleep and paralysis effects" — the variety-dependent third immunity is
        // descriptive only, since dragon variety is not a selectable choice.
        var immunities = template.CreationPermabuffs.OfType<GrantImmunity>().Select(g => g.Immunity).ToList();
        Assert.Contains("sleep", immunities);
        Assert.Contains("paralysis", immunities);
    }

    [Fact]
    public void HalfDragon_GrantsNoWingsToAMediumCharacter()
    {
        // "A half-dragon that is Large or larger has wings... A half-dragon that is Medium or
        // smaller does not have wings." Size-conditional wings are not expressible, and every PC
        // race here is Medium or smaller, so the template correctly grants no fly speed.
        var template = Content.Value.GetTemplate("template:half_dragon");
        Assert.Empty(template.SpeedModifiers);
    }

    [Fact]
    public void ArcaneTrickster_RequiresMageHandLevelAndSneakAttackTwoDice()
    {
        // "Ability to cast mage hand" — mage_hand is a 0-level spell; the prior
        // CanCastSpellLevel(3, Arcane) entry was too strict and is corrected here.
        // "Sneak attack +2d6" — sneak_attack_dice is incremented by ModifyCounter once per
        // die gained (rogue, assassin, blackguard, arcane_trickster all emit it).
        var driver = (HDDriver)Content.Value.GetDriver("class:arcane_trickster");

        Assert.Contains(driver.Prerequisites.OfType<CanCastSpellLevel>(),
            p => p.SpellLevel == 0 && p.CastingType == CastingType.Arcane);

        var sneakAttack = driver.Prerequisites.OfType<MinCounter>()
            .Single(c => c.CounterId == "sneak_attack_dice");
        Assert.Equal(2, sneakAttack.Value);
    }

    [Fact]
    public void DwarvenDefender_RequiresBeingADwarf()
    {
        // "Race: Dwarf."
        var driver = (HDDriver)Content.Value.GetDriver("class:dwarven_defender");

        Assert.Contains(driver.Prerequisites.OfType<HasRace>(), r => r.RaceId == "race:dwarf");
    }

    [Fact]
    public void PrestigeClasses_AreNotOfferedToAnUnqualifiedLowLevelCharacter()
    {
        var studio = new ReplayStudio(Content.Value);
        var state = Evaluate(Human(new Tick { DriverId = "class:fighter" }));

        foreach (var id in new[]
                 {
                     "class:loremaster", "class:mystic_theurge", "class:shadowdancer",
                     "class:dragon_disciple", "class:eldritch_knight", "class:archmage"
                 })
        {
            var driver = (HDDriver)Content.Value.GetDriver(id);
            Assert.False(driver.Prerequisites.All(p => p.IsMet(state)),
                $"{id} should not be available to a 1st-level fighter");
        }
    }

    [Fact]
    public void EldritchKnight_RequiresMartialProficiencyAndThirdLevelArcane()
    {
        var driver = (HDDriver)Content.Value.GetDriver("class:eldritch_knight");

        // Fighter 1 alone gives the proficiency but no arcane casting.
        var fighterOnly = Evaluate(Human(new Tick { DriverId = "class:fighter" }));
        Assert.False(driver.Prerequisites.All(p => p.IsMet(fighterOnly)));

        // Fighter 1 / Wizard 5 reaches 3rd-level arcane spells and qualifies.
        var ticks = new List<Tick> { new() { DriverId = "class:fighter" } };
        for (var i = 0; i < 5; i++) ticks.Add(new Tick { DriverId = "class:wizard" });
        var qualified = Evaluate(Human(ticks.ToArray()));

        Assert.True(driver.Prerequisites.All(p => p.IsMet(qualified)),
            string.Join("; ", driver.Prerequisites.Where(p => !p.IsMet(qualified)).Select(p => p.Description)));
    }

    [Fact]
    public void EldritchKnight_HasGoodFortitudeSave()
    {
        var driver = (HDDriver)Content.Value.GetDriver("class:eldritch_knight");

        Assert.Equal(ProgressionRate.Good, driver.SaveProgression.Fort);
        Assert.Equal(ProgressionRate.Poor, driver.SaveProgression.Will);
    }

    // ---- new prerequisite primitives --------------------------------------

    [Fact]
    public void MinSkillRanksAcross_CountsDistinctSkillsAtThreshold()
    {
        var prereq = new MinSkillRanksAcross
        {
            SkillIds = new List<string> { "skill:knowledge_arcana", "skill:knowledge_religion", "skill:knowledge_nature" },
            Value = 10,
            MinCount = 2
        };

        var state = new CharacterState();
        state.SkillHalfRanks["skill:knowledge_arcana"] = 20;   // 10 ranks
        Assert.False(prereq.IsMet(state));

        state.SkillHalfRanks["skill:knowledge_religion"] = 18; // 9 ranks — short
        Assert.False(prereq.IsMet(state));

        state.SkillHalfRanks["skill:knowledge_religion"] = 20; // 10 ranks
        Assert.True(prereq.IsMet(state));
    }

    [Fact]
    public void HasFeatOfAnyType_SumsAcrossTypes()
    {
        var prereq = new HasFeatOfAnyType
        {
            FeatTypes = new List<FeatType> { FeatType.Metamagic, FeatType.ItemCreation },
            MinCount = 3
        };

        var state = new CharacterState();
        state.FeatTypeCounts[FeatType.Metamagic] = 2;
        Assert.False(prereq.IsMet(state));

        // One of each type still totals three.
        state.FeatTypeCounts[FeatType.ItemCreation] = 1;
        Assert.True(prereq.IsMet(state));
    }

    [Fact]
    public void HasSpontaneousCasting_DistinguishesSorcererFromWizard()
    {
        var prereq = new HasSpontaneousCasting { CastingType = CastingType.Arcane };

        Assert.True(prereq.IsMet(Evaluate(Human(new Tick { DriverId = "class:sorcerer" }))));
        Assert.False(prereq.IsMet(Evaluate(Human(new Tick { DriverId = "class:wizard" }))));
    }

    [Fact]
    public void Barbarian_DamageReduction_AdvancesWithoutStacking()
    {
        var ticks = Enumerable.Range(0, 19)
            .Select(_ => new Tick { DriverId = "class:barbarian" })
            .ToArray();

        var state = Evaluate(Human(ticks));

        var dr = Assert.Single(state.DamageReduction);
        Assert.Equal("-", dr.BypassedBy);
        Assert.Equal(5, dr.Value);
    }

    [Fact]
    public void DomainClassSkills_AndSpellLinks_AreApplied()
    {
        var state = Evaluate(Human(new Tick
        {
            DriverId = "class:cleric",
            Choices = new TickChoices
            {
                ClassFeatureChoices = new Dictionary<string, List<string>>
                {
                    ["domains"] = new() { "domain:animal", "domain:trickery" }
                }
            }
        }));

        Assert.Contains("skill:knowledge_nature", state.ClassSkills);
        Assert.Contains("skill:bluff", state.ClassSkills);
        Assert.Contains("skill:disguise", state.ClassSkills);
        Assert.Contains("skill:hide", state.ClassSkills);
        Assert.Equal(3, Content.Value.GetSpell("spell:blacklight").ClassLevels["domain:darkness"]);
        Assert.Equal(7, Content.Value.GetSpell("spell:hardening").ClassLevels["domain:artifice"]);
        Assert.Equal(8, Content.Value.GetSpell("spell:maddening_scream").ClassLevels["domain:madness"]);
    }

    [Fact]
    public void ExtraMusic_RequiresBardicMusic_AndAddsFourUses()
    {
        var feat = Content.Value.GetFeat("feat:extra_music");
        var nonBard = Evaluate(Human(new Tick { DriverId = "class:fighter" }));
        Assert.False(feat.Prerequisites.All(p => p.IsMet(nonBard)));

        var bard = Evaluate(Human(new Tick
        {
            DriverId = "class:bard",
            Choices = new TickChoices { FeatIds = new List<string> { "feat:extra_music" } }
        }));
        Assert.Equal(5, bard.Counters["bardic_music_uses"]);
    }

    [Fact]
    public void HalfDragon_UpgradesOnlyRacialHitDice_AndGrantsLargeFlight()
    {
        var aranea = Human(new Tick { DriverId = "racial_hd:magical_beast" });
        aranea.RaceId = "race:aranea";
        Assert.Equal(10, Assert.Single(Evaluate(aranea).HitDice).DieSize);

        aranea.TemplateIds.Add("template:half_dragon");
        var racial = Evaluate(aranea);
        Assert.Equal(12, Assert.Single(racial.HitDice).DieSize); // magical beast d10 -> d12

        var giant = Human();
        giant.RaceId = "race:fire_giant";
        giant.TemplateIds.Add("template:half_dragon");
        Assert.Equal(80, Evaluate(giant).Speeds[MovementMode.Fly]);

        var classOnly = Human(new Tick { DriverId = "class:fighter" });
        classOnly.TemplateIds.Add("template:half_dragon");
        Assert.Equal(10, Assert.Single(Evaluate(classOnly).HitDice).DieSize);
    }

    [Fact]
    public void HalfFiendAndFiendish_PersistTheirDurableState()
    {
        var halfFiend = Human(new Tick { DriverId = "class:fighter" });
        halfFiend.TemplateIds.Add("template:half_fiend");
        var halfFiendState = Evaluate(halfFiend);
        Assert.Equal(30, halfFiendState.Speeds[MovementMode.Fly]);
        Assert.Equal(1, Assert.Single(halfFiendState.SLAs).CasterLevel);

        var fiendish = Human();
        fiendish.RaceId = "race:companion_badger";
        fiendish.TemplateIds.Add("template:fiendish");
        var fiendishState = Evaluate(fiendish);
        Assert.Equal(CreatureType.MagicalBeast, fiendishState.Type);
        Assert.Contains(fiendishState.Abilities, a => a.Id == "fiendish_darkvision_60");
        Assert.Contains(fiendishState.SpecialAttacks, a => a.Id == "fiendish_smite_good");
    }

    [Fact]
    public void Domains_ExposeScopedCasterAndItemActivationRules()
    {
        var character = Human(new Tick
        {
            DriverId = "class:cleric",
            Choices = new TickChoices { ClassFeatureChoices = new Dictionary<string, List<string>>
            {
                ["domains"] = new() { "domain:artifice", "domain:creation" }
            }}
        });
        var state = Evaluate(character);
        Assert.Equal(4, state.EffectiveCasterLevel("class:cleric", Content.Value.GetSpell("spell:minor_creation")));

        character.Ticks[0].Choices.ClassFeatureChoices!["domains"] = new() { "domain:magic", "domain:knowledge" };
        state = Evaluate(character);
        var itemRule = Assert.Single(state.ItemActivationLevelRules);
        Assert.Equal(1, itemRule.EffectiveLevel(state));
    }

    [Fact]
    public void WarDomain_UsesPlayerChosenFavoredWeapon()
    {
        var character = Human(new Tick
        {
            DriverId = "class:cleric",
            Choices = new TickChoices { ClassFeatureChoices = new Dictionary<string, List<string>>
            {
                ["domains"] = new() { "domain:war", "domain:knowledge" },
                [GrantWarDomainWeaponFeats.ChoiceKey] = new() { "weapon:longsword" }
            }}
        });

        var state = Evaluate(character);
        Assert.Contains("feat:martial_weapon_proficiency_weapon:longsword", state.Feats);
        Assert.Contains("feat:weapon_focus_weapon:longsword", state.Feats);
        Assert.DoesNotContain(state.Warnings, w => w.Message.Contains("War domain requires"));
    }

    // UA variant paladins: "A paladin of tyranny must be lawful evil"
    // (unearthedCoreClass.html). The content originally wrote the prerequisite as
    // {"alignment": "LE"} — a property MinSkillRanks-style silent drop turned into an
    // empty Allowed set, which made the class unbuildable for everyone.
    [Fact]
    public void PaladinOfTyranny_RequiresLawfulEvil()
    {
        var driver = Content.Value.GetDriver("class:paladin_of_tyranny");
        var alignReq = Assert.Single(driver.Prerequisites.OfType<AlignmentReq>());
        Assert.Equal(new HashSet<Alignment> { Alignment.LE }, alignReq.Allowed);
    }
}
