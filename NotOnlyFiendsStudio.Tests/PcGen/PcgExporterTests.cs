using System.Text;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.PcGen;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests.PcGen;

public class PcgExporterTests
{
    // Exact tag shapes below come from PCGen's PCGVer2Creator and the frozen
    // "High Priestess's Bodyguard.pcg" fixture, not from transcribing exporter output.
    private static Character Fighter() => new()
    {
        Name = "Brynn & Élan",
        Alignment = Alignment.NG,
        Gender = "Female",
        RaceId = "race:human",
        BaseAbilityScores = new AbilityScoreSet
        {
            STR = 16, DEX = 14, CON = 14, INT = 10, WIS = 12, CHA = 8,
        },
        Ticks =
        {
            new Tick
            {
                DriverId = "class:fighter",
                Choices = new TickChoices
                {
                    HitPointsRolled = 10,
                    FeatIds = new List<string>
                    {
                        "feat:power_attack", "feat:cleave", "feat:improved_initiative",
                    },
                    SkillAllocations = new List<SkillAllocation>
                    {
                        new() { SkillId = "skill:intimidate", HalfRanks = 8 },
                    },
                },
            },
        },
    };

    [Fact]
    public void Export_Fighter_WritesLoadableDecisionRecords()
    {
        var registry = TestContentHelper.LoadBundledPacks();

        var result = PcgExporter.Export(Fighter(), registry);

        Assert.Equal(PcgExportStatus.Exact, result.Status);
        Assert.Equal("utf-8", result.Encoding);
        Assert.DoesNotContain('\r', result.Content);
        Assert.Contains("PCGVERSION:2.0\n", result.Content);
        Assert.Contains("CHARACTERNAME:Brynn &amp; Élan\n", result.Content);
        Assert.Contains("CLASS:Fighter|LEVEL:1", result.Content);
        Assert.Contains("CLASSABILITIESLEVEL:Fighter=1|HITPOINTS:10", result.Content);
        Assert.Contains("SKILL:Intimidate|CLASSBOUGHT:[CLASS:Fighter|RANKS:4.0", result.Content);
        Assert.Single(result.Content.Split('\n'), line => line.StartsWith("ABILITY:Fighter Feat|"));
        Assert.Equal(2, result.Content.Split('\n').Count(line => line.StartsWith("ABILITY:FEAT|")));
    }

    [Fact]
    public void ExportThenImport_FighterPreservesCoreInputs()
    {
        var registry = TestContentHelper.LoadBundledPacks();
        var exported = PcgExporter.Export(Fighter(), registry);

        var parsed = PcgParser.ParseText(exported.Content, exported.FileName);
        var imported = PcgConverter.Convert(parsed, new PcgIdMapper(), registry).Character;

        Assert.Equal("Brynn & Élan", imported.Name);
        Assert.Equal("race:human", imported.RaceId);
        Assert.Equal(Alignment.NG, imported.Alignment);
        Assert.Equal("Female", imported.Gender);
        Assert.Equal(16, imported.BaseAbilityScores.STR);
        Assert.Equal("class:fighter", Assert.Single(imported.Ticks).DriverId);
        Assert.Equal(10, imported.Ticks[0].Choices.HitPointsRolled);
        Assert.Contains("feat:power_attack", imported.Ticks[0].Choices.FeatIds!);
        Assert.Contains(imported.Ticks[0].Choices.SkillAllocations!, allocation =>
            allocation.SkillId == "skill:intimidate" && allocation.HalfRanks == 8);
    }

    [Theory]
    [InlineData(0, "Divine Rank (0)", "Quasideity")]
    [InlineData(6, "Divine Rank (6)", "Lesser Deity")]
    [InlineData(25, "Divine Rank (21+)", "Overdeity")]
    public void ExportThenImport_PreservesDivineRank(int rank, string rankName, string band)
    {
        var registry = TestContentHelper.LoadBundledPacks();
        var character = Fighter();
        character.Divinity = new DivinityChoices { DivineRank = rank };

        var exported = PcgExporter.Export(character, registry);

        Assert.Contains($"TEMPLATESAPPLIED:[NAME:Divine Rank|CHOSENTEMPLATE:[NAME:{rankName}]]", exported.Content);
        Assert.Contains($"TEMPLATESAPPLIED:[NAME:{rankName}|CHOSENTEMPLATE:[NAME:{band}]]", exported.Content);

        var parsed = PcgParser.ParseText(exported.Content, exported.FileName);
        var reimported = PcgConverter.Convert(parsed, new PcgIdMapper(), registry);

        // 21+ is one PCGen row for every overdeity rank, so it round-trips to 21, not to 25.
        Assert.Equal(rank > 20 ? 21 : rank, reimported.Character.Divinity?.DivineRank);
        Assert.Empty(reimported.DroppedTemplates);
    }

    [Fact]
    public void Export_CrossClassSkill_WritesPcgenCostAndClassSkillFlag()
    {
        var registry = TestContentHelper.LoadBundledPacks();
        var character = Fighter();
        character.Ticks[0].Choices.SkillAllocations!.Add(new SkillAllocation
        {
            SkillId = "skill:heal",
            HalfRanks = 2,
        });

        var result = PcgExporter.Export(character, registry);

        Assert.Contains(
            "SKILL:Heal|CLASSBOUGHT:[CLASS:Fighter|RANKS:1.0|COST:2|CLASSSKILL:N]",
            result.Content);
    }

    [Fact]
    public void Export_ReportsMechanicsPcgenCannotRepresent()
    {
        var registry = TestContentHelper.LoadBundledPacks();
        var character = Fighter();
        character.PermanentEvents.Add(new PermanentEvent
        {
            BeforeTick = 1,
            Permabuffs = new List<Permabuff>
            {
                new ModifyAttribute { Target = AttributeTarget.AbilityScore, Value = 1, AbilityScore = Ability.STR },
            },
        });

        var result = PcgExporter.Export(character, registry);

        Assert.Equal(PcgExportStatus.Partial, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code == "unsupported_permanent_events");
        Assert.NotEmpty(result.Content);
    }

    [Fact]
    public void Export_Utf8ContentRoundTripsThroughBytes()
    {
        var registry = TestContentHelper.LoadBundledPacks();
        var exported = PcgExporter.Export(Fighter(), registry);

        var bytes = Encoding.UTF8.GetBytes(exported.Content);
        var decoded = Encoding.UTF8.GetString(bytes);

        Assert.Contains("Élan", decoded);
    }

    [Fact]
    public void ParseFile_ReadsUtf8AndFallsBackForLegacyLatin1()
    {
        var utf8Path = Path.Combine(Path.GetTempPath(), $"pcg_utf8_{Guid.NewGuid():N}.pcg");
        var latin1Path = Path.Combine(Path.GetTempPath(), $"pcg_latin1_{Guid.NewGuid():N}.pcg");
        const string content = "PCGVERSION:2.0\nCHARACTERNAME:Élan\nRACE:Human\n";
        try
        {
            File.WriteAllText(utf8Path, content, new UTF8Encoding(false));
            File.WriteAllBytes(latin1Path, Encoding.Latin1.GetBytes(content));

            Assert.Equal("Élan", PcgParser.Parse(utf8Path).CharacterName);
            Assert.Equal("Élan", PcgParser.Parse(latin1Path).CharacterName);
        }
        finally
        {
            File.Delete(utf8Path);
            File.Delete(latin1Path);
        }
    }

    [RequiresPcgFixturesFact]
    public void ImportedFighter_ExportThenImport_PreservesTimelineAndChosenFeats()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        var sourcePath = TestContentHelper.PcgFixture("High Priestess's Bodyguard.pcg");
        var source = PcgConverter.Convert(PcgParser.Parse(sourcePath), new PcgIdMapper(), registry).Character;

        var exported = PcgExporter.Export(source, registry);
        var reimported = PcgConverter.Convert(
            PcgParser.ParseText(exported.Content, exported.FileName), new PcgIdMapper(), registry).Character;

        Assert.NotEqual(PcgExportStatus.Blocked, exported.Status);
        Assert.Equal(source.Ticks.Select(tick => tick.DriverId), reimported.Ticks.Select(tick => tick.DriverId));
        Assert.Equal(
            source.Ticks.SelectMany(tick => tick.Choices.FeatIds ?? new List<string>()).OrderBy(id => id),
            reimported.Ticks.SelectMany(tick => tick.Choices.FeatIds ?? new List<string>()).OrderBy(id => id));
    }
}
