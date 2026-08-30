using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public class TemplateTests
{
    [Fact]
    public void PublicTemplates_AreSplitByAcquisitionMode()
    {
        var registry = TestContentHelper.LoadAllPacks();

        foreach (var id in new[]
                 { "template:celestial", "template:fiendish", "template:half_celestial", "template:half_dragon",
                   "template:half_fiend", "template:half_fey_creature_no_wings" })
            Assert.Equal(TemplateAcquisitionKind.Inherited, registry.GetTemplate(id).AcquisitionKind);

        foreach (var id in new[] { "template:lich", "template:vampire" })
            Assert.Equal(TemplateAcquisitionKind.Acquired, registry.GetTemplate(id).AcquisitionKind);

        Assert.Equal(TemplateAcquisitionKind.Internal, registry.GetTemplate("template:undead").AcquisitionKind);
    }

    [Fact]
    public void HalfFiend_RejectsGoodAlignmentAndVampireChecksBaseType()
    {
        var registry = TestContentHelper.LoadAllPacks();

        var halfFiendAlignment = Assert.IsType<AlignmentReq>(
            Assert.Single(registry.GetTemplate("template:half_fiend").ApplicabilityPrerequisites,
                prerequisite => prerequisite is AlignmentReq));
        Assert.DoesNotContain(Alignment.LG, halfFiendAlignment.Allowed);
        Assert.DoesNotContain(Alignment.NG, halfFiendAlignment.Allowed);
        Assert.DoesNotContain(Alignment.CG, halfFiendAlignment.Allowed);

        var vampire = registry.GetTemplate("template:vampire");
        var vampireTypeCheck = Assert.Single(vampire.ApplicabilityPrerequisites,
            prerequisite => prerequisite is AnyOf);
        var options = Assert.IsType<AnyOf>(vampireTypeCheck).Options;
        Assert.Contains(options, option => option is HasCreatureType { Type: CreatureType.Humanoid });
        Assert.Contains(options, option => option is HasCreatureType { Type: CreatureType.MonstrousHumanoid });
    }

    [Fact]
    public void InheritedTemplate_CannotBeManuallyDelayed()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var character = new Character
        {
            Name = "Delayed Heritage",
            RaceId = "race:human",
            TemplateIds = new List<string> { "template:half_dragon" },
            TemplateAcquisitionHD = new Dictionary<string, int> { ["template:half_dragon"] = 2 },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" }
            }
        };

        var state = new ReplayStudio(registry).Evaluate(character);

        Assert.DoesNotContain("template:half_dragon", state.TemplateIds);
        Assert.Contains(state.Warnings, warning =>
            warning.Message.Contains("inherited template Half-Dragon cannot be manually acquired"));
    }

    [Fact]
    public void HalfCelestial_ReplaysTypeAbilitiesThresholdsAndConditionalSlas()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var character = new Character
        {
            Name = "Half-Celestial Test",
            RaceId = "race:human",
            TemplateIds = new List<string> { "template:half_celestial" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = Enumerable.Range(0, 12)
                .Select(_ => new Tick { DriverId = "class:fighter" })
                .ToList()
        };

        var state = new ReplayStudio(registry).Evaluate(character);

        // monstersHtoI.html#half-celestial: inherited, outsider/native, +4 LA, ability
        // modifiers, flight, resistances, disease immunity, DR, SR, and cumulative SLAs.
        Assert.Equal(TemplateAcquisitionKind.Inherited, registry.GetTemplate("template:half_celestial").AcquisitionKind);
        Assert.Equal(CreatureType.Outsider, state.Type);
        Assert.Contains("native", state.Subtypes);
        Assert.Equal(4, state.LevelAdjustment);
        Assert.Equal(14, state.AbilityScores.STR);
        Assert.Equal(12, state.AbilityScores.DEX);
        Assert.Equal(14, state.AbilityScores.CON);
        Assert.Equal(12, state.AbilityScores.INT);
        Assert.Equal(14, state.AbilityScores.WIS);
        Assert.Equal(14, state.AbilityScores.CHA);
        Assert.Equal(1, state.NaturalArmor);
        Assert.Equal(60, state.Speeds[MovementMode.Fly]);
        Assert.Equal(FlightManeuverability.Good, state.FlyManeuverability!.Value);
        Assert.Equal(10, state.Resistances["acid"]);
        Assert.Equal(10, state.Resistances["cold"]);
        Assert.Equal(10, state.Resistances["electricity"]);
        Assert.Contains("disease", state.Immunities);
        Assert.Equal(10, Assert.Single(state.DamageReduction, dr => dr.BypassedBy == "magic").Value);
        Assert.Equal(22, state.SpellResistance);
        Assert.Contains(state.SpecialAttacks, attack => attack.Id == "hc_smite_evil");
        Assert.Equal(12, Assert.Single(state.SLAs, sla => sla.Id == "hc_sla_daylight").CasterLevel);
        Assert.Contains(state.SLAs, sla => sla.Id == "hc_sla_holy_word");
        Assert.DoesNotContain(state.SLAs, sla => sla.Id == "hc_sla_holy_aura"); // starts at HD 13

        var lowAbilities = character.Clone();
        lowAbilities.Name = "Half-Celestial Low Ability Scores";
        lowAbilities.BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 4, WIS = 3, CHA = 10 };
        lowAbilities.Ticks = new List<Tick> { new() { DriverId = "class:fighter" } };
        var lowState = new ReplayStudio(registry).Evaluate(lowAbilities);
        Assert.Contains(lowState.SLAs, sla => sla.Id == "hc_sla_daylight");
        Assert.DoesNotContain(lowState.SLAs, sla => sla.Id == "hc_sla_protection_from_evil");
    }

    [Fact]
    public void HalfFiend_LoadsFromJson()
    {
        var registry = new ContentRegistry();
        registry.LoadTemplateFromFile(Path.Combine(TestContentHelper.GetPacksPath(), "srd_core", "templates", "half_fiend.json"));

        var template = registry.GetTemplate("template:half_fiend");
        Assert.Equal("Half-Fiend", template.Name);
        Assert.Equal(CreatureType.Outsider, template.TypeOverride);
        Assert.Contains("native", template.SubtypeAdditions);
        Assert.Equal(4, template.AbilityModifiers!.STR);
        Assert.Equal(4, template.LevelAdjustment);
        Assert.Equal(1, template.NaturalArmor);
        var flight = Assert.Single(template.DerivedSpeedRules);
        Assert.Equal(MovementMode.Fly, flight.Mode);
        Assert.Equal(MovementMode.Land, flight.SourceMode);
        Assert.True(flight.PreserveBetterExisting);
        Assert.Equal(2, template.NaturalAttacks.Count);
        Assert.Equal(10, template.ScalingPermabuffs.Count); // SLAs at 10 HD thresholds
        Assert.Single(template.ScalingFormulas); // SR formula
    }

    [Fact]
    public void Outsider8_HalfFiend_FullIntegration()
    {
        var registry = TestContentHelper.LoadAllPacks();

        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Half-Fiend Outsider",
            RaceId = "race:outsider",
            TemplateIds = new List<string> { "template:half_fiend" },
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 14, WIS = 12, CHA = 10
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "racial_hd:outsider" }, // HD 1
                new() { DriverId = "racial_hd:outsider" }, // HD 2
                new() { DriverId = "racial_hd:outsider" }, // HD 3
                new() { DriverId = "racial_hd:outsider" }, // HD 4
                new() { DriverId = "racial_hd:outsider" }, // HD 5
                new() { DriverId = "racial_hd:outsider" }, // HD 6
                new() { DriverId = "racial_hd:outsider" }, // HD 7
                new() { DriverId = "racial_hd:outsider" }, // HD 8
            }
        };

        var state = engine.Evaluate(character);

        // Ability scores: base + half-fiend mods (STR+4, DEX+4, CON+2, INT+4, CHA+2)
        Assert.Equal(20, state.AbilityScores.STR);  // 16 + 4
        Assert.Equal(18, state.AbilityScores.DEX);   // 14 + 4
        Assert.Equal(16, state.AbilityScores.CON);   // 14 + 2
        Assert.Equal(18, state.AbilityScores.INT);   // 14 + 4
        Assert.Equal(12, state.AbilityScores.WIS);   // 12 + 0
        Assert.Equal(12, state.AbilityScores.CHA);   // 10 + 2

        // Type override: Outsider (from template, but was already Outsider from race)
        Assert.Equal(CreatureType.Outsider, state.Type);

        // Subtypes should include "native" (from both race and template, deduped by HashSet)
        Assert.Contains("native", state.Subtypes);

        // HD and progression
        Assert.Equal(8, state.TotalHD);
        Assert.Equal(8, state.BaseBAB); // Good BAB outsider 8

        // Saves: all good outsider 8 = 2 + 8/2 = 6
        Assert.Equal(6, state.BaseSaves.Fort);
        Assert.Equal(6, state.BaseSaves.Ref);
        Assert.Equal(6, state.BaseSaves.Will);

        // Level Adjustment: 4 (from half-fiend)
        Assert.Equal(4, state.LevelAdjustment);
        Assert.Equal(12, state.ECL); // 8 HD + 4 LA

        // Natural Armor: 1 (from half-fiend)
        Assert.Equal(1, state.NaturalArmor);

        // Natural attacks: bite + 2 claws
        Assert.Equal(2, state.NaturalAttacks.Count);
        Assert.Contains(state.NaturalAttacks, a => a.Name == "Bite");
        Assert.Contains(state.NaturalAttacks, a => a.Name == "Claw" && a.Count == 2);

        // Movement: Half-Fiend derives permanent flight from the base land speed.
        Assert.Equal(30, state.Speeds[MovementMode.Land]);
        Assert.Equal(30, state.Speeds[MovementMode.Fly]);

        // Resistances from half-fiend
        Assert.Equal(10, state.Resistances["acid"]);
        Assert.Equal(10, state.Resistances["cold"]);
        Assert.Equal(10, state.Resistances["electricity"]);
        Assert.Equal(10, state.Resistances["fire"]);

        // SLAs: at HD 8, should have darkness (HD1), desecrate (HD3), unholy_blight (HD5), poison (HD7)
        Assert.Contains(state.SLAs, s => s.Id == "hf_sla_darkness");
        Assert.Contains(state.SLAs, s => s.Id == "hf_sla_desecrate");
        Assert.Contains(state.SLAs, s => s.Id == "hf_sla_unholy_blight");
        Assert.Contains(state.SLAs, s => s.Id == "hf_sla_poison");
        Assert.All(state.SLAs, sla => Assert.Equal(8, sla.CasterLevel));
        // Should NOT have contagion (HD 9) yet
        Assert.DoesNotContain(state.SLAs, s => s.Id == "hf_sla_contagion");

        // Spell resistance: TotalHD + 10 = 18
        Assert.Equal(18, state.SpellResistance);

        // Abilities
        Assert.Contains(state.Abilities, a => a.Id == "hf_darkvision_60");
        Assert.Contains(state.Abilities, a => a.Id == "hf_immunity_poison");
        Assert.Contains(state.Abilities, a => a.Id == "hf_smite_good");
    }

    [Fact]
    public void MixedRacialHdClassAndTemplate_ReplaysCompleteFinalState()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var character = new Character
        {
            Name = "Mixed Half-Fiend Fighter",
            RaceId = "race:outsider",
            TemplateIds = new List<string> { "template:half_fiend" },
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 12, DEX = 12, CON = 12, INT = 12, WIS = 12, CHA = 12
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "racial_hd:outsider" },
                new() { DriverId = "racial_hd:outsider" },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" }
            }
        };

        var state = new ReplayStudio(registry).Evaluate(character);

        Assert.Equal(4, state.TotalHD);
        Assert.Equal(2, state.ClassLevels["class:fighter"]);
        Assert.Equal(4, state.BaseBAB);
        Assert.Equal(4, state.LevelAdjustment);
        Assert.Equal(8, state.ECL);
        Assert.Equal(16, state.AbilityScores.STR);
        Assert.Contains(state.Abilities, ability => ability.Id == "hf_smite_good");
        Assert.Contains(state.SLAs, sla => sla.Id == "hf_sla_darkness");
    }

    [Fact]
    public void HalfFiend_SLAs_CorrectAtVariousHD()
    {
        var registry = TestContentHelper.LoadAllPacks();

        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "SLA Test",
            RaceId = "race:outsider",
            TemplateIds = new List<string> { "template:half_fiend" },
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10
            },
            Ticks = Enumerable.Range(0, 20).Select(_ => new Tick { DriverId = "racial_hd:outsider" }).ToList()
        };

        // At HD 1: darkness
        var state1 = engine.Evaluate(character, upToHD: 1);
        Assert.Single(state1.SLAs);
        Assert.Equal("hf_sla_darkness", state1.SLAs[0].Id);
        Assert.All(state1.SLAs, sla => Assert.Equal(1, sla.CasterLevel));

        // At HD 5: darkness, desecrate, unholy_blight
        var state5 = engine.Evaluate(character, upToHD: 5);
        Assert.Equal(3, state5.SLAs.Count);
        Assert.All(state5.SLAs, sla => Assert.Equal(5, sla.CasterLevel));

        // At HD 9: +contagion = 5 total
        var state9 = engine.Evaluate(character, upToHD: 9);
        Assert.Equal(5, state9.SLAs.Count);
        Assert.All(state9.SLAs, sla => Assert.Equal(9, sla.CasterLevel));

        // At HD 19: all 10 SLAs
        var state19 = engine.Evaluate(character, upToHD: 19);
        Assert.Equal(10, state19.SLAs.Count);
        Assert.All(state19.SLAs, sla => Assert.Equal(19, sla.CasterLevel));
        Assert.Contains(state19.SLAs, s => s.Id == "hf_sla_destruction");
    }

    [Fact]
    public void HalfFiend_SpellResistance_ScalesWithHD()
    {
        var registry = TestContentHelper.LoadAllPacks();

        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "SR Test",
            RaceId = "race:outsider",
            TemplateIds = new List<string> { "template:half_fiend" },
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10
            },
            Ticks = Enumerable.Range(0, 15).Select(_ => new Tick { DriverId = "racial_hd:outsider" }).ToList()
        };

        Assert.Equal(11, engine.Evaluate(character, upToHD: 1).SpellResistance);   // 1 + 10
        Assert.Equal(15, engine.Evaluate(character, upToHD: 5).SpellResistance);   // 5 + 10
        Assert.Equal(20, engine.Evaluate(character, upToHD: 10).SpellResistance);  // 10 + 10
        Assert.Equal(25, engine.Evaluate(character, upToHD: 15).SpellResistance);  // 15 + 10
    }

    /// <summary>
    /// Life state follows creature type. A template that turns a creature undead says nothing
    /// about "living" anywhere in its JSON, so deriving it is the only way the flag can be right —
    /// and <see cref="Prerequisite"/> gates on it, so a lich that reads as living passes checks
    /// meant to exclude it.
    /// </summary>
    [Theory]
    [InlineData("template:lich", CreatureType.Undead, false)]
    [InlineData("template:vampire", CreatureType.Undead, false)]
    [InlineData("template:undead", CreatureType.Undead, false)]
    [InlineData("template:half_fiend", CreatureType.Outsider, true)]
    public void TypeChangingTemplate_SetsLifeStateFromTheResultingType(
        string templateId, CreatureType expectedType, bool expectedLiving)
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Templated Human",
            RaceId = "race:human",
            TemplateIds = new List<string> { templateId },
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 12, DEX = 12, CON = 12, INT = 12, WIS = 12, CHA = 12
            },
            Ticks = new List<Tick> { new() { DriverId = "class:fighter" } }
        };

        var state = engine.Evaluate(character);

        Assert.Equal(expectedType, state.Type);
        Assert.Equal(expectedLiving, state.IsLiving);
        // None of these templates is incorporeal, so all stay corporeal.
        Assert.True(state.IsCorporeal);
    }

    /// <summary>
    /// The SRD splits a template's additions into "Special Attacks" and "Special Qualities", and
    /// the engine has a list for each. The lich's touch is the reason the distinction matters: it
    /// is a supernatural attack taken once per round, not a natural weapon, so it must not sit
    /// among the abilities as inert prose — nor should it ever reach the attack lines, which
    /// would hand it iteratives it does not get.
    /// </summary>
    [Fact]
    public void LichAttacksAreSpecialAttacks_NotAbilitiesAndNotNaturalWeapons()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var state = new ReplayStudio(registry).Evaluate(Templated("race:human", "template:lich"));

        foreach (var id in new[] { "lich_touch_attack", "lich_paralyzing_touch", "lich_fear_aura" })
        {
            Assert.Contains(state.SpecialAttacks, attack => attack.Id == id);
            Assert.DoesNotContain(state.Abilities, ability => ability.Id == id);
        }

        Assert.Equal("1/round",
            Assert.Single(state.SpecialAttacks, a => a.Id == "lich_touch_attack").UsesPerDay);

        // No natural weapon, so nothing lands in the attack lines and no iteratives are implied.
        Assert.DoesNotContain(state.NaturalAttacks, attack => attack.Name.Contains("ouch"));

        // What the SRD files under Special Qualities stays an ability.
        foreach (var id in new[] { "lich_turn_resistance", "lich_rejuvenation", "lich_immunities" })
            Assert.Contains(state.Abilities, ability => ability.Id == id);
    }

    [Fact]
    public void VampireSpecialAttacksAreRecordedAsSpecialAttacks()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var state = new ReplayStudio(registry).Evaluate(Templated("race:human", "template:vampire"));

        foreach (var id in new[]
                 { "vampire_blood_drain", "vampire_children_of_the_night", "vampire_dominate",
                   "vampire_create_spawn", "vampire_energy_drain" })
        {
            Assert.Contains(state.SpecialAttacks, attack => attack.Id == id);
            Assert.DoesNotContain(state.Abilities, ability => ability.Id == id);
        }
    }

    /// <summary>
    /// Lichdom is earned, not inherited: "Each lich must make its own phylactery, which requires
    /// the Craft Wondrous Item feat. The character must be able to cast spells and have a caster
    /// level of 11th or higher", the template is "any evil", and it "can be added to any humanoid
    /// creature". Those gates are what fix the acquisition HD when the template stops being
    /// applied at creation, so they have to be expressed rather than assumed.
    /// </summary>
    [Fact]
    public void LichTemplate_GatesOnThePhylacteryRequirements()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // A fighter meets none of the casting requirements and is not evil.
        var unqualified = engine.Evaluate(Templated("race:human", "template:lich"));
        foreach (var expected in new[] { "feat:craft_wondrous_item", "Caster level 11+", "Alignment" })
            Assert.Contains(unqualified.Warnings,
                w => w.Message.Contains("prerequisite not met") && w.Message.Contains(expected));

        // The corpus lich is a 13th-level bard: evil, casting at 11+, and holding the feat.
        var lich = new Character
        {
            RaceId = "race:human",
            Alignment = Alignment.NE,
            TemplateIds = new List<string> { "template:lich" },
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 17, WIS = 10, CHA = 16
            },
            Ticks = Enumerable.Range(0, 13).Select(_ => new Tick { DriverId = "class:bard" }).ToList(),
        };
        lich.Ticks[^1].Choices.FeatIds = new List<string> { "feat:craft_wondrous_item" };

        Assert.DoesNotContain(engine.Evaluate(lich).Warnings,
            w => w.Message.Contains("prerequisite not met for template"));
    }

    /// <summary>
    /// The two undead templates word their natural armor differently and mean it. A lich has
    /// "a +5 natural armor bonus <em>or the base creature's, whichever is better</em>"; a vampire's
    /// base natural armor "<em>improves by</em> +6". Applied to a creature that already has
    /// natural armor the two diverge, and applied to one that has none they agree — which is why
    /// the whole corpus is silent on this and a test has to say it.
    /// </summary>
    [Fact]
    public void LichNaturalArmorIsAFloorWhileVampireNaturalArmorIsAnIncrease()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        // race:medusa is the corpus's handy base with natural armor of its own.
        var plain = engine.Evaluate(Templated("race:medusa"));
        Assert.True(plain.NaturalArmor > 0, "fixture race must have natural armor for this to bite");

        Assert.Equal(Math.Max(plain.NaturalArmor, 5), engine.Evaluate(Templated("race:medusa", "template:lich")).NaturalArmor);
        Assert.Equal(plain.NaturalArmor + 6, engine.Evaluate(Templated("race:medusa", "template:vampire")).NaturalArmor);

        // With no natural armor to beat, the lich's floor is simply the +5.
        Assert.Equal(0, engine.Evaluate(Templated("race:human")).NaturalArmor);
        Assert.Equal(5, engine.Evaluate(Templated("race:human", "template:lich")).NaturalArmor);
    }

    private static Character Templated(string raceId, params string[] templateIds) => new()
    {
        RaceId = raceId,
        TemplateIds = templateIds.ToList(),
        BaseAbilityScores = new AbilityScoreSet
        {
            STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10
        },
        Ticks = new List<Tick> { new() { DriverId = "class:fighter" } },
    };

    /// <summary>
    /// SRD Nonabilities: "These creatures do not have an ability score of 0 — they lack the
    /// ability altogether. The modifier for a nonability is +0." Undead have no Constitution and
    /// incorporeal creatures no Strength, so a placeholder score must not reach hit points,
    /// Fortitude saves or the skills keyed to it. The placeholder here is deliberately low
    /// enough to be visible if it leaks: Con 3 would be −4 a die.
    /// </summary>
    [Fact]
    public void UndeadTemplate_RemovesConstitutionRatherThanScoringItLow()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        Character Build(params string[] templateIds) => new()
        {
            RaceId = "race:human",
            TemplateIds = templateIds.ToList(),
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 3, INT = 10, WIS = 10, CHA = 10
            },
            Ticks = Enumerable.Range(0, 4)
                .Select(_ => new Tick { DriverId = "class:fighter", Choices = new TickChoices { HitPointsRolled = 6 } })
                .ToList(),
        };

        var living = engine.Evaluate(Build());
        Assert.True(living.HasAbility(Ability.CON));
        Assert.Equal(-4, living.AbilityModifier(Ability.CON));

        var undead = engine.Evaluate(Build("template:lich"));
        Assert.False(undead.HasAbility(Ability.CON));
        Assert.Equal(0, undead.AbilityModifier(Ability.CON));

        // The score itself is still whatever the source carried — the sheet renders the absence.
        Assert.Equal(3, undead.AbilityScores.CON);

        // Four d12 rolls of 6, at +0 rather than −4 a die.
        Assert.Equal(4 * 6, undead.HP);
        Assert.Equal(living.EffectiveSaves.Fort + 4, undead.EffectiveSaves.Fort);
    }

    [Theory]
    [InlineData("template:undead")]
    [InlineData("template:lich")]
    [InlineData("template:vampire")]
    public void UndeadTemplates_ExposeCoreNonabilityImmunities(string templateId)
    {
        var state = new ReplayStudio(TestContentHelper.LoadAllPacks()).Evaluate(
            Templated("race:human", templateId));

        Assert.Contains("physical ability damage", state.Immunities);
        Assert.Contains("Fortitude effects (unless harmless or affects objects)", state.Immunities);
    }

    /// <summary>
    /// The incorporeal subtype takes Strength the same way: "It has no Strength score, so its
    /// Dexterity modifier applies to both its melee attacks and its ranged attacks."
    /// race:companion_shadow is undead *and* incorporeal, so it should lack both.
    /// </summary>
    [Fact]
    public void IncorporealUndead_LacksBothStrengthAndConstitution()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var state = new ReplayStudio(registry).Evaluate(new Character
        {
            RaceId = "race:companion_shadow",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 3, DEX = 14, CON = 3, INT = 10, WIS = 10, CHA = 10
            },
            Ticks = new List<Tick> { new() { DriverId = "racial_hd:undead" } },
        });

        Assert.False(state.HasAbility(Ability.STR));
        Assert.False(state.HasAbility(Ability.CON));
        Assert.Equal(0, state.AbilityModifier(Ability.STR));
        Assert.Equal(0, state.AbilityModifier(Ability.CON));
        // Dexterity is untouched — an incorporeal creature very much has one, and reads through
        // the accessor exactly as the plain modifier of its (racially adjusted) score.
        Assert.True(state.HasAbility(Ability.DEX));
        Assert.Equal(
            AbilityScoreSet.Modifier(state.AbilityScores.DEX),
            state.AbilityModifier(Ability.DEX));
    }

    [Fact]
    public void IncorporealCreature_HasNoCarryingCapacity()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var state = new ReplayStudio(registry).Evaluate(new Character
        {
            RaceId = "race:companion_shadow",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 18, DEX = 14, CON = 3, INT = 10, WIS = 10, CHA = 10
            },
            Ticks = new List<Tick> { new() { DriverId = "racial_hd:undead" } },
        });

        Assert.False(state.HasAbility(Ability.STR));
        Assert.Equal(0, state.Encumbrance.LightMax);
        Assert.Equal(0, state.Encumbrance.MediumMax);
        Assert.Equal(0, state.Encumbrance.HeavyMax);
        Assert.Equal(LoadCategory.Light, state.Encumbrance.Load);
    }

    /// <summary>
    /// "Increase all current and future Hit Dice to d12s" — the undead templates' side of the
    /// bargain for having no Constitution score. It reaches class hit dice, which is what makes
    /// it different from the racial-only adjustment a half-dragon applies, and it is a floor: a
    /// d12 barbarian is already there. A wizard whose dice stayed d4 makes every hit-point roll
    /// on an imported undead read as out of range.
    /// </summary>
    [Theory]
    [InlineData("template:lich")]
    [InlineData("template:vampire")]
    [InlineData("template:undead")]
    public void UndeadTemplate_RaisesEveryHitDieToD12(string templateId)
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        Character Build(string classId, params string[] templateIds) => new()
        {
            RaceId = "race:human",
            TemplateIds = templateIds.ToList(),
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 12, DEX = 12, CON = 12, INT = 12, WIS = 12, CHA = 12
            },
            Ticks = Enumerable.Range(0, 3)
                .Select(_ => new Tick { DriverId = classId })
                .ToList(),
        };

        // A d4 class is raised all the way; a d12 class was already there and does not change.
        Assert.All(engine.Evaluate(Build("class:wizard")).HitDice, die => Assert.Equal(4, die.DieSize));
        Assert.All(engine.Evaluate(Build("class:wizard", templateId)).HitDice,
            die => Assert.Equal(12, die.DieSize));
        Assert.All(engine.Evaluate(Build("class:barbarian", templateId)).HitDice,
            die => Assert.Equal(12, die.DieSize));

        // And an imported roll a d4 could never produce stops being reported as out of range.
        var imported = Build("class:wizard", templateId);
        foreach (var tick in imported.Ticks)
            tick.Choices.HitPointsRolled = 11;
        Assert.DoesNotContain(engine.Evaluate(imported).Warnings,
            w => w.Message.Contains("outside d"));
    }

    /// <summary>
    /// The race path has the same rule. race:companion_shadow is undead and carries the
    /// incorporeal subtype, and authors neither flag — both must still come out right.
    /// </summary>
    [Fact]
    public void UndeadIncorporealRace_IsNeitherLivingNorCorporeal()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "Shadow",
            RaceId = "race:companion_shadow",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 14, CON = 10, INT = 6, WIS = 12, CHA = 13
            },
            Ticks = new List<Tick> { new() { DriverId = "racial_hd:undead" } }
        };

        var state = engine.Evaluate(character);

        Assert.Equal(CreatureType.Undead, state.Type);
        Assert.False(state.IsLiving);
        Assert.False(state.IsCorporeal);
    }

    [Fact]
    public void TemplateDriver_Manual_BasicTest()
    {
        var template = new TemplateDriver
        {
            Id = "template:test",
            Name = "Test Template",
            TypeOverride = CreatureType.Outsider,
            SubtypeAdditions = new List<string> { "native" },
            AbilityModifiers = new AbilityScoreSet { STR = 2, DEX = 0, CON = 0, INT = 0, WIS = 0, CHA = 0 },
            NaturalArmor = 3,
            SpeedModifiers = new Dictionary<MovementMode, int> { { MovementMode.Fly, 50 } },
            FlyManeuverability = FlightManeuverability.Good,
            LevelAdjustment = 2,
            NaturalAttacks = new List<NaturalAttack>
            {
                new() { Name = "Slam", Damage = "1d6", Count = 1 }
            },
            CreationPermabuffs = new List<Permabuff>
            {
                new GrantAbility { Ability = new GrantedAbility { Id = "test_ability", Name = "Test" } }
            }
        };

        var registry = new ContentRegistry();
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:human",
            Name = "Human",
            Type = CreatureType.Humanoid,
            Size = Size.Medium,
            Speeds = new Dictionary<MovementMode, int> { { MovementMode.Land, 30 } }
        });
        registry.RegisterTemplate(template);
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:fighter",
            Name = "Fighter",
            HitDie = 10,
            SkillPointsPerLevel = 2,
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Good, Ref = ProgressionRate.Poor, Will = ProgressionRate.Poor }
        });

        var engine = new ReplayStudio(registry);
        var character = new Character
        {
            Name = "Template Test",
            RaceId = "race:human",
            TemplateIds = new List<string> { "template:test" },
            BaseAbilityScores = new AbilityScoreSet { STR = 14, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick> { new() { DriverId = "class:fighter" } }
        };

        var state = engine.Evaluate(character);

        Assert.Equal(CreatureType.Outsider, state.Type); // type override
        Assert.Contains("native", state.Subtypes);
        Assert.Equal(16, state.AbilityScores.STR); // 14 base + 2 template
        Assert.Equal(3, state.NaturalArmor);
        Assert.Equal(2, state.LevelAdjustment);
        Assert.Equal(50, state.Speeds[MovementMode.Fly]);
        Assert.Equal(FlightManeuverability.Good, state.FlyManeuverability);
        Assert.Single(state.NaturalAttacks);
        Assert.Contains(state.Abilities, a => a.Id == "test_ability");
    }

    [Fact]
    public void Paragon_MaximizesHitDiceAndAppliesTheEpicChassis()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var character = new Character
        {
            Name = "Paragon Fighter",
            RaceId = "race:human",
            TemplateIds = new List<string> { "template:paragon" },
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter", Choices = new TickChoices { HitPointsRolled = 3 } },
                new() { DriverId = "class:fighter", Choices = new TickChoices { HitPointsRolled = 3 } }
            }
        };

        var state = new ReplayStudio(registry).Evaluate(character);

        // "All ability scores are 15 points higher than those of the base creature."
        Assert.Equal(25, state.AbilityScores.CON);
        // Maximum hit points outrank the saved rolls of 3: two d10s at 10, +7 Con each, and the
        // template's additional 12 hit points per Hit Die.
        Assert.Equal(2 * (10 + 7 + 12), state.HP);
        // Speed triples; natural armor is a floor of +5 over the base creature's.
        Assert.Equal(90, state.Speeds[MovementMode.Land]);
        Assert.Equal(5, state.NaturalArmor);
        Assert.Equal(12, state.AC.Components[BonusType.Insight]);
        Assert.Equal(12, state.AC.Components[BonusType.Luck]);
        Assert.Equal(10, state.Resistances["fire"]);
        Assert.Equal(10, state.Resistances["cold"]);
        Assert.Equal(20, state.FastHealing);
        Assert.Equal(10, Assert.Single(state.DamageReduction, entry => entry.BypassedBy == "epic").Value);
        foreach (var target in new[] { SaveTarget.Fort, SaveTarget.Ref, SaveTarget.Will })
            Assert.Equal(10, state.SaveBonuses
                .Where(bonus => bonus.Target == target && bonus.BonusType == BonusType.Insight)
                .Sum(bonus => bonus.Value));
        foreach (var slaId in new[] { "pgn_sla_greater_dispel_magic", "pgn_sla_haste", "pgn_sla_see_invisibility" })
            Assert.Equal(15, Assert.Single(state.SLAs, sla => sla.Id == slaId).CasterLevel);
    }

    [Fact]
    public void Paragon_KeepsTheBetterResistanceAndNaturalArmorOfTheBaseCreature()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var baseline = new Character
        {
            Name = "Succubus",
            RaceId = "race:demon_succubus",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 },
            Ticks = new List<Tick> { new() { DriverId = "racial_hd:outsider" } }
        };
        var paragon = baseline.Clone();
        paragon.TemplateIds = new List<string> { "template:paragon" };

        var studio = new ReplayStudio(registry);
        var plain = studio.Evaluate(baseline);
        var state = studio.Evaluate(paragon);

        // "If the creature already possesses such resistance, use whichever is better" — a demon's
        // fire and cold resistance 10 must not be added to the template's own 10.
        Assert.Equal(Math.Max(10, plain.Resistances.GetValueOrDefault("fire")), state.Resistances["fire"]);
        Assert.Equal(Math.Max(10, plain.Resistances.GetValueOrDefault("cold")), state.Resistances["cold"]);
        Assert.Equal(Math.Max(5, plain.NaturalArmor), state.NaturalArmor);
    }
}
