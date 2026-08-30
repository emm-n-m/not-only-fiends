using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// Exact values are derived from the bundled SRD mirror's divine.html rank tables and
/// salientAbilities.html slot rule, not copied from engine output.
/// </summary>
public class DivineCreationTests
{
    [Fact]
    public void QuasiDeity_GetsUniversalChassisButNoRankOnePowers()
    {
        var character = HumanFighter();
        character.Ticks[0].Choices.HitPointsRolled = 1;
        character.Divinity = new DivinityChoices { DivineRank = 0 };

        var state = Evaluate(character);

        Assert.Equal(DivineStatus.QuasiDeity, state.Divinity!.Status);
        Assert.Equal(10, state.HP); // d10 is maximized despite the saved roll of 1.
        Assert.Equal(60, state.Speeds[MovementMode.Land]);
        Assert.Contains("mind-affecting", state.Immunities);
        Assert.DoesNotContain("electricity", state.Immunities);
        Assert.Contains(state.DamageReduction, dr => dr.Value == 10 && dr.BypassedBy == "epic");
        Assert.Equal(32, state.SpellResistance);
        Assert.Equal(0, state.Divinity.SalientDivineAbilitySlots);
        Assert.False(state.Divinity.GrantsSpells);
        Assert.DoesNotContain(state.SLAs, sla => sla.Id.StartsWith("divine_travel_", StringComparison.Ordinal));
    }

    [Fact]
    public void LesserDeity_AppliesRankTablesDomainsAndAtWillSpellLikeAbilities()
    {
        var character = HumanFighter();
        character.Alignment = Alignment.LG;
        character.BaseAbilityScores.CHA = 18;
        character.Ticks[0].Choices.SkillAllocations = new()
        {
            new SkillAllocation { SkillId = "skill:climb", HalfRanks = 2 },
        };
        character.Divinity = new DivinityChoices
        {
            DivineRank = 6,
            Form = DivineForm.Quadruped,
            Portfolio = new() { "fire" },
            DomainIds = new() { "domain:fire", "domain:magic" },
            SalientDivineAbilityIds = new() { "salient:alter_size" },
        };

        var state = Evaluate(character);
        var divine = state.Divinity!;

        Assert.Equal(DivineStatus.LesserDeity, divine.Status);
        Assert.Equal(8, divine.SalientDivineAbilitySlots); // rank + lesser-deity bonus 2.
        Assert.Equal(7, divine.PendingSalientDivineAbilitySlots);
        Assert.Equal(100, state.Speeds[MovementMode.Land]);
        Assert.Equal(26, state.AC.Total); // 10 + rank divine + rank natural + Cha deflection.
        Assert.Equal(20, state.AC.Touch); // natural armor is excluded from touch AC.
        Assert.Equal(8, state.EffectiveSaves.Fort); // fighter base +2, Con +0, divine +6.
        Assert.Equal(7, state.SkillTotals["skill:climb"]); // 1 rank + divine rank 6.
        Assert.Contains(state.DamageReduction, dr => dr.Value == 20 && dr.BypassedBy == "epic");
        Assert.Equal(11, state.Resistances["fire"]);
        Assert.Equal(38, state.SpellResistance);
        Assert.Contains("electricity", state.Immunities);
        Assert.Contains("imprisonment and banishment effects", state.Immunities);

        var burningHands = Assert.Single(state.SLAs,
            sla => sla.Id == "divine_domain_sla_spell:burning_hands");
        Assert.Equal("at will", burningHands.UsesPerDay);
        Assert.Equal(16, burningHands.CasterLevel);
        Assert.Equal(21, burningHands.SaveDC); // 10 + spell level 1 + Cha 4 + rank 6.
        Assert.Contains(state.SLAs, sla => sla.Id == "divine_travel_teleport_greater" && sla.CasterLevel == 20);
        Assert.Contains(state.SLAs, sla => sla.Id == "divine_travel_plane_shift" && sla.CasterLevel == 20);
        Assert.Equal(20, divine.DivineAuraSaveDc);
        Assert.Equal("600 ft.", divine.DivineAuraRadius);
        Assert.Equal(5, divine.RemoteSensingLocations);
        Assert.Equal(30_000, divine.MaximumPortfolioItemValueGp);
        Assert.Equal(3, Assert.Single(state.ItemActivationLevelRules).EffectiveLevel(state));
        Assert.Contains(state.Abilities, ability => ability.Id == "salient:alter_size");
    }

    [Fact]
    public void SalientAbilityBudgetAndMinimumRank_AreValidatedWithoutDiscardingInputs()
    {
        var character = HumanFighter();
        character.Divinity = new DivinityChoices
        {
            DivineRank = 1,
            SalientDivineAbilityIds = new()
            {
                "salient:divine_creation", "salient:alter_size", "salient:alter_size",
            },
        };

        var state = Evaluate(character);

        Assert.Contains(state.Warnings, warning => warning.Message.Contains("3 salient", StringComparison.Ordinal));
        Assert.Contains(state.Warnings, warning => warning.Message.Contains("divine rank 16+", StringComparison.Ordinal));
        Assert.Contains(state.Warnings, warning => warning.Message.Contains("may only be selected once", StringComparison.Ordinal));
        Assert.Equal(character.Divinity.SalientDivineAbilityIds, state.Divinity!.SalientDivineAbilityIds);
    }

    [Fact]
    public void GreaterDeity_ExposesNarrativeRankCharacteristics()
    {
        var character = HumanFighter();
        character.Divinity = new DivinityChoices { DivineRank = 16 };

        var divine = Evaluate(character).Divinity!;

        Assert.Equal(DivineStatus.GreaterDeity, divine.Status);
        Assert.Equal(21, divine.SalientDivineAbilitySlots);
        Assert.True(divine.AlwaysMaximizesRolls);
        Assert.True(divine.AlwaysGetsTwentyOnChecks);
        Assert.True(divine.CanCreateArtifacts);
        Assert.Null(divine.MaximumPortfolioItemValueGp);
        Assert.Equal("16 miles", divine.DivineAuraRadius);
        Assert.Equal("100 miles on an Outer Plane; 100 ft. per rank elsewhere", divine.GodlyRealmControl);
    }

    [Fact]
    public void StaticSalientAbilities_ModifyTheComputedSheetAndRespectRepeatability()
    {
        var character = HumanFighter();
        character.Alignment = Alignment.LG;
        character.BaseAbilityScores.CON = 29;
        character.Divinity = new DivinityChoices
        {
            DivineRank = 16,
            SalientDivineAbilityIds = new()
            {
                "salient:divine_fast_healing", "salient:divine_fast_healing",
                "salient:increased_spell_resistance", "salient:increased_spell_resistance",
                "salient:increased_damage_reduction",
            },
        };

        var state = Evaluate(character);

        Assert.Equal(72, state.FastHealing); // 2 × (20 + rank 16).
        Assert.Equal(88, state.SpellResistance); // base 32 + rank 16 + two × 20.
        Assert.Contains(state.DamageReduction,
            dr => dr.Value == 35 && dr.BypassedBy == "epic and evil or chaotic");
        Assert.DoesNotContain(state.Warnings,
            warning => warning.Message.Contains("may only be selected once", StringComparison.Ordinal));
    }

    [Fact]
    public void TwentyOutsiderHitDice_UseRankPlusThirteenNaturalArmor()
    {
        var character = new Character
        {
            Name = "Outsider Deity",
            Alignment = Alignment.LG,
            RaceId = "race:outsider",
            BaseAbilityScores = Scores(),
            Divinity = new DivinityChoices { DivineRank = 5 },
            Ticks = Enumerable.Range(0, 20)
                .Select(_ => new Tick { DriverId = "racial_hd:outsider" })
                .ToList(),
        };

        var state = Evaluate(character);

        Assert.Equal(18, state.NaturalArmor); // rank 5 + 13.
        Assert.Contains("lawful", state.Subtypes);
        Assert.Contains("good", state.Subtypes);
    }

    [Fact]
    public void QuasiDeity_CastsEveryDomainSpellItGrantsAtWillWithoutClericLevels()
    {
        var character = HumanFighter();
        character.BaseAbilityScores.CHA = 18;
        character.Divinity = new DivinityChoices
        {
            DivineRank = 0,
            DomainIds = new() { "domain:fire" },
        };

        var state = Evaluate(character);

        // "A deity can use any domain spell it can grant as a spell-like ability at will" carries
        // no rank qualifier, so rank 0 gets these even though it grants no spells to worshippers
        // and has no cleric level of its own.
        Assert.False(state.Divinity!.GrantsSpells);
        Assert.DoesNotContain(state.ClassLevels, level => level.Key == "class:cleric");
        var burningHands = Assert.Single(state.SLAs,
            sla => sla.Id == "divine_domain_sla_spell:burning_hands");
        Assert.Equal("at will", burningHands.UsesPerDay);
        Assert.Equal(10, burningHands.CasterLevel); // 10 + divine rank 0.
        Assert.Equal(15, burningHands.SaveDC); // 10 + spell level 1 + Cha 4 + rank 0.
        // Every level of the domain, not just the low ones.
        Assert.Contains(state.SLAs, sla => sla.Id == "divine_domain_sla_spell:elemental_swarm");
        // Divine travel stays a rank-1 power.
        Assert.DoesNotContain(state.SLAs, sla => sla.Id.StartsWith("divine_travel_", StringComparison.Ordinal));
    }

    [Fact]
    public void Deity_TreatsDomainsChosenThroughAClassAsItsOwn()
    {
        var character = HumanFighter();
        character.BaseAbilityScores.WIS = 14;
        character.Ticks = new()
        {
            new Tick
            {
                DriverId = "class:cleric",
                Choices = new TickChoices
                {
                    ClassFeatureChoices = new() { ["domains"] = new() { "domain:fire", "domain:magic" } }
                }
            }
        };
        character.Divinity = new DivinityChoices { DivineRank = 1 };

        var state = Evaluate(character);

        // The deity declared no domains of its own; the ones it took as a cleric are the ones it
        // can grant, so they are the ones it casts at will.
        Assert.Empty(state.Divinity!.DomainIds);
        Assert.Contains("domain:fire", state.Domains);
        Assert.Contains(state.SLAs, sla =>
            sla.Id == "divine_domain_sla_spell:burning_hands" && sla.UsesPerDay == "at will");
    }

    [Fact]
    public void Deity_DoesNotStackItsDamageReductionWithATemplateThatAlreadyGrantsEpicDR()
    {
        var character = HumanFighter();
        character.TemplateIds = new List<string> { "template:paragon" };
        character.Divinity = new DivinityChoices { DivineRank = 0 };

        var state = Evaluate(character);

        // Paragon grants DR 10/epic and a rank-0 deity grants DR 10/epic. Same bypass, so one
        // entry at the better value — not two rows the sheet would read as 20.
        var epic = Assert.Single(state.DamageReduction, dr => dr.BypassedBy == "epic");
        Assert.Equal(10, epic.Value);
    }

    private static Character HumanFighter() => new()
    {
        Name = "Divine Test",
        RaceId = "race:human",
        BaseAbilityScores = Scores(),
        Ticks = new() { new Tick { DriverId = "class:fighter" } },
    };

    private static AbilityScoreSet Scores() => new()
        { STR = 10, DEX = 10, CON = 10, INT = 10, WIS = 10, CHA = 10 };

    private static CharacterState Evaluate(Character character) =>
        new ReplayStudio(TestContentHelper.LoadBundledPacks()).Evaluate(character);
}
