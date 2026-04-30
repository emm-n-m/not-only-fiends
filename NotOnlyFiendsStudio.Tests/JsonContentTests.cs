using System.Text.Json;
using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public class JsonContentTests
{
    private string SrdCorePath() => Path.Combine(TestContentHelper.GetPacksPath(), "srd_core");

    [Fact]
    public void JsonRoundTrip_ClassDriver()
    {
        var fighter = new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:fighter",
            Name = "Fighter",
            HitDie = 10,
            SkillPointsPerLevel = 2,
            ClassSkills = new List<string> { "climb", "swim" },
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Good,
                Ref = ProgressionRate.Poor,
                Will = ProgressionRate.Poor
            },
            LevelPermabuffs = new Dictionary<int, List<Permabuff>>
            {
                { 1, new List<Permabuff> { new GrantFeatSlot { Restriction = "fighter_bonus" } } }
            }
        };

        var json = JsonSerializer.Serialize<Driver>(fighter, JsonOptions.Default);
        var deserialized = JsonSerializer.Deserialize<Driver>(json, JsonOptions.Default);

        Assert.NotNull(deserialized);
        Assert.IsType<HDDriver>(deserialized);
        var result = (HDDriver)deserialized!;
        Assert.Equal("class:fighter", result.Id);
        Assert.Equal(10, result.HitDie);
        Assert.Equal(BABProgression.Good, result.BABProgression);
        Assert.Equal(ProgressionRate.Good, result.SaveProgression.Fort);
        Assert.Contains("climb", result.ClassSkills);
        Assert.True(result.LevelPermabuffs.ContainsKey(1));
        Assert.Single(result.LevelPermabuffs[1]);
        Assert.IsType<GrantFeatSlot>(result.LevelPermabuffs[1][0]);
    }

    [Fact]
    public void JsonRoundTrip_RacialHDDriver()
    {
        var outsider = new HDDriver
        {
            Kind = DriverKind.RacialHD,
            Id = "racial_hd:outsider",
            Name = "Outsider",
            HitDie = 8,
            SkillPointsPerLevel = 8,
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression
            {
                Fort = ProgressionRate.Good,
                Ref = ProgressionRate.Good,
                Will = ProgressionRate.Good
            },
            Prerequisites = new List<Prerequisite> { new HasRace { RaceId = "outsider" } }
        };

        var json = JsonSerializer.Serialize<Driver>(outsider, JsonOptions.Default);
        var deserialized = JsonSerializer.Deserialize<Driver>(json, JsonOptions.Default);

        Assert.NotNull(deserialized);
        Assert.IsType<HDDriver>(deserialized);
        var result = (HDDriver)deserialized!;
        Assert.Equal("racial_hd:outsider", result.Id);
        Assert.Equal(8, result.HitDie);
        Assert.Single(result.Prerequisites);
        Assert.IsType<HasRace>(result.Prerequisites[0]);
    }

    [Fact]
    public void LoadFromFile_Human()
    {
        var registry = new ContentRegistry();
        registry.LoadRaceFromFile(Path.Combine(SrdCorePath(), "races", "human.json"));

        var human = registry.GetRace("human");
        Assert.Equal("Human", human.Name);
        Assert.Equal(CreatureType.Humanoid, human.Type);
        Assert.Equal(Size.Medium, human.Size);
        Assert.Equal(30, human.Speeds[MovementMode.Land]);
        Assert.Equal(1, human.BonusFeats);
        Assert.Null(human.RacialHDDriverId);
    }

    [Fact]
    public void LoadFromFile_Outsider()
    {
        var registry = new ContentRegistry();
        registry.LoadRaceFromFile(Path.Combine(SrdCorePath(), "races", "outsider.json"));

        var outsider = registry.GetRace("outsider");
        Assert.Equal("Outsider", outsider.Name);
        Assert.Equal(CreatureType.Outsider, outsider.Type);
        Assert.Equal("racial_hd:outsider", outsider.RacialHDDriverId);
        Assert.Contains("native", outsider.Subtypes);
    }

    [Fact]
    public void LoadFromFile_OutsiderDriver()
    {
        var registry = new ContentRegistry();
        registry.LoadDriverFromFile(Path.Combine(SrdCorePath(), "racial_hd", "outsider.json"));

        var driver = registry.GetDriver("racial_hd:outsider");
        Assert.IsType<HDDriver>(driver);
        var rhd = (HDDriver)driver;
        Assert.Equal(8, rhd.HitDie);
        Assert.Equal(8, rhd.SkillPointsPerLevel);
        Assert.Equal(BABProgression.Good, rhd.BABProgression);
        Assert.Single(rhd.Prerequisites);
        Assert.IsType<HasRace>(rhd.Prerequisites[0]);
    }

    [Fact]
    public void LoadFromFile_Fighter()
    {
        var registry = new ContentRegistry();
        registry.LoadDriverFromFile(Path.Combine(SrdCorePath(), "classes", "base", "fighter.json"));

        var driver = registry.GetDriver("class:fighter");
        Assert.IsType<HDDriver>(driver);
        var fighter = (HDDriver)driver;
        Assert.Equal(10, fighter.HitDie);
        Assert.Equal(2, fighter.SkillPointsPerLevel);
        Assert.Equal(BABProgression.Good, fighter.BABProgression);
        Assert.Contains("climb", fighter.ClassSkills);
        // Fighter gets bonus feats at 1, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20
        Assert.True(fighter.LevelPermabuffs.ContainsKey(1));
        Assert.True(fighter.LevelPermabuffs.ContainsKey(2));
        Assert.True(fighter.LevelPermabuffs.ContainsKey(4));
    }

    [Fact]
    public void LoadFromFile_Barbarian()
    {
        var registry = new ContentRegistry();
        registry.LoadDriverFromFile(Path.Combine(SrdCorePath(), "classes", "base", "barbarian.json"));

        var driver = registry.GetDriver("class:barbarian");
        Assert.IsType<HDDriver>(driver);
        var barb = (HDDriver)driver;
        Assert.Equal(12, barb.HitDie);
        Assert.Equal(4, barb.SkillPointsPerLevel);
        Assert.Equal(BABProgression.Good, barb.BABProgression);
        // Has alignment prereq
        Assert.Single(barb.Prerequisites);
        Assert.IsType<AlignmentReq>(barb.Prerequisites[0]);
        var alignReq = (AlignmentReq)barb.Prerequisites[0];
        Assert.DoesNotContain(Alignment.LG, alignReq.Allowed);
        Assert.Contains(Alignment.CG, alignReq.Allowed);
    }

    [Fact]
    public void LoadContentDirectory_IntegrationTest()
    {
        var registry = TestContentHelper.LoadAllPacks();

        // Should have loaded all content
        Assert.NotNull(registry.GetRace("human"));
        Assert.NotNull(registry.GetRace("outsider"));
        Assert.NotNull(registry.GetDriver("class:fighter"));
        Assert.NotNull(registry.GetDriver("class:barbarian"));
        Assert.NotNull(registry.GetDriver("racial_hd:outsider"));
    }

    [Fact]
    public void LoadFromJSON_Fighter5_SameResultAsManual()
    {
        // Load content from JSON
        var registry = TestContentHelper.LoadAllPacks();

        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "JSON Fighter",
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter" },
                new() { DriverId = "class:fighter", Choices = new TickChoices { AbilityIncrease = Ability.STR } },
                new() { DriverId = "class:fighter" },
            }
        };

        var state = engine.Evaluate(character);

        Assert.Equal(5, state.TotalHD);
        Assert.Equal(5, state.BaseBAB);
        Assert.Equal(4, state.BaseSaves.Fort);
        Assert.Equal(1, state.BaseSaves.Ref);
        Assert.Equal(1, state.BaseSaves.Will);
        Assert.Equal(44, state.HP);
        Assert.Equal(17, state.AbilityScores.STR); // 16 base + 1 at HD 4
    }

    [Fact]
    public void ArcaneTrickster_AdvancesArcaneCasterLevel()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "AT Test",
            RaceId = "human",
            Alignment = Alignment.CN,
            BaseAbilityScores = new AbilityScoreSet { STR = 8, DEX = 16, CON = 10, INT = 10, WIS = 10, CHA = 16 },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:sorcerer" },
                new() { DriverId = "class:sorcerer" },
                new() { DriverId = "class:sorcerer" },
                new() { DriverId = "class:arcane_trickster" },
                new() { DriverId = "class:arcane_trickster" },
            }
        };

        var state = engine.Evaluate(character);

        // Sorcerer 3 gives CL 3, then 2 levels of AT should advance to CL 5
        var sorcCasting = state.Spellcasting["class:sorcerer"];
        Assert.Equal(5, sorcCasting.CasterLevel);
    }

    [Fact]
    public void LoadFromJSON_DrowRace_AppliesAbilityModifiers()
    {
        var registry = TestContentHelper.LoadAllPacks();
        var engine = new ReplayStudio(registry);

        var character = new Character
        {
            Name = "JSON Drow",
            RaceId = "drow",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10
            }
        };

        var state = engine.Evaluate(character);

        Assert.Equal(10, state.AbilityScores.STR);
        Assert.Equal(12, state.AbilityScores.DEX);
        Assert.Equal(8, state.AbilityScores.CON);
        Assert.Equal(12, state.AbilityScores.INT);
        Assert.Equal(10, state.AbilityScores.WIS);
        Assert.Equal(12, state.AbilityScores.CHA);
        Assert.Equal(2, state.LevelAdjustment);
    }
}
