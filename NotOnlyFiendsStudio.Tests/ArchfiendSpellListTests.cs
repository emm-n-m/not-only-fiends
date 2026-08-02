using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// The Archfiend base class (deceit_homebrew) is a Cha-based spontaneous arcane caster that
/// declares no spell list of its own. Three "choose your list" templates —
/// archfiend_arcane/cleric/druid_list — grant the borrowed list via an <c>AddSpellListSource</c>
/// permabuff. These tests pin that mechanism: with the arcane-list template, sorcerer spells such
/// as Haste become legal selections; without it (or with the wrong list) they do not.
/// </summary>
public class ArchfiendSpellListTests
{
    private static readonly SpellSelection Haste =
        new() { ClassId = "class:archfiend", SpellLevel = 3, SpellId = "spell:haste" };

    // 6 Archfiend levels reach caster level 6 — 3rd-level slots unlock at CL 5.
    private static Character Archfiend(string? listTemplate, params SpellSelection[] spellsAtLast)
    {
        var ticks = Enumerable.Range(0, 6)
            .Select(_ => new Tick { DriverId = "class:archfiend" })
            .ToList();
        ticks[^1].Choices.SpellSelections = spellsAtLast.ToList();

        var character = new Character
        {
            Name = "Archfiend probe",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 12, INT = 10, WIS = 10, CHA = 20 },
            Ticks = ticks
        };
        if (listTemplate != null)
            character.TemplateIds.Add(listTemplate);
        return character;
    }

    private static bool HasNotOnListWarning(CharacterState state, string spellId) =>
        state.Warnings.Any(w => w.Message.Contains($"spell '{spellId}'")
                                && w.Message.Contains("is not on the"));

    [RequiresPrivatePacksFact]
    public void ArcaneListTemplate_MakesSorcererSpellsLegal()
    {
        var content = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        var state = new ReplayStudio(content).Evaluate(Archfiend("template:archfiend_arcane_list", Haste));

        Assert.False(HasNotOnListWarning(state, "spell:haste"),
            "Haste (sorcerer 3) should be a legal selection for an Archfiend who took the arcane-list template");
        Assert.Contains(state.Spellcasting["class:archfiend"].SelectedSpells, s => s.SpellId == "spell:haste");
    }

    [RequiresPrivatePacksFact]
    public void WithoutListTemplate_ArchfiendHasNoSpellList()
    {
        var content = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        var state = new ReplayStudio(content).Evaluate(Archfiend(listTemplate: null, Haste));

        Assert.True(HasNotOnListWarning(state, "spell:haste"),
            "With no list template the Archfiend has no spell list, so Haste is not on it — the warning must fire");
    }

    [RequiresPrivatePacksFact]
    public void PickedDomain_AddsSpellsAtDomainLevel_WithNoBonusSlot()
    {
        var content = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();

        var ticks = Enumerable.Range(0, 6)
            .Select(_ => new Tick { DriverId = "class:archfiend" })
            .ToList();
        // The class grants 2 domain selections at level 1 (asSpellListSources).
        ticks[0].Choices.ClassFeatureChoices = new Dictionary<string, List<string>>
        {
            ["domains"] = new() { "domain:fire", "domain:lust" }
        };
        // Resist Energy is Fire-domain level 3 (sorcerer 2). Recorded at the domain level.
        ticks[^1].Choices.SpellSelections = new()
        {
            new() { ClassId = "class:archfiend", SpellLevel = 3, SpellId = "spell:resist_energy" }
        };

        var character = new Character
        {
            Name = "Archfiend domains",
            RaceId = "race:human",
            BaseAbilityScores = new AbilityScoreSet { STR = 10, DEX = 10, CON = 12, INT = 10, WIS = 10, CHA = 20 },
            Ticks = ticks
        };
        character.TemplateIds.Add("template:archfiend_arcane_list");

        var state = new ReplayStudio(content).Evaluate(character);
        var casting = state.Spellcasting["class:archfiend"];

        // Resist Energy validates at its Fire-domain level (3), not just the arcane level (2).
        Assert.False(HasNotOnListWarning(state, "spell:resist_energy"));
        Assert.DoesNotContain(state.Warnings,
            w => w.Message.Contains("spell:resist_energy") && w.Message.Contains("is level"));
        // Dragon-style: no cleric-style prepared domain bonus slot.
        Assert.Empty(casting.DomainBonusSlots);
    }

    [RequiresPrivatePacksFact]
    public void ClericListTemplate_DoesNotGrantArcaneOnlySpells()
    {
        var content = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();
        // Haste is arcane-only (bard/sorcerer/wizard); the cleric list must not make it legal.
        var state = new ReplayStudio(content).Evaluate(Archfiend("template:archfiend_cleric_list", Haste));

        Assert.True(HasNotOnListWarning(state, "spell:haste"),
            "The cleric-list template must not make arcane-only spells legal for an Archfiend");
    }
}
