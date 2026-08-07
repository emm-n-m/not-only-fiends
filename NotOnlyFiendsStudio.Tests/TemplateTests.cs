using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public class TemplateTests
{
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
}
