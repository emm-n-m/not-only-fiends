using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.PcGen;

namespace NotOnlyFiendsStudio.Tests.PcGen;

/// <summary>
/// End of the language chain: parsed names have to survive conversion and evaluation and actually
/// land in <c>CharacterState.Languages</c>, or the import is decorative.
///
/// Before this, the only content anywhere that granted a language was <c>race:hellbred</c>
/// (private pack), so <c>class:dragon_disciple</c> — core SRD — had a correctly implemented,
/// unit-tested <c>HasLanguage{draconic}</c> prerequisite that no character could satisfy by any
/// route. Importing a .pcg is now such a route.
/// </summary>
public class PcgLanguageImportTests
{
    private static string PolyglotPcg(params string[] languages) => string.Join("\n", new[]
    {
        "CHARACTERNAME:Polyglot",
        "RACE:Human",
        "ALIGN:NG",
        "STAT:STR|SCORE:10",
        "STAT:DEX|SCORE:10",
        "STAT:CON|SCORE:10",
        "STAT:INT|SCORE:16",
        "STAT:WIS|SCORE:10",
        "STAT:CHA|SCORE:10",
        string.Join("|", languages.Select(l => "LANGUAGE:" + l)),
        "CLASS:Wizard|LEVEL:1|SKILLPOOL:0",
        "CLASSABILITIESLEVEL:Wizard=1|HITPOINTS:4|SKILLSGAINED:2",
    });

    private static CharacterState ImportAndEvaluate(string pcgContent)
    {
        var registry = TestContentHelper.LoadBundledPacks();
        var data = PcgParser.ParseText(pcgContent, "polyglot.pcg");
        var result = PcgConverter.Convert(data, new PcgIdMapper(), registry);

        return new ReplayStudio(registry).Evaluate(result.Character);
    }

    [Fact]
    public void ImportedLanguages_LandInCharacterState()
    {
        var state = ImportAndEvaluate(PolyglotPcg("Common", "Draconic", "Abyssal", "Infernal"));

        Assert.Equal(
            new[] { "abyssal", "common", "draconic", "infernal" },
            state.Languages.OrderBy(l => l, StringComparer.Ordinal));
    }

    [Fact]
    public void DragonDisciplesLanguagePrerequisite_IsNowSatisfiable()
    {
        // The prerequisite instance is taken from real content rather than constructed, so this
        // fails if the id on either side drifts.
        var registry = TestContentHelper.LoadBundledPacks();
        var dragonDisciple = (HDDriver)registry.GetDriver("class:dragon_disciple");
        var languageRequirement = dragonDisciple.Prerequisites.OfType<HasLanguage>().Single();

        var withDraconic = ImportAndEvaluate(PolyglotPcg("Common", "Draconic"));
        Assert.True(languageRequirement.IsMet(withDraconic));

        var withoutDraconic = ImportAndEvaluate(PolyglotPcg("Common", "Elven"));
        Assert.False(languageRequirement.IsMet(withoutDraconic));
    }

    [Fact]
    public void ImportedLanguages_ReachTheCharacterSheet()
    {
        // CharacterSheet is the saved snapshot and the REST API payload, so this is the surface
        // an external tool or the UI reads.
        var sheet = CharacterSheet.FromState(ImportAndEvaluate(PolyglotPcg("Common", "Draconic")));

        Assert.Contains("draconic", sheet.Languages);
        Assert.Contains("common", sheet.Languages);
    }

    [Fact]
    public void ImportingLanguages_ProducesNoWarningsAndNoStrayTicks()
    {
        // The language grants ride on a permanent event, not a tick, so they must not disturb HD,
        // class levels or the warning list.
        var registry = TestContentHelper.LoadBundledPacks();
        var data = PcgParser.ParseText(PolyglotPcg("Common", "Draconic"), "polyglot.pcg");
        var result = PcgConverter.Convert(data, new PcgIdMapper(), registry);

        Assert.Empty(result.Warnings);
        Assert.Single(result.Character.Ticks);

        var state = new ReplayStudio(registry).Evaluate(result.Character);
        Assert.Equal(1, state.TotalHD);
        Assert.Empty(state.Warnings);
    }

    [Fact]
    public void RaceGrantedAndImportedLanguages_Merge()
    {
        // Languages is a HashSet, so a race grant and an import naming the same language are
        // idempotent rather than duplicated. Uses a synthetic race so the test does not depend on
        // the private packs, where the only language-granting race lives.
        var registry = TestContentHelper.LoadBundledPacks();
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:test_speaker",
            Name = "Test Speaker",
            Type = CreatureType.Humanoid,
            Size = Size.Medium,
            LevelAdjustment = 0,
            RacialPermabuffs = new List<Permabuff>
            {
                new GrantLanguage { LanguageId = "draconic" },
                new GrantLanguage { LanguageId = "sylvan" },
            },
        });

        var data = PcgParser.ParseText(PolyglotPcg("Common", "Draconic"), "polyglot.pcg");
        var character = PcgConverter.Convert(data, new PcgIdMapper(), registry).Character;
        character.RaceId = "race:test_speaker";

        var state = new ReplayStudio(registry).Evaluate(character);

        Assert.Equal(
            new[] { "common", "draconic", "sylvan" },
            state.Languages.OrderBy(l => l, StringComparer.Ordinal));
    }
}
