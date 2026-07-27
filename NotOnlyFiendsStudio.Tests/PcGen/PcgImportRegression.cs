using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.PcGen;
using NotOnlyFiendsStudio.Studio;
using Xunit.Abstractions;

namespace NotOnlyFiendsStudio.Tests.PcGen;

/// <summary>
/// Regression harness: runs the PcgConverter (same code path as the /api/characters/import-pcg
/// endpoint) over every .pcg file in PCGEN_CHARACTERS_PATH.
///
/// Two modes, toggled by the UPDATE_PCG_BASELINE env var:
///   - VERIFY (default): compare the fresh run to the committed baseline
///     (<c>pcg_import_report.json</c>). If they differ, write
///     <c>pcg_import_report.diff.md</c> with a human-readable delta and fail the test.
///     Also writes <c>pcg_import_report.latest.{json,md}</c> so the most recent run
///     is always inspectable alongside the untouched baseline.
///   - UPDATE (UPDATE_PCG_BASELINE=1): overwrite the baseline, delete any stale diff.
///     Use this to acknowledge that changes in the fresh run are intentional.
///
/// First run (no baseline yet) initializes the baseline and passes.
/// </summary>
public class PcgImportRegression
{
    private readonly ITestOutputHelper _output;

    public PcgImportRegression(ITestOutputHelper output)
    {
        _output = output;
    }

    [RequiresPcgenCharactersFact]
    public void ImportAllCharacters_WriteReport()
    {
        var dir = TestContentHelper.GetOptionalPcgenCharactersPath()!;
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        var engine = new ReplayStudio(registry);
        var mapper = new PcgIdMapper();

        var perCharacter = new List<CharacterReport>();
        var convertedCharacters = new List<(string Stem, Character Character)>();
        var parseFailures = new List<ParseFailure>();

        foreach (var path in Directory.GetFiles(dir, "*.pcg").OrderBy(f => f))
        {
            var fileName = Path.GetFileName(path);
            try
            {
                var data = PcgParser.ParseText(File.ReadAllText(path), fileName);
                var result = PcgConverter.Convert(data, mapper, registry);
                // Populate the "save-time snapshot" sheet (convention from Character.Sheet docs)
                // so files written to CHARACTERS_PATH match what the UI would produce. Also
                // captures the computed values below — content drift (e.g. a save-progression
                // fix silently changing every character that uses that class) otherwise passes
                // through this harness unnoticed, since only import-mapping fidelity was compared.
                CharacterState? state = null;
                try
                {
                    state = engine.Evaluate(result.Character);
                    result.Character.Sheet = CharacterSheet.FromState(state);
                }
                catch
                {
                    // If evaluation fails (broken ticks etc.) leave Sheet null — don't block persistence.
                }
                convertedCharacters.Add((Path.GetFileNameWithoutExtension(path), result.Character));
                perCharacter.Add(new CharacterReport
                {
                    File = fileName,
                    Name = result.Character.Name,
                    Hd = result.Character.Ticks.Count,
                    SourceRace = data.Race,
                    RaceId = result.Character.RaceId,
                    RaceDropped = result.RaceDropped,
                    Summary = result.Summary,
                    Warnings = result.Warnings,
                    DroppedClasses = result.DroppedClasses,
                    DroppedFeats = result.DroppedFeats,
                    DroppedSkills = result.DroppedSkills,
                    DroppedTemplates = result.DroppedTemplates,
                    DroppedDomains = result.DroppedDomains,
                    DroppedEquipment = result.DroppedEquipment,
                    Hp = state?.HP ?? 0,
                    Bab = state?.EffectiveBAB ?? 0,
                    Saves = state?.EffectiveSaves,
                    SkillRanks = state == null ? new() : new Dictionary<string, int>(state.SkillHalfRanks),
                    Feats = state == null ? new() : new List<string>(state.Feats),
                    ClassLevels = state == null ? new() : new Dictionary<string, int>(state.ClassLevels),
                    CasterLevels = state == null
                        ? new()
                        : state.Spellcasting.ToDictionary(kv => kv.Key, kv => kv.Value.CasterLevel),
                });
            }
            catch (Exception ex)
            {
                parseFailures.Add(new ParseFailure
                {
                    File = fileName,
                    ExceptionType = ex.GetType().Name,
                    Message = ex.Message,
                    StackTrace = ex.StackTrace
                });
            }
        }

        var aggregate = BuildAggregate(perCharacter, parseFailures);

        var reportDir = ResolveReportDirectory();
        Directory.CreateDirectory(reportDir);
        var overwrite = string.Equals(
            Environment.GetEnvironmentVariable("PCG_OVERWRITE_CHARACTERS"), "1", StringComparison.Ordinal);
        PersistConvertedCharacters(reportDir, convertedCharacters, overwrite, _output);
        var baselineJson = Path.Combine(reportDir, "pcg_import_report.json");
        var baselineMd = Path.Combine(reportDir, "pcg_import_report.md");
        var latestJson = Path.Combine(reportDir, "pcg_import_report.latest.json");
        var latestMd = Path.Combine(reportDir, "pcg_import_report.latest.md");
        var diffMd = Path.Combine(reportDir, "pcg_import_report.diff.md");

        var fresh = new Report
        {
            GeneratedUtc = DateTime.UtcNow,
            SourceDirectory = dir,
            Aggregate = aggregate,
            Characters = perCharacter,
            ParseFailures = parseFailures,
        };
        var freshJson = JsonSerializer.Serialize(fresh, JsonOpts);
        var freshMd = RenderMarkdown(fresh);

        File.WriteAllText(latestJson, freshJson, Encoding.UTF8);
        File.WriteAllText(latestMd, freshMd, Encoding.UTF8);

        var updateMode = string.Equals(
            Environment.GetEnvironmentVariable("UPDATE_PCG_BASELINE"), "1", StringComparison.Ordinal);
        var hasBaseline = File.Exists(baselineJson);

        _output.WriteLine("=== PCG Import Regression ===");
        _output.WriteLine($"Source: {dir}");
        _output.WriteLine($"Files: {perCharacter.Count + parseFailures.Count} " +
            $"(clean {aggregate.CleanImports}, warn {aggregate.WithWarnings}, parse-fail {parseFailures.Count})");
        _output.WriteLine($"Report dir: {reportDir}");

        if (updateMode || !hasBaseline)
        {
            File.WriteAllText(baselineJson, freshJson, Encoding.UTF8);
            File.WriteAllText(baselineMd, freshMd, Encoding.UTF8);
            if (File.Exists(diffMd)) File.Delete(diffMd);
            _output.WriteLine(updateMode
                ? "Mode: UPDATE — baseline overwritten."
                : "Mode: INIT — no baseline existed, initialized from this run.");
            return;
        }

        var baseline = JsonSerializer.Deserialize<Report>(File.ReadAllText(baselineJson), JsonOpts)
                       ?? throw new InvalidOperationException("Baseline deserialized to null");
        var delta = ComputeDelta(baseline, fresh);

        if (delta.IsEmpty)
        {
            if (File.Exists(diffMd)) File.Delete(diffMd);
            _output.WriteLine("Mode: VERIFY — no changes vs baseline.");
            return;
        }

        File.WriteAllText(diffMd, RenderDeltaMarkdown(delta, baseline, fresh), Encoding.UTF8);
        _output.WriteLine("Mode: VERIFY — changes detected.");
        _output.WriteLine($"Diff: {diffMd}");
        Assert.Fail(
            $"PCG import report differs from baseline.\n" +
            $"  Diff:     {diffMd}\n" +
            $"  Latest:   {latestJson}\n" +
            $"  Baseline: {baselineJson}\n" +
            $"If the changes are intentional, re-run with UPDATE_PCG_BASELINE=1 to update the baseline.");
    }

    private static AggregateReport BuildAggregate(List<CharacterReport> chars, List<ParseFailure> failures)
    {
        return new AggregateReport
        {
            TotalFiles = chars.Count + failures.Count,
            Imported = chars.Count,
            ParseFailures = failures.Count,
            CleanImports = chars.Count(c => c.Warnings.Count == 0 && !c.RaceDropped),
            WithWarnings = chars.Count(c => c.Warnings.Count > 0 || c.RaceDropped),
            RaceDroppedCount = chars.Count(c => c.RaceDropped),
            UnmappedRaces = Tally(chars.Where(c => c.RaceDropped).Select(c => c.SourceRace ?? "(blank)")),
            TopUnmappedClasses = Tally(chars.SelectMany(c => c.DroppedClasses)),
            TopUnmappedFeats = Tally(chars.SelectMany(c => c.DroppedFeats)),
            TopUnmappedSkills = Tally(chars.SelectMany(c => c.DroppedSkills)),
            TopUnmappedTemplates = Tally(chars.SelectMany(c => c.DroppedTemplates)),
            TopUnmappedDomains = Tally(chars.SelectMany(c => c.DroppedDomains)),
            TopUnmappedEquipment = Tally(chars.SelectMany(c => c.DroppedEquipment)),
        };
    }

    /// <summary>
    /// Saves each converted Character as a top-level JSON file in CHARACTERS_PATH (so the Feed
    /// app picks them up directly — CharacterStore treats filename-without-extension as the id).
    /// First run: writes any file that doesn't exist yet. Re-runs: skips existing files so
    /// in-UI edits aren't clobbered, unless PCG_OVERWRITE_CHARACTERS=1.
    /// Falls back to "{reportDir}/converted/" if CHARACTERS_PATH isn't configured.
    /// </summary>
    private static void PersistConvertedCharacters(
        string reportDir,
        List<(string Stem, Character Character)> characters,
        bool overwriteExisting,
        ITestOutputHelper log)
    {
        var charactersPath = TestContentHelper.GetOptionalCharactersPath();
        string destination;
        bool isFallback;
        if (!string.IsNullOrEmpty(charactersPath) && Directory.Exists(charactersPath))
        {
            destination = charactersPath;
            isFallback = false;
        }
        else
        {
            destination = Path.Combine(reportDir, "converted");
            Directory.CreateDirectory(destination);
            isFallback = true;
        }

        var invalid = Path.GetInvalidFileNameChars();
        int written = 0, skipped = 0, overwritten = 0;
        var preservedExamples = new List<string>();

        foreach (var (stem, character) in characters)
        {
            var safe = new string(stem.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            var path = Path.Combine(destination, safe + ".json");
            var exists = File.Exists(path);
            if (exists && !overwriteExisting)
            {
                skipped++;
                if (preservedExamples.Count < 5) preservedExamples.Add(safe + ".json");
                continue;
            }
            File.WriteAllText(path, JsonSerializer.Serialize(character, JsonOpts), Encoding.UTF8);
            if (exists) overwritten++; else written++;
        }

        var label = isFallback ? $"{destination} (CHARACTERS_PATH not set)" : destination;
        log.WriteLine($"Converted characters → {label}");
        log.WriteLine($"  new: {written}, overwritten: {overwritten}, preserved: {skipped}");
        if (skipped > 0 && !overwriteExisting)
        {
            var sample = string.Join(", ", preservedExamples);
            var more = skipped > preservedExamples.Count ? $", +{skipped - preservedExamples.Count} more" : "";
            log.WriteLine($"  (existing files kept: {sample}{more})");
            log.WriteLine($"  set PCG_OVERWRITE_CHARACTERS=1 to force re-conversion.");
        }
    }

    /// <summary>
    /// Prefers the private packs repo (EXTRA_PACKS_PATH) so the main OGL repo stays clean
    /// and the report can be diffed / used as a golden baseline in the private repo.
    /// Falls back to the main solution root if the private path isn't configured.
    /// </summary>
    private static string ResolveReportDirectory()
    {
        var privatePath = TestContentHelper.GetOptionalPrivatePacksPath();
        if (!string.IsNullOrEmpty(privatePath) && Directory.Exists(privatePath))
            return Path.Combine(privatePath, "test-reports");
        return Path.Combine(TestContentHelper.GetSolutionRoot(), "test-reports");
    }

    private static List<TallyEntry> Tally(IEnumerable<string> values) =>
        values
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TallyEntry { Value = g.Key, Count = g.Count() })
            .OrderByDescending(e => e.Count)
            .ThenBy(e => e.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string RenderMarkdown(Report r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# PCG Import Regression Report");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {r.GeneratedUtc:u}");
        sb.AppendLine($"- Source: `{r.SourceDirectory}`");
        sb.AppendLine($"- Files: {r.Aggregate.TotalFiles}");
        sb.AppendLine($"  - Clean imports: **{r.Aggregate.CleanImports}**");
        sb.AppendLine($"  - With warnings: **{r.Aggregate.WithWarnings}**");
        sb.AppendLine($"  - Race fallback used: **{r.Aggregate.RaceDroppedCount}**");
        sb.AppendLine($"  - Parse failures: **{r.Aggregate.ParseFailures}**");
        sb.AppendLine();

        void WriteTally(string heading, List<TallyEntry> tally)
        {
            sb.AppendLine($"## {heading} ({tally.Count} distinct)");
            sb.AppendLine();
            if (tally.Count == 0)
            {
                sb.AppendLine("_None._");
                sb.AppendLine();
                return;
            }
            sb.AppendLine("| Count | Value |");
            sb.AppendLine("| ----: | :---- |");
            foreach (var e in tally.Take(50))
                sb.AppendLine($"| {e.Count} | `{e.Value}` |");
            if (tally.Count > 50)
                sb.AppendLine($"| … | _({tally.Count - 50} more)_ |");
            sb.AppendLine();
        }

        WriteTally("Unmapped races", r.Aggregate.UnmappedRaces);
        WriteTally("Top unmapped classes", r.Aggregate.TopUnmappedClasses);
        WriteTally("Top unmapped feats", r.Aggregate.TopUnmappedFeats);
        WriteTally("Top unmapped skills", r.Aggregate.TopUnmappedSkills);
        WriteTally("Top unmapped templates", r.Aggregate.TopUnmappedTemplates);
        WriteTally("Top unmapped domains", r.Aggregate.TopUnmappedDomains);
        WriteTally("Top unmapped equipment", r.Aggregate.TopUnmappedEquipment);

        if (r.ParseFailures.Count > 0)
        {
            sb.AppendLine("## Parse failures");
            sb.AppendLine();
            foreach (var f in r.ParseFailures)
            {
                sb.AppendLine($"### `{f.File}`");
                sb.AppendLine($"- `{f.ExceptionType}`: {f.Message}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Per-character detail");
        sb.AppendLine();
        foreach (var c in r.Characters.OrderBy(c => c.File, StringComparer.OrdinalIgnoreCase))
        {
            var badge = c.Warnings.Count == 0 && !c.RaceDropped ? "OK" : "WARN";
            sb.AppendLine($"### [{badge}] `{c.File}` — {c.Name} (HD {c.Hd})");
            sb.AppendLine($"- Race: `{c.RaceId}`{(c.RaceDropped ? $" _(fallback, source: `{c.SourceRace}`)_" : "")}");
            sb.AppendLine($"- Summary: {c.Summary}");
            if (c.DroppedClasses.Count > 0) sb.AppendLine($"- Dropped classes: {string.Join(", ", c.DroppedClasses.Distinct())}");
            if (c.DroppedFeats.Count > 0) sb.AppendLine($"- Dropped feats: {string.Join(", ", c.DroppedFeats.Distinct())}");
            if (c.DroppedSkills.Count > 0) sb.AppendLine($"- Dropped skills: {string.Join(", ", c.DroppedSkills.Distinct())}");
            if (c.DroppedTemplates.Count > 0) sb.AppendLine($"- Dropped templates: {string.Join(", ", c.DroppedTemplates.Distinct())}");
            if (c.DroppedDomains.Count > 0) sb.AppendLine($"- Dropped domains: {string.Join(", ", c.DroppedDomains.Distinct())}");
            if (c.DroppedEquipment.Count > 0) sb.AppendLine($"- Dropped equipment: {string.Join(", ", c.DroppedEquipment.Distinct())}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ---------------------------------------------------------------
    // Delta (baseline vs fresh) — structured + markdown
    // ---------------------------------------------------------------

    private static DeltaReport ComputeDelta(Report baseline, Report fresh)
    {
        var delta = new DeltaReport();

        var baseByFile = baseline.Characters.ToDictionary(c => c.File, StringComparer.OrdinalIgnoreCase);
        var freshByFile = fresh.Characters.ToDictionary(c => c.File, StringComparer.OrdinalIgnoreCase);

        delta.AddedFiles = freshByFile.Keys.Except(baseByFile.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        delta.RemovedFiles = baseByFile.Keys.Except(freshByFile.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var file in baseByFile.Keys.Intersect(freshByFile.Keys, StringComparer.OrdinalIgnoreCase)
                                              .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var b = baseByFile[file];
            var f = freshByFile[file];
            var charDelta = CompareCharacters(b, f);
            if (charDelta != null)
                delta.ChangedCharacters.Add(charDelta);
        }

        delta.TallyChanges["Races"] = DiffTally(baseline.Aggregate.UnmappedRaces, fresh.Aggregate.UnmappedRaces);
        delta.TallyChanges["Classes"] = DiffTally(baseline.Aggregate.TopUnmappedClasses, fresh.Aggregate.TopUnmappedClasses);
        delta.TallyChanges["Feats"] = DiffTally(baseline.Aggregate.TopUnmappedFeats, fresh.Aggregate.TopUnmappedFeats);
        delta.TallyChanges["Skills"] = DiffTally(baseline.Aggregate.TopUnmappedSkills, fresh.Aggregate.TopUnmappedSkills);
        delta.TallyChanges["Templates"] = DiffTally(baseline.Aggregate.TopUnmappedTemplates, fresh.Aggregate.TopUnmappedTemplates);
        delta.TallyChanges["Domains"] = DiffTally(baseline.Aggregate.TopUnmappedDomains, fresh.Aggregate.TopUnmappedDomains);
        delta.TallyChanges["Equipment"] = DiffTally(baseline.Aggregate.TopUnmappedEquipment, fresh.Aggregate.TopUnmappedEquipment);

        var baseFail = baseline.ParseFailures.ToDictionary(f => f.File, StringComparer.OrdinalIgnoreCase);
        var freshFail = fresh.ParseFailures.ToDictionary(f => f.File, StringComparer.OrdinalIgnoreCase);
        foreach (var file in freshFail.Keys.Except(baseFail.Keys, StringComparer.OrdinalIgnoreCase)
                                            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            delta.AddedFailures.Add($"{file} — {freshFail[file].ExceptionType}: {freshFail[file].Message}");
        foreach (var file in baseFail.Keys.Except(freshFail.Keys, StringComparer.OrdinalIgnoreCase)
                                           .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            delta.RemovedFailures.Add($"{file} — {baseFail[file].ExceptionType}: {baseFail[file].Message}");
        foreach (var file in baseFail.Keys.Intersect(freshFail.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var b = baseFail[file];
            var f = freshFail[file];
            if (b.ExceptionType != f.ExceptionType || b.Message != f.Message)
                delta.ChangedFailures.Add($"{file} — `{b.ExceptionType}: {b.Message}` → `{f.ExceptionType}: {f.Message}`");
        }

        return delta;
    }

    private static CharDelta? CompareCharacters(CharacterReport b, CharacterReport f)
    {
        var oldStatus = StatusOf(b);
        var newStatus = StatusOf(f);

        var warnAdded = f.Warnings.Except(b.Warnings).ToList();
        var warnResolved = b.Warnings.Except(f.Warnings).ToList();
        var classesAdded = f.DroppedClasses.Except(b.DroppedClasses, StringComparer.OrdinalIgnoreCase).ToList();
        var classesResolved = b.DroppedClasses.Except(f.DroppedClasses, StringComparer.OrdinalIgnoreCase).ToList();
        var featsAdded = f.DroppedFeats.Except(b.DroppedFeats, StringComparer.OrdinalIgnoreCase).ToList();
        var featsResolved = b.DroppedFeats.Except(f.DroppedFeats, StringComparer.OrdinalIgnoreCase).ToList();
        var skillsAdded = f.DroppedSkills.Except(b.DroppedSkills, StringComparer.OrdinalIgnoreCase).ToList();
        var skillsResolved = b.DroppedSkills.Except(f.DroppedSkills, StringComparer.OrdinalIgnoreCase).ToList();
        var templatesAdded = f.DroppedTemplates.Except(b.DroppedTemplates, StringComparer.OrdinalIgnoreCase).ToList();
        var templatesResolved = b.DroppedTemplates.Except(f.DroppedTemplates, StringComparer.OrdinalIgnoreCase).ToList();
        var domainsAdded = f.DroppedDomains.Except(b.DroppedDomains, StringComparer.OrdinalIgnoreCase).ToList();
        var domainsResolved = b.DroppedDomains.Except(f.DroppedDomains, StringComparer.OrdinalIgnoreCase).ToList();
        var equipmentAdded = f.DroppedEquipment.Except(b.DroppedEquipment, StringComparer.OrdinalIgnoreCase).ToList();
        var equipmentResolved = b.DroppedEquipment.Except(f.DroppedEquipment, StringComparer.OrdinalIgnoreCase).ToList();
        var raceIdChanged = !string.Equals(b.RaceId, f.RaceId, StringComparison.Ordinal) || b.RaceDropped != f.RaceDropped;

        var hpChanged = b.Hp != f.Hp;
        var babChanged = b.Bab != f.Bab;
        var savesChanged = !SavesEqual(b.Saves, f.Saves);
        var skillRanksChanged = DictChanged(b.SkillRanks, f.SkillRanks);
        var classLevelsChanged = DictChanged(b.ClassLevels, f.ClassLevels);
        var casterLevelsChanged = DictChanged(b.CasterLevels, f.CasterLevels);
        var allFeatsAdded = f.Feats.Except(b.Feats, StringComparer.OrdinalIgnoreCase).ToList();
        var allFeatsResolved = b.Feats.Except(f.Feats, StringComparer.OrdinalIgnoreCase).ToList();

        var anyChange = oldStatus != newStatus
            || warnAdded.Count > 0 || warnResolved.Count > 0
            || classesAdded.Count > 0 || classesResolved.Count > 0
            || featsAdded.Count > 0 || featsResolved.Count > 0
            || skillsAdded.Count > 0 || skillsResolved.Count > 0
            || templatesAdded.Count > 0 || templatesResolved.Count > 0
            || domainsAdded.Count > 0 || domainsResolved.Count > 0
            || equipmentAdded.Count > 0 || equipmentResolved.Count > 0
            || raceIdChanged
            || hpChanged || babChanged || savesChanged
            || skillRanksChanged || classLevelsChanged || casterLevelsChanged
            || allFeatsAdded.Count > 0 || allFeatsResolved.Count > 0;

        if (!anyChange) return null;

        return new CharDelta
        {
            File = f.File,
            Name = f.Name,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            OldRaceId = b.RaceId,
            NewRaceId = f.RaceId,
            RaceIdChanged = raceIdChanged,
            WarningsAdded = warnAdded,
            WarningsResolved = warnResolved,
            ClassesAdded = classesAdded,
            ClassesResolved = classesResolved,
            FeatsAdded = featsAdded,
            FeatsResolved = featsResolved,
            SkillsAdded = skillsAdded,
            SkillsResolved = skillsResolved,
            TemplatesAdded = templatesAdded,
            TemplatesResolved = templatesResolved,
            DomainsAdded = domainsAdded,
            DomainsResolved = domainsResolved,
            EquipmentAdded = equipmentAdded,
            EquipmentResolved = equipmentResolved,
            HpChanged = hpChanged,
            OldHp = b.Hp,
            NewHp = f.Hp,
            BabChanged = babChanged,
            OldBab = b.Bab,
            NewBab = f.Bab,
            SavesChanged = savesChanged,
            OldSaves = b.Saves,
            NewSaves = f.Saves,
            SkillRanksChanged = skillRanksChanged,
            OldSkillRanks = b.SkillRanks,
            NewSkillRanks = f.SkillRanks,
            ClassLevelsChanged = classLevelsChanged,
            OldClassLevels = b.ClassLevels,
            NewClassLevels = f.ClassLevels,
            CasterLevelsChanged = casterLevelsChanged,
            OldCasterLevels = b.CasterLevels,
            NewCasterLevels = f.CasterLevels,
            AllFeatsAdded = allFeatsAdded,
            AllFeatsResolved = allFeatsResolved,
        };
    }

    private static bool SavesEqual(SaveSet? a, SaveSet? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.Fort == b.Fort && a.Ref == b.Ref && a.Will == b.Will;
    }

    private static bool DictChanged(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        if (a.Count != b.Count) return true;
        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other) || other != value) return true;
        }
        return false;
    }

    private static string StatusOf(CharacterReport c) =>
        c.Warnings.Count == 0 && !c.RaceDropped ? "OK" : "WARN";

    private static List<TallyDelta> DiffTally(List<TallyEntry> baseline, List<TallyEntry> fresh)
    {
        var baseMap = baseline.ToDictionary(e => e.Value, e => e.Count, StringComparer.OrdinalIgnoreCase);
        var freshMap = fresh.ToDictionary(e => e.Value, e => e.Count, StringComparer.OrdinalIgnoreCase);
        var all = baseMap.Keys.Union(freshMap.Keys, StringComparer.OrdinalIgnoreCase);
        return all
            .Select(v => new TallyDelta
            {
                Value = v,
                OldCount = baseMap.GetValueOrDefault(v),
                NewCount = freshMap.GetValueOrDefault(v),
            })
            .Where(d => d.OldCount != d.NewCount)
            .OrderByDescending(d => Math.Abs(d.NewCount - d.OldCount))
            .ThenBy(d => d.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string RenderDeltaMarkdown(DeltaReport delta, Report baseline, Report fresh)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# PCG Import Regression — Baseline Diff");
        sb.AppendLine();
        sb.AppendLine($"- Baseline generated: {baseline.GeneratedUtc:u}");
        sb.AppendLine($"- Latest generated:   {fresh.GeneratedUtc:u}");
        sb.AppendLine();

        var regressions = delta.ChangedCharacters.Where(c => c.OldStatus == "OK" && c.NewStatus == "WARN").ToList();
        var improvements = delta.ChangedCharacters.Where(c => c.OldStatus == "WARN" && c.NewStatus == "OK").ToList();
        var othersChanged = delta.ChangedCharacters.Except(regressions).Except(improvements).ToList();

        sb.AppendLine("## Headline");
        sb.AppendLine();
        sb.AppendLine($"- Regressions (OK → WARN): **{regressions.Count}**");
        sb.AppendLine($"- Improvements (WARN → OK): **{improvements.Count}**");
        sb.AppendLine($"- Other changes (same status, different drops): **{othersChanged.Count}**");
        sb.AppendLine($"- Files added: **{delta.AddedFiles.Count}**, removed: **{delta.RemovedFiles.Count}**");
        var totalTallyChanges = delta.TallyChanges.Values.Sum(v => v.Count);
        sb.AppendLine($"- Aggregate tally changes: **{totalTallyChanges}**");
        sb.AppendLine($"- Parse failures: added **{delta.AddedFailures.Count}**, removed **{delta.RemovedFailures.Count}**, changed **{delta.ChangedFailures.Count}**");
        sb.AppendLine();

        void WriteCharSection(string heading, List<CharDelta> items)
        {
            if (items.Count == 0) return;
            sb.AppendLine($"## {heading}");
            sb.AppendLine();
            foreach (var c in items.OrderBy(c => c.File, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"### `{c.File}` — {c.Name}  _({c.OldStatus} → {c.NewStatus})_");
                if (c.RaceIdChanged)
                    sb.AppendLine($"- Race: `{c.OldRaceId}` → `{c.NewRaceId}`");
                if (c.HpChanged) WriteScalarChange(sb, "HP", c.OldHp, c.NewHp);
                if (c.BabChanged) WriteScalarChange(sb, "BAB", c.OldBab, c.NewBab);
                if (c.SavesChanged)
                    WriteScalarChange(sb, "Saves",
                        c.OldSaves is { } os ? $"F{os.Fort}/R{os.Ref}/W{os.Will}" : "(none)",
                        c.NewSaves is { } ns ? $"F{ns.Fort}/R{ns.Ref}/W{ns.Will}" : "(none)");
                if (c.SkillRanksChanged) WriteDictChange(sb, "Skill ranks", c.OldSkillRanks, c.NewSkillRanks);
                if (c.ClassLevelsChanged) WriteDictChange(sb, "Class levels", c.OldClassLevels, c.NewClassLevels);
                if (c.CasterLevelsChanged) WriteDictChange(sb, "Caster levels", c.OldCasterLevels, c.NewCasterLevels);
                WriteListChange(sb, "Feats added", c.AllFeatsAdded);
                WriteListChange(sb, "Feats resolved", c.AllFeatsResolved);
                WriteListChange(sb, "Warnings added", c.WarningsAdded);
                WriteListChange(sb, "Warnings resolved", c.WarningsResolved);
                WriteListChange(sb, "Dropped classes added", c.ClassesAdded);
                WriteListChange(sb, "Dropped classes resolved", c.ClassesResolved);
                WriteListChange(sb, "Dropped feats added", c.FeatsAdded);
                WriteListChange(sb, "Dropped feats resolved", c.FeatsResolved);
                WriteListChange(sb, "Dropped skills added", c.SkillsAdded);
                WriteListChange(sb, "Dropped skills resolved", c.SkillsResolved);
                WriteListChange(sb, "Dropped templates added", c.TemplatesAdded);
                WriteListChange(sb, "Dropped templates resolved", c.TemplatesResolved);
                WriteListChange(sb, "Dropped domains added", c.DomainsAdded);
                WriteListChange(sb, "Dropped domains resolved", c.DomainsResolved);
                WriteListChange(sb, "Dropped equipment added", c.EquipmentAdded);
                WriteListChange(sb, "Dropped equipment resolved", c.EquipmentResolved);
                sb.AppendLine();
            }
        }

        WriteCharSection("Regressions (OK → WARN)", regressions);
        WriteCharSection("Improvements (WARN → OK)", improvements);
        WriteCharSection("Other per-character changes", othersChanged);

        if (delta.AddedFiles.Count > 0 || delta.RemovedFiles.Count > 0)
        {
            sb.AppendLine("## Corpus changes");
            sb.AppendLine();
            if (delta.AddedFiles.Count > 0)
            {
                sb.AppendLine("**Added files:**");
                foreach (var f in delta.AddedFiles) sb.AppendLine($"- `{f}`");
                sb.AppendLine();
            }
            if (delta.RemovedFiles.Count > 0)
            {
                sb.AppendLine("**Removed files:**");
                foreach (var f in delta.RemovedFiles) sb.AppendLine($"- `{f}`");
                sb.AppendLine();
            }
        }

        if (totalTallyChanges > 0)
        {
            sb.AppendLine("## Aggregate tally changes");
            sb.AppendLine();
            foreach (var (category, entries) in delta.TallyChanges)
            {
                if (entries.Count == 0) continue;
                sb.AppendLine($"### {category}");
                sb.AppendLine();
                sb.AppendLine("| Old | New | Δ | Value |");
                sb.AppendLine("| --: | --: | --: | :---- |");
                foreach (var e in entries)
                    sb.AppendLine($"| {e.OldCount} | {e.NewCount} | {(e.NewCount - e.OldCount):+#;-#;0} | `{e.Value}` |");
                sb.AppendLine();
            }
        }

        if (delta.AddedFailures.Count + delta.RemovedFailures.Count + delta.ChangedFailures.Count > 0)
        {
            sb.AppendLine("## Parse failures");
            sb.AppendLine();
            if (delta.AddedFailures.Count > 0)
            {
                sb.AppendLine("**New failures:**");
                foreach (var f in delta.AddedFailures) sb.AppendLine($"- {f}");
                sb.AppendLine();
            }
            if (delta.RemovedFailures.Count > 0)
            {
                sb.AppendLine("**Resolved failures:**");
                foreach (var f in delta.RemovedFailures) sb.AppendLine($"- {f}");
                sb.AppendLine();
            }
            if (delta.ChangedFailures.Count > 0)
            {
                sb.AppendLine("**Changed failures:**");
                foreach (var f in delta.ChangedFailures) sb.AppendLine($"- {f}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static void WriteListChange(StringBuilder sb, string label, List<string> items)
    {
        if (items.Count == 0) return;
        sb.AppendLine($"- **{label}:** {string.Join(", ", items.Select(i => $"`{i}`"))}");
    }

    private static void WriteScalarChange(StringBuilder sb, string label, object? oldValue, object? newValue)
    {
        sb.AppendLine($"- **{label}:** `{oldValue}` → `{newValue}`");
    }

    private static void WriteDictChange(StringBuilder sb, string label, Dictionary<string, int> oldDict, Dictionary<string, int> newDict)
    {
        var keys = oldDict.Keys.Union(newDict.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();
        foreach (var key in keys)
        {
            var hasOld = oldDict.TryGetValue(key, out var oldVal);
            var hasNew = newDict.TryGetValue(key, out var newVal);
            if (hasOld && hasNew && oldVal == newVal) continue;
            parts.Add(!hasOld ? $"{key}: added ({newVal})"
                : !hasNew ? $"{key}: removed (was {oldVal})"
                : $"{key}: {oldVal} → {newVal}");
        }
        if (parts.Count == 0) return;
        sb.AppendLine($"- **{label}:** {string.Join(", ", parts.Select(p => $"`{p}`"))}");
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed class Report
    {
        public DateTime GeneratedUtc { get; set; }
        public string SourceDirectory { get; set; } = string.Empty;
        public AggregateReport Aggregate { get; set; } = new();
        public List<CharacterReport> Characters { get; set; } = new();
        public List<ParseFailure> ParseFailures { get; set; } = new();
    }

    private sealed class AggregateReport
    {
        public int TotalFiles { get; set; }
        public int Imported { get; set; }
        public int ParseFailures { get; set; }
        public int CleanImports { get; set; }
        public int WithWarnings { get; set; }
        public int RaceDroppedCount { get; set; }
        public List<TallyEntry> UnmappedRaces { get; set; } = new();
        public List<TallyEntry> TopUnmappedClasses { get; set; } = new();
        public List<TallyEntry> TopUnmappedFeats { get; set; } = new();
        public List<TallyEntry> TopUnmappedSkills { get; set; } = new();
        public List<TallyEntry> TopUnmappedTemplates { get; set; } = new();
        public List<TallyEntry> TopUnmappedDomains { get; set; } = new();
        public List<TallyEntry> TopUnmappedEquipment { get; set; } = new();
    }

    private sealed class TallyEntry
    {
        public string Value { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    private sealed class CharacterReport
    {
        public string File { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Hd { get; set; }
        public string? SourceRace { get; set; }
        public string? RaceId { get; set; }
        public bool RaceDropped { get; set; }
        public string Summary { get; set; } = string.Empty;
        public List<string> Warnings { get; set; } = new();
        public List<string> DroppedClasses { get; set; } = new();
        public List<string> DroppedFeats { get; set; } = new();
        public List<string> DroppedSkills { get; set; } = new();
        public List<string> DroppedTemplates { get; set; } = new();
        public List<string> DroppedDomains { get; set; } = new();
        public List<string> DroppedEquipment { get; set; } = new();

        // Computed-sheet fields (item 1 of TODO.md §4): catches content drift — a rules fix
        // that silently changes a saved character's stats — as a reviewable diff instead of
        // passing through unnoticed. Null/default when evaluation itself failed.
        public int Hp { get; set; }
        public int Bab { get; set; }
        public SaveSet? Saves { get; set; }
        public Dictionary<string, int> SkillRanks { get; set; } = new();
        public List<string> Feats { get; set; } = new();
        public Dictionary<string, int> ClassLevels { get; set; } = new();
        public Dictionary<string, int> CasterLevels { get; set; } = new();
    }

    private sealed class ParseFailure
    {
        public string File { get; set; } = string.Empty;
        public string ExceptionType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? StackTrace { get; set; }
    }

    private sealed class DeltaReport
    {
        public List<string> AddedFiles { get; set; } = new();
        public List<string> RemovedFiles { get; set; } = new();
        public List<CharDelta> ChangedCharacters { get; set; } = new();
        public Dictionary<string, List<TallyDelta>> TallyChanges { get; set; } = new();
        public List<string> AddedFailures { get; set; } = new();
        public List<string> RemovedFailures { get; set; } = new();
        public List<string> ChangedFailures { get; set; } = new();

        public bool IsEmpty =>
            AddedFiles.Count == 0
            && RemovedFiles.Count == 0
            && ChangedCharacters.Count == 0
            && TallyChanges.Values.All(v => v.Count == 0)
            && AddedFailures.Count == 0
            && RemovedFailures.Count == 0
            && ChangedFailures.Count == 0;
    }

    private sealed class CharDelta
    {
        public string File { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string OldStatus { get; set; } = string.Empty;
        public string NewStatus { get; set; } = string.Empty;
        public string? OldRaceId { get; set; }
        public string? NewRaceId { get; set; }
        public bool RaceIdChanged { get; set; }
        public List<string> WarningsAdded { get; set; } = new();
        public List<string> WarningsResolved { get; set; } = new();
        public List<string> ClassesAdded { get; set; } = new();
        public List<string> ClassesResolved { get; set; } = new();
        public List<string> FeatsAdded { get; set; } = new();
        public List<string> FeatsResolved { get; set; } = new();
        public List<string> SkillsAdded { get; set; } = new();
        public List<string> SkillsResolved { get; set; } = new();
        public List<string> TemplatesAdded { get; set; } = new();
        public List<string> TemplatesResolved { get; set; } = new();
        public List<string> DomainsAdded { get; set; } = new();
        public List<string> DomainsResolved { get; set; } = new();
        public List<string> EquipmentAdded { get; set; } = new();
        public List<string> EquipmentResolved { get; set; } = new();

        public bool HpChanged { get; set; }
        public int OldHp { get; set; }
        public int NewHp { get; set; }
        public bool BabChanged { get; set; }
        public int OldBab { get; set; }
        public int NewBab { get; set; }
        public bool SavesChanged { get; set; }
        public SaveSet? OldSaves { get; set; }
        public SaveSet? NewSaves { get; set; }
        public bool SkillRanksChanged { get; set; }
        public Dictionary<string, int> OldSkillRanks { get; set; } = new();
        public Dictionary<string, int> NewSkillRanks { get; set; } = new();
        public bool ClassLevelsChanged { get; set; }
        public Dictionary<string, int> OldClassLevels { get; set; } = new();
        public Dictionary<string, int> NewClassLevels { get; set; } = new();
        public bool CasterLevelsChanged { get; set; }
        public Dictionary<string, int> OldCasterLevels { get; set; } = new();
        public Dictionary<string, int> NewCasterLevels { get; set; } = new();
        public List<string> AllFeatsAdded { get; set; } = new();
        public List<string> AllFeatsResolved { get; set; } = new();
    }

    private sealed class TallyDelta
    {
        public string Value { get; set; } = string.Empty;
        public int OldCount { get; set; }
        public int NewCount { get; set; }
    }
}
