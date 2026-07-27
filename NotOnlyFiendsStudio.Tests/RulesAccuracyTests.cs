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
        RaceId = "human",
        BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 14, CON = 14, INT = 16, WIS = 12, CHA = 14 },
        Ticks = ticks.ToList()
    };

    private static CharacterState Evaluate(Character character) =>
        new ReplayStudio(Content.Value).Evaluate(character);

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
                    new() { SkillId = "underwater_basketweaving", HalfRanks = 8 }
                }
            }
        }));

        Assert.Contains(state.Warnings, w => w.Contains("unknown skill 'underwater_basketweaving'"));
    }

    [Fact]
    public void DuplicateNonRepeatableFeat_WarnsAndIsNotApplied()
    {
        var state = Evaluate(Human(new Tick
        {
            DriverId = "class:fighter",
            Choices = new TickChoices { FeatIds = new List<string> { "dodge", "dodge" } }
        }));

        Assert.Contains(state.Warnings, w => w.Contains("duplicate feat 'dodge'"));
        Assert.Single(state.Feats, f => f == "dodge");
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
                FeatIds = new List<string> { "weapon_focus", "weapon_focus" }
            }
        }));

        Assert.DoesNotContain(state.Warnings, w => w.Contains("duplicate feat"));
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
                    new() { ClassId = "class:wizard", SpellLevel = 1, SpellId = "fake_spell_xyz" }
                }
            }
        }));

        Assert.Contains(state.Warnings, w => w.Contains("unknown spell 'fake_spell_xyz'"));
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
                    new() { ClassId = "class:wizard", SpellLevel = 1, SpellId = "cure_light_wounds" }
                }
            }
        }));

        Assert.Contains(state.Warnings, w => w.Contains("not on the class:wizard spell list"));
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
                    new() { ClassId = "class:wizard", SpellLevel = 0, SpellId = "magic_missile" }
                }
            }
        }));

        Assert.Contains(state.Warnings, w => w.Contains("is level 1 for class:wizard, not 0"));
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
                    new() { ClassId = "class:sorcerer", SpellLevel = 1, SpellId = "magic_missile" },
                    new() { ClassId = "class:sorcerer", SpellLevel = 1, SpellId = "shield" },
                    new() { ClassId = "class:sorcerer", SpellLevel = 1, SpellId = "mage_armor" },
                }
            }
        }));

        Assert.Contains(state.Warnings, w => w.Contains("knows 3 level-1 spells, exceeding 2"));
    }

    [Fact]
    public void PreparedCaster_HasNoSpellsKnownCap()
    {
        // A wizard's spellbook is unbounded — scribing many spells is legal.
        var many = new[] { "magic_missile", "shield", "mage_armor", "burning_hands", "true_strike" }
            .Select(id => new SpellSelection { ClassId = "class:wizard", SpellLevel = 1, SpellId = id })
            .ToList();

        var state = Evaluate(Human(new Tick
        {
            DriverId = "class:wizard",
            Choices = new TickChoices { SpellSelections = many }
        }));

        Assert.DoesNotContain(state.Warnings, w => w.Contains("exceeding"));
    }

    // ---- fighter bonus feat restriction ----------------------------------

    [Fact]
    public void FighterBonusSlot_ExcludesNonCombatGeneralFeats()
    {
        var studio = new ReplayStudio(Content.Value);
        var state = Evaluate(Human(new Tick { DriverId = "class:fighter" }));

        var options = studio.GetAvailableFeats(state, "fighter_bonus").Select(f => f.Id).ToHashSet();

        // On the SRD fighter bonus list...
        Assert.Contains("power_attack", options);
        Assert.Contains("combat_reflexes", options);
        Assert.Contains("weapon_finesse", options);
        // ...and emphatically not.
        Assert.DoesNotContain("skill_focus", options);
        Assert.DoesNotContain("acrobatic", options);
        Assert.DoesNotContain("negotiator", options);
        Assert.DoesNotContain("self_sufficient", options);
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
            Choices = new TickChoices { FeatIds = new List<string> { "weapon_proficiency_martial" } }
        }));

        Assert.Contains(state.Warnings, w => w.Contains("cannot be selected"));
        Assert.DoesNotContain("weapon_proficiency_martial", state.Feats);
    }

    [Fact]
    public void GrantOnlyFeats_AreNotSelectable()
    {
        var studio = new ReplayStudio(Content.Value);
        var state = Evaluate(Human(new Tick { DriverId = "class:fighter" }));

        // The blanket "all martial weapons" proficiency is granted by classes, never chosen.
        Assert.DoesNotContain("weapon_proficiency_martial",
            studio.GetAvailableFeats(state).Select(f => f.Id));
        // The real SRD feat (one weapon at a time) stays selectable.
        Assert.Contains("martial_weapon_proficiency",
            studio.GetAvailableFeats(state).Select(f => f.Id));
    }

    // ---- class proficiencies ---------------------------------------------

    [Theory]
    [InlineData("class:fighter", "simple_weapon_proficiency", "weapon_proficiency_martial", "armor_proficiency_heavy", "tower_shield_proficiency")]
    [InlineData("class:barbarian", "simple_weapon_proficiency", "weapon_proficiency_martial", "armor_proficiency_medium", "shield_proficiency")]
    [InlineData("class:cleric", "simple_weapon_proficiency", "armor_proficiency_heavy", "shield_proficiency", "armor_proficiency_light")]
    [InlineData("class:rogue", "simple_weapon_proficiency", "armor_proficiency_light", "armor_proficiency_light", "armor_proficiency_light")]
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

        Assert.DoesNotContain("simple_weapon_proficiency", state.Feats);
        Assert.DoesNotContain("weapon_proficiency_martial", state.Feats);
        Assert.DoesNotContain("armor_proficiency_light", state.Feats);
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

        Assert.Contains("simple_weapon_proficiency", state.Feats);
        Assert.Contains("armor_proficiency_light", state.Feats);
        Assert.DoesNotContain("shield_proficiency", state.Feats);
    }

    [Fact]
    public void CloisteredCleric_IsProficientWithLightArmor()
    {
        // "Cloistered clerics are proficient with simple weapons and with light armor."
        var state = Evaluate(Human(new Tick { DriverId = "class:cloistered_cleric" }));

        Assert.Contains("simple_weapon_proficiency", state.Feats);
        Assert.Contains("armor_proficiency_light", state.Feats);
    }

    [Fact]
    public void CloisteredCleric_HasAllKnowledgeSkillsAsClassSkills()
    {
        // UA: class skill list "includes ... all Knowledge skills".
        var state = Evaluate(Human(new Tick { DriverId = "class:cloistered_cleric" }));

        foreach (var knowledge in new[]
                 {
                     "knowledge_arcana", "knowledge_architecture", "knowledge_dungeoneering",
                     "knowledge_geography", "knowledge_history", "knowledge_local",
                     "knowledge_nature", "knowledge_nobility", "knowledge_planes",
                     "knowledge_religion"
                 })
        {
            Assert.Contains(knowledge, state.ClassSkills);
        }
    }

    [Theory]
    // UA: both variants have "all the standard <base> class features, except as noted below",
    // and weapon/armour proficiency is not among the exceptions.
    [InlineData("class:paladin_of_tyranny", "weapon_proficiency_martial", "armor_proficiency_heavy")]
    [InlineData("class:planar_ranger", "weapon_proficiency_martial", "armor_proficiency_light")]
    public void UnearthedArcanaVariants_InheritBaseClassProficiencies(
        string driverId, string a, string b)
    {
        var state = Evaluate(Human(new Tick { DriverId = driverId }));

        Assert.Contains("simple_weapon_proficiency", state.Feats);
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

    // ---- prestige class prerequisites -------------------------------------

    /// <summary>
    /// Every SRD prestige class. These are the ones a character opts into, so an empty
    /// prerequisite list silently permits an illegal build — the defect this whole audit
    /// was chasing. Alignment alone is not enough for classes whose SRD Requirements
    /// section also lists skills, feats, BAB or spellcasting.
    /// </summary>
    public static TheoryData<string, int> SrdPrestigeClasses() => new()
    {
        { "class:arcane_archer", 5 },
        { "class:arcane_trickster", 6 },
        { "class:archmage", 6 },
        { "class:assassin", 4 },
        { "class:blackguard", 7 },
        { "class:dragon_disciple", 2 },
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
    public void DuelistRequiresPerformRanks_ViaAnyPerformSubskill()
    {
        // "Skills: Perform 3 ranks" — Perform is an umbrella, so any single sub-skill counts.
        var driver = (HDDriver)Content.Value.GetDriver("class:duelist");
        var perform = driver.Prerequisites.OfType<MinSkillRanksAcross>().Single();

        var state = new CharacterState();
        state.SkillRanks["perform_dance"] = 4;   // 2 ranks — short
        Assert.False(perform.IsMet(state));

        state.SkillRanks["perform_dance"] = 6;   // 3 ranks
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
            .Single(f => f.FeatId.StartsWith("skill_focus", StringComparison.Ordinal));

        var wrongSkill = new CharacterState();
        wrongSkill.Feats.Add("skill_focus_spellcraft");
        Assert.False(skillFocus.IsMet(wrongSkill));

        var knowledge = new CharacterState();
        knowledge.Feats.Add("skill_focus_knowledge_arcana");
        Assert.True(skillFocus.IsMet(knowledge));

        var otherKnowledge = new CharacterState();
        otherKnowledge.Feats.Add("skill_focus_knowledge_religion");
        Assert.True(skillFocus.IsMet(otherKnowledge));
    }

    [Fact]
    public void Thaumaturgist_RequiresSpellFocusInConjurationSpecifically()
    {
        // "Feats: Spell Focus (conjuration)." — one named school, so pin it exactly.
        var driver = (HDDriver)Content.Value.GetDriver("class:thaumaturgist");
        var spellFocus = driver.Prerequisites.OfType<HasFeat>().Single();

        var wrongSchool = new CharacterState();
        wrongSchool.Feats.Add("spell_focus_evocation");
        Assert.False(spellFocus.IsMet(wrongSchool));

        var right = new CharacterState();
        right.Feats.Add("spell_focus_conjuration");
        Assert.True(spellFocus.IsMet(right));
    }

    [Fact]
    public void DwarvenDefender_RequiresBeingADwarf()
    {
        // "Race: Dwarf."
        var driver = (HDDriver)Content.Value.GetDriver("class:dwarven_defender");

        Assert.Contains(driver.Prerequisites.OfType<HasRace>(), r => r.RaceId == "dwarf");
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
            SkillIds = new List<string> { "knowledge_arcana", "knowledge_religion", "knowledge_nature" },
            Value = 10,
            MinCount = 2
        };

        var state = new CharacterState();
        state.SkillRanks["knowledge_arcana"] = 20;   // 10 ranks
        Assert.False(prereq.IsMet(state));

        state.SkillRanks["knowledge_religion"] = 18; // 9 ranks — short
        Assert.False(prereq.IsMet(state));

        state.SkillRanks["knowledge_religion"] = 20; // 10 ranks
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
}
