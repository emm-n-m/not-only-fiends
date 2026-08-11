using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.PcGen;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests.PcGen;

/// <summary>
/// Asserts what reconstruction is allowed to lose and what it must report losing.
///
/// A .pcg is a record of a character that was actually played, so the converter's contract is
/// "import everything mappable, drop nothing silently". These tests pin the three ways that
/// contract shows up: corpus-wide mapping completeness, the shape of a legitimate drop, and the
/// PCGen features this engine deliberately does not model.
/// </summary>
public class PcgReconstructionFidelityTests
{
    private static readonly Lazy<ContentRegistry> SharedRegistry =
        new(TestContentHelper.LoadBundledAndPrivatePacksIfAvailable);

    private static IEnumerable<string> CorpusFiles() =>
        Directory.GetFiles(TestContentHelper.GetOptionalPcgenCharactersPath()!, "*.pcg")
            .OrderBy(f => f, StringComparer.Ordinal);

    private static (PcgCharacterData Source, PcgConversionResult Result) Convert(string path)
    {
        var source = PcgParser.ParseText(File.ReadAllText(path), Path.GetFileName(path));
        return (source, PcgConverter.Convert(source, new PcgIdMapper(), SharedRegistry.Value));
    }

    // Named single-character tests assert exact values, so they read the frozen fixtures rather
    // than the live PCGen working set — see TestContentHelper.GetOptionalPcgFixturesPath. The
    // corpus sweeps above stay on the live directory: their job is to cover whatever exists now.
    private static (PcgCharacterData Source, PcgConversionResult Result) ConvertNamed(string fileName) =>
        Convert(TestContentHelper.PcgFixture(fileName));

    // ---------------------------------------------------------------
    // Corpus-wide: what must never be dropped
    // ---------------------------------------------------------------

    [RequiresPcgenGoldenDataFact]
    public void Corpus_ParsesAndConvertsEveryCharacter()
    {
        var failures = new List<string>();
        var count = 0;

        foreach (var path in CorpusFiles())
        {
            count++;
            try
            {
                Convert(path);
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(count > 0, "No .pcg files found — the corpus gate is not doing its job.");
        Assert.Empty(failures);
    }

    /// <summary>
    /// Every category of content in the corpus resolves to an engine ID that exists. Spells are
    /// the one exception — the corpus contains homebrew spells with no counterpart — and they get
    /// their own test below.
    /// </summary>
    [RequiresPcgenGoldenDataFact]
    public void Corpus_ResolvesEveryRaceClassFeatSkillTemplateDomainAndItem()
    {
        var dropped = new List<string>();

        foreach (var path in CorpusFiles())
        {
            var name = Path.GetFileName(path);
            var (source, result) = Convert(path);

            if (result.RaceDropped)
                dropped.Add($"{name}: race '{source.Race}'");
            foreach (var c in result.DroppedClasses) dropped.Add($"{name}: class '{c}'");
            foreach (var f in result.DroppedFeats) dropped.Add($"{name}: feat '{f}'");
            foreach (var s in result.DroppedSkills) dropped.Add($"{name}: skill '{s}'");
            foreach (var t in result.DroppedTemplates) dropped.Add($"{name}: template '{t}'");
            foreach (var d in result.DroppedDomains) dropped.Add($"{name}: domain '{d}'");
            foreach (var e in result.DroppedEquipment) dropped.Add($"{name}: equipment '{e}'");
        }

        Assert.Empty(dropped);
    }

    /// <summary>
    /// A dropped spell must be a genuine content gap, not a mapping miss. If the name resolves in
    /// the registry then the drop was the mapper's fault and the character silently lost a spell
    /// it could have kept.
    /// </summary>
    [RequiresPcgenGoldenDataFact]
    public void Corpus_DropsOnlySpellsWithNoCounterpartInContent()
    {
        var registry = SharedRegistry.Value;
        var mapper = new PcgIdMapper();
        var recoverable = new List<string>();
        var droppedCount = 0;

        foreach (var path in CorpusFiles())
        {
            var (_, result) = Convert(path);
            foreach (var spellName in result.DroppedSpells)
            {
                droppedCount++;
                if (mapper.MapSpell(spellName, registry) != null
                    || registry.TryGetSpellByName(spellName, out _))
                {
                    recoverable.Add($"{Path.GetFileName(path)}: '{spellName}'");
                }
            }
        }

        Assert.True(droppedCount > 0,
            "Expected the corpus to still contain homebrew spells — if this is now zero, delete the test.");
        Assert.Empty(recoverable);
    }

    /// <summary>
    /// PCGen's per-level HITPOINTS roll is a character input, not a derived value, so it must
    /// survive import byte for byte on every character — including rolls that fall outside the
    /// driver's die (see the lich case below).
    /// </summary>
    [RequiresPcgenGoldenDataFact]
    public void Corpus_PreservesEveryHitPointRoll()
    {
        var mismatches = new List<string>();

        foreach (var path in CorpusFiles())
        {
            var (source, result) = Convert(path);
            var imported = result.Character.Ticks
                .Select(tick => tick.Choices.HitPointsRolled ?? 0)
                .ToList();
            var expected = source.Levels.Select(level => level.HitPoints).ToList();

            if (!expected.SequenceEqual(imported))
                mismatches.Add($"{Path.GetFileName(path)}: {string.Join(",", expected)} → {string.Join(",", imported)}");
        }

        Assert.Empty(mismatches);
    }

    // ---------------------------------------------------------------
    // Intentionally unsupported PCGen features
    // ---------------------------------------------------------------

    /// <summary>
    /// PCGen tracks active temporary modifiers (a running Fox's Cunning, a situational feat
    /// toggle) in the save file. This engine stores inputs and computes everything else, so a
    /// temporary effect is not a character input: it must be reported and discarded, never folded
    /// into the ability scores or the feat list.
    /// </summary>
    [RequiresPcgFixturesFact]
    public void TemporaryModifiers_AreReportedAndDiscarded()
    {
        var (source, result) = ConvertNamed("Wizard.pcg");

        Assert.NotEmpty(source.TemporaryBonuses);
        Assert.Equal(
            new[] { "FEAT=Familiar ~ Within Reach", "SPELL=Fox's Cunning" },
            result.IgnoredTemporaryBonuses.Order());
        Assert.All(result.IgnoredTemporaryBonuses, label =>
            Assert.Contains(
                $"Active PCGen temporary modifier '{label}' is not a permanent character input — ignored",
                result.Warnings));

        // The discarded modifiers left nothing behind: no feat, no permanent event.
        Assert.DoesNotContain(
            result.Character.Ticks.SelectMany(t => t.Choices.FeatIds ?? new List<string>()),
            featId => featId.Contains("within_reach", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(result.Character.PermanentEvents);
    }

    /// <summary>
    /// PCGen bookkeeping templates ("Base Race Type ~ Humanoid", "Human Base") describe how PCGen
    /// assembled the sheet, not anything about the character. They are filtered at parse time and
    /// must never reach the reconstructed character as content.
    /// </summary>
    [RequiresPcgFixturesFact]
    public void InternalPcgenTemplates_NeverBecomeCharacterContent()
    {
        var (source, result) = ConvertNamed("High Priestess.pcg");

        // This character's source templates are all PCGen internals.
        Assert.NotEmpty(source.Templates);
        Assert.All(source.Templates, t => Assert.True(t.IsInternal, $"Expected '{t.Name}' to be internal"));

        Assert.Empty(result.Character.TemplateIds);
        Assert.Empty(result.DroppedTemplates);
        // Filtering is silent by design: an internal template is not a loss worth reporting.
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Template", StringComparison.Ordinal));
    }

    /// <summary>
    /// A template with acquisition prerequisites was earned mid-career, and the .pcg records
    /// nothing about when: the importer stamps the earliest HD the prerequisites allow. For
    /// the lich that is MinCasterLevel 11, first met when bard 11 completes, so lichdom
    /// starts at HD 12. The Undead and Augmented Humanoid rows PCGen materialized alongside
    /// are consequences the lich template itself now applies, so they come off the character
    /// — left in place they would make an evaluation at HD 8 undead, and she was alive then.
    /// </summary>
    [RequiresPcgFixturesFact]
    public void AcquiredTemplate_LichRecruiter_StampedAtTheEarliestLegalHD()
    {
        var (_, result) = ConvertNamed("Lich Recruiter.pcg");

        Assert.Equal(new[] { "template:lich" }, result.Character.TemplateIds);
        Assert.Equal(12, result.Character.TemplateAcquisitionHD["template:lich"]);
    }

    /// <summary>
    /// A lich's bard levels are stored as d12 rolls, because the template says "increase all
    /// current and future Hit Dice to d12s" — the undead's compensation for having no
    /// Constitution score. Every roll is preserved verbatim, and none of them is out of range,
    /// because the engine raises the dice the same way PCGen did.
    /// </summary>
    [RequiresPcgFixturesFact]
    public void LichHitPointRolls_ArePreservedAndFitTheRaisedDie()
    {
        var (source, result) = ConvertNamed("Lich Recruiter.pcg");
        var state = new ReplayStudio(SharedRegistry.Value).Evaluate(result.Character);

        // The fixture only exercises this if it really does hold rolls a bard's d6 cannot make.
        var oversized = source.Levels
            .Select((level, index) => (level.HitPoints, hd: index + 1))
            .Where(l => l.HitPoints > 6)
            .ToList();
        Assert.NotEmpty(oversized);

        // Preserved, not clamped.
        foreach (var (hitPoints, hd) in oversized)
            Assert.Equal(hitPoints, result.Character.Ticks[hd - 1].Choices.HitPointsRolled);

        Assert.All(state.HitDice, die => Assert.Equal(12, die.DieSize));
        Assert.DoesNotContain(state.Warnings, w => w.Message.Contains("outside d"));
    }

    /// <summary>
    /// PCGen stores companion links as relative file paths. Paths do not survive import, so the
    /// link is rebuilt from the derived character id and the path dependency is reported rather
    /// than followed.
    /// </summary>
    [RequiresPcgFixturesFact]
    public void ExternalCompanionFileReferences_AreWarnedAndLinkedById()
    {
        var (_, result) = ConvertNamed("Wizard.pcg");

        var warning = Assert.Single(
            result.Warnings, w => w.Contains("external relative file reference"));
        Assert.Contains("link preserved by character id", warning);

        // The link survives as an id-based reference, with no path left in it.
        Assert.NotEmpty(result.Character.CompanionLinks);
        Assert.All(result.Character.CompanionLinks, link =>
        {
            Assert.False(string.IsNullOrWhiteSpace(link.CompanionId));
            Assert.DoesNotContain("..", link.CompanionId, StringComparison.Ordinal);
            Assert.DoesNotContain(".pcg", link.CompanionId, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// The converter's own warnings and the replay engine's warnings are different channels:
    /// the first is "the import lost something", the second is "the rules were not satisfied".
    /// A clean import must produce neither, so this pins one character as the reference point.
    /// </summary>
    [RequiresPcgFixturesFact]
    public void CleanImport_ProducesNoWarningsOnEitherChannel()
    {
        var (_, result) = ConvertNamed("Drow Cult Wizard.pcg");
        var state = new ReplayStudio(SharedRegistry.Value).Evaluate(result.Character);

        Assert.False(result.RaceDropped);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.IgnoredTemporaryBonuses);
        Assert.Empty(state.Warnings);
        Assert.Equal("Clean import", result.Summary);
    }
}
