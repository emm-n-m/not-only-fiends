using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.PcGen;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests.PcGen;

public class PcgEpicSpellImportTests
{
    [Fact]
    public void Mapper_RecognizesEpicPseudoClassesAndReorderedMassFrogName()
    {
        var mapper = new PcgIdMapper();
        var registry = TestContentHelper.LoadBundledPacks();

        Assert.Equal(EpicSpellcasting.CharismaListId, mapper.MapClass("Epic Spells (CHA)"));
        Assert.Equal(EpicSpellcasting.IntelligenceListId, mapper.MapClass("Epic Spells (INT)"));
        Assert.Equal(EpicSpellcasting.WisdomListId, mapper.MapClass("Epic Spells (WIS)"));
        Assert.Equal("spell:frog_mass", mapper.MapSpell("Frog (Mass)", registry));
    }

    [RequiresPcgenCharactersFact]
    public void DuchessRoseElite_ImportsDevelopedEpicSpellsAndThreeOpenSlots()
    {
        var sourceRoot = TestContentHelper.GetOptionalPcgenCharactersPath()!;
        var path = Directory.GetFiles(
            sourceRoot, "Duchess Rose, Elite Succubus.pcg", SearchOption.AllDirectories).Single();
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        var data = PcgParser.ParseText(File.ReadAllText(path), Path.GetFileName(path));

        var result = PcgConverter.Convert(data, new PcgIdMapper(), registry);
        var selections = result.Character.Ticks
            .SelectMany(tick => tick.Choices.SpellSelections ?? Enumerable.Empty<SpellSelection>())
            .Where(selection => selection.ClassId == EpicSpellcasting.CharismaListId)
            .ToList();

        Assert.Equal(
            new[] { "spell:frog_mass", "spell:mind_rape" },
            selections.Select(selection => selection.SpellId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(selections, selection => Assert.Equal(10, selection.SpellLevel));
        Assert.DoesNotContain("Frog (Mass)", result.DroppedSpells);
        Assert.DoesNotContain("Mind Rape", result.DroppedSpells);
        Assert.DoesNotContain(result.Warnings, warning =>
            warning.Contains("Epic Spells (CHA)", StringComparison.Ordinal)
            && warning.Contains("skipped", StringComparison.OrdinalIgnoreCase));

        var state = new ReplayStudio(registry).Evaluate(result.Character);
        var epic = state.Spellcasting[EpicSpellcasting.CharismaListId];

        Assert.Equal(SpellAcquisition.Developed, epic.Acquisition);
        Assert.Equal(Ability.CHA, epic.CastingStat);
        Assert.Equal(23, epic.CasterLevel);
        Assert.Equal(10, epic.MaxSpellLevel);
        Assert.Equal(3, epic.SpellsPerDay[10]);
        Assert.Equal(32, state.SkillHalfRanks["skill:spellcraft"] / 2);
        Assert.Equal(
            new[] { "spell:frog_mass", "spell:mind_rape" },
            epic.SelectedSpells.Select(selection => selection.SpellId)
                .OrderBy(id => id, StringComparer.Ordinal));
        Assert.DoesNotContain(state.Warnings, warning =>
            warning.Message.Contains("unknown spellcasting class", StringComparison.Ordinal)
            || warning.Message.Contains("not on the", StringComparison.Ordinal)
            || warning.Message.Contains(
                "prerequisite not met for feat Ignore Material Components",
                StringComparison.Ordinal)
            || warning.Message.Contains(
                "prerequisite not met for Dark Temptress: Feat: feat:spell_focus_enchantment",
                StringComparison.Ordinal));
    }
}
