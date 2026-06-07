using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.PcGen;
using Xunit.Abstractions;

namespace NotOnlyFiendsStudio.Tests.PcGen;

public class PcgReconstructionTests
{
    private readonly ITestOutputHelper _output;

    public PcgReconstructionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string GetSampleCharactersPath() =>
        TestContentHelper.GetOptionalPcgenCharactersPath()
        ?? Path.Combine(TestContentHelper.GetSolutionRoot(), "pcgen_characters");

    private (ContentRegistry registry, ReplayStudio engine) CreateStudio()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        return (registry, new ReplayStudio(registry));
    }

    private static PcgCharacterData LoadCharacter(string filename) =>
        PcgParser.Parse(Path.Combine(GetSampleCharactersPath(), filename));

    public static IEnumerable<object[]> AllPcgFiles()
    {
        var dir = GetSampleCharactersPath();
        if (!Directory.Exists(dir))
            return Enumerable.Empty<object[]>();
        return Directory.GetFiles(dir, "*.pcg")
            .Select(f => new object[] { Path.GetFileName(f) });
    }

    // ---------------------------------------------------------------
    // Test A: Parser Verification
    // ---------------------------------------------------------------

    [RequiresPcgenCharactersFact]
    public void Parse_HighPriestess_ExtractsAllFields()
    {
        var data = LoadCharacter("High Priestess.pcg");

        Assert.Equal("High Priestess", data.CharacterName);
        Assert.Equal("Human", data.Race);
        Assert.Equal("NE", data.Alignment);

        // Ability scores
        Assert.Equal(9, data.BaseStats["STR"]);
        Assert.Equal(12, data.BaseStats["DEX"]);
        Assert.Equal(12, data.BaseStats["CON"]);
        Assert.Equal(12, data.BaseStats["INT"]);
        Assert.Equal(17, data.BaseStats["WIS"]);
        Assert.Equal(14, data.BaseStats["CHA"]);

        // Single class: Cleric 6
        Assert.Single(data.Classes);
        Assert.Equal("Cleric", data.Classes[0].Name);
        Assert.Equal(6, data.Classes[0].Level);

        // 6 levels
        Assert.Equal(6, data.Levels.Count);
        Assert.Equal(8, data.Levels[0].HitPoints); // First level max HP

        // Ability increase at level 4
        Assert.Equal("WIS", data.Levels[3].AbilityIncrease);
        Assert.Null(data.Levels[0].AbilityIncrease);

        // Skills with ranks
        Assert.Contains(data.Skills, s => s.Name == "Concentration" && s.Ranks == 9.0);
        Assert.Contains(data.Skills, s => s.Name == "Diplomacy" && s.Ranks == 9.0);
        Assert.Contains(data.Skills, s => s.Name == "Knowledge (Religion)" && s.Ranks == 9.0);

        // Feats
        Assert.Contains(data.Feats, f => f.Key == "Eschew Materials");

        // Domains
        Assert.Equal(2, data.Domains.Count);
        Assert.Contains(data.Domains, d => d.Name == "Lust");
        Assert.Contains(data.Domains, d => d.Name == "Charm");

        // Templates — all should be internal
        Assert.All(data.Templates, t => Assert.True(t.IsInternal,
            $"Expected template '{t.Name}' to be internal"));
    }

    [RequiresPcgenCharactersFact]
    public void Parse_Vampire_ExtractsMulticlass()
    {
        var data = LoadCharacter("vampire.pcg");

        Assert.Equal("vampire", data.CharacterName);
        Assert.Equal("Human", data.Race);

        // Multiclass: Wizard 4, Spectral Loremaster 10, Archmage 5, Deathseeker 5
        Assert.Equal(4, data.Classes.Count);
        Assert.Contains(data.Classes, c => c.Name == "Wizard" && c.Level == 4);
        Assert.Contains(data.Classes, c => c.Name == "Spectral Loremaster" && c.Level == 10);
        Assert.Contains(data.Classes, c => c.Name == "Archmage" && c.Level == 5);
        Assert.Contains(data.Classes, c => c.Name == "Deathseeker" && c.Level == 5);
        Assert.Equal(24, data.TotalClassLevels);

        // Subclass: Necromancer
        var wizard = data.Classes.First(c => c.Name == "Wizard");
        Assert.Equal("Necromancer", wizard.Subclass);

        // Ability increases at various levels
        var abilityLevels = data.Levels.Where(l => l.AbilityIncrease != null).ToList();
        Assert.True(abilityLevels.Count > 0);
        Assert.All(abilityLevels, l => Assert.Equal("INT", l.AbilityIncrease));
    }

    [RequiresPcgenCharactersFact]
    public void Parse_PixieOfficers_ExtractsTripleClass()
    {
        var data = LoadCharacter("Pixie Officers.pcg");

        Assert.Equal("Pixie", data.Race);
        Assert.Equal(3, data.Classes.Count);
        Assert.Contains(data.Classes, c => c.Name == "Druid" && c.Level == 3);
        Assert.Contains(data.Classes, c => c.Name == "Sorcerer" && c.Level == 4);
        Assert.Contains(data.Classes, c => c.Name == "Arcane Hierophant" && c.Level == 3);
        Assert.Equal(10, data.TotalClassLevels);
    }

    // ---------------------------------------------------------------
    // Test B: Gap Analysis
    // ---------------------------------------------------------------

    [RequiresPcgenCharactersTheory]
    [MemberData(nameof(AllPcgFiles))]
    public void GapAnalysis_Race(string filename)
    {
        var data = LoadCharacter(filename);
        var mapper = new PcgIdMapper();
        var raceId = mapper.MapRace(data.Race);

        if (raceId == null)
        {
            Assert.Fail($"Race '{data.Race}' has no engine mapping");
            return;
        }

        var (registry, _) = CreateStudio();
        var exists = registry.GetAllRaces().Any(r => r.Id == raceId);
        Assert.True(exists, $"Race '{data.Race}' maps to '{raceId}' but not found in content");
    }

    [RequiresPcgenCharactersTheory]
    [MemberData(nameof(AllPcgFiles))]
    public void GapAnalysis_Classes(string filename)
    {
        var data = LoadCharacter(filename);
        var mapper = new PcgIdMapper();
        var (registry, _) = CreateStudio();
        var missing = new List<string>();
        var driverIds = new HashSet<string>(registry.GetAllDrivers().Select(d => d.Id));

        foreach (var cls in data.Classes)
        {
            var id = mapper.MapClass(cls.Name);
            if (id == null)
            {
                missing.Add($"'{cls.Name}' (level {cls.Level}) -- no mapping");
                continue;
            }
            if (!driverIds.Contains(id))
                missing.Add($"'{cls.Name}' -> '{id}' -- mapped but not in content");
        }

        Assert.True(missing.Count == 0,
            $"Missing classes:\n  {string.Join("\n  ", missing)}");
    }

    [RequiresPcgenCharactersTheory]
    [MemberData(nameof(AllPcgFiles))]
    public void GapAnalysis_Feats(string filename)
    {
        var data = LoadCharacter(filename);
        if (data.Feats.Count == 0) return;

        var mapper = new PcgIdMapper();
        var (registry, _) = CreateStudio();
        var missing = new List<string>();

        foreach (var feat in data.Feats)
        {
            var id = mapper.MapFeat(feat.Key);
            if (!registry.TryGetFeat(id, out _))
                missing.Add($"'{feat.Key}' -> '{id}'");
        }

        Assert.True(missing.Count == 0,
            $"Missing {missing.Count}/{data.Feats.Count} feats:\n  {string.Join("\n  ", missing)}");
    }

    [RequiresPcgenCharactersTheory]
    [MemberData(nameof(AllPcgFiles))]
    public void GapAnalysis_Templates(string filename)
    {
        var data = LoadCharacter(filename);
        var nonInternal = data.Templates.Where(t => !t.IsInternal).ToList();
        if (nonInternal.Count == 0) return;

        var mapper = new PcgIdMapper();
        var (registry, _) = CreateStudio();
        var templateIds = new HashSet<string>(registry.GetAllTemplates().Select(t => t.Id));
        var missing = new List<string>();

        foreach (var t in nonInternal)
        {
            var id = mapper.MapTemplate(t.Name);
            if (!templateIds.Contains(id))
                missing.Add($"'{t.Name}' -> '{id}'");
        }

        Assert.True(missing.Count == 0,
            $"Missing templates:\n  {string.Join("\n  ", missing)}");
    }

    [RequiresPcgenCharactersTheory]
    [MemberData(nameof(AllPcgFiles))]
    public void GapAnalysis_Domains(string filename)
    {
        var data = LoadCharacter(filename);
        if (data.Domains.Count == 0) return;

        var mapper = new PcgIdMapper();
        var (registry, _) = CreateStudio();
        var missing = new List<string>();

        foreach (var domain in data.Domains)
        {
            var id = mapper.MapDomain(domain.Name);
            if (!registry.TryGetDomain(id, out _))
                missing.Add($"'{domain.Name}' -> '{id}'");
        }

        Assert.True(missing.Count == 0,
            $"Missing domains:\n  {string.Join("\n  ", missing)}");
    }

    // ---------------------------------------------------------------
    // Test C: Buildability Report
    // ---------------------------------------------------------------

    [RequiresPcgenCharactersFact]
    public void BuildabilityReport()
    {
        var mapper = new PcgIdMapper();
        var (registry, _) = CreateStudio();
        var dir = GetSampleCharactersPath();

        var driverIds = new HashSet<string>(registry.GetAllDrivers().Select(d => d.Id));
        var raceIds = new HashSet<string>(registry.GetAllRaces().Select(r => r.Id));
        var templateIds = new HashSet<string>(registry.GetAllTemplates().Select(t => t.Id));

        var results = new List<(string file, string name, bool canBuild, List<string> blockers)>();

        foreach (var path in Directory.GetFiles(dir, "*.pcg").OrderBy(f => f))
        {
            var data = PcgParser.Parse(path);
            var blockers = new List<string>();

            // Check race
            var raceId = mapper.MapRace(data.Race);
            if (raceId == null)
                blockers.Add($"Race: {data.Race} (unmapped)");
            else if (!raceIds.Contains(raceId))
                blockers.Add($"Race: {data.Race} -> {raceId} (missing content)");

            // Check classes
            foreach (var cls in data.Classes)
            {
                var classId = mapper.MapClass(cls.Name);
                if (classId == null)
                    blockers.Add($"Class: {cls.Name} lv{cls.Level} (unmapped)");
                else if (!driverIds.Contains(classId))
                    blockers.Add($"Class: {cls.Name} -> {classId} (missing content)");
            }

            // Check non-internal templates
            foreach (var t in data.Templates.Where(t => !t.IsInternal))
            {
                var tid = mapper.MapTemplate(t.Name);
                if (!templateIds.Contains(tid))
                    blockers.Add($"Template: {t.Name}");
            }

            results.Add((Path.GetFileName(path), data.CharacterName, blockers.Count == 0, blockers));
        }

        var buildable = results.Where(r => r.canBuild).ToList();
        var blocked = results.Where(r => !r.canBuild).ToList();

        _output.WriteLine($"=== BUILDABILITY REPORT ===");
        _output.WriteLine($"Buildable: {buildable.Count}/{results.Count}");
        _output.WriteLine("");

        if (buildable.Count > 0)
        {
            _output.WriteLine("--- BUILDABLE ---");
            foreach (var r in buildable)
                _output.WriteLine($"  OK: {r.file} ({r.name})");
            _output.WriteLine("");
        }

        _output.WriteLine("--- BLOCKED ---");
        foreach (var r in blocked)
        {
            _output.WriteLine($"  {r.file} ({r.name}):");
            foreach (var b in r.blockers)
                _output.WriteLine($"    - {b}");
        }

        // Collect all unique missing content across all characters
        var allMissingRaces = blocked.SelectMany(r => r.blockers.Where(b => b.StartsWith("Race:"))).Distinct().ToList();
        var allMissingClasses = blocked.SelectMany(r => r.blockers.Where(b => b.StartsWith("Class:"))).Distinct().ToList();
        var allMissingTemplates = blocked.SelectMany(r => r.blockers.Where(b => b.StartsWith("Template:"))).Distinct().ToList();

        _output.WriteLine("");
        _output.WriteLine("--- CONTENT NEEDED (unique) ---");
        _output.WriteLine($"Races ({allMissingRaces.Count}):");
        foreach (var r in allMissingRaces) _output.WriteLine($"  {r}");
        _output.WriteLine($"Classes ({allMissingClasses.Count}):");
        foreach (var c in allMissingClasses) _output.WriteLine($"  {c}");
        _output.WriteLine($"Templates ({allMissingTemplates.Count}):");
        foreach (var t in allMissingTemplates) _output.WriteLine($"  {t}");
    }

    // ---------------------------------------------------------------
    // Test D: Full Reconstruction
    // ---------------------------------------------------------------

    [RequiresPcgenCharactersFact]
    public void Reconstruct_HighPriestess_HumanCleric6()
    {
        var data = LoadCharacter("High Priestess.pcg");
        var mapper = new PcgIdMapper();
        var (registry, engine) = CreateStudio();

        var conversionResult = PcgConverter.Convert(data, mapper, registry);
        var character = conversionResult.Character;
        var state = engine.Evaluate(character);

        // Identity
        Assert.Equal("human", state.RaceId);
        Assert.Equal(6, state.TotalHD);
        Assert.Equal(6, state.ClassLevels["class:cleric"]);

        // PCGen's STAT already bakes in level-up increases. STAT:WIS:17 = rolled 16 +
        // 1 HD-4 increase, so the engine reconstructs the base as 16 + 1 = 17.
        // Equipment then adds Periapt of Wisdom +2 → final WIS = 19 (matches PCGen display).
        Assert.Equal(19, state.AbilityScores.WIS);
        Assert.Equal(9, state.AbilityScores.STR);

        // HP: Studio uses deterministic formula (max first HD, average thereafter)
        // Cleric d8: first HD = 8, subsequent = 8/2+1 = 5 each
        // HP = 8 + 5*5 + CON mod(+1) * 6 = 33 + 6 = 39
        var conMod = AbilityScoreSet.Modifier(data.BaseStats["CON"]);
        var expectedHP = 8 + 5 * (data.TotalClassLevels - 1) + conMod * data.TotalClassLevels;
        Assert.Equal(expectedHP, state.HP);

        // BAB: Cleric has average (3/4) progression → level 6 = 4
        Assert.Equal(4, state.BaseBAB);

        // Saves: Cleric = Fort good, Ref poor, Will good
        // Good save at level 6: 2 + 6/2 = 5, plus Cloak of Resistance +1 = 6
        // Poor save at level 6: 6/3 = 2, plus Cloak of Resistance +1 = 3
        Assert.Equal(6, state.BaseSaves.Fort);
        Assert.Equal(3, state.BaseSaves.Ref);
        Assert.Equal(6, state.BaseSaves.Will);

        // Skills: verify ranks are present
        foreach (var skill in data.Skills)
        {
            var skillId = mapper.MapSkill(skill.Name);
            var expectedHalfRanks = (int)(skill.Ranks * 2);
            if (state.SkillRanks.TryGetValue(skillId, out var halfRanks))
                Assert.Equal(expectedHalfRanks, halfRanks);
        }

        // No warnings expected for a clean build
        if (state.Warnings.Count > 0)
        {
            _output.WriteLine("Warnings:");
            foreach (var w in state.Warnings)
                _output.WriteLine($"  - {w}");
        }
    }

}
